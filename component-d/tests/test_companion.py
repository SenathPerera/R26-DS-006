"""Offline tests for the health-companion dialogue engine. Uses EchoBackend (a
fake LLM that records what it was asked and returns a scripted reply) - no model,
no network, no API key."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.companion import (CRISIS_REPLY, SYSTEM_PROMPT, EchoBackend,
                           HealthCompanion, is_crisis)
from componentd.companion.dialogue import _scrub_sensor_note
from componentd.companion.persona import SAFE_REDIRECT

# A realistic note echo as observed live from a small model - note (with its own
# nested parens) parroted verbatim ahead of the real reflection.
_LEAKED = ("(Private app-sensor note - do NOT read these to the student; use "
           "them only to judge how gently to speak: voice stress high (tense, "
           "7.6/10, confidence 0.83); heart/body stress elevated) "
           "It sounds like a lot is on you today.")


class RecordingBackend(EchoBackend):
    """EchoBackend that captures every (system, messages) it was called with."""
    def __init__(self, reply="How are you arriving today?"):
        self.calls = []
        super().__init__(lambda system, messages: self._record(system, messages, reply))

    def _record(self, system, messages, reply):
        self.calls.append({"system": system, "messages": list(messages)})
        return reply


def test_persona_has_mi_and_safety():
    p = SYSTEM_PROMPT.lower()
    assert "open-ended" in p and "reflective listening" in p     # MI
    assert "not a therapist" in p                                 # boundary
    assert "self-harm" in p and "crisis" in p                     # escalation


def test_crisis_is_caught_before_the_model():
    be = RecordingBackend()
    hc = HealthCompanion(backend=be)
    out = hc.reply("s1", "honestly I want to die")
    assert out == CRISIS_REPLY
    assert be.calls == []          # the model was never called on a crisis turn
    assert is_crisis("I can't go on") and not is_crisis("I'm just tired")


def test_simple_reply_passes_persona_and_user_text():
    be = RecordingBackend("How are you arriving today?")
    hc = HealthCompanion(backend=be)
    out = hc.reply("s1", "hi")
    assert out == "How are you arriving today?"
    assert be.calls[0]["system"] == SYSTEM_PROMPT
    assert be.calls[0]["messages"][-1]["content"] == "hi"


def test_stress_readings_injected_as_private_note():
    be = RecordingBackend()

    def get_voice(sid, phase):
        return {"stress_score": 6.9, "stress_level": "moderate",
                "stress_type": "activated", "confidence": 0.75}

    hc = HealthCompanion(get_voice=get_voice,
                         get_body=lambda s, p: "high", backend=be)
    hc.reply("s1", "i'm okay i guess", phase="pre")

    sent = be.calls[0]["messages"][-1]["content"]
    assert "do NOT read these" in sent          # the private note is present
    assert "moderate" in sent and "high" in sent
    assert "i'm okay i guess" in sent           # user text still there
    # history stays clean (note is transient, not stored)
    assert hc.sessions["s1"][0]["content"] == "i'm okay i guess"


def test_no_readings_no_note():
    be = RecordingBackend()
    hc = HealthCompanion(backend=be)             # no providers
    hc.reply("s1", "hello")
    assert be.calls[0]["messages"][-1]["content"] == "hello"   # no note prefixed


def test_history_persists_across_turns():
    be = RecordingBackend()
    hc = HealthCompanion(backend=be)
    hc.reply("s1", "hello")
    hc.reply("s1", "i'm stressed")
    msgs = be.calls[1]["messages"]
    assert msgs[0]["content"] == "hello"
    assert any(m["role"] == "assistant" for m in msgs)


# --- output guard: a weak model must never leak the private sensor note -------

def test_scrub_removes_echoed_note_keeps_reply():
    out = _scrub_sensor_note(_LEAKED)
    assert out == "It sounds like a lot is on you today."   # real reply survives
    assert "Private app-sensor note" not in out             # label gone
    assert "7.6" not in out and "0.83" not in out           # numbers gone
    assert "elevated" not in out                            # clinical term gone


def test_scrub_leaves_clean_reply_untouched():
    clean = "It sounds like today has felt heavy. What's weighing on you most?"
    assert _scrub_sensor_note(clean) == clean


def test_scrub_strips_paraphrased_readings_outside_the_note():
    # A weak model restated the readings in its own words (no note marker).
    para = "You seem really tense today, your stress is high (9.4/10, confidence 0.83)."
    out = _scrub_sensor_note(para)
    assert "9.4" not in out and "0.83" not in out and "confidence" not in out.lower()
    assert "10" not in out                                  # the /10 score is gone
    assert out.startswith("You seem really tense today")    # human words survive
    # bare (un-parenthesised) score form too
    assert "8/10" not in _scrub_sensor_note("Your stress is 8/10 right now.")


def test_engine_scrubs_leaked_note_from_reply_and_history():
    # A backend that parrots the exact turn it was handed (note + user text).
    leaker = EchoBackend(lambda system, messages: messages[-1]["content"])
    hc = HealthCompanion(
        get_voice=lambda s, p: {"stress_score": 7.6, "stress_level": "high",
                                "stress_type": "tense", "confidence": 0.83},
        get_body=lambda s, p: "elevated", backend=leaker)
    out = hc.reply("s1", "i'm exhausted", phase="pre")
    assert "Private app-sensor note" not in out and "7.6" not in out
    assert out == "i'm exhausted"                           # only the student's words
    # the stored assistant turn is scrubbed too (can't reinforce the echo)
    assert "Private app-sensor note" not in hc.sessions["s1"][-1]["content"]


def test_reply_that_is_only_the_note_falls_back_safely():
    note_only = _LEAKED[:_LEAKED.index(") ") + 1]            # drop the real reflection
    hc = HealthCompanion(backend=EchoBackend(lambda s, m: note_only))
    out = hc.reply("s1", "hi", phase="pre")
    assert out == SAFE_REDIRECT                             # non-empty, safe
    assert "Private app-sensor note" not in out
