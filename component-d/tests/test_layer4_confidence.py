"""Layer 4 Component-B integration: class-name normalisation and CONFIDENCE
GATING. The core rule under test: a voice/body disagreement is only asserted as
a genuine cognitive-physiological mismatch when the signals driving it are
confident; otherwise it is deferred to Component B (HRV). This is what stops
Component D's arousal-collapse uncertainty from producing false 'mismatch'
findings."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.layer4_crossmodal import (normalize_level, stress_level_to_score,
                                    validate_crossmodal,
                                    validate_crossmodal_levels)


def test_relaxed_normalises_to_no():
    # B's lowest class is "relaxed"; D calls it "no". Must not hit the 5.0 fallback.
    assert normalize_level("relaxed") == "no"
    assert normalize_level("RELAXED ") == "no"
    assert stress_level_to_score("relaxed") == stress_level_to_score("no") == 1.25


def test_b_relaxed_level_compares_like_no():
    r = validate_crossmodal_levels(1.5, 1.0, "relaxed", "relaxed")
    assert r["validated"] and r["mismatch_type"] is None
    assert r["body"]["level_pre"] == "no"          # normalised on the way through


def test_low_voice_confidence_defers_instead_of_mismatch():
    # Voice claims recovery (would be vocal_masking) but voice is UNCERTAIN.
    r = validate_crossmodal(6.5, 3.0, 38.0, 36.0, voice_conf=(0.1, 0.1))
    assert r["mismatch_type"] is None              # not asserted
    assert r["low_confidence"] and r["deferred_to"] == "body"
    assert r["unresolved_mismatch"] == "vocal_masking"
    assert not r["validated"]                      # unresolved, not "validated"


def test_high_confidence_still_flags_mismatch():
    # Same disagreement, but voice is confident -> the genuine mismatch stands.
    r = validate_crossmodal(6.5, 3.0, 38.0, 36.0, voice_conf=(0.9, 0.9))
    assert r["mismatch_type"] == "vocal_masking"
    assert not r.get("low_confidence")


def test_low_body_confidence_also_defers():
    # Voice confident, but B was uncertain (band -> low confidence) -> defer.
    r = validate_crossmodal(6.5, 3.0, 38.0, 36.0,
                            voice_conf=(0.9, 0.9), body_conf=(0.2, 0.2))
    assert r["mismatch_type"] is None and r["deferred_to"] == "body"


def test_confidence_is_reported():
    r = validate_crossmodal(7.0, 3.0, 30.0, 65.0, voice_conf=(0.8, 0.6))
    assert r["voice"]["confidence"] == {"pre": 0.8, "post": 0.6}
    assert r["validated"]                          # agreement, both confident


def test_defaults_preserve_legacy_behaviour():
    # No confidence args -> behaves exactly as before (mismatch asserted).
    r = validate_crossmodal(6.5, 3.0, 38.0, 36.0)
    assert r["mismatch_type"] == "vocal_masking" and not r.get("low_confidence")
