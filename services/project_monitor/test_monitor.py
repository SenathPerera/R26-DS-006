import json
import unittest
from types import SimpleNamespace
from unittest.mock import patch

from fastapi import FastAPI
from fastapi.testclient import TestClient

from . import app as monitor
from .flow import build_flow
from .instrumentation import ServiceMetrics, install_monitoring
from .relay_diagnostics import RelayDiagnostics


def services(session=None):
    rows = [{"id": key, "name": key, "url": "http://127.0.0.1:1", "reachable": True,
             "diagnostics": {"values": {}, "counts": {}}, "health": {}}
            for key in ("component-b", "component-d", "lyria", "relay")]
    rows[-1]["diagnostics"]["details"] = {"sessions": [session] if session else []}
    return rows


class FlowTests(unittest.TestCase):
    def test_missing_mobile_release_backend_is_actionable(self):
        collector = monitor.Collector()
        collector.services = services()
        collector.network = {
            "configuredHosts": [],
            "localAddresses": [],
            "mobileReleaseBackendConfigured": False,
        }
        flow = collector.snapshot()
        self.assertTrue(any(
            alert["title"] == "Mobile release backend is not configured"
            for alert in flow["alerts"]
        ))

        collector.network["mobileReleaseBackendConfigured"] = True
        flow = collector.snapshot()
        self.assertFalse(any(
            alert["title"] == "Mobile release backend is not configured"
            for alert in flow["alerts"]
        ))

    def test_health_is_not_a_working_flow(self):
        flow = build_flow(services(), now=100)
        states = {s["id"]: s["status"] for s in flow["stages"]}
        self.assertEqual("unknown", states["wearable"])
        self.assertEqual("waiting", states["stress"])
        self.assertEqual("unknown", states["audio"])
        self.assertEqual(14, len(states))

    def test_missing_diagnostics_reported(self):
        rows = services()
        rows[0]["diagnostics"] = None
        flow = build_flow(rows)
        self.assertTrue(any("no diagnostics" in a["title"] for a in flow["alerts"]))

    def test_stale_report_cannot_claim_active(self):
        session = {"label": "s", "questConnected": True, "quest": {"receivedAt": 10,
                   "values": {"audioRunning": True, "audioPlaying": True, "generatedClipReady": True}}}
        flow = build_flow(services(session), now=100)
        audio = next(s for s in flow["stages"] if s["id"] == "audio")
        self.assertEqual("stale", audio["status"])
        self.assertIsNone(audio["evidence"]["source playing"])

    def test_start_before_music_is_flagged(self):
        session = {"label": "s", "questConnected": True,
                   "quest": {"receivedAt": 99, "values": {"phase": "Adaptive", "generatedClipReady": False}}}
        flow = build_flow(services(session), now=100)
        self.assertTrue(any(a["title"] == "VR running before audio is ready" for a in flow["alerts"]))

    def test_acclimatization_also_requires_audio_readiness(self):
        session = {"label": "s", "questConnected": True,
                   "quest": {"receivedAt": 99, "values": {"phase": "Acclimatization", "generatedClipReady": False}}}
        flow = build_flow(services(session), now=100)
        self.assertEqual("blocked", next(s for s in flow["stages"] if s["id"] == "start")["status"])

    def test_crossmodal_failure_cannot_look_complete(self):
        rows = services()
        rows[1]["diagnostics"]["operations"] = {"pollB": {"lastSuccessAt": 90, "lastFailureAt": 99}}
        flow = build_flow(rows, now=100)
        self.assertEqual("blocked", next(s for s in flow["stages"] if s["id"] == "crossmodal")["status"])

    def test_parallel_controllers_and_delivery_evidence(self):
        session = {"label": "s", "questConnected": True, "quest": {"receivedAt": 99,
                   "values": {"phase": "Adaptive", "generatedClipReady": True, "audioRunning": True,
                              "audioPlaying": True, "audioMuted": False, "physiologyAgeSeconds": 5,
                              "visualPolicy": "ContextualBandit", "audioPolicy": "PersonalizedBandit"}}}
        flow = build_flow(services(session), now=100)
        states = {s["id"]: s["status"] for s in flow["stages"]}
        self.assertEqual("active", states["visual"])
        self.assertEqual("active", states["audio"])
        self.assertEqual("active", states["delivery"])
        self.assertFalse(any(a["title"] == "Controller modes differ" for a in flow["alerts"]))

    def test_session_selection_isolated(self):
        rows = services({"label": "a", "preferencesConfigured": True})
        rows[-1]["diagnostics"]["details"]["sessions"].append({"label": "b", "preferencesConfigured": False})
        flow = build_flow(rows, "b")
        self.assertEqual("b", flow["session"])
        self.assertEqual("waiting", flow["stages"][0]["status"])

    def test_relay_outage_invalidates_fresh_heartbeats(self):
        rows = services({"label": "s", "questConnected": True, "quest": {"receivedAt": 99, "values": {"audioPlaying": True}}})
        rows[-1]["reachable"] = False
        flow = build_flow(rows, now=100)
        self.assertEqual("unknown", next(s for s in flow["stages"] if s["id"] == "audio")["status"])


class DiagnosticTests(unittest.TestCase):
    def message(self, values, session="private-id"):
        return {"schemaVersion": "mindsync-monitor-v1", "messageType": "client_diagnostics",
                "payload": {"sessionId": session, "values": values}}

    def test_valid_snapshot_does_not_expose_identity(self):
        diagnostics = RelayDiagnostics()
        self.assertTrue(diagnostics.accept(self.message({"wearableConnected": True}), "private-id", "mobile"))
        snapshot = diagnostics.snapshot(SimpleNamespace(mobile={"private-id": 1}, quest={}))
        self.assertNotIn("private-id", json.dumps(snapshot))
        self.assertTrue(snapshot["sessions"][0]["mobileConnected"])

    def test_rejects_sensitive_fields_wrong_session_types_and_nonfinite(self):
        for values in ({"token": "secret"}, {"wearableConnected": "yes"}, {"telemetryAgeSeconds": float("nan")},
                       {"componentBState": "participant name"}, {"framesSent": -1}):
            d = RelayDiagnostics()
            self.assertTrue(d.accept(self.message(values), "private-id", "mobile"))
            self.assertEqual(0, len(d.sessions))
        d = RelayDiagnostics()
        d.accept(self.message({"wearableConnected": True}, "other"), "private-id", "mobile")
        self.assertEqual(0, len(d.sessions))

    def test_session_storage_bounded(self):
        d = RelayDiagnostics()
        for i in range(60):
            d.touch(str(i))
        self.assertEqual(32, len(d.sessions))

    def test_non_diagnostic_messages_unchanged(self):
        self.assertFalse(RelayDiagnostics().accept({"messageType": "session_command"}, "s", "mobile"))


class ServiceTests(unittest.TestCase):
    def test_monitor_local_only_and_operation_accounting(self):
        app = FastAPI()
        metrics = ServiceMetrics("test")
        install_monitoring(app, metrics)
        started = metrics.begin("generateClip")
        metrics.end("generateClip", started, False)
        with TestClient(app, client=("10.0.0.3", 80)) as client:
            self.assertEqual(403, client.get("/monitor/status").status_code)
        with TestClient(app, client=("127.0.0.1", 80)) as client:
            self.assertEqual(403, client.get("/monitor/status", headers={"Origin": "https://example.com"}).status_code)
            response = client.get("/monitor/status")
            self.assertEqual("no-store", response.headers["cache-control"])
            op = response.json()["operations"]["generateClip"]
            self.assertEqual(1, op["failures"])
            self.assertEqual(0, op["inflight"])

    def test_probes_never_call_generation_or_capability(self):
        paths = []
        def fake(url):
            paths.append(url)
            return {"status": "ok"} if url.endswith("/health") else None
        with patch.object(monitor, "read_json", fake):
            for spec in monitor.SERVICES:
                monitor.probe(spec)
        self.assertTrue(all(p.endswith(("/health", "/monitor/status")) for p in paths))

    def test_ui_host_and_origin_guards(self):
        # Isolate security tests from real services, including paid APIs.
        with patch.object(monitor, "probe", side_effect=lambda spec: services()[0]), TestClient(
            monitor.app, base_url="http://localhost", client=("127.0.0.1", 80)
        ) as client:
            self.assertEqual(200, client.get("/").status_code)
            self.assertEqual(200, client.get("/static/app.js").status_code)
            self.assertEqual(200, client.get("/static/styles.css").status_code)
            self.assertEqual(403, client.get("/api/snapshot", headers={"Origin": "https://example.com"}).status_code)
            self.assertEqual(400, client.get("/", headers={"Host": "attacker.example"}).status_code)


if __name__ == "__main__":
    unittest.main()
