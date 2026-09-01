# Lyria Backend For Component E

This backend exposes the local HTTP and WebSocket endpoints that the shared Unity VR project can call to generate or stream personalized meditation audio through Google's Lyria models.

## What it does

- Accepts a prompt from Unity
- Calls `lyria-3-clip-preview` by default for clip generation
- Exposes a realtime capability probe and realtime bridge path
- Saves generated MP3 files and metadata under `services/lyria_backend/generated/`
- Returns generated audio as base64 so Unity can load it into an `AudioClip`

## Prerequisites

- Python 3.10 or newer
- A Google AI Studio API key with access to the Gemini API music models

## Setup

```powershell
cd services\lyria_backend
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

Copy `.env.example` to `.env`, or set the environment variable before starting the server:

```powershell
$env:GEMINI_API_KEY="your_api_key_here"
```

## Run

```powershell
cd services\lyria_backend
.venv\Scripts\Activate.ps1
uvicorn app:app --host 127.0.0.1 --port 8000 --reload
```

## Endpoints

- `GET /health`
- `GET /realtime-capability`
- `POST /generate-clip`
- `WS /live-music`

Example request body:

```json
{
  "prompt": "Create a 30-second instrumental meditation clip with soft piano, forest ambience, slow pacing, and gentle consonant harmony.",
  "model": "lyria-3-clip-preview",
  "requestId": "demo-request-001",
  "instrumentalOnly": true
}
```

## Notes

- The current Unity installer defaults to `http://127.0.0.1:8000` and `ws://127.0.0.1:8000/live-music`.
- That default works when Unity and the backend run on the same PC.
- For a headset or another device on the network, change the backend host in the Unity installer to the PC LAN IP.
- Generated files stay local in `services/lyria_backend/generated/` for debugging and presentation evidence.
