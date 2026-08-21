"""Load trained artifacts.

The scaler MUST be the one fitted during training. Normalising live
data with different statistics degrades predictions silently — no
error is raised, the answers are just wrong.
"""

import json
import pickle
from pathlib import Path

ARTIFACTS = Path(__file__).resolve().parents[3] / "artifacts"


def load_model(name="mscgca_population"):
    """Load the population MS-CGCA network (`p_cnn` in the blend).

    Loaded with `compile=False`: the network was trained under
    `SparseFocalLoss`, whose `__init__` does not accept the `name` and
    `reduction` kwargs Keras passes when deserializing, so restoring the
    loss raises. Inference does not need it.
    """
    import tensorflow as tf
    path = ARTIFACTS / "models" / f"{name}.keras"
    if not path.exists():
        raise FileNotFoundError(
            f"Model not found: {path}\n"
            "Export it from your training notebook first."
        )
    return tf.keras.models.load_model(path, compile=False)


def load_xgb_model(name="xgb_population"):
    """Load the shipped XGBoost model."""
    from xgboost import XGBClassifier
    path = ARTIFACTS / "models" / f"{name}.json"
    if not path.exists():
        raise FileNotFoundError(
            f"Model not found: {path}\n"
            "Export it from your training notebook first."
        )
    model = XGBClassifier()
    model.load_model(path)
    return model


def load_scaler(name="feature_scaler"):
    path = ARTIFACTS / "scalers" / f"{name}.pkl"
    if not path.exists():
        raise FileNotFoundError(
            f"Scaler not found: {path}\n"
            "Export the StandardScaler fitted during training."
        )
    with open(path, "rb") as f:
        return pickle.load(f)


def load_config(name="model_config"):
    path = ARTIFACTS / "config" / f"{name}.json"
    with open(path) as f:
        return json.load(f)


def check_config(name="model_config"):
    """Assert the exported config still describes the pipeline in config.py.

    Feature order and channel order are load-bearing — the scaler and the
    booster were fit against exact positions, so a mismatch produces
    plausible-looking numbers rather than an exception. The export notebook
    writes what it actually trained on; this refuses to run if that has
    drifted from what `src/` assembles.

    Returns the config dict so callers can reuse it.
    """
    from componentb import config as cfg
    exported = load_config(name)

    expected = {
        "window_beats": cfg.WINDOW_BEATS,
        "step_beats": cfg.STEP_BEATS,
        "labeling": cfg.LABELING,
        "ewma_halflives": cfg.EWMA_HALFLIVES,
        "zscore_halflife": cfg.ZSCORE_HALFLIFE,
        "population_rr_ms": cfg.POPULATION_RR_MS,
        "roll_window": cfg.ROLL_WINDOW,
        "xgb_feature_dim": cfg.XGB_FEATURE_DIM,
        "xgb_feature_order": cfg.XGB_FEATURE_ORDER,
        "cnn_sequence_channels": cfg.CNN_SEQUENCE_CHANNELS,
        "cnn_circadian_dim": cfg.CIRCADIAN_DIM,
        "class_names": cfg.CLASS_NAMES,
        "n_classes": cfg.N_CLASSES,
    }

    mismatches = [
        f"  {key}: exported {exported[key]!r} != config.py {want!r}"
        for key, want in expected.items()
        if key in exported and exported[key] != want
    ]
    if mismatches:
        raise ValueError(
            "model_config.json disagrees with src/componentb/config.py:\n"
            + "\n".join(mismatches)
            + "\nThe artifacts were trained against different settings than "
              "this code assembles. Re-export or fix config.py — do not "
              "run inference across this gap."
        )
    return exported


def load_ensemble_weights(name="model_config"):
    """Blend weights `(w_xgb, w_cnn)` for the 2-way ensemble, summing to 1.

    The schema is the one `notebook-train-export-2way.ipynb` cell 8 writes:

        "ensemble_weights": {"xgb": float(wx), "cnn": float(wc)}

    The shipped pair is (0.20, 0.80), selected by pooled grid search over
    17 points. It is not hardcoded here: the weights are what produce the
    reported F1, so a missing or malformed value must fail loudly rather
    than fall back to a default and silently ship a different model.
    """
    cfg = load_config(name)
    try:
        w = cfg["ensemble_weights"]
        weights = (float(w["xgb"]), float(w["cnn"]))
    except (KeyError, TypeError) as exc:
        raise KeyError(
            "model_config.json has no usable 'ensemble_weights' "
            "{xgb, cnn}. Export them from notebook-train-export-2way.ipynb "
            "cell 8 — do not substitute defaults, the blend is what "
            "produces the reported F1."
        ) from exc

    total = sum(weights)
    if abs(total - 1.0) > 1e-6:
        raise ValueError(f"ensemble weights must sum to 1, got {total}")
    return weights
