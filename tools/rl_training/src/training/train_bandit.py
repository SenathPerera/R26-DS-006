from __future__ import annotations

from pathlib import Path
from typing import Dict, List

import numpy as np

from ..baselines.contextual_bandit import LinearUCBBandit, build_action_library
from ..env.simulated_env import SimulatedAdaptiveAudioEnv
from ..utils.io import write_json


def train_contextual_bandit(
    train_users,
    reward_weights: Dict[str, float],
    episode_horizon: int,
    max_delta: float,
    training_episodes: int,
    seed: int,
    output_path: str | Path,
):
    probe_env = SimulatedAdaptiveAudioEnv(train_users[0], reward_weights, episode_horizon=episode_horizon, max_delta=max_delta, seed=seed)
    bandit = LinearUCBBandit(context_dim=probe_env.observation_space.shape[0], action_library=build_action_library(max_delta=max_delta))

    reward_curve = []
    for episode in range(training_episodes):
        user = train_users[episode % len(train_users)]
        env = SimulatedAdaptiveAudioEnv(user, reward_weights, episode_horizon=episode_horizon, max_delta=max_delta, seed=seed + episode)
        obs, _ = env.reset(seed=seed + episode)
        total_reward = 0.0
        done = False
        while not done:
            action_index, action = bandit.select_action(obs)
            next_obs, reward, terminated, truncated, _ = env.step(action.vector / max_delta)
            bandit.update(action_index, obs, reward)
            obs = next_obs
            total_reward += reward
            done = terminated or truncated
        reward_curve.append({"episode": episode, "mean_reward": total_reward})

    payload = {
        "alpha": bandit.alpha,
        "action_names": [a.name for a in bandit.action_library],
        "A": [matrix.tolist() for matrix in bandit.A],
        "b": [vector.tolist() for vector in bandit.b],
        "reward_curve": reward_curve,
    }
    write_json(output_path, payload)
    return bandit, reward_curve
