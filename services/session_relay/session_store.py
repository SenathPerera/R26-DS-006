from __future__ import annotations

from dataclasses import dataclass
import secrets
import threading
import time
from typing import Callable


@dataclass
class PreparedSession:
    request_id: str
    session_id: str
    participant_pseudonym: str
    scene_id: str
    preferred_environment: dict[str, float]
    pairing_code: str
    expires_at_unix_seconds: float
    mobile_token: str
    quest_client_id: str | None = None
    ended: bool = False
    completion_phase: str | None = None
    visual_log_message_count: int | None = None
    visual_log_last_message_id: str | None = None


class SessionStore:
    """In-memory authority for the controlled single-participant pilot."""

    def __init__(
        self,
        code_lifetime_seconds: float = 300.0,
        clock: Callable[[], float] = time.time,
        code_factory: Callable[[], str] | None = None,
        token_factory: Callable[[], str] | None = None,
    ) -> None:
        if code_lifetime_seconds <= 0:
            raise ValueError("code_lifetime_seconds must be positive")
        self._code_lifetime_seconds = code_lifetime_seconds
        self._clock = clock
        self._code_factory = code_factory or self._new_code
        self._token_factory = token_factory or (lambda: secrets.token_urlsafe(32))
        self._sessions_by_id: dict[str, PreparedSession] = {}
        self._session_ids_by_request: dict[str, str] = {}
        self._session_ids_by_code: dict[str, str] = {}
        self._lock = threading.RLock()

    def create(
        self,
        request_id: str,
        participant_pseudonym: str,
        scene_id: str,
        preferred_environment: dict[str, float],
    ) -> PreparedSession:
        request_id = self._required(request_id, "request_id")
        participant_pseudonym = self._required(
            participant_pseudonym, "participant_pseudonym"
        )
        scene_id = self._required(scene_id, "scene_id")
        self._validate_environment(preferred_environment)

        with self._lock:
            existing_id = self._session_ids_by_request.get(request_id)
            if existing_id:
                return self._sessions_by_id[existing_id]

            now = self._clock()
            self._expire_unpaired(now)
            if any(not session.ended for session in self._sessions_by_id.values()):
                raise ActiveSessionExistsError("active-session-exists")

            session_id = f"session-{secrets.token_hex(12)}"
            code = self._unique_code()
            session = PreparedSession(
                request_id=request_id,
                session_id=session_id,
                participant_pseudonym=participant_pseudonym,
                scene_id=scene_id,
                preferred_environment=dict(preferred_environment),
                pairing_code=code,
                expires_at_unix_seconds=now + self._code_lifetime_seconds,
                mobile_token=self._token_factory(),
            )
            self._sessions_by_id[session_id] = session
            self._session_ids_by_request[request_id] = session_id
            self._session_ids_by_code[code] = session_id
            return session

    def pair(self, pairing_code: str, quest_client_id: str) -> PreparedSession:
        pairing_code = self._required(pairing_code, "pairing_code").upper()
        quest_client_id = self._required(quest_client_id, "quest_client_id")
        with self._lock:
            session_id = self._session_ids_by_code.get(pairing_code)
            if not session_id:
                raise PairingRejectedError("code-invalid")
            session = self._sessions_by_id[session_id]
            if session.ended:
                raise PairingRejectedError("session-ended")
            if self._clock() >= session.expires_at_unix_seconds:
                session.ended = True
                raise PairingRejectedError("code-expired")
            if session.quest_client_id is not None:
                raise PairingRejectedError("code-already-used")
            session.quest_client_id = quest_client_id
            return session

    def authenticate_mobile(
        self,
        session_id: str,
        mobile_token: str,
        allow_ended: bool = False,
    ) -> PreparedSession:
        with self._lock:
            session = self._sessions_by_id.get(session_id)
            if session is None or not secrets.compare_digest(
                session.mobile_token, mobile_token
            ):
                raise MobileAuthenticationError("mobile-authentication-failed")
            if session.ended and not allow_ended:
                raise MobileAuthenticationError("session-ended")
            return session

    def get(self, session_id: str) -> PreparedSession | None:
        with self._lock:
            return self._sessions_by_id.get(session_id)

    def end(self, session_id: str, completion_phase: str | None = None) -> None:
        if completion_phase not in {None, "completed", "aborted"}:
            raise ValueError("completion_phase is invalid")
        with self._lock:
            session = self._sessions_by_id.get(session_id)
            if session is not None:
                session.ended = True
                if completion_phase is not None:
                    session.completion_phase = completion_phase

    def acknowledge_visual_log(
        self,
        session_id: str,
        message_count: int,
        last_message_id: str,
    ) -> PreparedSession:
        if (
            isinstance(message_count, bool)
            or not isinstance(message_count, int)
            or message_count < 1
        ):
            raise VisualLogAcknowledgementError("visual-log-message-count-invalid")
        last_message_id = self._required(last_message_id, "last_message_id")
        with self._lock:
            session = self._sessions_by_id.get(session_id)
            if session is None:
                raise VisualLogAcknowledgementError("session-not-found")
            if session.completion_phase is None:
                raise VisualLogAcknowledgementError("visual-log-not-finalized")
            if session.visual_log_message_count is not None:
                if (
                    session.visual_log_message_count != message_count
                    or session.visual_log_last_message_id != last_message_id
                ):
                    raise VisualLogAcknowledgementError(
                        "visual-log-acknowledgement-conflict"
                    )
                return session
            session.visual_log_message_count = message_count
            session.visual_log_last_message_id = last_message_id
            return session

    def _expire_unpaired(self, now: float) -> None:
        for session in self._sessions_by_id.values():
            if (
                not session.ended
                and session.quest_client_id is None
                and now >= session.expires_at_unix_seconds
            ):
                session.ended = True

    def _unique_code(self) -> str:
        for _ in range(100):
            candidate = self._required(self._code_factory(), "pairing_code").upper()
            if candidate not in self._session_ids_by_code:
                return candidate
        raise RuntimeError("unable-to-generate-unique-pairing-code")

    @staticmethod
    def _new_code() -> str:
        return f"{secrets.randbelow(1_000_000):06d}"

    @staticmethod
    def _required(value: str, field_name: str) -> str:
        if not isinstance(value, str) or not value.strip():
            raise ValueError(f"{field_name} is required")
        return value.strip()

    @staticmethod
    def _validate_environment(values: dict[str, float]) -> None:
        required = {
            "illumination",
            "warmth",
            "atmosphericSoftness",
            "colorRichness",
            "ambientMotion",
        }
        if not isinstance(values, dict) or set(values) != required:
            raise ValueError("preferred_environment fields are invalid")
        if any(
            isinstance(value, bool)
            or not isinstance(value, (int, float))
            or not 0.0 <= float(value) <= 1.0
            for value in values.values()
        ):
            raise ValueError("preferred_environment values must be normalized")


class ActiveSessionExistsError(RuntimeError):
    pass


class PairingRejectedError(RuntimeError):
    pass


class MobileAuthenticationError(RuntimeError):
    pass


class VisualLogAcknowledgementError(RuntimeError):
    pass
