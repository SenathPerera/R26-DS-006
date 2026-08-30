from __future__ import annotations

import argparse
from pathlib import Path
import tempfile
from typing import Dict, List
from zipfile import ZipFile

import numpy as np
import torch
from stable_baselines3 import PPO, SAC, TD3

from ..env.simulated_env import SimulatedAdaptiveAudioEnv
from ..users.simulated_user import SimulatedUserGenerator
from ..utils.config import load_json_config
from ..utils.io import ensure_dir, write_json


SB3_LOADERS = {
    "ppo": PPO,
    "sac": SAC,
    "td3": TD3,
}


OBSERVATION_FEATURES = [
    "pref_intensity",
    "pref_density",
    "pref_brightness",
    "pref_tempo",
    "pref_music_mix",
    "pref_ambient_mix",
    "pref_rhythm_amount",
    "pref_nature_level",
    "pref_reverb_amount",
    "pref_novelty_amount",
    "pref_dissonance_allowance",
    "pref_relaxation_responsiveness",
    "pref_confidence_sensitivity",
    "state_intensity",
    "state_density",
    "state_brightness",
    "state_tempo",
    "state_fade",
    "state_music_mix",
    "state_ambient_mix",
    "stress",
    "confidence",
    "stress_trend_shifted",
    "confidence_trend_shifted",
    "recent_delta_intensity_shifted",
    "recent_delta_density_shifted",
    "recent_delta_brightness_shifted",
    "recent_delta_tempo_shifted",
    "recent_delta_fade_shifted",
    "recent_delta_music_mix_shifted",
    "recent_delta_ambient_mix_shifted",
    "session_progress",
    "novelty_count_normalized",
    "recent_action_size_normalized",
]


ACTION_FEATURES = [
    "delta_intensity",
    "delta_density",
    "delta_brightness",
    "delta_tempo",
    "delta_fade",
    "delta_music_mix",
    "delta_ambient_mix",
]


def export_unity_policy(
    root: Path,
    algorithm: str,
    seed: int,
    output_path: Path,
    export_user_count: int,
    episodes_per_user: int,
    step_limit: int,
) -> Path:
    experiment_config = load_json_config(root / "configs" / "experiment.json")
    reward_weights = load_json_config(root / "configs" / "reward.json")

    model_path = root / "results" / f"seed_{seed}" / algorithm / f"{algorithm}_final.zip"
    if not model_path.exists():
        raise FileNotFoundError(f"Could not find trained model at {model_path}")

    if algorithm == "ppo":
        policy_state = _load_policy_state_dict(model_path)
        predict_action = lambda obs: _predict_ppo_action(policy_state, obs)
    else:
        model = SB3_LOADERS[algorithm].load(str(model_path))
        predict_action = lambda obs, model=model: model.predict(obs, deterministic=True)[0]

    generator = SimulatedUserGenerator(seed + 5000)
    export_users = generator.generate_users(export_user_count, "unity_export_user")

    samples: List[Dict[str, object]] = []
    sample_counter = 0
    max_delta = float(experiment_config["max_delta"])
    episode_horizon = int(experiment_config["episode_horizon"])

    for user_index, user in enumerate(export_users):
        env = SimulatedAdaptiveAudioEnv(
            user,
            reward_weights,
            episode_horizon=episode_horizon,
            max_delta=max_delta,
            seed=seed + (user_index * 17),
        )

        for episode_index in range(episodes_per_user):
            obs, _ = env.reset(seed=seed + (user_index * 101) + episode_index)
            max_steps = min(step_limit, episode_horizon)

            for step_index in range(max_steps):
                action = predict_action(obs)
                samples.append(
                    {
                        "observation": np.asarray(obs, dtype=np.float32).tolist(),
                        "action": np.asarray(action, dtype=np.float32).tolist(),
                    }
                )
                sample_counter += 1

                obs, _, terminated, truncated, _ = env.step(action)
                if terminated or truncated:
                    break

    payload = {
        "modelId": f"{algorithm}_seed_{seed}_unity_knn",
        "algorithm": algorithm,
        "seed": seed,
        "observationDimension": len(OBSERVATION_FEATURES),
        "actionDimension": len(ACTION_FEATURES),
        "maxDelta": max_delta,
        "episodeHorizon": episode_horizon,
        "kNeighbors": 8,
        "exportUserCount": export_user_count,
        "episodesPerUser": episodes_per_user,
        "stepLimit": step_limit,
        "sampleCount": sample_counter,
        "observationFeatures": OBSERVATION_FEATURES,
        "actionFeatures": ACTION_FEATURES,
        "samples": samples,
    }

    write_json(output_path, payload)
    return output_path


def _load_policy_state_dict(model_zip_path: Path):
    with ZipFile(model_zip_path, "r") as zip_file:
        if "policy.pth" not in zip_file.namelist():
            raise FileNotFoundError(f"policy.pth was not found inside {model_zip_path}")

        with tempfile.TemporaryDirectory() as temp_directory:
            zip_file.extract("policy.pth", path=temp_directory)
            policy_path = Path(temp_directory) / "policy.pth"
            return torch.load(policy_path, map_location="cpu", weights_only=False)


def _predict_ppo_action(policy_state, observation: np.ndarray) -> np.ndarray:
    obs_tensor = torch.as_tensor(np.asarray(observation, dtype=np.float32))
    latent = _linear_forward(obs_tensor, policy_state["mlp_extractor.policy_net.0.weight"], policy_state["mlp_extractor.policy_net.0.bias"])
    latent = torch.tanh(latent)
    latent = _linear_forward(latent, policy_state["mlp_extractor.policy_net.2.weight"], policy_state["mlp_extractor.policy_net.2.bias"])
    latent = torch.tanh(latent)
    action_mean = _linear_forward(latent, policy_state["action_net.weight"], policy_state["action_net.bias"])
    action = torch.clamp(action_mean, -1.0, 1.0)
    return action.detach().cpu().numpy().astype(np.float32)


def _linear_forward(x: torch.Tensor, weight: torch.Tensor, bias: torch.Tensor) -> torch.Tensor:
    return torch.nn.functional.linear(x, weight, bias)


def main() -> None:
    parser = argparse.ArgumentParser(description="Export a trained SB3 adaptive audio policy to a Unity-readable sample set.")
    parser.add_argument("--root", type=str, default="rl_training")
    parser.add_argument("--algorithm", type=str, default="ppo", choices=["ppo", "sac", "td3"])
    parser.add_argument("--seed", type=int, default=37)
    parser.add_argument("--export-user-count", type=int, default=32)
    parser.add_argument("--episodes-per-user", type=int, default=2)
    parser.add_argument("--step-limit", type=int, default=80)
    parser.add_argument("--output", type=str, default="")
    args = parser.parse_args()

    root = Path(args.root).resolve()
    repo_root = root.parent
    output = Path(args.output).resolve() if args.output else repo_root / "Assets" / "StreamingAssets" / "Training" / f"{args.algorithm}_seed_{args.seed}_unity_policy.json"
    ensure_dir(output.parent)

    result_path = export_unity_policy(
        root=root,
        algorithm=args.algorithm,
        seed=args.seed,
        output_path=output,
        export_user_count=args.export_user_count,
        episodes_per_user=args.episodes_per_user,
        step_limit=args.step_limit,
    )
    print(f"Exported Unity policy sample set to {result_path}")


if __name__ == "__main__":
    main()
