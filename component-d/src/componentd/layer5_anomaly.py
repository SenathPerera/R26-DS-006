"""Layer 5: longitudinal anomaly detection (trained autoencoder).

Idea: an autoencoder trained on NORMAL sessions learns to compress and
reconstruct them well. A session it reconstructs badly (high error) is
unlike anything normal -> anomaly. Threshold becomes PER-USER once a
user has enough history, so "abnormal" means abnormal FOR THAT PERSON.
"""

import sys
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.config import ANOMALY, ANOMALY_FEATURES

N_FEATURES = len(ANOMALY_FEATURES)
# Position of "delta" (post_stress - pre_stress) within the feature
# vector, used below to tell an unusually GOOD session apart from an
# unusually BAD one - the raw reconstruction error alone cannot.
DELTA_INDEX = ANOMALY_FEATURES.index("delta")


class SessionAutoencoder(nn.Module):
    """12 -> 16 -> 8 -> 4 -> 8 -> 16 -> 12 (bottleneck forces the model
    to learn the essential structure of a normal session)."""

    def __init__(self, n_features: int = N_FEATURES,
                 hidden_dims: list[int] | None = None):
        super().__init__()
        dims = hidden_dims or ANOMALY["hidden_dims"]

        encoder_layers, last = [], n_features
        for d in dims:
            encoder_layers += [nn.Linear(last, d), nn.ReLU()]
            last = d
        # No activation on the bottleneck itself - keep it linear so the
        # compressed representation is not needlessly restricted.
        self.encoder = nn.Sequential(*encoder_layers[:-1])

        decoder_layers, last = [], dims[-1]
        for d in reversed(dims[:-1]):
            decoder_layers += [nn.Linear(last, d), nn.ReLU()]
            last = d
        decoder_layers += [nn.Linear(last, n_features)]
        self.decoder = nn.Sequential(*decoder_layers)

    def forward(self, x):
        return self.decoder(self.encoder(x))


class SessionVAE(nn.Module):
    """Variational autoencoder over session summaries - the upgraded Layer 5.

    Over the plain autoencoder this adds two things: a PROBABILISTIC latent
    (the score reflects a whole neighbourhood of a session, not one point)
    and a HETEROSCEDASTIC decoder that predicts a variance per feature.
    Anomalies are scored by RECONSTRUCTION PROBABILITY (An & Cho, 2015): the
    variance-normalised reconstruction error averaged over samples of the
    latent. Weighting each feature by its learned variance is what makes
    this more principled than raw reconstruction error - a feature the model
    is naturally unsure about is not punished as harshly as one it usually
    nails.
    """

    def __init__(self, n_features: int = N_FEATURES,
                 hidden_dims: list[int] | None = None, latent_dim: int = 4):
        super().__init__()
        dims = hidden_dims or ANOMALY["hidden_dims"]
        trunk = dims[:-1] if len(dims) > 1 else dims

        enc, last = [], n_features
        for d in trunk:
            enc += [nn.Linear(last, d), nn.ReLU()]
            last = d
        self.enc = nn.Sequential(*enc)
        self.fc_mu = nn.Linear(last, latent_dim)
        self.fc_logvar = nn.Linear(last, latent_dim)

        dec, last = [], latent_dim
        for d in reversed(trunk):
            dec += [nn.Linear(last, d), nn.ReLU()]
            last = d
        self.dec = nn.Sequential(*dec)
        self.dec_mu = nn.Linear(last, n_features)
        self.dec_logvar = nn.Linear(last, n_features)
        self.latent_dim = latent_dim

    def encode(self, x):
        h = self.enc(x)
        return self.fc_mu(h), self.fc_logvar(h)

    def decode(self, z):
        h = self.dec(z)
        return self.dec_mu(h), self.dec_logvar(h)

    def forward(self, x):
        mu, logvar = self.encode(x)
        z = mu + torch.randn_like(mu) * torch.exp(0.5 * logvar)
        mu_x, logvar_x = self.decode(z)
        return mu_x, logvar_x, mu, logvar

    @torch.no_grad()
    def anomaly_score(self, x, n_samples: int = 32):
        """Variance-normalised reconstruction error averaged over latent
        samples (>= 0, higher = more anomalous). Returns per-feature scores
        so the caller can name which features drove an anomaly."""
        mu_z, logvar_z = self.encode(x)
        std_z = torch.exp(0.5 * logvar_z)
        acc = torch.zeros_like(x)
        for _ in range(n_samples):
            z = mu_z + torch.randn_like(std_z) * std_z
            mu_x, logvar_x = self.decode(z)
            var_x = torch.exp(logvar_x).clamp(min=1e-6)
            acc = acc + (x - mu_x) ** 2 / var_x
        return acc / n_samples


def vae_elbo_loss(x, mu_x, logvar_x, mu_z, logvar_z):
    """Negative ELBO: Gaussian reconstruction NLL + KL to a unit prior."""
    var_x = torch.exp(logvar_x).clamp(min=1e-6)
    recon_nll = 0.5 * (logvar_x + (x - mu_x) ** 2 / var_x).sum(dim=-1).mean()
    kl = -0.5 * (1 + logvar_z - mu_z ** 2 - logvar_z.exp()).sum(dim=-1).mean()
    return recon_nll + kl


def simulate_sessions(n: int = 2000, seed: int = 42) -> np.ndarray:
    """Plausible NORMAL sessions for cold-start training, in
    ANOMALY_FEATURES order. Honest limitation, stated openly: replaced
    by real session data once the app has collected some."""
    rng = np.random.RandomState(seed)
    pre = rng.uniform(3.0, 8.0, n)
    improvement = rng.normal(1.5, 1.0, n).clip(-1, 5)   # sessions usually help
    post = (pre - improvement).clip(0, 10)
    data = np.stack([
        pre,                                    # pre_stress
        post,                                   # post_stress
        post - pre,                             # delta
        rng.uniform(0.3, 1.0, n),               # confidence_pre
        rng.uniform(0.3, 1.0, n),               # confidence_post
        rng.normal(15.0, 4.0, n).clip(5, 40),   # session_duration (min)
        rng.uniform(0.5, 1.0, n),               # hrv_agreement
        rng.uniform(0.05, 0.5, n),              # acoustic_variance
        rng.uniform(0.005, 0.05, n),            # ambient_rms
        rng.randint(1, 60, n).astype(float),    # session_number
        rng.uniform(6, 23, n),                  # time_of_day
        rng.exponential(2.0, n).clip(0, 30),    # days_since_last
    ], axis=1)
    return data.astype(np.float32)


class SessionAnomalyDetector:
    """Loads a trained autoencoder and scores sessions per user."""

    def __init__(self, checkpoint_path: str, device: str = "cpu", store=None):
        ckpt = torch.load(checkpoint_path, map_location=device,
                          weights_only=False)
        # Checkpoints tag their architecture; older ones (no tag) are the
        # plain autoencoder. The scoring differs but everything downstream
        # (threshold, severity, direction, reasons) is identical.
        self.model_type = ckpt.get("model_type", "ae")
        if self.model_type == "vae":
            self.model = SessionVAE(ckpt["n_features"], ckpt["hidden_dims"],
                                    ckpt.get("latent_dim", 4))
        else:
            self.model = SessionAutoencoder(ckpt["n_features"],
                                            ckpt["hidden_dims"])
        self.model.load_state_dict(ckpt["state_dict"])
        self.model.eval()
        # Standardisation stats + global threshold from training time.
        self.mean = np.asarray(ckpt["feat_mean"], dtype=np.float32)
        self.std = np.asarray(ckpt["feat_std"], dtype=np.float32)
        self.global_threshold = float(ckpt["threshold"])
        # Per-user reconstruction-error history. Optionally backed by a durable
        # store (componentd.store) so min_personal_sessions becomes reachable
        # across restarts and the per-user threshold actually engages (PROBLEM 6).
        self.store = store
        self.user_errors: dict[str, list[float]] = (
            store.load_anomaly_history() if store is not None else {})

    def _reconstruction(self, features: np.ndarray):
        x = (features - self.mean) / (self.std + 1e-8)
        x = torch.from_numpy(x.astype(np.float32)).unsqueeze(0)
        with torch.no_grad():
            if self.model_type == "vae":
                # reconstruction-probability score (variance-normalised)
                per_dim = self.model.anomaly_score(x).squeeze(0).numpy()
            else:
                recon = self.model(x)
                per_dim = ((recon - x) ** 2).squeeze(0).numpy()
        return float(per_dim.mean()), per_dim

    def _threshold_for(self, user_id: str) -> float:
        """Global threshold until the user has history, then personal:
        their own error mean + 3 sigma. This is what makes 'anomalous'
        mean 'anomalous for THIS user'."""
        history = self.user_errors.get(user_id, [])
        if len(history) >= ANOMALY["min_personal_sessions"]:
            h = np.asarray(history)
            return float(h.mean() + ANOMALY["threshold_sigma"] * (h.std() + 1e-8))
        return self.global_threshold

    def check(self, user_id: str, features: np.ndarray,
              session_id: str | None = None) -> dict:
        """features: one session summary in ANOMALY_FEATURES order."""
        features = np.asarray(features, dtype=np.float32).flatten()
        assert features.shape == (len(self.mean),), \
            f"expected {len(self.mean)} features, got {features.shape}"

        error, per_dim = self._reconstruction(features)
        threshold = self._threshold_for(user_id)
        is_anomalous = error > threshold

        # Severity from how far past the threshold the error lands.
        ratio = error / (threshold + 1e-8)
        if not is_anomalous:
            severity = "none"
        elif ratio < 1.5:
            severity = "mild"
        elif ratio < 2.5:
            severity = "moderate"
        else:
            severity = "severe"

        # Explainability: name the features that drove the anomaly.
        reasons = []
        if is_anomalous:
            top = np.argsort(per_dim)[::-1][:3]
            reasons = [ANOMALY_FEATURES[i] for i in top if per_dim[i] > error]

        # Reconstruction error alone cannot tell an unusually GOOD session
        # (a much bigger stress drop than normal) apart from an unusually
        # BAD one (stress rose sharply) - both are simply "far from what
        # the model has seen". A wellness app must not present a great
        # session to the user as an alarming "severe anomaly", so the
        # sign of delta (post_stress - pre_stress) resolves the direction:
        # delta <= 0 means stress fell (an unusual IMPROVEMENT), delta > 0
        # means stress rose (an unusual WORSENING that may warrant a
        # gentle follow-up prompt in the app).
        anomaly_direction = None
        if is_anomalous:
            delta_value = float(features[DELTA_INDEX])
            anomaly_direction = ("unusual_improvement" if delta_value <= 0
                                 else "unusual_worsening")

        # Only NORMAL sessions extend the user's baseline - otherwise one
        # anomaly would poison the personal threshold.
        if not is_anomalous:
            self.user_errors.setdefault(user_id, []).append(error)
            if self.store is not None:
                self.store.observe_anomaly(user_id, session_id or "", error)

        return {
            "anomaly": bool(is_anomalous),
            "anomaly_direction": anomaly_direction,
            "severity": severity,
            "reasons": reasons,
            "error": round(error, 5),
            "threshold": round(threshold, 5),
            "personalised": len(self.user_errors.get(user_id, []))
                            >= ANOMALY["min_personal_sessions"],
        }
