"""The JSON contract other components depend on.

Component C (VR adaptation) and the website consume these responses, so
the shape here is an interface, not an implementation detail. In
particular both `mode` values must survive a round trip — a consumer
that assumes `level` is always present breaks on a merged band.
"""

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from server.main import app
from server.schemas.messages import PPGBatch
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


def test_ingest_contract_accepts_exact_component_b_frame():
    frame = PPGBatch(
        timestamp=1787282838.4,
        sample_rate=64.0,
        ppg=[1834.2] * 960,
        temperature=33.7,
    )

    assert len(frame.ppg) == 960
    assert frame.temperature == 33.7


@pytest.mark.parametrize(
    ("patch", "field"),
    [
        ({"ppg": [1834.2] * 959}, "ppg"),
        ({"ppg": [1834.2] * 961}, "ppg"),
        ({"sample_rate": 100.0}, "sample_rate"),
        ({"timestamp": float("nan")}, "timestamp"),
        ({"temperature": float("inf")}, "temperature"),
    ],
)
def test_ingest_contract_rejects_malformed_frames(patch, field):
    payload = {
        "timestamp": 1787282838.4,
        "sample_rate": 64.0,
        "ppg": [1834.2] * 960,
        "temperature": None,
        **patch,
    }

    with pytest.raises(ValidationError) as exc:
        PPGBatch.model_validate(payload)

    assert any(error["loc"] == (field,) for error in exc.value.errors())


def test_ingest_socket_rejects_bad_frame_then_accepts_next(client, monkeypatch):
    monkeypatch.setattr("server.main.new_stream", lambda: None)
    monkeypatch.setattr("server.main.unavailable_reason", lambda: "test model unavailable")
    monkeypatch.setattr("server.main.ppg_to_rr", lambda _ppg, _rate: (None, None, None))

    with client.websocket_connect("/ingest") as websocket:
        assert websocket.receive_json()["status"] == "model_unavailable"

        websocket.send_json({
            "timestamp": 1787282838.4,
            "sample_rate": 64.0,
            "ppg": [1834.2] * 20,
            "temperature": 33.7,
        })
        assert websocket.receive_json()["status"] == "invalid_batch"

        websocket.send_json({
            "timestamp": 1787282838.4,
            "sample_rate": 64.0,
            "ppg": [1834.2] * 960,
            "temperature": None,
        })
        accepted = websocket.receive_json()
        assert accepted == {
            "status": "accepted",
            "timestamp": 1787282838.4,
            "samples": 960,
            "temperature": 33.7,
            "temperature_source": "synthetic_backend",
        }


def test_temperature_does_not_leak_between_ingest_connections(client, monkeypatch):
    """The resolver is per connection, exactly like the inference engine.

    It caches the last measured temperature for WEARABLE_CACHE_SECONDS. A
    module-level instance would satisfy every other test in this file while
    silently serving one wearer's skin temperature to the next wearer who
    connects inside that window — and the frame would still be reported as
    `wearable_cached`, so nothing downstream could tell.
    """
    monkeypatch.setattr("server.main.new_stream", lambda: None)
    monkeypatch.setattr("server.main.unavailable_reason", lambda: "test unavailable")
    monkeypatch.setattr("server.main.ppg_to_rr", lambda _ppg, _rate: (None, None, None))

    frame = {"timestamp": 1787282838.4, "sample_rate": 64.0, "ppg": [1834.2] * 960}

    with client.websocket_connect("/ingest") as first:
        assert first.receive_json()["status"] == "model_unavailable"
        first.send_json({**frame, "temperature": 36.9})
        accepted = first.receive_json()
        assert accepted["temperature"] == 36.9
        assert accepted["temperature_source"] == "wearable"

    # 10 s later: well inside the 60 s cache window a shared resolver would
    # still be serving 36.9 from the connection above.
    with client.websocket_connect("/ingest") as second:
        assert second.receive_json()["status"] == "model_unavailable"
        second.send_json({**frame, "timestamp": frame["timestamp"] + 10.0,
                          "temperature": None})
        accepted = second.receive_json()
        assert accepted["temperature_source"] == "synthetic_backend"
        assert accepted["temperature"] != 36.9


def test_ingest_initializes_model_off_the_asgi_event_loop(client, monkeypatch):
    calls = []

    async def tracked_to_thread(function, *args, **kwargs):
        calls.append(function)
        return function(*args, **kwargs)

    monkeypatch.setattr("server.main.asyncio.to_thread", tracked_to_thread)
    monkeypatch.setattr("server.main.new_stream", lambda: None)
    monkeypatch.setattr("server.main.unavailable_reason", lambda: "test unavailable")

    with client.websocket_connect("/ingest") as websocket:
        assert websocket.receive_json()["status"] == "model_unavailable"

    assert len(calls) == 1


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
