"""Sanitized diagnostic side channel, separate from research/session logs."""
from collections import OrderedDict
import copy
import hashlib
import math
import re
import time

from .agent_diagnostics import AgentHistory, SCHEMA, HISTORY_SECONDS

MOBILE_FIELDS = {
    "wearableConnected": bool, "telemetryAgeSeconds": float,
    "componentBState": str, "framesSent": int, "framesAccepted": int,
    "rawSamplesBuffered": int, "cloudSyncState": str,
    "voiceStage": str, "voiceBusy": bool, "voiceError": bool,
}
QUEST_FIELDS = {
    "phase": str, "visualPolicy": str, "visualInitialized": bool,
    "audioPolicy": str, "audioPrepared": bool, "audioRunning": bool,
    "audioPlaying": bool, "audioMuted": bool,
    "generatedClipReady": bool, "generating": bool, "audioBackendReachable": bool,
    "audioBackendConfigured": bool, "audioBackendSdkReady": bool,
    "audioGenerationFailures": int, "audioOnlineUpdates": int,
    "physiologyPayloads": int, "physiologyAgeSeconds": float,
    "physiologySocket": str, "visualPhysiologyWindows": int,
    "audioPreferenceError": bool, "pendingTelemetry": int,
}


class RelayDiagnostics:
    def __init__(self):
        self.sessions = OrderedDict()
        self.agents = {}

    def touch(self, session_id, **fields):
        item = self.sessions.setdefault(session_id, {
            "label": hashlib.sha256(session_id.encode()).hexdigest()[:10],
            "createdAt": time.time(), "phase": "prepared", "telemetryBatches": 0,
        })
        item.update(fields)
        self.sessions.move_to_end(session_id)
        while len(self.sessions) > 32:
            removed, _ = self.sessions.popitem(last=False)
            self.agents.pop(removed, None)
        return item

    def accept(self, message, session_id, role):
        if not isinstance(message, dict) or message.get("messageType") != "client_diagnostics":
            return False
        # Consume invalid diagnostics without forwarding them or logging payloads.
        if message.get("schemaVersion") != "mindsync-monitor-v1":
            return True
        payload = message.get("payload")
        if not isinstance(payload, dict) or payload.get("sessionId") != session_id:
            return True
        values = payload.get("values")
        agents = None
        if role == "quest" and isinstance(values, dict):
            values = dict(values)
            agents = values.pop("agents", None)
        allowed = MOBILE_FIELDS if role == "mobile" else QUEST_FIELDS
        if not isinstance(values, dict) or not values or not set(values) <= set(allowed):
            return True
        for key, value in values.items():
            expected = allowed[key]
            if expected is float:
                if type(value) not in (int, float) or not -1 <= value <= 1e12 or not math.isfinite(value):
                    return True
            elif type(value) is not expected:
                return True
            elif expected is str and not re.fullmatch(r"[A-Za-z0-9_-]{1,48}", value):
                return True
            elif expected is int and not 0 <= value <= 10**12:
                return True
        self.touch(session_id, **{role: {"receivedAt": time.time(), "values": dict(values)}})
        if agents is not None:
            self.agents.setdefault(session_id, AgentHistory()).append(agents, time.time())
        return True

    def agent_snapshot(self, label, channels):
        now = time.time()
        session_id = next((sid for sid, row in self.sessions.items() if row["label"] == label), None)
        history = self.agents.get(session_id)
        samples = history.snapshot(now) if history else []
        connected = session_id in channels.quest
        state = ("unreported" if not samples else "disconnected" if not connected
                 else "stale" if now - samples[-1]["receivedAt"] > 15 else "live")
        return {"schemaVersion": SCHEMA, "session": label, "observedAt": now,
                "state": state, "retentionSeconds": HISTORY_SECONDS, "samples": samples}

    def snapshot(self, channels):
        for history in self.agents.values():
            history.prune(time.time())
        rows = []
        for session_id, item in reversed(self.sessions.items()):
            row = copy.deepcopy(item)
            row["mobileConnected"] = session_id in channels.mobile
            row["questConnected"] = session_id in channels.quest
            rows.append(row)
        return {"sessions": rows, "mobileConnections": len(channels.mobile),
                "questConnections": len(channels.quest)}
