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

Copy `.env.example` to `.env`. Configure the API key and the PC's current LAN
host in that one file:

```powershell
GEMINI_API_KEY=your_api_key_here
MINDSYNC_DEVELOPMENT_HOST=192.168.1.100
```

## Run

```powershell
cd services\lyria_backend
.venv\Scripts\Activate.ps1
uvicorn app:app --host 0.0.0.0 --port 8002 --reload
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

- Unity derives all local Component B, Lyria, and session-relay endpoints from
  `MINDSYNC_DEVELOPMENT_HOST`. When Wi-Fi changes, update only that value and
  run `Adaptive Meditation > Sync Local Development Host From Environment`.
  Android builds also synchronize this value automatically before building.
- For a headset or another device on the network, set the host to the PC LAN IP;
  do not use `localhost`.
- Generated files stay local in `services/lyria_backend/generated/` for debugging and presentation evidence.
