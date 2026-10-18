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

Configure the API key and the PC's current LAN host in the local, Git-ignored
`services/lyria_backend/.env` file:

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

## Continuous Session Music

The Unity installer now defaults `Prefer Realtime Music` to enabled. The existing
personalized initial clip is still prepared as the startup/fallback source.
Once an active session starts, the realtime bridge automatically connects to
`models/lyria-realtime-exp`, buffers six seconds of stereo PCM, and crossfades
from that clip into the continuous stream over eight seconds. The fixed scene
ambience remains a separate source at unchanged pitch. Music is not regenerated
as a repeating MP3 on a one-minute timer.

Session pause, completion, abort, headset suspension, and emergency mute stop
automatic streaming. Playback resumes through a new buffered connection when
the session can run again. Manual Stop/Pause remains respected until an explicit
restart or a new participant session. Disable `Prefer Realtime Music` on the
installer to retain clip-only operation.

Transport errors, stalled PCM, and significant underruns cancel pending RL
outcomes, return to cached music, and retry with bounded backoff up to 30 seconds.
The fallback fade defaults to 2.5 seconds; buffered outgoing PCM is retained for
that fade. Recovery waits for clean physiology before attributing rewards.
No implementation can reconstruct missing music after the playback buffer has
already emptied, so prolonged outages can still be audible.

Both music sources use equal-power crossfading. For an acknowledged generation
revision, the PCM queue also blends up to one second of its buffered tail with
the new revision, preserving a two-second queue reserve. That blend is inside
the audio agent's existing reservation and does not start another learning turn.
BPM/key changes still require a provider context reset; neither this blend nor
the revision tag proves beat matching, identical loudness, or the exact acoustic
moment the provider adopted a new setting. Headset listening tests remain needed.

Backend `/monitor/status` includes aggregate `realtimeConnections`,
`realtimeAudioChunks`, `realtimeAudioBytes`, `realtimeFailures`, and
`lastRealtimeAudioAt`. It never records the API key, prompts, or audio content.

## Live Verification

From the repository root, with the updated backend running:

```powershell
.\services\lyria_backend\.venv\Scripts\python.exe .\tools\check-lyria-realtime.py --seconds 15 --steer
```

This makes real generation requests and consumes your account's quota. It checks
nonzero PCM, stereo format, chunk gaps, and a density update's acknowledgement
plus matching revision-tagged PCM. It does not play, store, or print audio or
credentials, and does not establish actual Unity speaker output.

For an explicit 20-minute transport soak test:

```powershell
.\services\lyria_backend\.venv\Scripts\python.exe .\tools\check-lyria-realtime.py --seconds 1200 --steer
```

Run this separately from a headset session so another stream does not consume
unnecessary quota. For headset verification, start the participant session and
listen through startup, live changes, pause/resume, a backend interruption, and
recovery. Monitor `BufferedSeconds`, dropped samples, underruns, and RL cancellation
reasons. Restart the backend and rebuild/redeploy Unity to use these changes;
there is no firmware, mobile APK, or database migration for this integration.
