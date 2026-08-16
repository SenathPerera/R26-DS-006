"""Layer 4: cross-modal validation - voice stress vs HRV (Component B).

Two independent signals agreeing is far stronger evidence than either
alone. Rule-based by design: the agreement logic is explainable, and no
labelled dataset of voice-vs-HRV mismatches exists to train on.

Component boundary: Component B PRODUCES the HRV values (RMSSD); this
layer CONSUMES them. All cross-modal logic belongs to Component D.
"""


class HRVProvider:
    """Interface Component B implements. RMSSD in milliseconds."""

    def get_rmssd(self, session_id: str, phase: str) -> float | None:
        raise NotImplementedError


class StoredHRVProvider(HRVProvider):
    """Production provider: Component B pushes values through the API
    endpoint /session-update; this class stores and serves them. B may send
    either its ORDINAL stress level (preferred - its own WESAD prediction) or
    raw RMSSD; both are stored per (session_id, phase)."""

    def __init__(self):
        self.store: dict[tuple[str, str], float] = {}      # raw RMSSD
        self.levels: dict[tuple[str, str], str] = {}       # B's ordinal level
        self.level_conf: dict[tuple[str, str], float] = {} # B's confidence 0-1

    def push(self, session_id: str, phase: str, rmssd: float):
        self.store[(session_id, phase)] = rmssd

    def get_rmssd(self, session_id: str, phase: str) -> float | None:
        return self.store.get((session_id, phase))

    def push_level(self, session_id: str, phase: str, level: str,
                   confidence: float | None = None):
        # Normalise B's class name (e.g. "relaxed" -> "no") on the way in.
        self.levels[(session_id, phase)] = normalize_level(level)
        if confidence is not None:
            self.level_conf[(session_id, phase)] = float(confidence)

    def get_level(self, session_id: str, phase: str) -> str | None:
        return self.levels.get((session_id, phase))

    def get_level_confidence(self, session_id: str, phase: str) -> float | None:
        return self.level_conf.get((session_id, phase))


class MockHRVProvider(HRVProvider):
    """Demo/test stand-in: mildly stressed before, recovered after."""

    def __init__(self, pre_rmssd: float = 35.0, post_rmssd: float = 55.0):
        self.values = {"pre": pre_rmssd, "post": post_rmssd}

    def get_rmssd(self, session_id: str, phase: str) -> float | None:
        return self.values.get(phase)


def hrv_stress_score(rmssd: float) -> float:
    """Map RMSSD to a 0-10 stress proxy. Physiology: LOWER heart-rate
    variability = MORE stressed. Typical adult resting range spans
    roughly 20 ms (stressed) to 80 ms (relaxed)."""
    score01 = (80.0 - rmssd) / 60.0
    return round(min(1.0, max(0.0, score01)) * 10, 2)


# Voice and body stress levels within this distance (0-10 scale) agree.
LEVEL_TOLERANCE = 3.0

# Below this EFFECTIVE confidence (min of voice + body at the compared point), a
# voice/body disagreement is NOT asserted as a genuine cognitive-physiological
# mismatch - it is reported as unresolved and DEFERRED to Component B (HRV), the
# more reliable arousal signal. This stops Component D's known arousal-collapse
# uncertainty (low |valence|) from masquerading as a research finding.
CONF_MIN = 0.4

# Component B (WESAD) names its lowest level "relaxed"; Component D calls the same
# state "no". Normalise B's class names to D's so cross-modal compares like with
# like - and so an unmapped label never silently becomes the 5.0 fallback.
_LEVEL_ALIASES = {"relaxed": "no", "relax": "no", "none": "no"}


def normalize_level(level: str) -> str:
    """Map any accepted class name onto Component D's four levels."""
    lv = str(level).strip().lower()
    return _LEVEL_ALIASES.get(lv, lv)


# Each ordinal level -> a representative 0-10 score (bin midpoint) so B's level
# compares to the voice score.
STRESS_LEVEL_TO_SCORE = {"no": 1.25, "mild": 3.75, "moderate": 6.25, "high": 8.75}


def stress_level_to_score(level: str) -> float:
    """Ordinal level (relaxed/no/mild/moderate/high) -> 0-10 score."""
    return STRESS_LEVEL_TO_SCORE.get(normalize_level(level), 5.0)


def _compare(voice_pre: float, voice_post: float,
             body_pre: float, body_post: float, body_extra: dict,
             voice_conf: tuple[float, float] = (1.0, 1.0),
             body_conf: tuple[float, float] = (1.0, 1.0)) -> dict:
    """Core cross-modal comparison of two 0-10 stress signals (voice vs body):
    checks both time points AND the trends, names any disagreement, and gates
    that verdict on CONFIDENCE.

    voice_conf / body_conf are (pre, post) certainties in [0,1]. A disagreement
    is only asserted as a genuine cognitive-physiological mismatch when the
    signals driving it are confident; otherwise it is reported as unresolved and
    deferred to Component B (HRV). This is what keeps Component D's arousal-
    collapse uncertainty from producing false 'mismatch' findings."""
    voice_trend = voice_post - voice_pre
    body_trend = body_post - body_pre
    # Trends agree if both move the same direction, or both are ~flat.
    trends_agree = (voice_trend * body_trend > 0) or \
                   (abs(voice_trend) < 1.0 and abs(body_trend) < 1.0)

    pre_agree = abs(voice_pre - body_pre) <= LEVEL_TOLERANCE
    post_agree = abs(voice_post - body_post) <= LEVEL_TOLERANCE

    # Mismatch typing. Trend disagreement is classified FIRST because it
    # carries the most meaning (direction of change), then level gaps.
    raw = None
    if not trends_agree:
        raw = "vocal_masking" if voice_trend < body_trend \
            else "cognitive_persistence"        # body recovered, voice not
    elif not pre_agree and not post_agree:
        raw = "baseline_divergence"             # signals disagree throughout
    elif not post_agree:
        raw = "outcome_divergence"              # agreed before, not after
    elif not pre_agree:
        raw = "baseline_divergence"

    # Which confidence governs THIS disagreement? Trend mismatches depend on all
    # four points; a single-point divergence on just that point.
    vcp, vcpo = voice_conf
    bcp, bcpo = body_conf
    if raw in ("vocal_masking", "cognitive_persistence", "baseline_divergence"):
        governing = min(vcp, vcpo, bcp, bcpo)
    elif raw == "outcome_divergence":
        governing = min(vcpo, bcpo)
    else:
        governing = 1.0

    low_conf = raw is not None and governing < CONF_MIN
    mismatch = None if low_conf else raw        # suppress uncertain mismatches

    closeness = 1.0 - (abs(voice_pre - body_pre) + abs(voice_post - body_post)) / 20.0
    result = {
        "validated": mismatch is None and not low_conf,
        "agreement": round(max(0.0, closeness), 3),
        "mismatch_type": mismatch,
        "voice": {"pre": voice_pre, "post": voice_post,
                  "trend": round(voice_trend, 2),
                  "confidence": {"pre": round(vcp, 3), "post": round(vcpo, 3)}},
        "body": {"pre": body_pre, "post": body_post,
                 "trend": round(body_trend, 2),
                 "confidence": {"pre": round(bcp, 3), "post": round(bcpo, 3)},
                 **body_extra},
    }
    if low_conf:
        # Disagreement exists but we cannot trust it -> don't cry "mismatch".
        result["low_confidence"] = True
        result["deferred_to"] = "body"          # HRV is the reliable arousal axis
        result["unresolved_mismatch"] = raw     # what WOULD have been flagged
        result["note"] = ("voice signal too uncertain to confirm a cognitive-"
                          "physiological mismatch; deferring to HRV")
    return result


def validate_crossmodal(voice_pre: float, voice_post: float,
                        hrv_pre_rmssd: float, hrv_post_rmssd: float,
                        voice_conf: tuple[float, float] = (1.0, 1.0),
                        body_conf: tuple[float, float] = (1.0, 1.0)) -> dict:
    """Raw-HRV path: Component B (or the mock) provides RMSSD in ms, which we
    convert to a 0-10 body-stress proxy, then compare to the voice."""
    body_pre, body_post = hrv_stress_score(hrv_pre_rmssd), hrv_stress_score(hrv_post_rmssd)
    return _compare(voice_pre, voice_post, body_pre, body_post,
                    {"rmssd_pre": hrv_pre_rmssd, "rmssd_post": hrv_post_rmssd},
                    voice_conf, body_conf)


def validate_crossmodal_levels(voice_pre: float, voice_post: float,
                               body_pre_level: str, body_post_level: str,
                               voice_conf: tuple[float, float] = (1.0, 1.0),
                               body_conf: tuple[float, float] = (1.0, 1.0)) -> dict:
    """Component B path: B sends its OWN ordinal stress prediction per phase
    (WESAD: relaxed/mild/moderate/high). We normalise + use it directly - no HRV
    maths on our side - and compare it against the voice stress."""
    return _compare(voice_pre, voice_post,
                    stress_level_to_score(body_pre_level),
                    stress_level_to_score(body_post_level),
                    {"level_pre": normalize_level(body_pre_level),
                     "level_post": normalize_level(body_post_level)},
                    voice_conf, body_conf)
