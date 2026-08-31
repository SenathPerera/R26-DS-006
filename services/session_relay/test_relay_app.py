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
                "preferredEnvironment": {
                    "illumination": 0.319,
                    "warmth": 0.5,
                    "atmosphericSoftness": 0.0,
                    "colorRichness": 0.5,
                    "ambientMotion": 0.75,
                },
            },
        )
        self.assertEqual(200, prepared.status_code)
        session = prepared.json()
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

        visual_log = self.client.get(
            f"/sessions/{session_id}/visual-log",
            params={"mobileToken": session["mobileToken"]},
        )
        self.assertEqual(200, visual_log.status_code)
        self.assertEqual(
            [ready, duplicate_ready, telemetry],
            visual_log.json()["messages"],
        )

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
