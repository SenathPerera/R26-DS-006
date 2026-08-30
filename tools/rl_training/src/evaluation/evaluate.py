from __future__ import annotations

from dataclasses import dataclass
from typing import Callable, Dict, List

import numpy as np

from ..env.simulated_env import SimulatedAdaptiveAudioEnv
from ..baselines.contextual_bandit import LinearUCBBandit, build_action_library


@dataclass
class EvaluationResult:
    mean_episode_reward: float
    preference_satisfaction_score: float
    safety_violation_rate: float
    smoothness_score: float
    intervention_frequency: float


def evaluate_policy_on_users(
    users,
    reward_weights: Dict[str, float],
    action_fn_builder: Callable[[SimulatedAdaptiveAudioEnv], Callable[[np.ndarray], np.ndarray]],
    episodes_per_user: int,
    episode_horizon: int,
    max_delta: float,
    seed: int,
) -> EvaluationResult:
    rewards: List[float] = []
    pref_scores: List[float] = []
    violation_rates: List[float] = []
    smoothness_scores: List[float] = []
    intervention_rates: List[float] = []

    for user_index, user in enumerate(users):
        env = SimulatedAdaptiveAudioEnv(user=user, reward_weights=reward_weights, episode_horizon=episode_horizon, max_delta=max_delta, seed=seed + user_index)
        action_fn = action_fn_builder(env)

        for episode in range(episodes_per_user):
            obs, _ = env.reset(seed=seed + user_index + episode)
            done = False
            episode_reward = 0.0
            pref_episode = []
            violations = 0
            smoothness = []
            interventions = 0
            while not done:
                action = action_fn(obs)
                obs, reward, terminated, truncated, info = env.step(action)
                done = terminated or truncated
                episode_reward += reward
                pref_episode.append(info["preference_match_score"])
                violations += 1 if info["safety_violation"] else 0
                smoothness.append(1.0 - info["abrupt_change_penalty"])
                interventions += 1 if float(np.mean(np.abs(np.asarray(info["safe_action"])))) > 0.01 else 0

            rewards.append(episode_reward)
            pref_scores.append(float(np.mean(pref_episode)))
            violation_rates.append(violations / max(1, env.step_count))
            smoothness_scores.append(float(np.mean(smoothness)))
            intervention_rates.append(interventions / max(1, env.step_count))

    return EvaluationResult(
        mean_episode_reward=float(np.mean(rewards)),
        preference_satisfaction_score=float(np.mean(pref_scores)),
        safety_violation_rate=float(np.mean(violation_rates)),
        smoothness_score=float(np.mean(smoothness_scores)),
        intervention_frequency=float(np.mean(intervention_rates)),
    )
