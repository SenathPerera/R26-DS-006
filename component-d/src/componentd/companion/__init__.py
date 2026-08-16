"""Health-companion package: the AI voice that talks with the student before and
after a session. The LLM (dialogue) stage of the voice pipeline STT -> LLM -> TTS.
Provider-agnostic - defaults to a local Ollama model (free, private).
See README.md for the panel-study notes."""

from .backends import ClaudeBackend, EchoBackend, LLMBackend, OllamaBackend
from .dialogue import HealthCompanion
from .persona import CRISIS_REPLY, SYSTEM_PROMPT, is_crisis

__all__ = [
    "HealthCompanion",
    "LLMBackend", "OllamaBackend", "ClaudeBackend", "EchoBackend",
    "SYSTEM_PROMPT", "CRISIS_REPLY", "is_crisis",
]
