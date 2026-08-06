"""Load trained artifacts.

The scaler MUST be the one fitted during training. Normalising live
data with different statistics degrades predictions silently — no
error is raised, the answers are just wrong.
"""

import json
import pickle
from pathlib import Path

ARTIFACTS = Path(__file__).resolve().parents[3] / "artifacts"


def load_model(name="cnn_population"):
    """Load the shipped Keras model."""
    import tensorflow as tf
    path = ARTIFACTS / "models" / f"{name}.keras"
    if not path.exists():
        raise FileNotFoundError(
            f"Model not found: {path}\n"
            "Export it from your training notebook first."
        )
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
