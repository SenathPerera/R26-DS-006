"""FastAPI backend.

The ONLY place inference runs. Mobile relays raw PPG; Quest and the
web dashboard subscribe to predictions.
"""

import asyncio
import logging

from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import ValidationError

from componentb.signal.ppg import clean_rr, ppg_to_rr

from server.engine import new_stream, unavailable_reason
from server.schemas.messages import PPGBatch, StressPrediction
from server.state import latest
from server.temperature import TemperatureResolver

app = FastAPI(title="Component B — Stress Inference")
log = logging.getLogger(__name__)

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


@app.get("/stress/latest", response_model=StressPrediction,
         responses={503: {"description": "No prediction produced yet"}})
async def stress_latest():
    """Most recent prediction, for consumers that poll instead of subscribe.

    Returns 503 until the first window is complete — at STEP_BEATS=5 and
    WINDOW_BEATS=60 that is roughly the first 45 s of a session. Callers
    must handle BOTH `stress.mode` values: "point" carries `stress.level`,
    "band" carries `stress.level_low`/`level_high`
    (docs/ARCHITECTURE.md §6).
    """
    if latest.is_empty:
        return JSONResponse(
            status_code=503,
            content={"detail": "no prediction yet — waiting for the first "
                               "full window"},
        )
    return latest.get()


@app.websocket("/ingest")
async def ingest(ws: WebSocket):
    """Mobile app sends raw PPG batches here.

    One inference engine per connection: the causal state (EWMA levels,
    z-score statistics, rolling buffers) belongs to one wearer's session
    and must not be shared between them.
    """
    await ws.accept()
    try:
        # TensorFlow/XGBoost artifact loading is synchronous and can take
        # several seconds. Keep it off the ASGI event loop so prediction
        # subscribers and additional handshakes remain responsive.
        engine = await asyncio.to_thread(new_stream)
        # Per connection for the same reason as `engine`: the resolver
        # caches this wearer's last measured temperature and anchors the
        # synthetic curve's phase to when this session started. A shared
        # instance would leak one wearer's skin temperature into another's
        # window during the 60 s cache period.
        temperature_resolver = TemperatureResolver()
        if engine is None:
            await ws.send_json({"status": "model_unavailable",
                                "detail": unavailable_reason()})

        while True:
            try:
                payload = await ws.receive_json()
                batch = PPGBatch.model_validate(payload)
            except WebSocketDisconnect:
                raise
            except (ValidationError, ValueError, TypeError) as exc:
                detail = exc.errors() if isinstance(exc, ValidationError) else str(exc)
                await ws.send_json({"status": "invalid_batch", "detail": detail})
                continue

            temperature = temperature_resolver.resolve(
                batch.temperature,
                batch.timestamp,
            )
            await ws.send_json({
                "status": "accepted",
                "timestamp": batch.timestamp,
                "samples": len(batch.ppg),
                "temperature": temperature.value_c,
                "temperature_source": temperature.source,
            })

            if temperature.value_c is None:
                await ws.send_json({
                    "status": "waiting_for_temperature",
                    "detail": "temperature is unavailable and synthetic fallback is disabled",
                })
                continue

            try:
                rr, ts, _ = ppg_to_rr(batch.ppg, batch.sample_rate)
            except Exception:
                log.exception("PPG processing failed for frame %.3f",
                              batch.timestamp)
                await ws.send_json({
                    "status": "processing_error",
                    "detail": "PPG beat detection failed for this frame",
                })
                continue
            if rr is None:
                continue                      # too few beats in this batch
            # `ok` marks which beats survived filtering; it becomes
            # signalQuality on the wire, so it travels with the beats
            rr, ts, ok = clean_rr(rr, ts)
            if engine is None:
                continue                      # beats detected, nothing to run

            for beat, offset, usable in zip(rr, ts, ok):
                # buffering is per beat; inference only at step boundaries
                if engine.observe(beat, temperature.value_c,
                                  ts=batch.timestamp + float(offset),
                                  ok=bool(usable)):
                    out = engine.predict()
                    await broadcast(StressPrediction(**out).model_dump())
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
    latest.set(payload)
    dead = []
    for ws in subscribers:
        try:
            await ws.send_json(payload)
        except Exception:
            dead.append(ws)
    for ws in dead:
        subscribers.discard(ws)
