"""The health-companion dialogue engine (provider-agnostic).

Drives a spoken check-in with the student using the Motivational-Interviewing
persona in persona.py, through any LLM backend (default: local Ollama - free and
private). It is STRESS-AWARE, but instead of asking a small model to make tool
calls (which they do unreliably), the APP pre-fetches Component D's voice stress
and Component B's HRV level and drops them into the prompt as a private note.
That is simpler, works with any model, and needs no tool loop.

This is the LLM stage of the voice pipeline:  STT -> [this] -> TTS.  It works in
text so it stays testable and provider-agnostic.
"""

import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent.parent))
from componentd.companion.backends import OllamaBackend
from componentd.companion.persona import (CRISIS_REPLY, SAFE_REDIRECT, SYSTEM_PROMPT,
                                   is_crisis)

# The private sensor note is labelled with this phrase; the builder and the
# output guard (`_scrub_sensor_note`) share it so they can never drift apart.
NOTE_LABEL = "Private app-sensor note"


def _scrub_sensor_note(text: str) -> str:
    """Defensive guard: strip any echoed copy of the private sensor note from a
    model reply BEFORE it is spoken to the student.

    The note carries stress numbers and clinical terms and is prompt-marked
    "do NOT read these", but a small local model can ignore that and parrot it
    back - and that reply goes straight to TTS. So we do not trust the model to
    withhold it; we remove it here, the same "never trust a small local model"
    stance as the crisis net. Handles the note's own nested parentheses (e.g.
    "(tense, 7.6/10, confidence 0.83)") via balanced-paren matching, drops any
    leftover line that still names the note, and finally strips raw score /
    confidence tokens in case a weak model *paraphrases* the readings back
    outside the note marker ("your stress is 9.4/10")."""
    out, i = [], 0
    start = "(" + NOTE_LABEL
    while (j := text.find(start, i)) != -1:
        out.append(text[i:j])
        depth, k = 0, j
        while k < len(text):                     # scan to the matching ')'
            if text[k] == "(":
                depth += 1
            elif text[k] == ")":
                depth -= 1
                if depth == 0:
                    k += 1
                    break
            k += 1
        i = k                                    # k == len(text) if never closed
    out.append(text[i:])
    cleaned = "".join(out)
    # belt-and-braces: drop any line that still names the note (reformatted echo)
    cleaned = "\n".join(ln for ln in cleaned.splitlines()
                        if NOTE_LABEL.lower() not in ln.lower())
    # ...and strip raw readings the model may paraphrase out of the marker: a
    # parenthetical carrying a score/confidence, then any bare score/confidence.
    cleaned = re.sub(r"\([^()]*(?:/\s*10|confidence)[^()]*\)", "", cleaned, flags=re.I)
    cleaned = re.sub(r"\b\d+(?:\.\d+)?\s*/\s*10\b", "", cleaned)
    cleaned = re.sub(r"\bconfidence\s+0?\.\d+\b", "", cleaned, flags=re.I)
    cleaned = re.sub(r"\s+([.,;:])", r"\1", cleaned)         # tidy stripped seams
    return re.sub(r"\s{2,}", " ", cleaned).strip()


class HealthCompanion:
    """One spoken check-in agent. Holds per-session history and calls the LLM a
    turn at a time. `get_voice`/`get_body` are callables the app injects to
    supply the app's own stress readings; `backend` is any LLMBackend."""

    def __init__(self, get_voice=None, get_body=None, backend=None,
                 max_history_turns: int = 20):
        self.get_voice = get_voice        # (session_id, phase) -> dict | None
        self.get_body = get_body          # (session_id, phase) -> str  | None
        self.backend = backend or OllamaBackend()
        self.max_history_turns = max_history_turns
        self.sessions: dict[str, list] = {}   # session_id -> [{"role","content"}]

    def _stress_note(self, session_id: str, phase: str) -> str:
        """Phrase the app's readings as a private note for the model - it must
        NOT read these to the student, only use them to judge how gently to
        speak. Returns '' when no readings exist yet."""
        bits = []
        if self.get_voice:
            v = self.get_voice(session_id, phase)
            if v:
                bits.append(f"voice stress {v.get('stress_level')} "
                            f"({v.get('stress_type')}, {v.get('stress_score')}/10, "
                            f"confidence {v.get('confidence')})")
        if self.get_body:
            b = self.get_body(session_id, phase)
            if b:
                bits.append(f"heart/body stress {b}")
        if not bits:
            return ""
        return (f"({NOTE_LABEL} - do NOT read these to the student; "
                "use them only to judge how gently to speak: "
                + "; ".join(bits) + ")")

    def reply(self, session_id: str, user_text: str, phase: str = "pre") -> str:
        """One conversational turn: the student said `user_text` in `phase`
        (pre/post); return the companion's spoken reply."""
        # Hard safety net FIRST - never rely on the model to catch crisis language.
        if is_crisis(user_text):
            return CRISIS_REPLY

        history = self.sessions.setdefault(session_id, [])
        history.append({"role": "user", "content": user_text})

        # Build the call: augment the CURRENT user turn with the stress note,
        # but keep history clean (the note is transient context, not dialogue).
        note = self._stress_note(session_id, phase)
        call_messages = list(history)
        if note:
            call_messages[-1] = {"role": "user",
                                 "content": f"{note}\n\n{user_text}"}

        reply = self.backend.chat(SYSTEM_PROMPT, call_messages)
        # Guard: never let an echoed private note reach the student / TTS. Store
        # the scrubbed reply so history stays clean and can't reinforce the echo.
        reply = _scrub_sensor_note(reply) or SAFE_REDIRECT
        history.append({"role": "assistant", "content": reply})
        self._trim(history)
        return reply

    def _trim(self, history: list) -> None:
        if len(history) > self.max_history_turns * 2:
            del history[: len(history) - self.max_history_turns * 2]

    def reset(self, session_id: str) -> None:
        self.sessions.pop(session_id, None)
