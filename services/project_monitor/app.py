from __future__ import annotations

import asyncio
import contextlib
import json
import os
from pathlib import Path
import re
import socket
import time
from urllib.error import HTTPError, URLError
from urllib.request import build_opener, ProxyHandler, HTTPRedirectHandler

from fastapi import FastAPI, Request
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from starlette.middleware.trustedhost import TrustedHostMiddleware

from .flow import build_flow
from .instrumentation import is_loopback

ROOT = Path(__file__).resolve().parents[2]
STATIC = Path(__file__).parent / "static"
SERVICES = [
    ("component-b", "B / Stress inference", "MONITOR_B_PORT", 8000),
    ("component-d", "D / Voice assessment", "MONITOR_D_PORT", 8010),
    ("relay", "Session relay", "MONITOR_RELAY_PORT", 8080),
    ("lyria", "E / Lyria generation", "MONITOR_LYRIA_PORT", 8002),
]


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def read_json(url, max_bytes=256_000):
    # Never use system HTTP proxies or follow redirects to external services.
    opener = build_opener(ProxyHandler({}), NoRedirect())
    try:
        with opener.open(url, timeout=1.2) as response:
            content = response.read(max_bytes + 1)
            if len(content) > max_bytes:
                return None
            value = json.loads(content)
            return value if isinstance(value, dict) else None
    except (HTTPError, URLError, OSError, ValueError):
        return None


def probe(spec):
    service_id, name, env, default = spec
    port = int(os.getenv(env, str(default)))
    if not 1 <= port <= 65535:
        raise ValueError("Invalid monitor port for " + service_id)
    url = f"http://127.0.0.1:{port}"
    started = time.monotonic()
    health = read_json(url + "/health")
    latency = round((time.monotonic() - started) * 1000)
    diagnostics = read_json(url + "/monitor/status") if health else None
    if diagnostics and (diagnostics.get("schemaVersion") != "mindsync-monitor-v1" or diagnostics.get("service") != service_id):
        diagnostics = None
    # Health responses are explicitly allowlisted; do not expose arbitrary server output.
    allowed = {"status", "sdkReady", "apiKeyConfigured", "layers", "schemaVersion"}
    return {"id": service_id, "name": name, "url": url, "reachable": health is not None and health.get("status") == "ok",
            "latencyMs": latency, "health": {k: v for k, v in (health or {}).items() if k in allowed},
            "diagnostics": diagnostics}


def network_configuration():
    entries = []
    generated_config = ROOT / "MindSync-VR/react-native-app/src/config/generatedEnvironment.ts"
    generated_text = generated_config.read_text(encoding="utf-8-sig") if generated_config.is_file() else ""
    pilot_match = re.search(r'[\x27\"]?pilotBackendHost[\x27\"]?\s*:\s*[\x27\"]([^\x27\"]*)', generated_text)
    pilot_backend_host = pilot_match.group(1) if pilot_match else ""
    candidates = [(generated_config, r'[\x27\"]?pilotBackendHost[\x27\"]?\s*:\s*[\x27\"]([^\x27\"]+)'),
                  (ROOT / "MindSync-VR/react-native-app/src/config/environment.ts", r"developmentBackendHost\s*=\s*[\x27\"]([^\x27\"]+)")]
    candidates += [(p, r"(?:backendHost|serverHost|host):\s*(\S+)") for p in (ROOT / "unity/Assets").glob("**/*LocalDevelopmentNetwork*.asset")]
    for path, pattern in candidates:
        if path.is_file():
            match = re.search(pattern, path.read_text(encoding="utf-8-sig"))
            if match:
                entries.append({"source": str(path.relative_to(ROOT)).replace("\\", "/"), "host": match.group(1)})
    try:
        addresses = sorted({a[4][0] for a in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET)})
    except OSError:
        addresses = []
    return {"configuredHosts": entries, "localAddresses": addresses,
            "mobileReleaseBackendConfigured": bool(pilot_backend_host)}


class Collector:
    def __init__(self):
        self.services = []
        self.checked_at = None
        self.network = {}

    async def refresh(self):
        results = await asyncio.gather(*(asyncio.to_thread(probe, spec) for spec in SERVICES), return_exceptions=True)
        self.services = [result if isinstance(result, dict) else {
            "id": spec[0], "name": spec[1], "url": "Invalid port configuration", "reachable": False,
            "diagnostics": None, "health": {}, "latencyMs": None,
        } for spec, result in zip(SERVICES, results)]
        self.checked_at = time.time()

    async def loop(self):
        while True:
            await self.refresh()
            await asyncio.sleep(3)

    def snapshot(self, selected=None):
        flow = build_flow(self.services, selected)
        hosts = self.network.get("configuredHosts", [])
        if len({h["host"] for h in hosts}) > 1:
            flow["alerts"].append({"severity": "warning", "title": "Backend hosts differ in source configuration",
                                   "detail": "Check the network panel. Source settings may not match installed app builds; no settings have been changed."})
        if not self.network.get("mobileReleaseBackendConfigured"):
            flow["alerts"].append({"severity": "warning", "title": "Mobile release backend is not configured",
                                   "detail": "Set PILOT_BACKEND_HOST in react-native-app/.env, regenerate the environment, rebuild the APK, and restart this monitor."})
        addresses = self.network.get("localAddresses", [])
        if addresses and any(re.fullmatch(r"\d+\.\d+\.\d+\.\d+", h["host"]) and h["host"] not in addresses and h["host"] != "127.0.0.1" for h in hosts):
            flow["alerts"].append({"severity": "warning", "title": "Configured backend IP is not a current PC address",
                                   "detail": "Check the LAN address in mobile and Unity settings. The listed source configuration is not proof of installed-device configuration."})
        return {"checkedAt": self.checked_at, "services": self.services, "network": self.network, **flow}


collector = Collector()


@contextlib.asynccontextmanager
async def lifespan(app):
    collector.network = await asyncio.to_thread(network_configuration)
    await collector.refresh()
    task = asyncio.create_task(collector.loop())
    try:
        yield
    finally:
        task.cancel()
        with contextlib.suppress(asyncio.CancelledError):
            await task


app = FastAPI(title="MindSync Project Monitor", lifespan=lifespan, docs_url=None, redoc_url=None, openapi_url=None)
app.add_middleware(TrustedHostMiddleware, allowed_hosts=["localhost", "127.0.0.1", "[::1]"])


@app.middleware("http")
async def local_only(request: Request, call_next):
    if not request.client or not is_loopback(request.client.host):
        return JSONResponse({"detail": "localhost-only"}, status_code=403)
    origin = request.headers.get("origin")
    if origin and origin != str(request.base_url).rstrip("/"):
        return JSONResponse({"detail": "same-origin-only"}, status_code=403)
    response = await call_next(request)
    response.headers["Cache-Control"] = "no-store"
    response.headers["X-Content-Type-Options"] = "nosniff"
    response.headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'none'"
    return response


@app.get("/api/snapshot")
async def snapshot(session: str | None = None):
    return collector.snapshot(session)


@app.get("/api/agents")
async def agents(session: str = ""):
    if not re.fullmatch(r"[0-9a-f]{10}", session):
        return JSONResponse({"detail": "invalid-session-label"}, status_code=400)
    relay = next((s for s in collector.services if s["id"] == "relay"), {})
    if not relay.get("reachable"):
        return JSONResponse({"detail": "relay-unavailable"}, status_code=503)
    # URL was constructed from the configured localhost port, never from user input.
    result = await asyncio.to_thread(read_json, relay["url"] + "/monitor/agents?session=" + session, 1_048_576)
    if (not result or result.get("schemaVersion") != "mindsync-agents-v1"
            or result.get("session") != session):
        return JSONResponse({"detail": "agent-diagnostics-unavailable"}, status_code=503)
    return result


@app.get("/")
async def index():
    return FileResponse(STATIC / "index.html")


app.mount("/static", StaticFiles(directory=STATIC), name="static")
