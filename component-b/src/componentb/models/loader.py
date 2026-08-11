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
