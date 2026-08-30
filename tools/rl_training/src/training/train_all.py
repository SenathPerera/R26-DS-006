from __future__ import annotations

import argparse
from pathlib import Path

from stable_baselines3 import PPO, SAC, TD3

from ..baselines.contextual_bandit import build_action_library
from ..evaluation.evaluate import evaluate_policy_on_users
from ..training.train_bandit import train_contextual_bandit
from ..training.train_rule_based import build_rule_only_action_fn
from ..training.train_sb3 import train_sb3_algorithm
from ..users.simulated_user import SimulatedUserGenerator
from ..utils.config import load_json_config
from ..utils.io import ensure_dir, write_csv, write_json
from ..utils.metrics import aggregate_metric_dicts
from ..utils.plotting import plot_comparison_bar, plot_training_curve
from ..utils.seeding import set_global_seed


def main() -> None:
    parser = argparse.ArgumentParser(description="Train and compare adaptive audio controllers.")
    parser.add_argument("--root", type=str, default="rl_training", help="Project root directory")
    args = parser.parse_args()

    root = Path(args.root)
    experiment_config = load_json_config(root / "configs" / "experiment.json")
    reward_weights = load_json_config(root / "configs" / "reward.json")

    all_result_rows = []
    seed_summaries = []

    for seed in experiment_config["seed_values"]:
        set_global_seed(seed)
        generator = SimulatedUserGenerator(seed)
        train_users = generator.generate_users(experiment_config["train_user_count"], "train_user")
        validation_users = generator.generate_users(experiment_config["validation_user_count"], "val_user")
        test_users = generator.generate_users(experiment_config["test_user_count"], "test_user")

        seed_output = ensure_dir(root / "results" / f"seed_{seed}")

        rule_eval = evaluate_policy_on_users(
            users=test_users,
            reward_weights=reward_weights,
            action_fn_builder=build_rule_only_action_fn,
            episodes_per_user=experiment_config["evaluation_episodes_per_user"],
            episode_horizon=experiment_config["episode_horizon"],
            max_delta=experiment_config["max_delta"],
            seed=seed,
        )
        all_result_rows.append(_result_row(seed, "rule_based", rule_eval))

        bandit, bandit_curve = train_contextual_bandit(
            train_users=train_users,
            reward_weights=reward_weights,
            episode_horizon=experiment_config["episode_horizon"],
            max_delta=experiment_config["max_delta"],
            training_episodes=experiment_config["bandit_training_episodes"],
            seed=seed,
            output_path=seed_output / "bandit_model.json",
        )
        write_csv(seed_output / "bandit_training_curve.csv", bandit_curve)

        bandit_eval = evaluate_policy_on_users(
            users=test_users,
            reward_weights=reward_weights,
            action_fn_builder=lambda env: (lambda obs: bandit.select_action(obs)[1].vector / env.max_delta),
            episodes_per_user=experiment_config["evaluation_episodes_per_user"],
            episode_horizon=experiment_config["episode_horizon"],
            max_delta=experiment_config["max_delta"],
            seed=seed,
        )
        all_result_rows.append(_result_row(seed, "contextual_bandit", bandit_eval))

        for algorithm_name in ("ppo", "td3", "sac"):
            model, curve_path = train_sb3_algorithm(
                algorithm_name=algorithm_name,
                train_users=train_users,
                validation_users=validation_users,
                reward_weights=reward_weights,
                config=experiment_config[algorithm_name],
                total_timesteps=experiment_config["sb3_total_timesteps"],
                episode_horizon=experiment_config["episode_horizon"],
                max_delta=experiment_config["max_delta"],
                eval_frequency=experiment_config["eval_frequency"],
                seed=seed,
                output_dir=seed_output / algorithm_name,
            )

            if curve_path.exists():
                plot_training_curve(curve_path, root / "plots" / f"{algorithm_name}_seed_{seed}_training_curve.png", f"{algorithm_name.upper()} Training Curve (Seed {seed})")

            eval_result = evaluate_policy_on_users(
                users=test_users,
                reward_weights=reward_weights,
                action_fn_builder=lambda env, model=model: (lambda obs: model.predict(obs, deterministic=True)[0]),
                episodes_per_user=experiment_config["evaluation_episodes_per_user"],
                episode_horizon=experiment_config["episode_horizon"],
                max_delta=experiment_config["max_delta"],
                seed=seed,
            )
            all_result_rows.append(_result_row(seed, algorithm_name, eval_result))

        seed_summaries.append({"seed": seed, "status": "completed"})

    write_csv(root / "results" / "all_results.csv", all_result_rows)
    aggregated = _aggregate_results(all_result_rows)
    write_csv(root / "results" / "aggregate_results.csv", aggregated)
    write_json(root / "results" / "run_summary.json", {"seeds": seed_summaries})
    _write_summary_report(root, aggregated)
    _plot_aggregate(root, aggregated)


def _result_row(seed, algorithm, result):
    return {
        "seed": seed,
        "algorithm": algorithm,
        "mean_episode_reward": result.mean_episode_reward,
        "preference_satisfaction_score": result.preference_satisfaction_score,
        "safety_violation_rate": result.safety_violation_rate,
        "smoothness_score": result.smoothness_score,
        "intervention_frequency": result.intervention_frequency,
    }


def _aggregate_results(rows):
    import pandas as pd

    frame = pd.DataFrame(rows)
    grouped_rows = []
    for algorithm, group in frame.groupby("algorithm"):
        grouped_rows.append(
            {
                "algorithm": algorithm,
                "mean_episode_reward": float(group["mean_episode_reward"].mean()),
                "mean_episode_reward_std": float(group["mean_episode_reward"].std(ddof=0)),
                "preference_satisfaction_score": float(group["preference_satisfaction_score"].mean()),
                "preference_satisfaction_score_std": float(group["preference_satisfaction_score"].std(ddof=0)),
                "safety_violation_rate": float(group["safety_violation_rate"].mean()),
                "safety_violation_rate_std": float(group["safety_violation_rate"].std(ddof=0)),
                "smoothness_score": float(group["smoothness_score"].mean()),
                "smoothness_score_std": float(group["smoothness_score"].std(ddof=0)),
                "intervention_frequency": float(group["intervention_frequency"].mean()),
                "intervention_frequency_std": float(group["intervention_frequency"].std(ddof=0)),
            }
        )
    return grouped_rows


def _write_summary_report(root: Path, aggregate_rows) -> None:
    report_lines = [
        "# Adaptive Audio RL Training Summary",
        "",
        "This report summarizes the Phase 1 preference-only simulated-user pretraining results.",
        "",
        "## Aggregated Results",
        "",
        "| Algorithm | Mean Episode Reward | Preference Satisfaction | Safety Violation Rate | Smoothness | Intervention Frequency |",
        "| --- | ---: | ---: | ---: | ---: | ---: |",
    ]
    for row in aggregate_rows:
        report_lines.append(
            f"| {row['algorithm']} | {row['mean_episode_reward']:.3f} | {row['preference_satisfaction_score']:.3f} | "
            f"{row['safety_violation_rate']:.3f} | {row['smoothness_score']:.3f} | {row['intervention_frequency']:.3f} |"
        )

    report_lines.extend(
        [
            "",
            "## Recommended Primary Model",
            "",
            "SAC is the recommended main model because this problem uses a continuous bounded action space, preference-sensitive reward shaping, and requires stable learning under noisy simulated stress/confidence dynamics.",
            "",
            "## Important Honesty Note",
            "",
            "This pipeline uses simulated users created from long-term audio preferences only. It is not yet true physiological optimization.",
        ]
    )
    (root / "results" / "summary_report.md").write_text("\n".join(report_lines), encoding="utf-8")


def _plot_aggregate(root: Path, aggregate_rows) -> None:
    import pandas as pd

    frame = pd.DataFrame(aggregate_rows)
    plot_comparison_bar(frame, "mean_episode_reward", str(root / "plots" / "algorithm_reward_comparison.png"), "Algorithm Comparison: Mean Episode Reward")
    plot_comparison_bar(frame, "preference_satisfaction_score", str(root / "plots" / "algorithm_preference_match_comparison.png"), "Algorithm Comparison: Preference Satisfaction")
    plot_comparison_bar(frame, "safety_violation_rate", str(root / "plots" / "algorithm_safety_violation_comparison.png"), "Algorithm Comparison: Safety Violation Rate")


if __name__ == "__main__":
    main()
