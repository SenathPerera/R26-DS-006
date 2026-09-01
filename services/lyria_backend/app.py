from __future__ import annotations

import asyncio
import base64
import json
import os
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Optional
from uuid import uuid4

from fastapi import FastAPI, HTTPException, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware
from dotenv import load_dotenv
from pydantic import BaseModel, Field

try:
    from google import genai
    from google.genai import types as genai_types
except ImportError as exc:  # pragma: no cover - import failure handled at runtime
    genai = None
    genai_types = None
    IMPORT_ERROR = exc
else:
    IMPORT_ERROR = None


APP_ROOT = Path(__file__).resolve().parent
GENERATED_DIR = APP_ROOT / "generated"
GENERATED_DIR.mkdir(parents=True, exist_ok=True)
load_dotenv(APP_ROOT / ".env")

DEFAULT_MODEL = "lyria-3-clip-preview"
SUPPORTED_MODELS = {"lyria-3-clip-preview", "lyria-3-pro-preview"}
DEFAULT_REALTIME_MODEL = "models/lyria-realtime-exp"
SUPPORTED_REALTIME_MODELS = {"models/lyria-realtime-exp", "lyria-realtime-exp"}
REALTIME_SAMPLE_RATE = 48000
REALTIME_CHANNELS = 2
REALTIME_FORMAT = "pcm_s16le"

app = FastAPI(title="Lyria Phase 1 Backend", version="0.1.0")
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)


class GenerateClipRequest(BaseModel):
    prompt: str = Field(..., min_length=12, max_length=4000)
    model: str = Field(default=DEFAULT_MODEL)
    requestId: Optional[str] = Field(default=None)
    instrumentalOnly: bool = Field(default=True)


class GenerateClipResponse(BaseModel):
    success: bool
    requestId: str
    model: str
    promptUsed: str
    lyrics: Optional[str] = None
    audioBase64: Optional[str] = None
    mimeType: str = "audio/mpeg"
    savedFileName: Optional[str] = None
    generatedAtUtc: str
    errorMessage: Optional[str] = None


class HealthResponse(BaseModel):
    status: str
    sdkReady: bool
    apiKeyConfigured: bool
    defaultModel: str
    realtimeModel: str


class RealtimeCapabilityResponse(BaseModel):
    available: bool
    model: str
    checkedAtUtc: str
    message: str


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(
        status="ok",
        sdkReady=genai is not None,
        apiKeyConfigured=bool(os.getenv("GEMINI_API_KEY")),
        defaultModel=DEFAULT_MODEL,
        realtimeModel=DEFAULT_REALTIME_MODEL,
    )


@app.get("/realtime-capability", response_model=RealtimeCapabilityResponse)
async def realtime_capability(model: str = DEFAULT_REALTIME_MODEL) -> RealtimeCapabilityResponse:
    checked_at_utc = _utc_now()
    normalized_model = _normalize_realtime_model(model)

    if genai is None or genai_types is None:
        return RealtimeCapabilityResponse(
            available=False,
            model=normalized_model,
            checkedAtUtc=checked_at_utc,
            message=f"google-genai is not installed or could not be imported: {IMPORT_ERROR}",
        )

    api_key = os.getenv("GEMINI_API_KEY")
    if not api_key:
        return RealtimeCapabilityResponse(
            available=False,
            model=normalized_model,
            checkedAtUtc=checked_at_utc,
            message="GEMINI_API_KEY is not configured.",
        )

    if normalized_model not in SUPPORTED_REALTIME_MODELS:
        return RealtimeCapabilityResponse(
            available=False,
            model=normalized_model,
            checkedAtUtc=checked_at_utc,
            message=f"Unsupported realtime model '{model}'.",
        )

    available, message = await _probe_realtime_capability(api_key, normalized_model)
    return RealtimeCapabilityResponse(
        available=available,
        model=normalized_model,
        checkedAtUtc=checked_at_utc,
        message=message,
    )


@app.post("/generate-clip", response_model=GenerateClipResponse)
def generate_clip(payload: GenerateClipRequest) -> GenerateClipResponse:
    if genai is None:
        raise HTTPException(
            status_code=500,
            detail=f"google-genai is not installed or could not be imported: {IMPORT_ERROR}",
        )

    api_key = os.getenv("GEMINI_API_KEY")
    if not api_key:
        raise HTTPException(status_code=500, detail="GEMINI_API_KEY is not configured.")

    model = payload.model.strip() if payload.model else DEFAULT_MODEL
    if model not in SUPPORTED_MODELS:
        raise HTTPException(status_code=400, detail=f"Unsupported model '{model}'.")

    prompt = _compose_prompt(payload.prompt, payload.instrumentalOnly)
    request_id = payload.requestId or uuid4().hex

    try:
        client = genai.Client(api_key=api_key)
        interaction = client.interactions.create(
            model=model,
            input=prompt,
        )
    except Exception as exc:  # pragma: no cover - depends on remote API
        return GenerateClipResponse(
            success=False,
            requestId=request_id,
            model=model,
            promptUsed=prompt,
            generatedAtUtc=_utc_now(),
            errorMessage=str(exc),
        )

    output_audio = getattr(interaction, "output_audio", None)
    output_text = getattr(interaction, "output_text", None)
    audio_b64 = getattr(output_audio, "data", None) if output_audio else None

    if not audio_b64:
        return GenerateClipResponse(
            success=False,
            requestId=request_id,
            model=model,
            promptUsed=prompt,
            lyrics=output_text,
            generatedAtUtc=_utc_now(),
            errorMessage="The Lyria response did not include audio data.",
        )

    file_stem = f"{datetime.now(timezone.utc):%Y%m%d_%H%M%S}_{request_id}"
    file_name = f"{file_stem}.mp3"
    audio_bytes = base64.b64decode(audio_b64)
    audio_path = GENERATED_DIR / file_name
    audio_path.write_bytes(audio_bytes)

    metadata = {
        "requestId": request_id,
        "model": model,
        "promptUsed": prompt,
        "lyrics": output_text,
        "generatedAtUtc": _utc_now(),
        "savedFileName": file_name,
        "byteLength": len(audio_bytes),
    }
    metadata_path = GENERATED_DIR / f"{file_stem}.json"
    metadata_path.write_text(json.dumps(metadata, indent=2), encoding="utf-8")

    return GenerateClipResponse(
        success=True,
        requestId=request_id,
        model=model,
        promptUsed=prompt,
        lyrics=output_text,
        audioBase64=audio_b64,
        savedFileName=file_name,
        generatedAtUtc=metadata["generatedAtUtc"],
    )


def _compose_prompt(prompt: str, instrumental_only: bool) -> str:
    normalized = " ".join(prompt.split())
    if instrumental_only and "instrumental only" not in normalized.lower():
        normalized = f"{normalized} Instrumental only."
    return normalized


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


@app.websocket("/live-music")
async def live_music(websocket: WebSocket, model: str = DEFAULT_REALTIME_MODEL) -> None:
    await websocket.accept()

    if genai is None or genai_types is None:
        await _send_ws_message(
            websocket,
            {
                "type": "error",
                "message": f"google-genai is not installed or could not be imported: {IMPORT_ERROR}",
            },
        )
        await websocket.close(code=1011)
        return

    api_key = os.getenv("GEMINI_API_KEY")
    if not api_key:
        await _send_ws_message(websocket, {"type": "error", "message": "GEMINI_API_KEY is not configured."})
        await websocket.close(code=1011)
        return

    realtime_model = _normalize_realtime_model(model)
    if realtime_model not in SUPPORTED_REALTIME_MODELS:
        await _send_ws_message(websocket, {"type": "error", "message": f"Unsupported realtime model '{model}'."})
        await websocket.close(code=1008)
        return

    client = genai.Client(api_key=api_key, http_options={"api_version": "v1beta"})

    try:
        async with client.aio.live.music.connect(model=realtime_model) as session:
            await _send_ws_message(
                websocket,
                {
                    "type": "connected",
                    "model": realtime_model,
                    "sampleRate": REALTIME_SAMPLE_RATE,
                    "channels": REALTIME_CHANNELS,
                    "format": REALTIME_FORMAT,
                    "message": "Lyria realtime session connected.",
                },
            )

            forward_task = asyncio.create_task(_forward_live_music_messages(session, websocket))
            try:
                while True:
                    raw_message = await websocket.receive_text()
                    payload = json.loads(raw_message)
                    await _handle_live_music_client_message(session, websocket, payload)
            except WebSocketDisconnect:
                pass
            finally:
                forward_task.cancel()
                await asyncio.gather(forward_task, return_exceptions=True)
    except Exception as exc:  # pragma: no cover - depends on remote API
        await _send_ws_message(websocket, {"type": "error", "message": str(exc)})
    finally:
        await client.aio.aclose()
        try:
            await websocket.close()
        except Exception:
            pass


async def _forward_live_music_messages(session: Any, websocket: WebSocket) -> None:
    async for message in session.receive():
        server_content = getattr(message, "server_content", None)
        if server_content is not None:
            audio_chunks = getattr(server_content, "audio_chunks", None) or []
            for chunk in audio_chunks:
                chunk_bytes = getattr(chunk, "data", None)
                if not chunk_bytes:
                    continue

                await _send_ws_message(
                    websocket,
                    {
                        "type": "audio",
                        "sampleRate": REALTIME_SAMPLE_RATE,
                        "channels": REALTIME_CHANNELS,
                        "format": REALTIME_FORMAT,
                        "data": base64.b64encode(chunk_bytes).decode("ascii"),
                    },
                )

        filtered_prompt = getattr(message, "filtered_prompt", None)
        if filtered_prompt is not None:
            await _send_ws_message(
                websocket,
                {
                    "type": "filtered_prompt",
                    "message": _extract_filtered_prompt_text(filtered_prompt),
                    "filteredReason": _safe_to_string(getattr(filtered_prompt, "filtered_reason", None)),
                },
            )

        warning_message = getattr(message, "warning", None)
        if warning_message:
            await _send_ws_message(websocket, {"type": "warning", "message": _safe_to_string(warning_message)})

        if getattr(message, "setup_complete", None) is not None:
            await _send_ws_message(websocket, {"type": "state", "state": "setup_complete", "message": "Realtime setup complete."})


async def _handle_live_music_client_message(session: Any, websocket: WebSocket, payload: dict[str, Any]) -> None:
    message_type = _safe_to_string(payload.get("type")).lower()

    if message_type == "ping":
        await _send_ws_message(websocket, {"type": "pong", "message": "ok"})
        return

    if message_type in {"sync", "prompts"}:
        prompts = payload.get("weightedPrompts") or []
        await session.set_weighted_prompts(prompts=_parse_weighted_prompts(prompts))
        if message_type == "prompts":
            await _send_ws_message(websocket, {"type": "state", "state": "prompts_updated", "message": "Weighted prompts updated."})

    if message_type in {"sync", "config"}:
        config_payload = payload.get("config") or {}
        await session.set_music_generation_config(config=_parse_live_music_config(config_payload))
        if message_type == "config":
            await _send_ws_message(websocket, {"type": "state", "state": "config_updated", "message": "Music generation config updated."})

    if message_type == "sync":
        if payload.get("autoPlay", True):
            await session.play()
            await _send_ws_message(websocket, {"type": "state", "state": "playing", "message": "Realtime playback started."})
        else:
            await _send_ws_message(websocket, {"type": "state", "state": "synced", "message": "Realtime prompts/config synced."})
        return

    if message_type == "play":
        await session.play()
        await _send_ws_message(websocket, {"type": "state", "state": "playing", "message": "Realtime playback started."})
        return

    if message_type == "pause":
        await session.pause()
        await _send_ws_message(websocket, {"type": "state", "state": "paused", "message": "Realtime playback paused."})
        return

    if message_type == "stop":
        await session.stop()
        await _send_ws_message(websocket, {"type": "state", "state": "stopped", "message": "Realtime playback stopped."})
        return

    if message_type == "reset_context":
        await session.reset_context()
        await _send_ws_message(websocket, {"type": "state", "state": "context_reset", "message": "Realtime context reset."})
        return

    if message_type not in {"sync", "prompts", "config"}:
        await _send_ws_message(websocket, {"type": "warning", "message": f"Unknown realtime message type '{message_type}'."})


def _parse_weighted_prompts(prompts_payload: list[dict[str, Any]]) -> list[Any]:
    prompts: list[Any] = []
    for item in prompts_payload:
        text = _safe_to_string(item.get("text")).strip()
        weight = float(item.get("weight", 0.0))
        if not text or abs(weight) < 1e-4:
            continue
        prompts.append(genai_types.WeightedPrompt(text=text, weight=weight))

    if not prompts:
        prompts.append(genai_types.WeightedPrompt(text="calm ambient meditation", weight=1.0))

    return prompts


def _parse_live_music_config(config_payload: dict[str, Any]) -> Any:
    config_kwargs: dict[str, Any] = {}

    if "temperature" in config_payload:
        config_kwargs["temperature"] = float(config_payload["temperature"])
    if "topK" in config_payload:
        config_kwargs["top_k"] = int(config_payload["topK"])
    if "seed" in config_payload:
        config_kwargs["seed"] = int(config_payload["seed"])
    if "guidance" in config_payload:
        config_kwargs["guidance"] = float(config_payload["guidance"])
    if "bpm" in config_payload:
        config_kwargs["bpm"] = int(config_payload["bpm"])
    if "density" in config_payload:
        config_kwargs["density"] = float(config_payload["density"])
    if "brightness" in config_payload:
        config_kwargs["brightness"] = float(config_payload["brightness"])
    if "muteBass" in config_payload:
        config_kwargs["mute_bass"] = bool(config_payload["muteBass"])
    if "muteDrums" in config_payload:
        config_kwargs["mute_drums"] = bool(config_payload["muteDrums"])
    if "onlyBassAndDrums" in config_payload:
        config_kwargs["only_bass_and_drums"] = bool(config_payload["onlyBassAndDrums"])

    scale_name = _safe_to_string(config_payload.get("scale"))
    if scale_name and hasattr(genai_types.Scale, scale_name):
        config_kwargs["scale"] = getattr(genai_types.Scale, scale_name)

    mode_name = _safe_to_string(config_payload.get("musicGenerationMode"))
    if mode_name and hasattr(genai_types.MusicGenerationMode, mode_name):
        config_kwargs["music_generation_mode"] = getattr(genai_types.MusicGenerationMode, mode_name)

    return genai_types.LiveMusicGenerationConfig(**config_kwargs)


async def _send_ws_message(websocket: WebSocket, payload: dict[str, Any]) -> None:
    await websocket.send_text(json.dumps(payload))


async def _probe_realtime_capability(api_key: str, model: str) -> tuple[bool, str]:
    client = genai.Client(api_key=api_key, http_options={"api_version": "v1beta"})
    try:
        await asyncio.wait_for(_open_and_close_realtime_session(client, model), timeout=12.0)
        return True, f"Realtime model '{model}' accepted the session request."
    except Exception as exc:  # pragma: no cover - depends on remote API
        return False, _summarize_realtime_probe_error(exc)
    finally:
        await client.aio.aclose()


async def _open_and_close_realtime_session(client: Any, model: str) -> None:
    async with client.aio.live.music.connect(model=model):
        return


def _summarize_realtime_probe_error(exc: Exception) -> str:
    message = _safe_to_string(exc).strip() or exc.__class__.__name__
    lowered = message.lower()

    if "http 404" in lowered or "status code 404" in lowered:
        return (
            "Google accepted the request path locally, but the current API key/project does not appear "
            "to have working Lyria Realtime access yet (upstream returned HTTP 404)."
        )

    if "http 403" in lowered or "forbidden" in lowered:
        return (
            "The current API key/project is authenticated, but Google rejected Lyria Realtime access "
            "(HTTP 403 Forbidden)."
        )

    if "timed out" in lowered or "timeout" in lowered:
        return "The realtime capability probe timed out before Google opened the session."

    return message


def _normalize_realtime_model(model: str) -> str:
    normalized = (model or DEFAULT_REALTIME_MODEL).strip()
    if normalized == "lyria-realtime-exp":
        return "models/lyria-realtime-exp"
    return normalized


def _extract_filtered_prompt_text(filtered_prompt: Any) -> str:
    text = _safe_to_string(getattr(filtered_prompt, "text", None))
    if text:
        return text

    prompt_value = getattr(filtered_prompt, "prompt", None)
    return _safe_to_string(prompt_value)


def _safe_to_string(value: Any) -> str:
    if value is None:
        return ""
    return str(value)
