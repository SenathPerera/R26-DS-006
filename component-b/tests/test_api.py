"""The JSON contract other components depend on.

Component C (VR adaptation) and the website consume these responses, so
the shape here is an interface, not an implementation detail. In
particular both `mode` values must survive a round trip — a consumer
that assumes `level` is always present breaks on a merged band.
"""

import pytest
from fastapi.testclient import TestClient

from server.main import app
from server.state import latest

POINT = {
    "timestamp": 1787282898.4,
    "heartRate": 78.4,
    "rmssd": 34.1,
    "sdnn": 42.0,
    "stress": {
        "mode": "point",
        "level": 2,
        "label": "moderate",
        "confidence": 0.81,
        "adjacent": False,
        "probabilities": {"relaxed": 0.04, "mild": 0.11, "moderate": 0.81,
                          "high": 0.04},
        "continuous_score": 1.85,
    },
    "signalQuality": 0.98,
    "windowStart": 1787282838.4,
    "windowEnd": 1787282898.4,
}

BAND = {
    "timestamp": 1787282902.4,
    "heartRate": 81.2,
    "rmssd": 28.7,
    "sdnn": 38.5,
    "stress": {
        "mode": "band",
        "level_low": 1,
        "level_high": 2,
        "label": "mild-to-moderate",
        # confidence is the top-two MARGIN, and a band means it fell
        # below CONFIDENCE_TAU = 0.15
        "confidence": 0.10,
        "adjacent": True,
        "probabilities": {"relaxed": 0.08, "mild": 0.40, "moderate": 0.50,
                          "high": 0.02},
        "continuous_score": 1.46,
    },
    "signalQuality": 0.92,
    "windowStart": 1787282842.4,
    "windowEnd": 1787282902.4,
}


@pytest.fixture(autouse=True)
def _reset_latest():
    latest.clear()
    yield
    latest.clear()


@pytest.fixture
def client():
    return TestClient(app)


def test_health(client):
    assert client.get("/health").json() == {"status": "ok"}


def test_latest_is_503_before_first_window(client):
    """Not 404: the resource exists, it just has no value yet."""
    r = client.get("/stress/latest")
    assert r.status_code == 503
    assert "detail" in r.json()


def test_latest_returns_point_prediction(client):
    latest.set(POINT)
    r = client.get("/stress/latest")
    assert r.status_code == 200
    assert r.json()["stress"]["mode"] == "point"
    assert r.json()["stress"]["level"] == 2


def test_latest_returns_band_prediction(client):
    latest.set(BAND)
    s = client.get("/stress/latest").json()["stress"]
    assert s["mode"] == "band"
    assert (s["level_low"], s["level_high"]) == (1, 2)
    assert s["label"] == "mild-to-moderate"
    assert s["level"] is None         # absent for bands, never guessed


def test_latest_reflects_the_most_recent_only(client):
    latest.set(POINT)
    latest.set(BAND)
    assert client.get("/stress/latest").json()["stress"]["mode"] == "band"


def test_physiology_travels_with_the_prediction(client):
    """Consumers get HR/RMSSD/SDNN without re-deriving them from beats.

    These are the raw physical values, not the scaled vector the model
    consumes — a dashboard plotting the latter would be plotting z-scores
    and calling them milliseconds.
    """
    latest.set(BAND)
    b = client.get("/stress/latest").json()
    assert (b["heartRate"], b["rmssd"], b["sdnn"]) == (81.2, 28.7, 38.5)
    assert b["windowStart"] < b["windowEnd"]
    assert b["timestamp"] == b["windowEnd"]     # endpoint labeling
    assert 0.0 <= b["signalQuality"] <= 1.0


def test_probabilities_survive_the_round_trip(client):
    """Documented in README and ARCHITECTURE §6, so it is part of the
    contract: the full distribution ships alongside the decision.

    Consumers are told not to argmax it — which is only enforceable if
    the field is actually there.
    """
    latest.set(BAND)
    s = client.get("/stress/latest").json()["stress"]

    assert set(s["probabilities"]) == {"relaxed", "mild", "moderate", "high"}
    assert abs(sum(s["probabilities"].values()) - 1.0) < 5e-3
    # the gate emitted a band, so `level` stays absent even though the
    # distribution has a clear argmax — that is the point of the gate
    assert s["mode"] == "band" and s["level"] is None
    # derived, not predicted: sum(i * p_i)
    assert s["continuous_score"] == 1.46
