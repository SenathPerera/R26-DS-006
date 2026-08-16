"""Swappable LLM backends for the health companion.

Default is **Ollama** - a local open model (Qwen 2.5 / Llama 3.1 / Gemma 2).
Free, no API key, and PRIVATE: the student's voice/emotion text never leaves the
device, which matters for a health app. Claude is kept as an optional higher-
quality backend. All backends expose the same `.chat(system, messages) -> str`,
so the companion is provider-agnostic.
"""

import json
import os
import urllib.request


class LLMBackend:
    """A chat backend: system prompt + message list -> assistant reply text."""

    def chat(self, system: str, messages: list[dict]) -> str:
        raise NotImplementedError


class OllamaBackend(LLMBackend):
    """Local open model via Ollama (http://localhost:11434) - free, private,
    no API key. Setup:  `ollama pull qwen2.5`  then  `ollama serve`.
    Uses the stdlib (urllib) so it adds no Python dependency."""

    def __init__(self, model: str | None = None, host: str | None = None,
                 timeout: int = 60, temperature: float = 0.7, num_predict: int = 220):
        # Model/host are overridable via env so the demo can run a small local
        # model (OLLAMA_MODEL=qwen2.5:0.5b) while the panel build points at the
        # full qwen2.5 - no code change, just the environment.
        self.model = model or os.environ.get("OLLAMA_MODEL", "qwen2.5")
        self.host = (host or os.environ.get("OLLAMA_HOST", "http://localhost:11434")).rstrip("/")
        self.timeout = timeout
        self.temperature = temperature
        self.num_predict = num_predict     # cap reply length (spoken = short)

    def chat(self, system: str, messages: list[dict]) -> str:
        payload = {
            "model": self.model,
            "messages": [{"role": "system", "content": system}, *messages],
            "stream": False,
            "options": {"temperature": self.temperature,
                        "num_predict": self.num_predict},
        }
        req = urllib.request.Request(
            f"{self.host}/api/chat",
            data=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
        )
        with urllib.request.urlopen(req, timeout=self.timeout) as resp:
            data = json.loads(resp.read())
        return data["message"]["content"].strip()


class ClaudeBackend(LLMBackend):
    """Optional: Anthropic Claude - best empathy/safety, but paid + needs
    ANTHROPIC_API_KEY. Client injectable for tests."""

    def __init__(self, model: str = "claude-opus-4-8", client=None):
        self.model = model
        self._client = client

    def chat(self, system: str, messages: list[dict]) -> str:
        if self._client is None:
            import anthropic
            self._client = anthropic.Anthropic()
        resp = self._client.messages.create(
            model=self.model, max_tokens=400, system=system, messages=messages)
        return "".join(b.text for b in resp.content if b.type == "text").strip()


class EchoBackend(LLMBackend):
    """Offline/test backend: returns whatever `fn(system, messages)` returns
    (default a canned line). Lets the companion be tested with no model."""

    def __init__(self, fn=None):
        self.fn = fn or (lambda system, messages: "How are you arriving today?")

    def chat(self, system: str, messages: list[dict]) -> str:
        return self.fn(system, messages)
