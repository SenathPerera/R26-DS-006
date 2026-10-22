"""Opt-in live PCM smoke/soak test. The API key stays in the backend, never here."""
import argparse
import asyncio
import base64
import json
import math
import struct
import time
from uuid import uuid4

import websockets


async def inspect_stream(url, seconds, steer=False):
    chunks = byte_count = samples = 0
    sum_squares = peak = 0.0
    first_audio = None
    max_gap = 0.0
    previous_audio = None
    ack = False
    tagged_pcm = False
    action_id = uuid4().hex
    steered = False
    started = time.monotonic()
    async with websockets.connect(url, open_timeout=15, max_size=8 * 1024 * 1024) as socket:
        config = {"bpm": 70, "density": 0.25, "brightness": 0.3, "guidance": 3,
                  "scale": "C_MAJOR_A_MINOR", "muteDrums": True, "musicGenerationMode": "QUALITY"}
        await socket.send(json.dumps({"type": "sync", "autoPlay": True,
            "weightedPrompts": [{"text": "gentle instrumental meditation, soft piano and warm sustained pads, no sudden changes", "weight": 1}],
            "config": config}))
        while first_audio is None or time.monotonic() - first_audio < seconds:
            remaining = 30 if first_audio is None else min(30, seconds - (time.monotonic() - first_audio))
            try:
                message = json.loads(await asyncio.wait_for(socket.recv(), max(0.1, remaining)))
            except asyncio.TimeoutError:
                if first_audio is not None and time.monotonic() - first_audio >= seconds:
                    break
                raise RuntimeError("No PCM received within 30 seconds") from None
            if message.get("type") == "error":
                # Provider error details can include request URLs. Do not print them.
                raise RuntimeError("Bridge reported a realtime error; inspect backend logs privately")
            if message.get("type") == "state" and message.get("requestId") == action_id:
                ack = True
            if message.get("type") != "audio":
                continue
            if (message.get("sampleRate"), message.get("channels"), message.get("format")) != (48000, 2, "pcm_s16le"):
                raise RuntimeError("Unexpected PCM format")
            pcm = base64.b64decode(message["data"], validate=True)
            tagged_pcm = tagged_pcm or message.get("requestId") == action_id
            if len(pcm) % 4:
                raise RuntimeError("Incomplete stereo PCM frame")
            now = time.monotonic()
            if first_audio is None:
                first_audio = now
            if previous_audio is not None:
                max_gap = max(max_gap, now - previous_audio)
            previous_audio = now
            chunks += 1
            byte_count += len(pcm)
            for (sample,) in struct.iter_unpack("<h", pcm):
                value = sample / 32768.0
                sum_squares += value * value
                peak = max(peak, abs(value))
                samples += 1
            if steer and not steered and now - first_audio >= seconds / 2:
                config["density"] = 0.28
                await socket.send(json.dumps({"type": "config", "requestId": action_id, "config": config}))
                steered = True
        await socket.send(json.dumps({"type": "stop"}))
    if samples == 0 or peak <= 0:
        raise RuntimeError("Stream contained no nonzero PCM")
    if steer and (not ack or not tagged_pcm):
        raise RuntimeError("Steering acknowledgement and matching PCM were not both received")
    return {"status": "pass", "observedSeconds": round(time.monotonic() - first_audio, 2),
            "firstAudioLatencySeconds": round(first_audio - started, 2), "chunks": chunks,
            "pcmBytes": byte_count, "generatedAudioSeconds": round(samples / 96000, 2),
            "rms": round(math.sqrt(sum_squares / samples), 6), "peak": round(peak, 6),
            "maximumChunkGapSeconds": round(max_gap, 2), "steeringAcknowledged": ack if steer else None,
            "steeredPcmReceived": tagged_pcm if steer else None,
            "sampleRate": 48000, "channels": 2,
            "note": "PCM transport verified, not Unity speaker output or perceived musical continuity."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="ws://127.0.0.1:8002/live-music")
    parser.add_argument("--seconds", type=int, default=15, help="Live generation duration, 5-1200 seconds; consumes quota")
    parser.add_argument("--steer", action="store_true", help="Test a small density update halfway through")
    args = parser.parse_args()
    if not 5 <= args.seconds <= 1200:
        parser.error("--seconds must be 5-1200")
    try:
        result = asyncio.run(inspect_stream(args.url, args.seconds, args.steer))
    except Exception as exc:
        print(json.dumps({"status": "fail", "errorType": type(exc).__name__,
                          "message": "Live stream test failed. Verify backend access and inspect its logs privately."}))
        return 1
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
