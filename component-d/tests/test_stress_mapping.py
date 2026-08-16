"""Tests for the stress definition (the VALENCE-PRIMARY mapping) and the
Component B ordinal cross-modal path. These lock in the Phase-1 design: stress
MAGNITUDE is driven by negative valence (the axis proven reliable on real voices);
arousal names the TYPE only and must not gate the score. So subdued/low-arousal
negative stress registers just as strongly as activated stress, while pleasant or
neutral voices stay near zero."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.config import stress_from_va, stress_level_from_score, stress_type_from_va
from componentd.layer4_crossmodal import stress_level_to_score, validate_crossmodal_levels


def test_subdued_stress_registers():
    # sad = negative valence, LOW arousal -> the "freeze" form of stress.
    # Valence-primary scoring registers it (max(0,-valence)); the old arousal-
    # gated formula collapsed it toward the calm end on real voices.
    assert stress_from_va(-0.6, -0.4) * 10 > 4.0


def test_arousal_does_not_change_magnitude():
    # Same negative valence => same stress magnitude whether arousal reads high
    # (activated) or low (shutdown). Arousal is unreliable on real voices, so it
    # must NOT drive the score - it only names the type (see below).
    activated = stress_from_va(-0.6, 0.8) * 10
    shutdown = stress_from_va(-0.6, -0.4) * 10
    assert activated > 4.0 and shutdown > 4.0
    assert abs(activated - shutdown) < 1e-9


def test_pleasant_stays_low_even_when_activated():
    # excited-happy: high arousal but POSITIVE valence -> not stress.
    assert stress_from_va(0.7, 0.7) * 10 < 2.5


def test_calm_scores_low():
    assert stress_from_va(0.4, -0.5) * 10 < 2.5


def test_ordinal_levels():
    assert stress_level_from_score(1.0) == "no"
    assert stress_level_from_score(3.5) == "mild"
    assert stress_level_from_score(6.0) == "moderate"
    assert stress_level_from_score(8.0) == "high"


def test_stress_type_named_only_when_stressed():
    assert stress_type_from_va(-0.6, 0.8) == "activated"   # agitated
    assert stress_type_from_va(-0.6, -0.4) == "shutdown"   # withdrawn
    assert stress_type_from_va(0.4, -0.5) is None          # calm -> no type


def test_component_b_ordinal_crossmodal():
    # Voice and B both high pre, both low post -> agree.
    r = validate_crossmodal_levels(8.0, 3.0, "high", "no")
    assert r["validated"] and r["mismatch_type"] is None
    assert r["body"]["level_pre"] == "high" and r["body"]["level_post"] == "no"


def test_level_to_score_is_ordered():
    lv = [stress_level_to_score(x) for x in ("no", "mild", "moderate", "high")]
    assert lv == sorted(lv) and lv[0] < lv[-1]
