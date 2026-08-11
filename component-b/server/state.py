"""Last-prediction store.

The WebSocket at /stream is the real-time path: subscribers get every
prediction as it is produced. But a consumer that cannot hold a socket
open — curl, Postman, a Unity UnityWebRequest, a marker looking at the
system — needs somewhere to pull the current state from.

This keeps only the most recent prediction. It is deliberately not a
history buffer: Component B is a live inference service, and storing a
session's physiological trace here would be a data-retention decision,
not an implementation detail.
"""

from typing import Optional


class LatestPrediction:
    """Single-slot, last-write-wins."""

    def __init__(self):
        self._value: Optional[dict] = None

    def set(self, payload: dict) -> None:
        self._value = payload

    def get(self) -> Optional[dict]:
        return self._value

    def clear(self) -> None:
        self._value = None

    @property
    def is_empty(self) -> bool:
        return self._value is None


latest = LatestPrediction()
