"""Layer 3: pre/post session comparison.

Statistics, no ML. The design principle is HONESTY: a wellness app must
not celebrate a 0.2-point "improvement" that is really measurement
noise. Changes smaller than the noise band report as no_reliable_change.
"""

# Score changes (0-10 scale) below this are treated as measurement noise.
NOISE_FLOOR = 0.75
# Below this confidence the comparison refuses to interpret at all.
MIN_CONFIDENCE = 0.15


def _magnitude(delta_abs: float) -> str:
    if delta_abs < 1.5:
        return "slight"
    if delta_abs < 3.0:
        return "moderate"
    return "strong"


def compare_scores(pre: dict, post: dict) -> dict:
    """Compare two Layer 2 reports taken before and after a session.

    pre/post: {"stress_score": 0-10, "confidence": 0-1, ...}
    """
    delta = round(post["stress_score"] - pre["stress_score"], 2)
    mean_confidence = (pre["confidence"] + post["confidence"]) / 2

    # KEY LINE: low model confidence widens the noise band, so uncertain
    # measurements need a bigger change before we call it real.
    noise_band = NOISE_FLOOR * (2.0 - mean_confidence)
    reliable = abs(delta) >= noise_band and mean_confidence >= MIN_CONFIDENCE

    if not reliable:
        direction = "no_reliable_change"
    elif delta < 0:
        direction = "improved"      # stress went DOWN
    else:
        direction = "worsened"

    return {
        "pre_stress": pre["stress_score"],
        "post_stress": post["stress_score"],
        "delta": delta,
        "direction": direction,
        "improved": reliable and delta < 0,
        "magnitude": _magnitude(abs(delta)) if reliable else "none",
        "reliable": reliable,
        "mean_confidence": round(mean_confidence, 3),
    }
