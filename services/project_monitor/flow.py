"""Interpret evidence conservatively. Reachability is not end-to-end readiness."""
import time


def age(timestamp, now):
    return max(0, round(now - timestamp, 1)) if isinstance(timestamp, (int, float)) else None


def build_flow(services, selected=None, now=None):
    now = time.time() if now is None else now
    stages, alerts = [], []
    by_id = {item["id"]: item for item in services}
    relay = by_id.get("relay", {})
    sessions = (relay.get("diagnostics") or {}).get("details", {}).get("sessions", [])
    session = next((s for s in sessions if s["label"] == selected), None) if selected else None
    if session is None and not selected:
        session = next((s for s in sessions if s.get("questConnected") or s.get("mobileConnected")), None)
        session = session or (sessions[0] if sessions else {})
    session = session or {}

    def add(key, group, title, status, detail, evidence=None):
        stages.append(dict(id=key, group=group, title=title, status=status,
                           detail=detail, evidence=evidence or {}))

    def client(role):
        report = session.get(role, {})
        seconds = age(report.get("receivedAt"), now)
        if not relay.get("reachable") or not report:
            return {}, "unknown"
        if not session.get(role + "Connected") or seconds is None or seconds > 15:
            return {}, "stale"
        return report.get("values", {}), None

    mobile, mobile_problem = client("mobile")
    quest, quest_problem = client("quest")
    b = by_id.get("component-b", {})
    bd = b.get("diagnostics") or {}
    bv, bc = bd.get("values", {}), bd.get("counts", {})
    d = by_id.get("component-d", {})
    lyria = by_id.get("lyria", {})

    add("preferences", "01 / Prepare", "Preferences & session", "complete" if session.get("preferencesConfigured") else "waiting",
        "Audio preferences received by relay." if session.get("preferencesConfigured") else "Prepare a session in the mobile app with audio preferences.",
        {"session": session.get("label", "Not reported"), "study condition": session.get("studyCondition", "Not reported")})
    sensor_age = mobile.get("telemetryAgeSeconds", -1)
    sensor_status = mobile_problem or ("waiting" if not mobile.get("wearableConnected") or sensor_age < 0 else "active" if sensor_age <= 10 else "stale")
    add("wearable", "02 / Sense", "A / Wearable to phone", sensor_status,
        "BLE telemetry observed." if sensor_status == "active" else "Connect the wearable. Phone reports begin after session preparation.",
        {"BLE connected": mobile.get("wearableConnected"), "telemetry age (s)": sensor_age if sensor_age >= 0 else None})
    ingest_status = mobile_problem or ("active" if mobile.get("componentBState") == "connected" and mobile.get("framesAccepted", 0) > 0 else "waiting")
    if not b.get("reachable"):
        ingest_status = "blocked"
    elif ingest_status == "active" and (age(bv.get("lastFrameAt"), now) is None or age(bv["lastFrameAt"], now) > 90):
        ingest_status = "stale"
    add("ingest", "02 / Sense", "Phone to B / Raw PPG", ingest_status,
        "Mobile acknowledgements plus B receive freshness. B counters are process-wide, not participant-matched.",
        {"mobile socket": mobile.get("componentBState"), "frames sent": mobile.get("framesSent"),
         "frames acknowledged": mobile.get("framesAccepted"), "B frame age (s)": age(bv.get("lastFrameAt"), now)})
    prediction_age = age(bv.get("lastPredictionAt"), now)
    stress_status = "blocked" if not b.get("reachable") or bv.get("modelReady") is False else "unknown" if not bd else "waiting" if prediction_age is None else "active" if prediction_age <= 90 else "stale"
    add("stress", "03 / Infer", "B / Stress inference", stress_status,
        "Wait for a valid physiology window; HTTP health alone does not confirm model inference.",
        {"model loaded": bv.get("modelReady"), "predictions since restart": bc.get("predictions", 0),
         "prediction age (s)": prediction_age, "temperature source": bv.get("temperatureSource")})
    dd = d.get("diagnostics") or {}
    layers = (d.get("health") or {}).get("layers", {})
    voice_op = dd.get("operations", {}).get("http:/infer", {})
    add("voice", "03 / Infer", "D / Voice check-in", "blocked" if not d.get("reachable") or mobile.get("voiceError") else "active" if mobile.get("voiceBusy") else "waiting" if layers.get("layer2_fusion") is not True else "ready",
        "Parallel pre/post-session path. Phone state and service-wide inference HTTP outcomes are distinct evidence.",
        {"phone voice stage": mobile.get("voiceStage"), "phone busy": mobile.get("voiceBusy"),
         "fusion ready": layers.get("layer2_fusion"), "inference HTTP successes": voice_op.get("successes"),
         "last inference age (s)": age(voice_op.get("lastSuccessAt"), now),
         "HTTP requests": dd.get("counts", {}).get("httpRequests"),
         "HTTP failures": dd.get("counts", {}).get("httpFailures", 0)})
    poll_b = dd.get("operations", {}).get("pollB", {})
    poll_success, poll_failure = poll_b.get("lastSuccessAt", 0) or 0, poll_b.get("lastFailureAt", 0) or 0
    add("crossmodal", "03 / Infer", "B to D / Cross-modal check", "blocked" if not d.get("reachable") or not b.get("reachable") else "active" if poll_b.get("inflight", 0) else "waiting" if not poll_b else "blocked" if poll_failure > poll_success else "complete",
        "D polls B during a requested voice check-in, not continuously. Success means a reading was returned, not that confidence was sufficient.",
        {"successful reads": poll_b.get("successes"), "failed or unavailable reads": poll_b.get("failures"),
         "last successful read age (s)": age(poll_b.get("lastSuccessAt"), now)})
    add("pairing", "04 / Prepare VR", "Relay / Mobile + Unity", "blocked" if not relay.get("reachable") else "ready" if session.get("mobileConnected") and session.get("questConnected") else "waiting",
        "Authenticated session sockets; configuration travels from mobile through relay to Unity.",
        {"mobile socket": session.get("mobileConnected", False), "Unity socket": session.get("questConnected", False),
         "configuration sent": bool(session.get("configurationSentAt"))})
    add("configuration", "04 / Prepare VR", "Unity / Apply preferences", quest_problem or ("blocked" if quest.get("audioPreferenceError") else "ready" if quest.get("audioPrepared") and quest.get("visualInitialized") else "waiting"),
        "Both scene initialization and audio preparation must be reported by Unity.",
        {"visual initialized": quest.get("visualInitialized"), "audio prepared": quest.get("audioPrepared"), "preference error": quest.get("audioPreferenceError")})
    generation_status = quest_problem or ("ready" if quest.get("generatedClipReady") else "active" if quest.get("generating") else "blocked" if not lyria.get("reachable") or quest.get("audioGenerationFailures", 0) else "waiting")
    add("generation", "04 / Prepare VR", "E / Generate & load music", generation_status,
        "Unity confirms a generated clip is loaded. A backend response alone is not playback readiness.",
        {"generating": quest.get("generating"), "generated clip loaded": quest.get("generatedClipReady"),
         "failures": quest.get("audioGenerationFailures"), "backend reachable from Unity": quest.get("audioBackendReachable"),
         "key configured (Unity probe)": quest.get("audioBackendConfigured"), "SDK ready (Unity probe)": quest.get("audioBackendSdkReady")})
    phase = str(quest.get("phase", session.get("phase", "unknown"))).lower()
    running = phase in {"acclimatization", "adaptive", "stabilization"}
    terminal = phase in {"completed", "aborted"}
    add("start", "05 / Run", "Session start gate", quest_problem or ("blocked" if running and not quest.get("generatedClipReady") else "complete" if terminal else "active" if running else "waiting"),
        "VR and audio readiness are observed separately. This monitor does not control the start gate.",
        {"phase": phase, "start sent by relay": bool(session.get("startSentAt")), "audio running": quest.get("audioRunning")})
    physiology_age = quest.get("physiologyAgeSeconds", -1)
    physiology_status = quest_problem or ("waiting" if physiology_age < 0 else "active" if physiology_age <= 90 else "stale")
    add("delivery", "05 / Run", "B stream to Unity", physiology_status,
        "Audio-side receiver confirms accepted Component B windows; no signal values are exposed.",
        {"B stream socket": quest.get("physiologySocket"), "audio accepted windows": quest.get("physiologyPayloads"),
         "visual forwarded windows": quest.get("visualPhysiologyWindows"), "window age (s)": physiology_age if physiology_age >= 0 else None})
    add("visual", "05 / Run", "C / Adaptive scene", quest_problem or ("complete" if terminal else "active" if phase == "adaptive" else "ready" if running else "waiting"),
        "Visual policy and session phase reported by Unity. This does not verify rendered headset output.",
        {"policy": quest.get("visualPolicy"), "phase": phase, "telemetry batches": session.get("telemetryBatches")})
    add("audio", "05 / Run", "E / Adaptive audio", quest_problem or ("complete" if terminal else "active" if quest.get("audioRunning") and quest.get("audioPlaying") and not quest.get("audioMuted") else "waiting"),
        "Audio source playback and online update count. Listen on the device to verify audible output.",
        {"policy": quest.get("audioPolicy"), "source playing": quest.get("audioPlaying"),
         "muted": quest.get("audioMuted"), "online updates": quest.get("audioOnlineUpdates")})
    add("results", "06 / Complete", "Results & delivery", "complete" if session.get("logAcknowledgedAt") else "waiting",
        "Relay visual-log acknowledgement is verified independently of the phone's account-sync status.",
        {"session ended": terminal, "visual log acknowledged": bool(session.get("logAcknowledgedAt")),
         "pending Unity telemetry": quest.get("pendingTelemetry"), "phone cloud sync": mobile.get("cloudSyncState")})

    for service in services:
        if not service.get("reachable"):
            alerts.append({"severity": "error", "title": service["name"] + " is unreachable", "detail": "Start the service or verify its configured port: " + service["url"]})
        elif not service.get("diagnostics"):
            alerts.append({"severity": "warning", "title": service["name"] + " has no diagnostics", "detail": "Restart it with the updated code. Health is reachable but detailed activity is not reported."})
    if running and quest and not quest.get("generatedClipReady"):
        alerts.append({"severity": "error", "title": "VR running before audio is ready", "detail": "Pause the test and check generation. The existing relay start delay does not guarantee audio readiness."})
    if session and quest_problem:
        alerts.append({"severity": "warning", "title": "Unity report " + quest_problem, "detail": "Recompile/rebuild Unity, pair the session, and keep it running. Reports expire after 15 seconds."})
    if running and physiology_status in {"waiting", "stale", "unknown"}:
        alerts.append({"severity": "warning", "title": "Live physiology not confirmed in Unity", "detail": "Check the wearable, B ingest and the Unity Component B stream connection."})
    expected = {"ContextualBandit": "PersonalizedBandit", "RuleBasedAdaptive": "RuleBasedAdaptive", "StaticPersonalized": "StaticPersonalized"}
    if quest and quest.get("visualPolicy") in expected and quest.get("audioPolicy") != expected[quest["visualPolicy"]]:
        alerts.append({"severity": "warning", "title": "Controller modes differ", "detail": "Visual: " + quest["visualPolicy"] + "; audio: " + str(quest.get("audioPolicy"))})
    return {"stages": stages, "alerts": alerts, "session": session.get("label"),
            "sessions": [{"label": s["label"], "phase": s.get("phase", "unknown")} for s in sessions]}
