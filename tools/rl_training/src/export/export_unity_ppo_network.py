from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import tempfile
from zipfile import ZipFile

import numpy as np
import torch


LAYER_KEYS = (
    ("mlp_extractor.policy_net.0.weight", "mlp_extractor.policy_net.0.bias"),
    ("mlp_extractor.policy_net.2.weight", "mlp_extractor.policy_net.2.bias"),
    ("action_net.weight", "action_net.bias"),
)


def load_policy_state(model_path: Path) -> dict[str, torch.Tensor]:
    with ZipFile(model_path, "r") as archive:
        if "policy.pth" not in archive.namelist():
            raise FileNotFoundError(f"policy.pth was not found in {model_path}")
        with tempfile.TemporaryDirectory() as directory:
            archive.extract("policy.pth", directory)
            return torch.load(Path(directory) / "policy.pth", map_location="cpu", weights_only=False)


def forward(state: dict[str, torch.Tensor], observation: np.ndarray) -> np.ndarray:
    value = torch.as_tensor(observation, dtype=torch.float32)
    for index, (weight_key, bias_key) in enumerate(LAYER_KEYS):
        value = torch.nn.functional.linear(value, state[weight_key], state[bias_key])
        if index < len(LAYER_KEYS) - 1:
            value = torch.tanh(value)
    return torch.clamp(value, -1.0, 1.0).detach().cpu().numpy().astype(np.float32)


def export_network(model_path: Path, output_path: Path, seed: int, max_delta: float, horizon: int) -> None:
    state = load_policy_state(model_path)
    layers = []
    for weight_key, bias_key in LAYER_KEYS:
        weight = state[weight_key].detach().cpu().numpy().astype(np.float32)
        bias = state[bias_key].detach().cpu().numpy().astype(np.float32)
        layers.append(
            {
                "inputSize": int(weight.shape[1]),
                "outputSize": int(weight.shape[0]),
                "weights": weight.reshape(-1).tolist(),
                "biases": bias.tolist(),
            }
        )

    observation_dimension = layers[0]["inputSize"]
    verification_observations = [
        np.full(observation_dimension, 0.5, dtype=np.float32),
        np.linspace(0.0, 1.0, observation_dimension, dtype=np.float32),
        np.random.default_rng(seed).uniform(0.0, 1.0, observation_dimension).astype(np.float32),
    ]

    payload = {
        "modelId": f"ppo_seed_{seed}_direct_mlp",
        "algorithm": "ppo",
        "seed": seed,
        "observationDimension": observation_dimension,
        "actionDimension": layers[-1]["outputSize"],
        "maxDelta": max_delta,
        "episodeHorizon": horizon,
        "activation": "tanh",
        "sourceModelSha256": hashlib.sha256(model_path.read_bytes()).hexdigest(),
        "layers": layers,
        "verificationObservations": [{"values": item.tolist()} for item in verification_observations],
        "verificationActions": [{"values": forward(state, item).tolist()} for item in verification_observations],
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description="Export an SB3 PPO policy MLP for direct Unity inference.")
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--seed", type=int, default=37)
    parser.add_argument("--max-delta", type=float, default=0.08)
    parser.add_argument("--episode-horizon", type=int, default=120)
    args = parser.parse_args()
    export_network(args.model.resolve(), args.output.resolve(), args.seed, args.max_delta, args.episode_horizon)
    print(f"Exported direct PPO network to {args.output.resolve()}")


if __name__ == "__main__":
    main()
