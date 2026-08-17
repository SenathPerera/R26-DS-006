"""Component B integration client tests (Layer 4 poll path).

Everything here runs on Component D's side alone - no live B. The HTTP layer is
exercised with a tiny fake client so 200/503/error paths are deterministic.
The payloads are Component B's OWN example StressPredictions (copied verbatim from
component-b/tests/test_api.py) so we validate against B's real wire format, not a
paraphrase of it. The remaining Senath-dependent step - pointing this at a running
B - is the live joint test, documented in docs/DEPLOYMENT.md.
"""

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.component_b_client import (BAND_CONF_CAP, BodyReading,
                                           feed_into_store, map_stress_prediction,
                                           poll_into_store, poll_latest)
from componentd.layer4_crossmodal import StoredHRVProvider, validate_crossmodal_levels

# --- Component B's real example payloads (component-b/tests/test_api.py) --------
B_POINT = {
    "timestamp": 1754985600.0,
    "mode": "point",
    "level": 2,
    "label": "moderate",
    "confidence": 0.81,
    "deviation": {"rmssd": -1.42, "sdnn": -0.87, "hr": 1.31},
    "baseline_maturity": "personal",
}
B_BAND = {
    "timestamp": 1754985604.0,
    "mode": "band",
    "level_low": 1,
    "level_high": 2,
    "label": "mild-to-moderate",
    "confidence": 0.54,
    "deviation": {"rmssd": -0.31, "sdnn": -0.22, "hr": 0.44},
    "baseline_maturity": "converging",
}


class FakeResponse:
    def __init__(self, status_code, payload=None):
        self.status_code = status_code
        self._payload = payload

    def json(self):
        return self._payload


class FakeClient:
    """httpx-like stand-in. Serves a queued response or raises to simulate a
    dead B. Records the URL it was asked for."""

    def __init__(self, response=None, raises=None):
        self._response = response
        self._raises = raises
        self.requested_url = None

    def get(self, url, timeout=None):
        self.requested_url = url
        if self._raises is not None:
            raise self._raises
        return self._response


# ----------------------------------------------------------- pure mapping
def test_point_maps_level_and_confidence():
    r = map_stress_prediction(B_POINT)
    assert r.level == "moderate"          # B level int 2 -> CLASS_NAMES[2]
    assert r.confidence == 0.81           # B's confidence passes straight through
    assert r.mode == "point"


@pytest.mark.parametrize("level_int,expected", [
    (0, "no"),        # B "relaxed" normalises to D "no"
    (1, "mild"),
    (2, "moderate"),
    (3, "high"),
])
def test_every_point_level_maps(level_int, expected):
    pred = {**B_POINT, "level": level_int}
    assert map_stress_prediction(pred).level == expected


def test_relaxed_alias_becomes_no():
    assert map_stress_prediction({**B_POINT, "level": 0}).level == "no"


def test_band_takes_higher_level_and_caps_confidence():
    r = map_stress_prediction(B_BAND)
    assert r.level == "moderate"          # higher of mild/moderate (don't under-call)
    assert r.confidence == BAND_CONF_CAP  # 0.54 capped down to 0.2 -> Layer 4 defers
    assert r.mode == "band"


def test_band_keeps_confidence_when_already_below_cap():
    r = map_stress_prediction({**B_BAND, "confidence": 0.05})
    assert r.confidence == 0.05           # never inflate B's own low confidence


def test_band_higher_level_regardless_of_field_order():
    r = map_stress_prediction({**B_BAND, "level_low": 3, "level_high": 1})
    assert r.level == "high"


# ---------------------------------------------------- mapping guardrails
@pytest.mark.parametrize("bad_level", [-1, 4, 99])
def test_out_of_range_level_raises(bad_level):
    with pytest.raises(ValueError):
        map_stress_prediction({**B_POINT, "level": bad_level})


def test_bool_level_rejected():
    # bool is an int subclass; must not sneak through as level 0/1.
    with pytest.raises(ValueError):
        map_stress_prediction({**B_POINT, "level": True})


def test_missing_confidence_raises():
    bad = {k: v for k, v in B_POINT.items() if k != "confidence"}
    with pytest.raises(ValueError):
        map_stress_prediction(bad)


def test_unknown_mode_raises():
    with pytest.raises(ValueError):
        map_stress_prediction({**B_POINT, "mode": "trend"})


def test_non_dict_raises():
    with pytest.raises(ValueError):
        map_stress_prediction([1, 2, 3])


# ------------------------------------------------------------ HTTP layer
def test_poll_200_point_returns_reading():
    c = FakeClient(FakeResponse(200, B_POINT))
    r = poll_latest("s1", "pre", base_url="http://b:8000", client=c)
    assert isinstance(r, BodyReading) and r.level == "moderate"
    assert c.requested_url == "http://b:8000/stress/latest"


def test_poll_200_band_returns_reading():
    c = FakeClient(FakeResponse(200, B_BAND))
    r = poll_latest("s1", "post", client=c)
    assert r.level == "moderate" and r.confidence == BAND_CONF_CAP


def test_poll_503_returns_none():
    # B not warmed up yet -> voice-only fallback, never a faked value.
    c = FakeClient(FakeResponse(503))
    assert poll_latest("s1", "pre", client=c) is None


def test_poll_connection_error_returns_none():
    c = FakeClient(raises=ConnectionError("B is down"))
    assert poll_latest("s1", "pre", client=c) is None


def test_poll_other_status_returns_none():
    assert poll_latest("s1", "pre", client=FakeClient(FakeResponse(500))) is None


def test_poll_malformed_body_returns_none_not_crash():
    # A live session must survive B sending garbage; map_* still raises for tests.
    c = FakeClient(FakeResponse(200, {"mode": "point", "level": 42, "confidence": 0.9}))
    assert poll_latest("s1", "pre", client=c) is None


def test_base_url_trailing_slash_stripped():
    c = FakeClient(FakeResponse(200, B_POINT))
    poll_latest("s1", "pre", base_url="http://b:8000/", client=c)
    assert c.requested_url == "http://b:8000/stress/latest"


# ---------------------------------------- feed + end-to-end into Layer 4
def test_feed_into_store_matches_push_path():
    store = StoredHRVProvider()
    feed_into_store(store, "s1", "pre", BodyReading("mild", 0.7, "point"))
    assert store.get_level("s1", "pre") == "mild"
    assert store.get_level_confidence("s1", "pre") == 0.7


def test_poll_into_store_stores_on_success():
    store = StoredHRVProvider()
    c = FakeClient(FakeResponse(200, B_POINT))
    r = poll_into_store(store, "s1", "pre", client=c)
    assert r.level == "moderate"
    assert store.get_level("s1", "pre") == "moderate"


def test_poll_into_store_skips_on_503():
    store = StoredHRVProvider()
    c = FakeClient(FakeResponse(503))
    assert poll_into_store(store, "s1", "pre", client=c) is None
    assert store.get_level("s1", "pre") is None   # nothing stored -> voice-only


def test_end_to_end_poll_to_crossmodal_verdict():
    """Poll B for both phases, feed the store, run Layer 4 - the real join.

    B: moderate (conf .81) before, relaxed/"no" (conf .81) after. Voice: high (8)
    before, low (2) after. Both signals fall together and confidently -> Layer 4
    validates (agreement, no mismatch)."""
    store = StoredHRVProvider()
    pre_client = FakeClient(FakeResponse(200, {**B_POINT, "level": 2}))    # moderate
    post_client = FakeClient(FakeResponse(200, {**B_POINT, "level": 0}))   # -> "no"
    b_pre = poll_into_store(store, "sess", "pre", client=pre_client)
    b_post = poll_into_store(store, "sess", "post", client=post_client)

    result = validate_crossmodal_levels(
        8.0, 2.0,                                       # voice pre/post
        store.get_level("sess", "pre"), store.get_level("sess", "post"),
        voice_conf=(0.9, 0.9),
        body_conf=(b_pre.confidence, b_post.confidence),
    )
    assert result["validated"] is True
    assert result["mismatch_type"] is None
    assert result["body"]["level_pre"] == "moderate"
    assert result["body"]["level_post"] == "no"


def test_end_to_end_low_confidence_band_defers_to_body():
    """When B returns a band (uncertain) and voice disagrees, Layer 4 must NOT
    assert a mismatch - the capped band confidence trips the defer gate."""
    store = StoredHRVProvider()
    # Body says calm both phases (band -> low conf); voice says stressed throughout.
    calm_band = {**B_BAND, "level_low": 0, "level_high": 1}   # -> higher "mild"
    poll_into_store(store, "s", "pre", client=FakeClient(FakeResponse(200, calm_band)))
    poll_into_store(store, "s", "post", client=FakeClient(FakeResponse(200, calm_band)))
    b_conf = BAND_CONF_CAP

    result = validate_crossmodal_levels(
        8.0, 8.0, store.get_level("s", "pre"), store.get_level("s", "post"),
        voice_conf=(0.9, 0.9), body_conf=(b_conf, b_conf),
    )
    assert result.get("low_confidence") is True
    assert result["deferred_to"] == "body"
    assert result["mismatch_type"] is None
