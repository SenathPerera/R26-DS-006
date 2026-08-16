"""Train the Layer 5 session autoencoder.

Cold start trains on simulated normal sessions (honest limitation,
stated in the report). Re-run with --sessions-csv once the app has
collected real session summaries.

Usage:
  python scripts/train_anomaly.py --out models/anomaly_v2.pt
"""

import argparse
import sys
from pathlib import Path

import numpy as np
import pandas as pd
import torch

sys.path.insert(0, str(Path(__file__).parent.parent / "src"))
from componentd.config import ANOMALY, ANOMALY_FEATURES, MODELS_DIR
from componentd.layer5_anomaly import (SessionAutoencoder, SessionVAE,
                                simulate_sessions, vae_elbo_loss)


def compare_baselines(data, mean, std):
    """Fit classical anomaly detectors (Isolation Forest, One-Class SVM) on
    the same standardised sessions and report how they rate a normal vs an
    extreme session - so the VAE is evaluated against baselines, not alone."""
    try:
        from sklearn.ensemble import IsolationForest
        from sklearn.svm import OneClassSVM
    except Exception:
        print("  (sklearn unavailable - skipping baseline comparison)")
        return
    xn = (data - mean) / std
    iforest = IsolationForest(random_state=0).fit(xn)
    ocsvm = OneClassSVM(gamma="auto").fit(xn)
    normal = np.array([6, 4, -2, .8, .8, 15, .85, .2, .02, 10, 14, 2], np.float32)
    extreme = np.array([.1, 10, 9.9, .01, .01, 300, 0, 5, .9, 500, 3, 200], np.float32)
    print("baseline comparison (sanity):")
    for name, s in [("normal ", normal), ("extreme", extreme)]:
        z = ((s - mean) / std).reshape(1, -1)
        iso = "anomaly" if iforest.predict(z)[0] == -1 else "normal"
        svm = "anomaly" if ocsvm.predict(z)[0] == -1 else "normal"
        print(f"  {name}: IsolationForest={iso:8} OneClassSVM={svm}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=str(MODELS_DIR / "anomaly_v2.pt"))
    ap.add_argument("--sessions-csv", default=None,
                    help="csv of REAL sessions with ANOMALY_FEATURES columns")
    ap.add_argument("--epochs", type=int, default=500)
    ap.add_argument("--model", choices=["vae", "ae"], default="vae",
                    help="vae = reconstruction-probability (default); ae = legacy")
    args = ap.parse_args()

    if args.sessions_csv:
        df = pd.read_csv(args.sessions_csv)
        data = df[ANOMALY_FEATURES].to_numpy(np.float32)
        print(f"training on {len(data)} REAL sessions")
    else:
        data = simulate_sessions(2000)
        print("training on 2000 simulated sessions (cold start)")

    # Standardise and keep the stats - inference must use the same ones.
    mean, std = data.mean(axis=0), data.std(axis=0) + 1e-8
    x = torch.from_numpy((data - mean) / std)

    LATENT_DIM = 4
    torch.manual_seed(0)
    if args.model == "vae":
        model = SessionVAE(len(ANOMALY_FEATURES), ANOMALY["hidden_dims"], LATENT_DIM)
        opt = torch.optim.Adam(model.parameters(), lr=1e-3)
        for epoch in range(args.epochs):
            opt.zero_grad()
            mu_x, logvar_x, mu_z, logvar_z = model(x)
            loss = vae_elbo_loss(x, mu_x, logvar_x, mu_z, logvar_z)
            loss.backward()
            opt.step()
            if epoch % 100 == 0:
                print(f"epoch {epoch:3d}  -ELBO {float(loss.detach()):.5f}")
        model.eval()
        with torch.no_grad():
            errors = model.anomaly_score(x).mean(dim=1).numpy()
    else:
        model = SessionAutoencoder(len(ANOMALY_FEATURES), ANOMALY["hidden_dims"])
        opt = torch.optim.Adam(model.parameters(), lr=1e-3)
        for epoch in range(args.epochs):
            opt.zero_grad()
            loss = ((model(x) - x) ** 2).mean()
            loss.backward()
            opt.step()
            if epoch % 100 == 0:
                print(f"epoch {epoch:3d}  loss {float(loss.detach()):.5f}")
        model.eval()
        with torch.no_grad():
            errors = ((model(x) - x) ** 2).mean(dim=1).numpy()

    # Global threshold: mean + 3 sigma of the training score distribution.
    threshold = float(errors.mean()
                      + ANOMALY["threshold_sigma"] * errors.std())

    ckpt = {
        "state_dict": model.state_dict(),
        "model_type": args.model,
        "n_features": len(ANOMALY_FEATURES),
        "hidden_dims": ANOMALY["hidden_dims"],
        "feat_mean": mean, "feat_std": std,
        "threshold": threshold,
        "trained_on": "real" if args.sessions_csv else "simulated",
    }
    if args.model == "vae":
        ckpt["latent_dim"] = LATENT_DIM
    torch.save(ckpt, args.out)
    print(f"saved {args.model} -> {args.out}  (threshold {threshold:.5f})")

    compare_baselines(data, mean, std)


if __name__ == "__main__":
    main()
