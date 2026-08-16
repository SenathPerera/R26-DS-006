# CogniVoice — Component D demo UI

The PP2 demo/integration front end: a calm, production-grade web app that runs
the **whole Component D pipeline live** (no VR headset). It is a browser stand-in
for the Quest client — it calls the same server APIs the Quest app will.

## Flow

`Room check (L1)` → `Before check-in (L2 + companion)` → `Calm moment` →
`After check-in (L2 + companion)` → `Your insight (L3–5)` → **`The research`**
(the PP1 → PP2 story for the panel; deep-link: open `/#research`).

## Design

Plain, token-driven CSS in `src/index.css` (no Tailwind utilities). Two accents
carry the thesis: **teal = the sensing/ML**, **clay = the human companion**.
Light + dark both supported (toggle in the top bar).

| File | Role |
|---|---|
| `src/api.js` | typed client for every server endpoint |
| `src/media.js` | mic recorder → WAV, browser TTS, browser STT |
| `src/ui.jsx` | TopBar, Rail, Circumplex, StressCard, Companion, recorder |
| `src/steps.jsx` | the six screens |
| `src/App.jsx` | session state + orchestration (calls the live API) |

## Run it (three processes)

```bash
# 1) Companion LLM — local Ollama (free, private)
ollama serve                       # if not already running
ollama pull qwen2.5:0.5b           # small demo model (use `qwen2.5` 7B for the panel)

# 2) Component D API on :8001 — point the companion at your pulled model
cd ..                              # repo root
OLLAMA_MODEL=qwen2.5:0.5b .venv/bin/uvicorn api_server:app --host 127.0.0.1 --port 8001

# 3) This UI on :5173
cd frontend && npm run dev
```

Then open **http://localhost:5173** (Chrome/Edge for speech-to-text; you can
always type instead). The top-bar chips turn green when the model + companion are
reachable. First `/infer` loads the emotion2vec encoder (cached after first use).

> `OLLAMA_MODEL` / `OLLAMA_HOST` env vars pick the companion model/host — the demo
> runs the tiny local model; the panel build can point at the full `qwen2.5`.
