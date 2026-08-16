# Health companion — panel study notes

**Job:** the AI voice that *talks with* the student before and after a session —
inviting them to say how they feel, and reflecting it back. It is the LLM stage
of the voice pipeline, and its questions are what produce the recordings
Component D scores.

**One-line answer for the panel:** *"The companion uses Motivational Interviewing
— open questions and reflective listening — which both build rapport and elicit
~30 seconds of natural emotional speech, which is exactly the input my stress
model needs. The conversation and the ML are designed together."*

---

## Where it sits — the voice pipeline

```
student speaks → STT (Whisper) → HealthCompanion (local LLM) → TTS (ElevenLabs) → student hears
                                        │
                    persona + history + a private "sensor note" the app builds
                    from Component D (voice stress) + Component B (HRV level)
```

This package is the **middle stage** (dialogue). Speech-in and speech-out wire in
at the edges (Whisper / ElevenLabs), so the dialogue engine stays text-based and
testable.

## Runs on a FREE, LOCAL model (privacy)

The backend is **provider-agnostic** (`backends.py`); the default is **Ollama** -
a local open model (Qwen 2.5 / Llama 3.1), free, no API key. For a *health*
companion this is a deliberate choice: the student's voice/emotion text **never
leaves the device**. Claude is available as an optional higher-quality backend.

## Why Motivational Interviewing (the research)

Wellness conversational agents are grounded in **Motivational Interviewing (MI)** —
open-ended questions, reflective listening, affirmations, respect for autonomy
(JMIR 2025 reviews of AI-delivered MI). MI's open questions do double duty here:
they build rapport **and** get the student *talking*, which gives Component D a
natural voice sample instead of a clipped yes/no.

## Stress-aware — but never clinical

The **app pre-fetches** the session's readings and drops them into the prompt as a
private "sensor note" (small local models are unreliable at tool-calling, so we
don't ask them to):
- Component D's voice stress (0–10 score + level + type)
- Component B's ordinal HRV level

The companion uses the note only to judge how gently to speak, **translates** it
into human language ("it sounds like a lot was on you today"), and **never reads
numbers or clinical terms** back to the student.

## Safety (built into the persona)

Wellness companion, **not a therapist**: no diagnosis, no medical advice, stays on
the check-in, and **escalates to real support on any sign of crisis**. See
`persona.py`.

## Safety net (belt and braces)

Beyond the persona's crisis instruction, the app checks each utterance for crisis
language **before** the model (`is_crisis` in `persona.py`) and returns a fixed,
safe `CRISIS_REPLY` — never trusting a small local model to catch it.

## Files
- `persona.py` — the MI system prompt, crisis keywords + `CRISIS_REPLY`
- `backends.py` — swappable LLM backends: `OllamaBackend` (default), `ClaudeBackend`, `EchoBackend`
- `dialogue.py` — `HealthCompanion`: per-session history, sensor-note injection
- tested offline by `tests/test_companion.py` (EchoBackend — no model, no key)

## Running it live
1. Install Ollama (https://ollama.com), then `ollama pull qwen2.5` and `ollama serve`.
2. Call `POST /companion/message {session_id, text}` (optional `?phase=pre|post`).

No API key, no cost, fully local. To use Claude instead, construct
`HealthCompanion(backend=ClaudeBackend())` (needs `anthropic` + `ANTHROPIC_API_KEY`).
