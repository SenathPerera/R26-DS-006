import unittest

from services.session_relay.session_store import (
    ActiveSessionExistsError,
    PairingRejectedError,
    SessionStore,
)


PREFERENCE = {
    "illumination": 0.319,
    "warmth": 0.5,
    "atmosphericSoftness": 0.0,
    "colorRichness": 0.5,
    "ambientMotion": 0.75,
}


class SessionStoreTests(unittest.TestCase):
    def setUp(self) -> None:
        self.now = 1000.0
        self.codes = iter(["123456", "654321"])
        self.store = SessionStore(
            code_lifetime_seconds=300,
            clock=lambda: self.now,
            code_factory=lambda: next(self.codes),
            token_factory=lambda: "mobile-token",
        )

    def test_create_is_idempotent_for_request_id(self) -> None:
        first = self.store.create("request-1", "participant-1", "temple-pond", PREFERENCE)
        second = self.store.create("request-1", "participant-1", "temple-pond", PREFERENCE)
        self.assertIs(first, second)
        self.assertEqual("123456", first.pairing_code)

    def test_only_one_active_session_is_allowed(self) -> None:
        self.store.create("request-1", "participant-1", "temple-pond", PREFERENCE)
        with self.assertRaises(ActiveSessionExistsError):
            self.store.create("request-2", "participant-2", "temple-pond", PREFERENCE)

    def test_pairing_code_is_one_time(self) -> None:
        session = self.store.create("request-1", "participant-1", "temple-pond", PREFERENCE)
        paired = self.store.pair(session.pairing_code, "quest-1")
        self.assertEqual("quest-1", paired.quest_client_id)
        with self.assertRaisesRegex(PairingRejectedError, "code-already-used"):
            self.store.pair(session.pairing_code, "quest-1")

    def test_expired_code_is_rejected_and_allows_new_session(self) -> None:
        session = self.store.create("request-1", "participant-1", "temple-pond", PREFERENCE)
        self.now = session.expires_at_unix_seconds
        with self.assertRaisesRegex(PairingRejectedError, "code-expired"):
            self.store.pair(session.pairing_code, "quest-1")
        replacement = self.store.create(
            "request-2", "participant-1", "temple-pond", PREFERENCE
        )
        self.assertEqual("654321", replacement.pairing_code)


if __name__ == "__main__":
    unittest.main()
