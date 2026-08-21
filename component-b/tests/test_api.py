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
    "timestamp": 1754985600.0,
    "mode": "point",
    "level": 2,
    "label": "moderate",
    "confidence": 0.81,
    "probabilities": {"relaxed": 0.04, "mild": 0.11, "moderate": 0.81,
                      "high": 0.04},
    "deviation": {"rmssd": -1.42, "sdnn": -0.87, "hr": 1.31},
    "baseline_maturity": "personal",
}

BAND = {
    "timestamp": 1754985604.0,
    "mode": "band",
    "level_low": 1,
    "level_high": 2,
    "label": "mild-to-moderate",
    "confidence": 0.54,
    "probabilities": {"relaxed": 0.08, "mild": 0.36, "moderate": 0.54,
                      "high": 0.02},
    "deviation": {"rmssd": -0.31, "sdnn": -0.22, "hr": 0.44},
    "baseline_maturity": "converging",
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
    assert r.json()["mode"] == "point"
    assert r.json()["level"] == 2


def test_latest_returns_band_prediction(client):
    latest.set(BAND)
    body = client.get("/stress/latest").json()
    assert body["mode"] == "band"
    assert (body["level_low"], body["level_high"]) == (1, 2)
    assert body["label"] == "mild-to-moderate"
    assert body["level"] is None      # absent for bands, never guessed


def test_latest_reflects_the_most_recent_only(client):
    latest.set(POINT)
    latest.set(BAND)
    assert client.get("/stress/latest").json()["mode"] == "band"


def test_probabilities_survive_the_round_trip(client):
    """Documented in README and ARCHITECTURE §6, so it is part of the
    contract: the full distribution ships alongside the decision.

    Consumers are told not to argmax it — which is only enforceable if
    the field is actually there.
    """
    latest.set(BAND)
    body = client.get("/stress/latest").json()

    assert set(body["probabilities"]) == {"relaxed", "mild", "moderate",
                                          "high"}
    assert abs(sum(body["probabilities"].values()) - 1.0) < 1e-6
    # the gate emitted a band, so `level` stays absent even though the
    # distribution has a clear argmax — that is the point of the gate
    assert body["mode"] == "band" and body["level"] is None
