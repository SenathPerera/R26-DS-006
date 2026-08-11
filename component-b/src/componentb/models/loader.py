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

    Name kept as `load_model` for the existing callers; the artifact it
    loads is the MS-CGCA network, not the superseded CNN-LSTM.
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


def load_ft_model(name="mscgca_finetuned"):
    """Load the personalised fine-tuned MS-CGCA head.

    Third member of the shipped ensemble (docs/ARCHITECTURE.md §2,
    blended as `p_ft` in notebook-newmodel.ipynb cell 7). Unlike the
    other two this one is per-subject, so it is optional: a brand new
    user has no fine-tuned head yet and the blend falls back to the
    population pair.
    """
    import tensorflow as tf
    path = ARTIFACTS / "models" / f"{name}.keras"
    if not path.exists():
        return None
    return tf.keras.models.load_model(path, compile=False)


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


def load_ensemble_weights(name="model_config"):
    """Blend weights for (p_ft, p_xgb, p_cnn), summing to 1.

    These are NOT derivable from the notebook as it stands: cell 7 picks
    `w_star` per outer fold by nested CV, so there are 15 triples, not
    one. Whichever triple is deployed is a decision that has to be made
    and recorded — this function refuses to guess.
    """
    cfg = load_config(name)
    try:
        w = cfg["ensemble_weights"]
        weights = (float(w["w_ft"]), float(w["w_xgb"]), float(w["w_cnn"]))
    except (KeyError, TypeError) as exc:
        raise KeyError(
            "model_config.json has no usable 'ensemble_weights' "
            "{w_ft, w_xgb, w_cnn}. Export the chosen triple from "
            "notebook-newmodel.ipynb cell 7 — do not substitute defaults, "
            "the blend is what produces the reported F1."
        ) from exc

    total = sum(weights)
    if abs(total - 1.0) > 1e-6:
        raise ValueError(f"ensemble weights must sum to 1, got {total}")
    return weights
