"""FastAPI backend.

The ONLY place inference runs. Mobile relays raw PPG; Quest and the
web dashboard subscribe to predictions.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware

app = FastAPI(title="Component B — Stress Inference")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],          # tighten before any public deployment
    allow_methods=["*"],
    allow_headers=["*"],
)

subscribers: set[WebSocket] = set()


@app.get("/health")
async def health():
    return {"status": "ok"}


@app.websocket("/ingest")
async def ingest(ws: WebSocket):
    """Mobile app sends raw PPG batches here."""
    await ws.accept()
    try:
        while True:
            msg = await ws.receive_json()
            # TODO: ppg_to_rr -> clean_rr -> StreamingInference.push
            # then broadcast(result)
            _ = msg
    except WebSocketDisconnect:
        pass


@app.websocket("/stream")
async def stream(ws: WebSocket):
    """Quest 2 and the website subscribe here."""
    await ws.accept()
    subscribers.add(ws)
    try:
        while True:
            await ws.receive_text()      # keepalive
    except WebSocketDisconnect:
        subscribers.discard(ws)


async def broadcast(payload: dict):
    dead = []
    for ws in subscribers:
        try:
            await ws.send_json(payload)
        except Exception:
            dead.append(ws)
    for ws in dead:
        subscribers.discard(ws)
