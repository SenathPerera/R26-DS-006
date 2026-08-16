"""Hyperparameter tuning for the fusion head, using Optuna.

Cheap because features are cached: each trial only retrains the ~176K
head (seconds), never the frozen encoder. Optuna searches smartly (it
learns which regions of the search space are promising) and returns the
best configuration by validation score.

Tunes: learning rate, weight decay, batch size, head width, dropout, and
the class-balance strength (how strongly to oversample the minority
stressed class - the main lever for MELD's calm-heavy imbalance).

IMPORTANT: tuning optimises for the VALIDATION set. Run it on the
combined data (MELD + acted + real collected), not MELD alone, so it
optimises for real-voice performance, not just acted speech.

Usage:
  python scripts/tune_fusion.py --features data/features_meld.npz \
      --trials 50 --out models/fusion_tuned.pt
"""

import argparse
import json
import sys
from pathlib import Path

import numpy as np
import torch
from sklearn.metrics import f1_score
from torch.utils.data import DataLoader, TensorDataset, WeightedRandomSampler

sys.path.insert(0, str(Path(__file__).parent.parent / "src"))
from componentd.config import MODELS_DIR
from componentd.layer2_fusion import GatedFusionModel, ccc, ccc_loss


def load_features(paths: list[str]) -> dict:
    parts = [np.load(p, allow_pickle=False) for p in paths]
    keys = ["emb", "pros", "valence", "arousal", "stress_label", "split"]
    return {k: np.concatenate([p[k] for p in parts]) for k in keys}


def split_masks(data: dict):
    train = data["split"] == "train"
    val = data["split"] == "val"
    if not val.any():
        rng = np.random.RandomState(42)
        idx = np.where(train)[0]
        vidx = rng.choice(idx, size=max(1, len(idx) // 10), replace=False)
        val = np.zeros_like(train)
        val[vidx] = True
        train = train & ~val
    return train, val


def make_tensors(data: dict, mask, pros_mean, pros_std):
    pros = (data["pros"][mask] - pros_mean) / pros_std
    return TensorDataset(
        torch.from_numpy(data["emb"][mask]),
        torch.from_numpy(pros.astype(np.float32)),
        torch.from_numpy(data["valence"][mask]),
        torch.from_numpy(data["arousal"][mask]),
        torch.from_numpy(data["stress_label"][mask]),
    )


def balanced_sampler(labels: np.ndarray, strength: float):
    """Oversample the minority stressed class. strength=0 means no
    balancing (natural distribution); strength=1 means fully balanced.
    Ambiguous labels (-1) get normal weight."""
    counts = {c: max(1, np.sum(labels == c)) for c in (0, 1)}
    weights = np.ones(len(labels), dtype=np.float64)
    for c in (0, 1):
        # interpolate between 1.0 (no balancing) and inverse-frequency
        inv_freq = len(labels) / counts[c]
        weights[labels == c] = (1 - strength) * 1.0 + strength * inv_freq
    return WeightedRandomSampler(weights, num_samples=len(labels),
                                 replacement=True)


@torch.no_grad()
def evaluate(model, loader, device):
    model.eval()
    pv, pa, tv, ta, lab = [], [], [], [], []
    for emb, pros, v, a, y in loader:
        ov, oa, _ = model(emb.to(device), pros.to(device))
        pv.append(ov.cpu()); pa.append(oa.cpu())
        tv.append(v); ta.append(a); lab.append(y)
    pv, pa = torch.cat(pv), torch.cat(pa)
    tv, ta, lab = torch.cat(tv), torch.cat(ta), torch.cat(lab)
    m = {"ccc_v": float(ccc(pv, tv)), "ccc_a": float(ccc(pa, ta))}
    mask = lab >= 0
    if mask.any():
        # matches config.stress_from_va (negativity-driven, arousal a modifier)
        neg = (1 - pv[mask]) / 2
        stress = neg * (0.5 + 0.5 * ((pa[mask] + 1) / 2))
        pred = (stress > 0.45).long()
        m["f1"] = float(f1_score(lab[mask], pred, zero_division=0))
    else:
        m["f1"] = 0.0
    return m


def train_one(data, train_mask, val_mask, pros_mean, pros_std, hp, device,
              max_epochs=60, patience=8):
    """Train the head once with hyperparameters hp; return best val metrics
    and the trained model."""
    train_ds = make_tensors(data, train_mask, pros_mean, pros_std)
    val_ds = make_tensors(data, val_mask, pros_mean, pros_std)

    sampler = balanced_sampler(data["stress_label"][train_mask],
                               hp["class_balance"])
    train_loader = DataLoader(train_ds, batch_size=hp["batch_size"],
                              sampler=sampler)
    val_loader = DataLoader(val_ds, batch_size=hp["batch_size"])

    model = GatedFusionModel(
        emb_dim=data["emb"].shape[1], pros_dim=data["pros"].shape[1],
        proj_dim=hp["proj_dim"], hidden_dim=hp["hidden_dim"],
        dropout=hp["dropout"]).to(device)
    opt = torch.optim.AdamW(model.parameters(), lr=hp["lr"],
                            weight_decay=hp["weight_decay"])

    best_score, best_state, best_metrics, waited = -1e9, None, None, 0
    for _ in range(max_epochs):
        model.train()
        for emb, pros, v, a, _ in train_loader:
            opt.zero_grad()
            pv, pa, _ = model(emb.to(device), pros.to(device))
            loss = ccc_loss(pv, pa, v.to(device), a.to(device))
            loss.backward()
            opt.step()
        m = evaluate(model, val_loader, device)
        score = m["ccc_v"] + m["ccc_a"] + m["f1"]   # combined objective
        if score > best_score:
            best_score, best_metrics = score, m
            best_state = {k: v.cpu().clone() for k, v in model.state_dict().items()}
            waited = 0
        else:
            waited += 1
            if waited >= patience:
                break
    model.load_state_dict(best_state)
    return best_score, best_metrics, model


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--features", nargs="+", required=True)
    ap.add_argument("--trials", type=int, default=50)
    ap.add_argument("--out", default=str(MODELS_DIR / "fusion_tuned.pt"))
    args = ap.parse_args()

    import optuna

    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"device: {device}")
    data = load_features(args.features)
    train_mask, val_mask = split_masks(data)
    pros_mean = data["pros"][train_mask].mean(axis=0)
    pros_std = data["pros"][train_mask].std(axis=0) + 1e-8
    print(f"train {train_mask.sum()}, val {val_mask.sum()}")

    def objective(trial):
        hp = {
            "lr": trial.suggest_float("lr", 1e-4, 5e-3, log=True),
            "weight_decay": trial.suggest_float("weight_decay", 1e-6, 1e-3, log=True),
            "batch_size": trial.suggest_categorical("batch_size", [32, 64, 128]),
            "proj_dim": trial.suggest_categorical("proj_dim", [64, 128, 256]),
            "hidden_dim": trial.suggest_categorical("hidden_dim", [32, 64, 128]),
            "dropout": trial.suggest_float("dropout", 0.1, 0.5),
            "class_balance": trial.suggest_float("class_balance", 0.0, 1.0),
        }
        score, _, _ = train_one(data, train_mask, val_mask,
                                pros_mean, pros_std, hp, device)
        return score

    study = optuna.create_study(direction="maximize",
                                sampler=optuna.samplers.TPESampler(seed=42))
    study.optimize(objective, n_trials=args.trials)

    print("\n=== best hyperparameters ===")
    print(json.dumps(study.best_params, indent=2))
    print(f"best objective (ccc_v + ccc_a + f1): {study.best_value:.4f}")

    # Retrain the final model with the best config and save it, in the same
    # checkpoint format the inference wrapper / API expect.
    best_hp = study.best_params
    _, metrics, model = train_one(data, train_mask, val_mask,
                                  pros_mean, pros_std, best_hp, device,
                                  max_epochs=100, patience=12)
    torch.save({
        "state_dict": model.state_dict(),
        "emb_dim": data["emb"].shape[1],
        "pros_dim": data["pros"].shape[1],
        "fusion_config": {"proj_dim": best_hp["proj_dim"],
                          "hidden_dim": best_hp["hidden_dim"],
                          "dropout": best_hp["dropout"]},
        "pros_mean": pros_mean, "pros_std": pros_std,
        "encoder": "plus_large",
        "best_hyperparameters": best_hp,
        "val_metrics": metrics,
    }, args.out)
    print(f"\nsaved tuned model -> {args.out}")
    print(f"val metrics: {json.dumps(metrics, indent=2)}")


if __name__ == "__main__":
    main()
