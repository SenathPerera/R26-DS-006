from __future__ import annotations

import argparse
from pathlib import Path

from stable_baselines3 import PPO, SAC, TD3

from .evaluate import evaluate_policy_on_users
from ..baselines.contextual_bandit import LinearUCBBandit, build_action_library
from ..users.simulated_user import SimulatedUserGenerator
from ..env.simulated_env import SimulatedAdaptiveAudioEnv
from ..utils.config import load_json_config
from ..utils.io import write_csv


SB3_LOADERS = {
    "ppo": PPO,
    "sac": SAC,
    "td3": TD3,
}


def main() -> None:
    parser = argparse.ArgumentParser(description="Evaluate saved adaptive audio models on held-out simulated users.")
    parser.add_argument("--root", type=str, default="rl_training")
    parser.add_argument("--algorithm", type=str, required=True, choices=["rule_based", "contextual_bandit", "ppo", "td3", "sac"])
    parser.add_argument("--seed", type=int, required=True)
    args = parser.parse_args()

    root = Path(args.root)
    experiment_config = load_json_config(root / "configs" / "experiment.json")
    reward_weights = load_json_config(root / "configs" / "reward.json")
    generator = SimulatedUserGenerator(args.seed)
    _ = generator.generate_users(experiment_config["train_user_count"], "train_user")
    _ = generator.generate_users(experiment_config["validation_user_count"], "val_user")
    test_users = generator.generate_users(experiment_config["test_user_count"], "test_user")

    if args.algorithm == "rule_based":
        action_fn_builder = lambda env: (lambda obs: env.action_space.low * 0.0)
    elif args.algorithm == "contextual_bandit":
        import json
        payload = json.loads((root / "results" / f"seed_{args.seed}" / "bandit_model.json").read_text(encoding="utf-8"))
        library = build_action_library(experiment_config["max_delta"])
        probe_env = SimulatedAdaptiveAudioEnv(test_users[0], reward_weights, episode_horizon=experiment_config["episode_horizon"], max_delta=experiment_config["max_delta"], seed=args.seed)
        bandit = LinearUCBBandit(probe_env.observation_space.shape[0], library)
        bandit.A = [__import__("numpy").array(matrix, dtype=float) for matrix in payload["A"]]
        bandit.b = [__import__("numpy").array(vector, dtype=float) for vector in payload["b"]]
        action_fn_builder = lambda env, bandit=bandit: (lambda obs: bandit.select_action(obs)[1].vector / env.max_delta)
    else:
        model_dir = root / "results" / f"seed_{args.seed}" / args.algorithm / f"{args.algorithm}_final.zip"
        model = SB3_LOADERS[args.algorithm].load(str(model_dir))
        action_fn_builder = lambda env, model=model: (lambda obs: model.predict(obs, deterministic=True)[0])

    result = evaluate_policy_on_users(
        users=test_users,
        reward_weights=reward_weights,
        action_fn_builder=action_fn_builder,
        episodes_per_user=experiment_config["evaluation_episodes_per_user"],
        episode_horizon=experiment_config["episode_horizon"],
        max_delta=experiment_config["max_delta"],
        seed=args.seed,
    )

    write_csv(
        root / "results" / f"seed_{args.seed}" / f"{args.algorithm}_saved_eval.csv",
        [
            {
                "algorithm": args.algorithm,
                "seed": args.seed,
                "mean_episode_reward": result.mean_episode_reward,
                "preference_satisfaction_score": result.preference_satisfaction_score,
                "safety_violation_rate": result.safety_violation_rate,
                "smoothness_score": result.smoothness_score,
                "intervention_frequency": result.intervention_frequency,
            }
        ],
    )


if __name__ == "__main__":
    main()
