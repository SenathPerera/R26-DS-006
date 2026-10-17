import json
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from fastapi import FastAPI
from fastapi.testclient import TestClient

from . import app as monitor
from .agent_diagnostics import AgentHistory, MAX_SAMPLES, validate_snapshot
from .instrumentation import ServiceMetrics, install_monitoring
from .relay_diagnostics import RelayDiagnostics


def sample(at=100):
    def agent(size):
        return {
            "available": True, "mode": "ContextualBandit" if size == 5 else "PersonalizedBandit",
            "state": "Adaptive", "transitioning": False, "updates": 1,
            "applied": [.5] * size, "target": [.6] * size,
            "input": {"available": True, "source": "ComponentB", "stress": .6,
                      "confidence": .9, "quality": .8, "ageSeconds": 3},
            "decision": {"available": True, "sequence": 2, "at": at, "stress": .7,
                         "proposed": "IncreaseWarmth", "executed": "IncreaseWarmth",
                         "safety": "Accepted", "result": "Selected",
                         "proposedDelta": [.1] * size, "safeDelta": [.08] * size},
            "reward": {"available": True, "valid": True, "at": at, "sequence": 1,
                       "action": "NoChange", "status": "Updated", "total": .3},
        }
    return {"schemaVersion": "mindsync-agents-v1", "capturedAt": at, "phase": "Adaptive",
            "visual": agent(5), "audio": agent(7)}


def message(snapshot, session="private-session"):
    return {"schemaVersion": "mindsync-monitor-v1", "messageType": "client_diagnostics",
            "payload": {"sessionId": session, "values": {"phase": "Adaptive", "agents": snapshot}}}


class AgentTests(unittest.TestCase):
    def test_coordination_extension_is_allowlisted_and_keeps_legacy_reports_valid(self):
        payload = sample()
        payload["coordination"] = {
            "owner": "Audio", "stage": "Measuring", "reason": "Waiting for a complete post action window",
            "eligiblePostStartUtc": 125, "remainingSeconds": 180,
            "completedTurns": 3, "cancelledTurns": 1,
        }
        self.assertTrue(validate_snapshot(payload))
        history = AgentHistory()
        history.append(payload, 100)
        self.assertEqual("Audio", history.snapshot(100)[0]["coordination"]["owner"])
        self.assertTrue(validate_snapshot(sample()), "Older Unity snapshots must remain readable")
        for key, invalid in (("owner", "Both"), ("stage", "<script>"), ("remainingSeconds", float("nan")),
                             ("cancelledTurns", -1), ("token", "secret")):
            changed = json.loads(json.dumps(payload))
            changed["coordination"][key] = invalid
            self.assertFalse(validate_snapshot(changed))

    def test_strict_schema_rejects_extra_fields_bad_shapes_nonfinite_and_bool_numbers(self):
        self.assertTrue(validate_snapshot(sample()))
        mutations = [
            lambda s: s.update(token="secret"),
            lambda s: s["audio"].update(prompt="private"),
            lambda s: s["visual"]["input"].update(stress=1.1),
            lambda s: s["visual"]["input"].update(stress=True),
            lambda s: s["audio"]["input"].update(stress=float("nan")),
            lambda s: s["audio"]["input"].update(ageSeconds=10**1000),
            lambda s: s["audio"].update(applied=[.1] * 6),
            lambda s: s["visual"]["decision"].update(safeDelta=[2] * 5),
            lambda s: s["visual"]["reward"].update(total=float("inf")),
            lambda s: s["audio"].update(updates=-1),
            lambda s: s["audio"].update(state="<script>"),
            lambda s: s.pop("visual"),
        ]
        for mutate in mutations:
            payload = sample(); mutate(payload)
            self.assertFalse(validate_snapshot(payload), payload)

    def test_history_bounded_deduplicated_pruned_and_copied(self):
        history = AgentHistory()
        for i in range(250):
            history.append(sample(100 + i * 5), 100 + i * 5)
        self.assertEqual(MAX_SAMPLES, len(history.samples))
        final = history.samples[-1]["capturedAt"]
        history.append(sample(final), final + 5)
        history.append(sample(final - 1), final + 5)
        history.append(sample(final + .5), final + .5)
        self.assertEqual(final, history.samples[-1]["capturedAt"])
        public = history.snapshot(final)
        public[-1]["audio"]["target"][0] = 999
        self.assertEqual(.6, history.samples[-1]["audio"]["target"][0])
        self.assertEqual([], history.snapshot(final + 901))
        history.append(sample(1), final + 902)
        self.assertEqual(1, len(history.samples), "Expired timestamps must not lock out a new client clock")

    def test_authenticated_quest_only_and_session_isolation(self):
        diagnostics = RelayDiagnostics()
        channels = SimpleNamespace(mobile={}, quest={"private-session": 1})
        with patch("services.project_monitor.relay_diagnostics.time.time", return_value=100):
            diagnostics.accept(message(sample()), "private-session", "mobile")
            self.assertEqual({}, diagnostics.agents)
            diagnostics.accept(message(sample(), "wrong"), "private-session", "quest")
            self.assertEqual({}, diagnostics.agents)
            diagnostics.accept(message(sample()), "private-session", "quest")
            label = diagnostics.sessions["private-session"]["label"]
            response = diagnostics.agent_snapshot(label, channels)
            self.assertEqual("live", response["state"])
            self.assertEqual(1, len(response["samples"]))
            self.assertEqual([], diagnostics.agent_snapshot("aaaaaaaaaa", channels)["samples"])
            status = diagnostics.snapshot(channels)
            self.assertNotIn("agents", json.dumps(status))
            self.assertNotIn("private-session", json.dumps(response))
        with patch("services.project_monitor.relay_diagnostics.time.time", return_value=116):
            self.assertEqual("stale", diagnostics.agent_snapshot(label, channels)["state"])
        channels.quest.clear()
        with patch("services.project_monitor.relay_diagnostics.time.time", return_value=110):
            self.assertEqual("disconnected", diagnostics.agent_snapshot(label, channels)["state"])
        for i in range(40):
            diagnostics.touch(str(i))
        self.assertEqual({}, diagnostics.agents)

    def test_invalid_agent_extension_does_not_break_operational_heartbeat(self):
        d = RelayDiagnostics()
        payload = sample(); payload["audio"]["input"]["stress"] = None
        d.accept(message(payload), "private-session", "quest")
        self.assertEqual("Adaptive", d.sessions["private-session"]["quest"]["values"]["phase"])
        self.assertEqual([], list(d.agents["private-session"].samples))

    def test_loopback_origin_and_session_guards_for_agent_endpoint(self):
        app = FastAPI()
        install_monitoring(app, ServiceMetrics("relay"), agents=lambda label: {"session": label})
        for host in ("10.0.0.2", "127.0.0.1"):
            with TestClient(app, client=(host, 80)) as client:
                response = client.get("/monitor/agents?session=0123456789")
                self.assertEqual(403 if host.startswith("10.") else 200, response.status_code)
                if host == "127.0.0.1":
                    self.assertEqual("no-store", response.headers["cache-control"])
                    self.assertEqual(400, client.get("/monitor/agents?session=private-session").status_code)
                    self.assertEqual(403, client.get("/monitor/agents?session=0123456789",
                        headers={"Origin": "https://example.com"}).status_code)

    def test_monitor_proxy_scoped_read_only_and_handles_relay_outage(self):
        relay = {"id": "relay", "url": "http://127.0.0.1:8080", "reachable": True}
        client = TestClient(monitor.app, base_url="http://localhost", client=("127.0.0.1", 80))
        with patch.object(monitor.collector, "services", [relay]):
            payload = {"schemaVersion": "mindsync-agents-v1", "session": "0123456789", "samples": []}
            with patch.object(monitor, "read_json", return_value=payload) as read:
                response = client.get("/api/agents?session=0123456789")
                self.assertEqual(200, response.status_code)
                read.assert_called_once_with("http://127.0.0.1:8080/monitor/agents?session=0123456789", 1_048_576)
                self.assertEqual(400, client.get("/api/agents?session=http://external").status_code)
            with patch.object(monitor, "read_json", return_value={**payload, "session": "wrong"}):
                self.assertEqual(503, client.get("/api/agents?session=0123456789").status_code)
            relay["reachable"] = False
            self.assertEqual(503, client.get("/api/agents?session=0123456789").status_code)

    def test_maximum_history_fits_transport_response_limit(self):
        history = AgentHistory()
        for i in range(MAX_SAMPLES):
            history.append(sample(i * 5), i * 5)
        self.assertLess(len(json.dumps(history.snapshot(900))), 1_048_576)

    def test_unity_field_names_and_dimensions_match_wire_schema(self):
        root = Path(__file__).resolve().parents[2]
        dto = (root / "unity/Assets/Laminar VR/Scripts/Runtime/Networking/SessionRuntimeDiagnostics.cs").read_text()
        import re
        coordination = {"owner": "Audio", "stage": "Measuring", "reason": "Waiting",
                        "eligiblePostStartUtc": 100, "remainingSeconds": 100, "completedTurns": 1, "cancelledTurns": 0}
        types = {"AgentMonitorSnapshot": {**sample(), "coordination": coordination},
                 "AgentMonitorCoordination": coordination, "AgentMonitorState": sample()["audio"],
                 "AgentMonitorInput": sample()["audio"]["input"],
                 "AgentMonitorDecision": sample()["audio"]["decision"],
                 "AgentMonitorReward": sample()["audio"]["reward"]}
        for name, expected in types.items():
            body = re.search(r"class " + name + r"\s*\{([^}]+)\}", dto).group(1)
            fields = re.findall(r"public \w+(?:\[\])? (\w+)[; =]", body)
            self.assertEqual(set(expected), set(fields), name)
        reporter = (root / "unity/Assets/AdaptiveAudioVR/Scripts/Integration/ProjectMonitorReporter.cs").read_text()
        self.assertIn("mixer.CurrentAppliedParameters.ToControlVector()", reporter)
        self.assertIn("input.Stress.ContinuousScore / 3d", reporter)
        self.assertNotIn("ParticipantPseudonym", reporter)
        self.assertIn("observation.CurrentEnvironment", reporter)
        self.assertNotIn("features[", reporter)


if __name__ == "__main__":
    unittest.main()
