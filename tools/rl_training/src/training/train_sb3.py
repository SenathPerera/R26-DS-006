from __future__ import annotations

from pathlib import Path
from typing import Dict, List, Tuple

import numpy as np
import pandas as pd
from stable_baselines3 import PPO, SAC, TD3
from stable_baselines3.common.callbacks import EvalCallback
from stable_baselines3.common.monitor import Monitor
from stable_baselines3.common.vec_env import DummyVecEnv

from ..env.simulated_env import SimulatedAdaptiveAudioEnv
from ..utils.io import ensure_dir


ALGORITHM_REGISTRY = {
    "ppo": PPO,
    "sac": SAC,
    "td3": TD3,
}


def train_sb3_algorithm(
    algorithm_name: str,
    train_users,
    validation_users,
    reward_weights: Dict[str, float],
    config: Dict[str, float],
    total_timesteps: int,
    episode_horizon: int,
    max_delta: float,
    eval_frequency: int,
    seed: int,
    output_dir: str | Path,
):
    output_dir = ensure_dir(output_dir)
    algo_cls = ALGORITHM_REGISTRY[algorithm_name]

    def make_train_env(offset: int):
        user = train_users[offset % len(train_users)]
        return Monitor(
            SimulatedAdaptiveAudioEnv(
                user,
                reward_weights,
                episode_horizon=episode_horizon,
                max_delta=max_delta,
                seed=seed + offset,
                user_pool=train_users,
            )
        )

    train_env = DummyVecEnv([lambda offset=i: make_train_env(offset) for i in range(4)])
    eval_user = validation_users[0]
    eval_env = DummyVecEnv([lambda: Monitor(SimulatedAdaptiveAudioEnv(eval_user, reward_weights, episode_horizon=episode_horizon, max_delta=max_delta, seed=seed + 999))])

    model = algo_cls("MlpPolicy", train_env, seed=seed, verbose=0, tensorboard_log=str(output_dir / "tb"), **config)
    eval_callback = EvalCallback(
        eval_env,
        best_model_save_path=str(output_dir / "best_model"),
        log_path=str(output_dir / "eval_logs"),
        eval_freq=eval_frequency,
        deterministic=True,
        render=False,
    )
    model.learn(total_timesteps=total_timesteps, callback=eval_callback)
    model.save(str(output_dir / f"{algorithm_name}_final"))

    eval_csv = output_dir / "eval_logs" / "evaluations.npz"
    curve_path = output_dir / "training_curve.csv"
    curve_rows = _convert_eval_npz_to_rows(eval_csv)
    pd.DataFrame(curve_rows).to_csv(curve_path, index=False)
    return model, curve_path


def _convert_eval_npz_to_rows(npz_path: Path):
    if not npz_path.exists():
        return []
    data = np.load(npz_path, allow_pickle=True)
    timesteps = data["timesteps"]
    results = data["results"]
    rows = []
    for timestep, reward_values in zip(timesteps, results):
        rows.append(
            {
                "timesteps": int(timestep),
                "mean_reward": float(np.mean(reward_values)),
                "std_reward": float(np.std(reward_values)),
            }
        )
    return rows
