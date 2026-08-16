"""Layer 5 tests: train a small autoencoder in-test, then verify that
normal sessions pass, extreme sessions flag, and thresholds personalise."""

import sys
from pathlib import Path

import numpy as np
import pytest
import torch

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.config import ANOMALY, ANOMALY_FEATURES
from componentd.layer5_anomaly import (SessionAnomalyDetector, SessionAutoencoder,
                                SessionVAE, simulate_sessions, vae_elbo_loss)


@pytest.fixture(scope="module")
def checkpoint(tmp_path_factory):
    """Quick training run - same recipe as scripts/train_anomaly.py."""
    data = simulate_sessions(1000)
    mean, std = data.mean(axis=0), data.std(axis=0) + 1e-8
    x = torch.from_numpy((data - mean) / std)

    torch.manual_seed(0)
    model = SessionAutoencoder(len(ANOMALY_FEATURES), ANOMALY["hidden_dims"])
    opt = torch.optim.Adam(model.parameters(), lr=1e-3)
    for _ in range(300):
        opt.zero_grad()
        loss = ((model(x) - x) ** 2).mean()
        loss.backward()
        opt.step()

    model.eval()
    with torch.no_grad():
        errors = ((model(x) - x) ** 2).mean(dim=1).numpy()
    path = tmp_path_factory.mktemp("models") / "anomaly_test.pt"
    torch.save({
        "state_dict": model.state_dict(),
        "n_features": len(ANOMALY_FEATURES),
        "hidden_dims": ANOMALY["hidden_dims"],
        "feat_mean": mean, "feat_std": std,
        "threshold": float(errors.mean() + 3 * errors.std()),
    }, path)
    return str(path)


def normal_session():
    return np.array([6.0, 4.0, -2.0, 0.8, 0.8, 15.0, 0.85, 0.2, 0.02,
                     10, 14.0, 2.0])


def extreme_session():
    return np.array([0.1, 10.0, 9.9, 0.01, 0.01, 300.0, 0.0, 5.0, 0.9,
                     500, 3.0, 200.0])


def unusual_improvement_session():
    """Every feature typical EXCEPT a much larger stress drop than any
    simulated training example (delta -8.0; training caps improvement
    at 5, i.e. delta never below -5). Mirrors the real /full-session
    result observed live: pre 8.76 -> post 2.27, delta -6.49."""
    return np.array([9.0, 1.0, -8.0, 0.8, 0.8, 15.0, 0.85, 0.2, 0.02,
                     10, 14.0, 2.0])


def unusual_worsening_session():
    """Every feature typical EXCEPT stress rising sharply instead of
    falling (delta +6.0; training data never has post_stress exceed
    pre_stress by more than ~1 point)."""
    return np.array([3.0, 9.0, 6.0, 0.8, 0.8, 15.0, 0.85, 0.2, 0.02,
                     10, 14.0, 2.0])


def test_normal_session_passes(checkpoint):
    det = SessionAnomalyDetector(checkpoint)
    r = det.check("user1", normal_session())
    assert not r["anomaly"] and r["severity"] == "none"
    assert r["anomaly_direction"] is None   # no direction when not anomalous


def test_extreme_session_flags(checkpoint):
    det = SessionAnomalyDetector(checkpoint)
    r = det.check("user1", extreme_session())
    assert r["anomaly"]
    assert r["severity"] in ("mild", "moderate", "severe")
    assert len(r["reasons"]) >= 1   # explainability: reasons must be named
    # delta = 9.9 (post >> pre): stress rose, so this must read as worsening
    assert r["anomaly_direction"] == "unusual_worsening"


def test_unusual_improvement_labelled_correctly(checkpoint):
    """A session outside the model's experience purely because the
    IMPROVEMENT was unusually large must never be labelled a worsening -
    a wellness app must not alarm a user after their best session."""
    det = SessionAnomalyDetector(checkpoint)
    r = det.check("user_improve", unusual_improvement_session())
    assert r["anomaly"], "expected this out-of-distribution delta to flag"
    assert r["anomaly_direction"] == "unusual_improvement"


def test_unusual_worsening_labelled_correctly(checkpoint):
    """The mirror case: stress rising far more than the model has ever
    seen must be labelled a worsening, not silently called improvement."""
    det = SessionAnomalyDetector(checkpoint)
    r = det.check("user_worsen", unusual_worsening_session())
    assert r["anomaly"], "expected this out-of-distribution delta to flag"
    assert r["anomaly_direction"] == "unusual_worsening"


def test_threshold_personalises_after_history(checkpoint):
    det = SessionAnomalyDetector(checkpoint)
    for _ in range(ANOMALY["min_personal_sessions"]):
        det.check("user2", normal_session())
    r = det.check("user2", normal_session())
    assert r["personalised"]


def test_wrong_feature_count_rejected(checkpoint):
    det = SessionAnomalyDetector(checkpoint)
    with pytest.raises(AssertionError):
        det.check("user3", np.zeros(5))


# --------------------------------------------------- VAE (upgraded Layer 5)
@pytest.fixture(scope="module")
def vae_checkpoint(tmp_path_factory):
    """Train a small VAE - same recipe as train_anomaly.py --model vae."""
    data = simulate_sessions(1000)
    mean, std = data.mean(axis=0), data.std(axis=0) + 1e-8
    x = torch.from_numpy((data - mean) / std)

    torch.manual_seed(0)
    model = SessionVAE(len(ANOMALY_FEATURES), ANOMALY["hidden_dims"], 4)
    opt = torch.optim.Adam(model.parameters(), lr=1e-3)
    for _ in range(400):
        opt.zero_grad()
        mu_x, logvar_x, mu_z, logvar_z = model(x)
        vae_elbo_loss(x, mu_x, logvar_x, mu_z, logvar_z).backward()
        opt.step()

    model.eval()
    with torch.no_grad():
        errors = model.anomaly_score(x).mean(dim=1).numpy()
    path = tmp_path_factory.mktemp("models") / "anomaly_vae_test.pt"
    torch.save({
        "state_dict": model.state_dict(), "model_type": "vae",
        "n_features": len(ANOMALY_FEATURES), "hidden_dims": ANOMALY["hidden_dims"],
        "latent_dim": 4, "feat_mean": mean, "feat_std": std,
        "threshold": float(errors.mean() + 3 * errors.std()),
    }, path)
    return str(path)


def test_vae_normal_session_passes(vae_checkpoint):
    det = SessionAnomalyDetector(vae_checkpoint)
    assert det.model_type == "vae"
    r = det.check("u1", normal_session())
    assert not r["anomaly"] and r["anomaly_direction"] is None


def test_vae_extreme_session_flags(vae_checkpoint):
    det = SessionAnomalyDetector(vae_checkpoint)
    r = det.check("u1", extreme_session())
    assert r["anomaly"] and r["anomaly_direction"] == "unusual_worsening"
    assert len(r["reasons"]) >= 1
