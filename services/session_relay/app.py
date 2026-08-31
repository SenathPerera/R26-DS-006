from __future__ import annotations

import asyncio
import json
import os
from pathlib import Path
import time
import uuid

from fastapi import FastAPI, HTTPException, WebSocket, WebSocketDisconnect
from pydantic import BaseModel, ConfigDict, Field

from .session_store import (
    ActiveSessionExistsError,
    MobileAuthenticationError,
    PairingRejectedError,
    PreparedSession,
    SessionStore,
)


SCHEMA_VERSION = "mindsync-session-v1"
CODE_LIFETIME_SECONDS = float(os.getenv("SESSION_RELAY_CODE_LIFETIME_SECONDS", "300"))
DATA_DIRECTORY = Path(
    os.getenv("SESSION_RELAY_DATA_DIR", str(Path(__file__).parent / "data"))
)


class PreferredEnvironment(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    illumination: float = Field(ge=0, le=1)
    warmth: float = Field(ge=0, le=1)
    atmospheric_softness: float = Field(alias="atmosphericSoftness", ge=0, le=1)
    color_richness: float = Field(alias="colorRichness", ge=0, le=1)
    ambient_motion: float = Field(alias="ambientMotion", ge=0, le=1)


class CreateSessionRequest(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    request_id: str = Field(alias="requestId", min_length=1, max_length=128)
    participant_pseudonym: str = Field(
        alias="participantPseudonym", min_length=1, max_length=128
    )
    scene_id: str = Field(alias="sceneId", min_length=1, max_length=128)
    preferred_environment: PreferredEnvironment = Field(alias="preferredEnvironment")


class CreateSessionResponse(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    schema_version: str = Field(alias="schemaVersion")
    session_id: str = Field(alias="sessionId")
    pairing_code: str = Field(alias="pairingCode")
    expires_at: float = Field(alias="expiresAt")
    mobile_token: str = Field(alias="mobileToken")


class SessionChannels:
    def __init__(self) -> None:
        self.mobile: dict[str, WebSocket] = {}
        self.quest: dict[str, WebSocket] = {}
        self.pending_for_mobile: dict[str, list[dict]] = {}
        self.lock = asyncio.Lock()

    async def attach_mobile(self, session_id: str, socket: WebSocket) -> None:
        async with self.lock:
            old = self.mobile.get(session_id)
            if old is not None and old is not socket:
                await old.close(code=4409, reason="mobile-replaced")
            self.mobile[session_id] = socket
            pending = self.pending_for_mobile.pop(session_id, [])
        for message in pending:
            await socket.send_json(message)

    async def attach_quest(self, session_id: str, socket: WebSocket) -> None:
        async with self.lock:
            if session_id in self.quest:
                raise PairingRejectedError("quest-already-connected")
            self.quest[session_id] = socket

    async def detach(self, session_id: str, role: str, socket: WebSocket) -> None:
        async with self.lock:
            collection = self.mobile if role == "mobile" else self.quest
            if collection.get(session_id) is socket:
                collection.pop(session_id, None)

    async def send_to_mobile(self, session_id: str, message: dict) -> None:
        async with self.lock:
            socket = self.mobile.get(session_id)
            if socket is None:
                self.pending_for_mobile.setdefault(session_id, []).append(message)
                return
        await socket.send_json(message)

    async def send_to_quest(self, session_id: str, message: dict) -> bool:
        async with self.lock:
            socket = self.quest.get(session_id)
        if socket is None:
            return False
        await socket.send_json(message)
        return True


store = SessionStore(code_lifetime_seconds=CODE_LIFETIME_SECONDS)
channels = SessionChannels()
app = FastAPI(title="MindSync Session Relay", version="1.0.0")


@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok", "schemaVersion": SCHEMA_VERSION}


@app.post("/sessions", response_model=CreateSessionResponse)
async def create_session(request: CreateSessionRequest) -> CreateSessionResponse:
    environment = request.preferred_environment.model_dump(by_alias=True)
    try:
        session = store.create(
            request.request_id,
            request.participant_pseudonym,
            request.scene_id,
            environment,
        )
    except ActiveSessionExistsError as error:
        raise HTTPException(status_code=409, detail=str(error)) from error
    return CreateSessionResponse(
        schemaVersion=SCHEMA_VERSION,
        sessionId=session.session_id,
        pairingCode=session.pairing_code,
        expiresAt=session.expires_at_unix_seconds,
        mobileToken=session.mobile_token,
    )


@app.get("/sessions/{session_id}/visual-log")
async def get_visual_log(
    session_id: str,
    mobileToken: str,
) -> dict:
    try:
        store.authenticate_mobile(
            session_id,
            mobileToken,
            allow_ended=True,
        )
    except MobileAuthenticationError as error:
        raise HTTPException(status_code=401, detail=str(error)) from error

    path = DATA_DIRECTORY / f"{session_id}.jsonl"
    messages = await asyncio.to_thread(_read_messages, path)
    return {
        "schemaVersion": SCHEMA_VERSION,
        "sessionId": session_id,
        "messages": messages,
    }


@app.websocket("/realtime")
async def realtime(websocket: WebSocket) -> None:
    role = websocket.query_params.get("role", "quest")
    if role == "mobile":
        await _mobile_connection(websocket)
    elif role == "quest":
        await _quest_connection(websocket)
    else:
        await websocket.close(code=4400, reason="role-unsupported")


async def _mobile_connection(websocket: WebSocket) -> None:
    session_id = websocket.query_params.get("sessionId", "")
    mobile_token = websocket.query_params.get("mobileToken", "")
    try:
        store.authenticate_mobile(session_id, mobile_token)
    except MobileAuthenticationError:
        await websocket.close(code=4401, reason="mobile-authentication-failed")
        return

    await websocket.accept()
    await channels.attach_mobile(session_id, websocket)
    try:
        while True:
            message = await websocket.receive_json()
            if not _valid_mobile_command(message, session_id):
                await websocket.send_json(_error("mobile-message-invalid"))
                continue
            delivered = await channels.send_to_quest(session_id, message)
            if not delivered:
                await websocket.send_json(_error("quest-not-connected"))
    except WebSocketDisconnect:
        pass
    finally:
        await channels.detach(session_id, "mobile", websocket)


async def _quest_connection(websocket: WebSocket) -> None:
    await websocket.accept()
    session: PreparedSession | None = None
    try:
        request = await asyncio.wait_for(websocket.receive_json(), timeout=20)
        if not _valid_pairing_request(request):
            await websocket.send_json(_pairing_rejection("pairing-request-invalid"))
            await websocket.close(code=4400)
            return
        payload = request["payload"]
        try:
            session = store.pair(payload["pairingCode"], payload["questClientId"])
            await channels.attach_quest(session.session_id, websocket)
        except PairingRejectedError as error:
            await websocket.send_json(_pairing_rejection(str(error)))
            await websocket.close(code=4403)
            return

        await websocket.send_json(
            _envelope(
                "pairing_result",
                {
                    "accepted": True,
                    "sessionId": session.session_id,
                    "rejectionCode": None,
                },
            )
        )
        await websocket.send_json(_configuration(session))
        await websocket.send_json(_command(session.session_id, "start"))

        while True:
            message = await websocket.receive_json()
            if not _valid_quest_message(message, session.session_id):
                await websocket.send_json(_error("quest-message-invalid"))
                continue
            await _append_durable(session.session_id, message)
            await channels.send_to_mobile(session.session_id, message)
            if message["messageType"] == "visual_telemetry_batch":
                await websocket.send_json(
                    _envelope(
                        "delivery_ack",
                        {
                            "sessionId": session.session_id,
                            "acknowledgedMessageId": message["messageId"],
                        },
                    )
                )
            if (
                message["messageType"] == "quest_state"
                and message["payload"].get("phase") in {"completed", "aborted"}
            ):
                store.end(session.session_id)
    except (WebSocketDisconnect, asyncio.TimeoutError):
        pass
    finally:
        if session is not None:
            await channels.detach(session.session_id, "quest", websocket)


def _configuration(session: PreparedSession) -> dict:
    return _envelope(
        "session_configuration",
        {
            "sessionId": session.session_id,
            "participantPseudonym": session.participant_pseudonym,
            "sceneId": session.scene_id,
            "preferredEnvironment": session.preferred_environment,
        },
    )


def _command(session_id: str, command: str) -> dict:
    return _envelope("session_command", {"sessionId": session_id, "command": command})


def _pairing_rejection(code: str) -> dict:
    return _envelope(
        "pairing_result",
        {"accepted": False, "sessionId": None, "rejectionCode": code},
    )


def _error(code: str) -> dict:
    return _envelope("relay_error", {"code": code})


def _envelope(message_type: str, payload: dict) -> dict:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "messageId": str(uuid.uuid4()),
        "messageType": message_type,
        "payload": payload,
    }


def _valid_pairing_request(message: object) -> bool:
    if not _valid_envelope(message, "pairing_request"):
        return False
    payload = message["payload"]
    return (
        payload.get("clientRole") == "quest"
        and isinstance(payload.get("pairingCode"), str)
        and bool(payload["pairingCode"].strip())
        and isinstance(payload.get("questClientId"), str)
        and bool(payload["questClientId"].strip())
        and isinstance(payload.get("appVersion"), str)
        and bool(payload["appVersion"].strip())
    )


def _valid_mobile_command(message: object, session_id: str) -> bool:
    return (
        _valid_envelope(message, "session_command")
        and message["payload"].get("sessionId") == session_id
        and message["payload"].get("command")
        in {"pause", "resume", "stop", "emergency_stop"}
    )


def _valid_quest_message(message: object, session_id: str) -> bool:
    if not isinstance(message, dict) or message.get("messageType") not in {
        "quest_state",
        "visual_telemetry_batch",
    }:
        return False
    if not _valid_envelope(message, message["messageType"]):
        return False
    payload = message["payload"]
    if message["messageType"] == "quest_state":
        return payload.get("sessionId") == session_id
    events = payload.get("events")
    return isinstance(events, list) and bool(events) and all(
        isinstance(event, dict) and event.get("sessionId") == session_id
        for event in events
    )


def _valid_envelope(message: object, message_type: str) -> bool:
    return (
        isinstance(message, dict)
        and message.get("schemaVersion") == SCHEMA_VERSION
        and message.get("messageType") == message_type
        and isinstance(message.get("messageId"), str)
        and bool(message["messageId"].strip())
        and isinstance(message.get("payload"), dict)
    )


async def _append_durable(session_id: str, message: dict) -> None:
    DATA_DIRECTORY.mkdir(parents=True, exist_ok=True)
    path = DATA_DIRECTORY / f"{session_id}.jsonl"
    line = json.dumps(
        {"receivedAt": time.time(), "message": message}, separators=(",", ":")
    )
    await asyncio.to_thread(_append_line, path, line)


def _append_line(path: Path, line: str) -> None:
    with path.open("a", encoding="utf-8") as output:
        output.write(line)
        output.write("\n")
        output.flush()
        os.fsync(output.fileno())


def _read_messages(path: Path) -> list[dict]:
    if not path.exists():
        return []
    messages: list[dict] = []
    with path.open("r", encoding="utf-8") as source:
        for line in source:
            try:
                record = json.loads(line)
            except json.JSONDecodeError:
                continue
            message = record.get("message") if isinstance(record, dict) else None
            if isinstance(message, dict):
                messages.append(message)
    return messages
