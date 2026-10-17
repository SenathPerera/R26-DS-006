from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from fastapi.testclient import TestClient

from . import app as relay
from .session_store import SessionStore


class SessionRelayAppTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self._initialization_delay_seconds = relay.INITIALIZATION_DELAY_SECONDS
        relay.DATA_DIRECTORY = Path(self._temporary_directory.name)
        relay.INITIALIZATION_DELAY_SECONDS = 0
        relay.store = SessionStore(code_factory=lambda: "482731")
        relay.channels = relay.SessionChannels()
        relay.diagnostics = relay.RelayDiagnostics()
        self._client_context = TestClient(relay.app)
        self.client = self._client_context.__enter__()

    def tearDown(self) -> None:
        self._client_context.__exit__(None, None, None)
        relay.INITIALIZATION_DELAY_SECONDS = self._initialization_delay_seconds
        self._temporary_directory.cleanup()

    def test_mobile_and_quest_complete_pairing_command_and_telemetry_flow(self) -> None:
        prepared = self.client.post(
            "/sessions",
            json={
                "requestId": "request-1",
                "participantPseudonym": "participant-7",
                "sceneId": "temple-pond",
                "studyCondition": "ContextualBandit",
                "preferredEnvironment": {
                    "illumination": 0.319,
                    "warmth": 0.5,
                    "atmosphericSoftness": 0.0,
                    "colorRichness": 0.5,
                    "ambientMotion": 0.75,
                },
                "sessionContext": self._session_context(),
            },
        )
        self.assertEqual(200, prepared.status_code)
        session = prepared.json()
        self.assertEqual("ContextualBandit", session["studyCondition"])
        session_id = session["sessionId"]

        mobile_path = (
            "/realtime?role=mobile"
            f"&sessionId={session_id}&mobileToken={session['mobileToken']}"
        )
        with self.client.websocket_connect(mobile_path) as mobile:
            with self.client.websocket_connect("/realtime?role=quest") as quest:
                quest.send_json(
                    self._envelope(
                        "pairing_request",
                        {
                            "clientRole": "quest",
                            "pairingCode": session["pairingCode"],
                            "questClientId": "quest-test-client",
                            "appVersion": "1.0.0-test",
                        },
                        "pairing-message",
                    )
                )

                pairing = quest.receive_json()
                configuration = quest.receive_json()
                self.assertTrue(pairing["payload"]["accepted"])
                self.assertEqual("session_configuration", configuration["messageType"])
                self.assertEqual(
                    "ContextualBandit",
                    configuration["payload"]["studyCondition"],
                )
                self.assertEqual(
                    self._session_context(),
                    configuration["payload"]["sessionContext"],
                )

                # Diagnostic messages are session-authenticated, never forwarded,
                # and must not change the durable study-log count below.
                mobile.send_json({
                    "schemaVersion": "mindsync-monitor-v1", "messageType": "client_diagnostics",
                    "payload": {"sessionId": session_id, "values": {"wearableConnected": True}},
                })
                quest.send_json({
                    "schemaVersion": "mindsync-monitor-v1", "messageType": "client_diagnostics",
                    "payload": {"sessionId": session_id, "values": {"audioPrepared": True}},
                })

                # A command sent before readiness must arrive before start. This
                # guards against starting immediately after configuration.
                mobile.send_json(
                    self._envelope(
                        "session_command",
                        {"sessionId": session_id, "command": "pause"},
                        "mobile-command",
                    )
                )
                self.assertEqual("pause", quest.receive_json()["payload"]["command"])

                ready = self._envelope(
                    "quest_state",
                    {
                        "sessionId": session_id,
                        "phase": "ready",
                        "timestamp": 1787282800.0,
                    },
                    "ready-message",
                )
                quest.send_json(ready)
                self.assertEqual(ready, mobile.receive_json())
                start = quest.receive_json()
                self.assertEqual("start", start["payload"]["command"])
                self.assertTrue(relay.diagnostics.sessions[session_id]["mobile"]["values"]["wearableConnected"])
                self.assertTrue(relay.diagnostics.sessions[session_id]["quest"]["values"]["audioPrepared"])

                duplicate_ready = self._envelope(
                    "quest_state",
                    ready["payload"],
                    "duplicate-ready-message",
                )
                quest.send_json(duplicate_ready)
                self.assertEqual(duplicate_ready, mobile.receive_json())
                mobile.send_json(
                    self._envelope(
                        "session_command",
                        {"sessionId": session_id, "command": "pause"},
                        "post-ready-command",
                    )
                )
                self.assertEqual("pause", quest.receive_json()["payload"]["command"])

                telemetry = self._envelope(
                    "visual_telemetry_batch",
                    {
                        "events": [
                            {
                                "sessionId": session_id,
                                "eventType": "policy_decision",
                                "timestamp": 1787282898.4,
                            }
                        ]
                    },
                    "telemetry-message",
                )
                quest.send_json(telemetry)
                acknowledged = quest.receive_json()
                forwarded = mobile.receive_json()
                self.assertEqual("delivery_ack", acknowledged["messageType"])
                self.assertEqual(
                    "telemetry-message",
                    acknowledged["payload"]["acknowledgedMessageId"],
                )
                self.assertEqual(telemetry, forwarded)

                quest.send_json(telemetry)
                duplicate_acknowledgement = quest.receive_json()
                self.assertEqual(
                    "telemetry-message",
                    duplicate_acknowledgement["payload"]["acknowledgedMessageId"],
                )

                completed = self._envelope(
                    "quest_state",
                    {
                        "sessionId": session_id,
                        "phase": "completed",
                        "timestamp": 1787284000.0,
                    },
                    "completed-message",
                )
                quest.send_json(completed)
                self.assertEqual(completed, mobile.receive_json())

        visual_log = self.client.get(
            f"/sessions/{session_id}/visual-log",
            params={"mobileToken": session["mobileToken"]},
        )
        self.assertEqual(200, visual_log.status_code)
        visual_log_body = visual_log.json()
        self.assertTrue(visual_log_body["finalized"])
        self.assertEqual("completed", visual_log_body["completionPhase"])
        self.assertFalse(visual_log_body["deliveryAcknowledged"])
        self.assertEqual(4, visual_log_body["messageCount"])
        self.assertEqual("completed-message", visual_log_body["lastMessageId"])
        self.assertEqual(
            [ready, duplicate_ready, telemetry, completed],
            visual_log_body["messages"],
        )

        acknowledgement_path = (
            f"/sessions/{session_id}/visual-log/acknowledgement"
            f"?mobileToken={session['mobileToken']}"
        )
        receipt = {"messageCount": 4, "lastMessageId": "completed-message"}
        acknowledgement = self.client.post(acknowledgement_path, json=receipt)
        repeated = self.client.post(acknowledgement_path, json=receipt)
        self.assertEqual(200, acknowledgement.status_code)
        self.assertEqual(acknowledgement.json(), repeated.json())
        self.assertTrue(acknowledgement.json()["acknowledged"])
        acknowledged_log = self.client.get(
            f"/sessions/{session_id}/visual-log",
            params={"mobileToken": session["mobileToken"]},
        )
        self.assertTrue(acknowledged_log.json()["deliveryAcknowledged"])

        stale_receipt = self.client.post(
            acknowledgement_path,
            json={"messageCount": 3, "lastMessageId": "telemetry-message"},
        )
        self.assertEqual(409, stale_receipt.status_code)

    def test_audio_preferences_reach_paired_quest_and_request_id_cannot_change_identity(self) -> None:
        long_term = self._audio_profile()
        temporary = {**long_term, "preferredInstruments": ["bells"], "mood": "sleepy"}
        request = {
            "requestId": "audio-request-1", "participantPseudonym": "participant-7",
            "sceneId": "temple-pond",
            "studyCondition": "ContextualBandit",
            "preferredEnvironment": {
                "illumination": 0.3, "warmth": 0.5,
                "atmosphericSoftness": 0.5, "colorRichness": 0.5,
                "ambientMotion": 0.5,
            },
            "sessionContext": self._session_context(),
            "audioPreferences": {
                "schemaVersion": "mindsync-audio-preferences-v1",
                "longTerm": long_term,
                "preferenceMode": "adjust",
                "sessionOverride": temporary,
            },
        }
        prepared = self.client.post("/sessions", json=request)
        self.assertEqual(200, prepared.status_code)
        self.assertEqual(prepared.json(), self.client.post("/sessions", json=request).json())
        changed = {**request, "participantPseudonym": "participant-8"}
        self.assertEqual(409, self.client.post("/sessions", json=changed).status_code)
        changed_audio = {**request, "audioPreferences": {
            **request["audioPreferences"], "preferenceMode": "usual",
            "sessionOverride": None,
        }}
        self.assertEqual(409, self.client.post("/sessions", json=changed_audio).status_code)
        session = prepared.json()
        with self.client.websocket_connect("/realtime?role=quest") as quest:
            quest.send_json(self._envelope("pairing_request", {
                "clientRole": "quest", "pairingCode": session["pairingCode"],
                "questClientId": "quest-audio-test", "appVersion": "test",
            }, "pair-audio"))
            self.assertTrue(quest.receive_json()["payload"]["accepted"])
            configuration = quest.receive_json()
            self.assertEqual(session["sessionId"], configuration["payload"]["sessionId"])
            self.assertEqual(request["studyCondition"], configuration["payload"]["studyCondition"])
            self.assertEqual(request["sessionContext"], configuration["payload"]["sessionContext"])
            self.assertEqual(request["audioPreferences"], configuration["payload"]["audioPreferences"])

    def test_rejects_incomplete_audio_adjustment(self) -> None:
        request = {
            "requestId": "bad-audio-request", "participantPseudonym": "participant-7",
            "sceneId": "temple-pond",
            "studyCondition": "StaticPersonalized",
            "preferredEnvironment": {
                "illumination": 0.5, "warmth": 0.5,
                "atmosphericSoftness": 0.5, "colorRichness": 0.5,
                "ambientMotion": 0.5,
            },
            "sessionContext": self._session_context(),
            "audioPreferences": {
                "schemaVersion": "mindsync-audio-preferences-v1",
                "longTerm": self._audio_profile(),
                "preferenceMode": "adjust", "sessionOverride": None,
            },
        }
        self.assertEqual(422, self.client.post("/sessions", json=request).status_code)

    def test_create_session_rejects_unknown_study_condition(self) -> None:
        response = self.client.post(
            "/sessions",
            json={
                "requestId": "request-invalid-condition",
                "participantPseudonym": "participant-7",
                "sceneId": "temple-pond",
                "studyCondition": "ExperimentalPolicy",
                "preferredEnvironment": {
                    "illumination": 0.5,
                    "warmth": 0.5,
                    "atmosphericSoftness": 0.5,
                    "colorRichness": 0.5,
                    "ambientMotion": 0.5,
                },
                "sessionContext": self._session_context(),
            },
        )

        self.assertEqual(422, response.status_code)

    def test_create_session_rejects_missing_or_invalid_session_context(self) -> None:
        request = {
            "requestId": "request-invalid-context",
            "participantPseudonym": "participant-7",
            "sceneId": "temple-pond",
            "studyCondition": "StaticPersonalized",
            "preferredEnvironment": {
                "illumination": 0.5,
                "warmth": 0.5,
                "atmosphericSoftness": 0.5,
                "colorRichness": 0.5,
                "ambientMotion": 0.5,
            },
        }
        self.assertEqual(422, self.client.post("/sessions", json=request).status_code)
        request["sessionContext"] = {
            **self._session_context(),
            "subjectiveStress": 11,
        }
        self.assertEqual(422, self.client.post("/sessions", json=request).status_code)

    @staticmethod
    def _audio_profile() -> dict:
        return {
            "preferredInstruments": ["piano", "flute"], "mood": "calm",
            "tempoPreference": "slow", "audioIntensity": 0.3,
            "ambientMusicBalance": "balanced", "brightnessPreference": "soft_dark",
            "noveltyTolerance": 0.2, "avoidDissonance": True,
            "rhythmPreference": "minimal", "natureSoundPreference": "medium",
            "reverbPreference": "spacious", "volumePreference": "low",
        }

    @staticmethod
    def _session_context() -> dict:
        return {
            "schemaVersion": "mindsync-session-context-v1",
            "collectedAt": "2026-10-10T08:30:00.000Z",
            "subjectiveStress": 6,
            "moodValence": -0.2,
            "fatigue": 0.7,
            "sleepQuality": 0.4,
            "headacheOrEyeStrainToday": False,
            "timeOfDayMinutes": 510,
            "sessionSequenceNumber": 2,
            "daysSincePreviousSession": 3,
        }

    @staticmethod
    def _envelope(message_type: str, payload: dict, message_id: str) -> dict:
        return {
            "schemaVersion": relay.SCHEMA_VERSION,
            "messageId": message_id,
            "messageType": message_type,
            "payload": payload,
        }


if __name__ == "__main__":
    unittest.main()
