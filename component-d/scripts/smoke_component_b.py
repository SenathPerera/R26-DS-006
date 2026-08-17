"""Live smoke test for the Component B poll integration (Phase 4, Layer 4).

Everything in the poll client is unit-tested offline; this is the ONE check that
needs Senath's B actually running. It performs a real GET B:/stress/latest, prints
B's raw StressPrediction and Component D's mapped BodyReading, and confirms the
503-not-ready path degrades to voice-only. It writes nothing and starts no session.

Run (from component-d/):
    COMPONENT_B_URL=http://<B-host>:8000 .venv/bin/python scripts/smoke_component_b.py

Exit 0 = a usable reading was mapped; 2 = B reachable but not ready yet (503, still
a PASS for the contract); 1 = B unreachable / bad contract.
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "src"))

from componentd.config import COMPONENT_B_URL, COMPONENT_B_TIMEOUT
from componentd.component_b_client import map_stress_prediction, poll_latest


def main() -> int:
    base = COMPONENT_B_URL.rstrip("/")
    url = f"{base}/stress/latest"
    print(f"Polling Component B at {url} (timeout {COMPONENT_B_TIMEOUT}s)\n")

    import httpx
    try:
        resp = httpx.get(url, timeout=COMPONENT_B_TIMEOUT)
    except Exception as e:
        print(f"UNREACHABLE: {type(e).__name__}: {e}")
        print("→ In a real session Component D falls back to VOICE-ONLY. "
              "Is B running and is COMPONENT_B_URL correct?")
        return 1

    print(f"HTTP {resp.status_code}")
    if resp.status_code == 503:
        print("B has no full ~45s window yet (503).")
        print("→ Component D correctly degrades to VOICE-ONLY here. Contract OK; "
              "re-run once B has been streaming PPG for ~45s.")
        return 2
    if resp.status_code != 200:
        print(f"Unexpected status; body: {resp.text[:300]}")
        return 1

    payload = resp.json()
    print("\nB StressPrediction (raw):")
    print(json.dumps(payload, indent=2))

    try:
        reading = map_stress_prediction(payload)
    except ValueError as e:
        print(f"\nCONTRACT MISMATCH: could not map B's payload: {e}")
        return 1

    print("\nComponent D mapped BodyReading:")
    print(f"  level      = {reading.level}   (feeds Layer 4)")
    print(f"  confidence = {reading.confidence}   (gate CONF_MIN=0.4)")
    print(f"  mode       = {reading.mode}   label={reading.label!r}")

    # Sanity: the poll_latest path (200 -> map) agrees with the direct map.
    live = poll_latest("smoke", "pre", base_url=base, timeout=COMPONENT_B_TIMEOUT)
    assert live is not None and live.level == reading.level
    print("\nPASS: live B reading mapped and ready for Layer 4.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
