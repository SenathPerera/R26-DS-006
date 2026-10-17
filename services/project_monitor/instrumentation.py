from __future__ import annotations

import copy
import ipaddress
import threading
import time
from collections import Counter

from fastapi import Request
from fastapi.responses import JSONResponse


def is_loopback(host: str) -> bool:
    try:
        return ipaddress.ip_address(host).is_loopback
    except ValueError:
        return False


class ServiceMetrics:
    """Process-local aggregate evidence; never capture request bodies or URLs."""

    def __init__(self, service: str):
        self.service = service
        self.started_at = time.time()
        self.lock = threading.Lock()
        self.counts = Counter()
        self.values = {}
        self.operations = {}

    def count(self, name: str, amount: int = 1):
        with self.lock:
            self.counts[name] += amount

    def set(self, **values):
        with self.lock:
            self.values.update(values)

    def begin(self, operation: str):
        with self.lock:
            item = self.operations.setdefault(operation, {
                "requests": 0, "successes": 0, "failures": 0, "inflight": 0,
                "lastStartedAt": None, "lastFinishedAt": None,
                "lastSuccessAt": None, "lastFailureAt": None,
            })
            item["requests"] += 1
            item["inflight"] += 1
            item["lastStartedAt"] = time.time()
        return time.monotonic()

    def end(self, operation: str, started: float, success: bool):
        with self.lock:
            item = self.operations[operation]
            item["inflight"] -= 1
            item["successes" if success else "failures"] += 1
            item["lastFinishedAt"] = time.time()
            item["lastSuccessAt" if success else "lastFailureAt"] = time.time()
            item["lastDurationMs"] = round((time.monotonic() - started) * 1000)

    def snapshot(self):
        with self.lock:
            return copy.deepcopy({
                "schemaVersion": "mindsync-monitor-v1", "service": self.service,
                "startedAt": self.started_at, "observedAt": time.time(),
                "counts": dict(self.counts), "values": self.values,
                "operations": self.operations,
            })


def monitor_access_error(request):
    if not request.client or not is_loopback(request.client.host):
        return JSONResponse({"detail": "localhost-only"}, status_code=403)
    origin = request.headers.get("origin")
    if origin and origin != str(request.base_url).rstrip("/"):
        return JSONResponse({"detail": "same-origin-only"}, status_code=403)
    return None


def install_monitoring(app, metrics: ServiceMetrics, details=None, agents=None):
    @app.get("/monitor/status", include_in_schema=False)
    async def monitor_status(request: Request):
        denied = monitor_access_error(request)
        if denied is not None:
            return denied
        result = metrics.snapshot()
        if details is not None:
            result["details"] = details()
        return JSONResponse(result, headers={"Cache-Control": "no-store"})

    if agents is not None:
        @app.get("/monitor/agents", include_in_schema=False)
        async def monitor_agents(request: Request, session: str = ""):
            denied = monitor_access_error(request)
            if denied is not None:
                return denied
            # Public diagnostic labels only; never accept a raw session ID.
            import re
            if not re.fullmatch(r"[0-9a-f]{10}", session):
                return JSONResponse({"detail": "invalid-session-label"}, status_code=400)
            return JSONResponse(agents(session), headers={"Cache-Control": "no-store"})

    @app.middleware("http")
    async def record_http(request: Request, call_next):
        voice_routes = {"/infer", "/ambient-check", "/companion/voice-turn", "/companion/tts", "/full-session", "/compare"}
        operation = "http:" + request.url.path if metrics.service == "component-d" and request.url.path in voice_routes else None
        started = metrics.begin(operation) if operation else None
        success = False
        try:
            response = await call_next(request)
            success = response.status_code < 400
        finally:
            if operation:
                metrics.end(operation, started, success)
        route = request.scope.get("route")
        path = getattr(route, "path", "")
        if path and path not in {"/health", "/monitor/status", "/monitor/agents"}:
            # Route templates omit pairing tokens, session IDs and query strings.
            metrics.count("httpRequests")
            metrics.set(lastHttpAt=time.time())
            if response.status_code >= 400:
                metrics.count("httpFailures")
        return response
