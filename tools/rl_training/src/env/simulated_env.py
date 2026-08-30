from __future__ import annotations

from collections import deque
from dataclasses import asdict
from typing import Any, Deque, Dict, Optional

import gymnasium as gym
from gymnasium import spaces
import numpy as np

from .audio_state import AudioControlState
from .reward import RewardCalculator, RewardBreakdown
from .rule_based import RuleBasedAdaptiveController
from .safety import ConfidenceAwareSafetyFilter
from ..users.preferences import PREFERENCE_VALUE_MAP
from ..users.simulated_user import SimulatedUser


class SimulatedAdaptiveAudioEnv(gym.Env):
    metadata = {"render_modes": []}

    def __init__(
        self,
        user: SimulatedUser,
        reward_weights: Dict[str, float],
        episode_horizon: int = 120,
        max_delta: float = 0.08,
        seed: int = 0,
        user_pool: Optional[list[SimulatedUser]] = None,
    ) -> None:
        super().__init__()
        self.user = user
        self.user_pool = user_pool
        self.episode_horizon = episode_horizon
        self.max_delta = max_delta
        self.rng = np.random.default_rng(seed)
        self.reward_calculator = RewardCalculator(reward_weights)
        self.rule_controller = RuleBasedAdaptiveController(max_delta=max_delta)
        self.safety_filter = ConfidenceAwareSafetyFilter(max_delta=max_delta)

        self.action_space = spaces.Box(low=-1.0, high=1.0, shape=(7,), dtype=np.float32)
        self.observation_space = spaces.Box(low=0.0, high=1.0, shape=(34,), dtype=np.float32)

        self.current_state = self._baseline_state()
        self.previous_state = self.current_state.copy()
        self.stress = 0.5
        self.confidence = 0.8
        self.previous_stress = self.stress
        self.previous_confidence = self.confidence
        self.novelty_count = 0
        self.step_count = 0
        self.last_residual_action = np.zeros(7, dtype=np.float32)
        self.recent_actions: Deque[np.ndarray] = deque(maxlen=3)
        self.recent_actions.append(np.zeros(7, dtype=np.float32))
        self.last_info: Dict[str, Any] = {}

    def reset(self, *, seed: Optional[int] = None, options: Optional[Dict[str, Any]] = None):
        super().reset(seed=seed)
        if seed is not None:
            self.rng = np.random.default_rng(seed)
        if self.user_pool:
            self.user = self.user_pool[int(self.rng.integers(0, len(self.user_pool)))]
        self.current_state = self._baseline_state()
        self.previous_state = self.current_state.copy()
        self.stress = float(self.rng.uniform(0.35, 0.75))
        self.confidence = float(self.rng.uniform(0.65, 0.95))
        self.previous_stress = self.stress
        self.previous_confidence = self.confidence
        self.novelty_count = 0
        self.step_count = 0
        self.last_residual_action = np.zeros(7, dtype=np.float32)
        self.recent_actions.clear()
        self.recent_actions.append(np.zeros(7, dtype=np.float32))
        obs = self._build_observation()
        return obs, {}

    def step(self, action: np.ndarray):
        residual_action = np.clip(np.asarray(action, dtype=np.float32), -1.0, 1.0) * self.max_delta
        baseline_state = self._baseline_state()
        baseline_action = self.rule_controller.get_action(self.stress, self.confidence, self.current_state, baseline_state)
        combined_action = baseline_action + residual_action

        safety_result = self.safety_filter.apply(combined_action, self.current_state, baseline_state, self.confidence)

        self.previous_state = self.current_state.copy()
        self.current_state = safety_result.safe_state
        self.last_residual_action = residual_action
        self.recent_actions.append(residual_action.copy())

        if float(np.mean(np.abs(residual_action))) > 0.015:
            self.novelty_count += 1

        self.previous_stress = self.stress
        self.previous_confidence = self.confidence
        self._update_proxies(safety_result.safe_action)

        reward_breakdown = self.reward_calculator.compute(
            user=self.user,
            current_state=self.current_state,
            previous_state=self.previous_state,
            stress=self.stress,
            confidence=self.confidence,
            action=safety_result.safe_action,
            novelty_count=self.novelty_count,
        )

        self.step_count += 1
        terminated = self.step_count >= self.episode_horizon
        truncated = False
        obs = self._build_observation()

        self.last_info = {
            "preference_match_score": reward_breakdown.preference_match_score,
            "calmness_score": reward_breakdown.calmness_score,
            "stability_score": reward_breakdown.stability_score,
            "abrupt_change_penalty": reward_breakdown.abrupt_change_penalty,
            "low_confidence_overreaction_penalty": reward_breakdown.low_confidence_overreaction_penalty,
            "excessive_novelty_penalty": reward_breakdown.excessive_novelty_penalty,
            "unnecessary_intervention_penalty": reward_breakdown.unnecessary_intervention_penalty,
            "safety_mode": safety_result.safety_mode,
            "safety_violation": safety_result.safety_violation,
            "stress": self.stress,
            "confidence": self.confidence,
            "baseline_action": baseline_action.tolist(),
            "residual_action": residual_action.tolist(),
            "safe_action": safety_result.safe_action.tolist(),
            "audio_state": self.current_state.to_dict(),
        }
        return obs, reward_breakdown.total_reward, terminated, truncated, self.last_info

    def _build_observation(self) -> np.ndarray:
        pref = self._encode_preferences()
        current = self.current_state.as_vector()
        trends = np.array(
            [
                self.stress,
                self.confidence,
                np.clip(self.stress - self.previous_stress + 0.5, 0.0, 1.0),
                np.clip(self.confidence - self.previous_confidence + 0.5, 0.0, 1.0),
            ],
            dtype=np.float32,
        )
        action_hist = np.mean(np.stack(list(self.recent_actions), axis=0), axis=0).astype(np.float32)
        time_features = np.array(
            [
                self.step_count / max(1, self.episode_horizon),
                min(1.0, self.novelty_count / 20.0),
                min(1.0, np.mean(np.abs(action_hist)) / max(1e-6, self.max_delta)),
            ],
            dtype=np.float32,
        )
        return np.concatenate([pref, current, trends, action_hist + 0.5, time_features]).astype(np.float32)

    def _encode_preferences(self) -> np.ndarray:
        target = self.user.target_profile
        pref_vector = np.array(
            [
                target["intensity"],
                target["density"],
                target["brightness"],
                target["tempo"],
                target["music_mix"],
                target["ambient_mix"],
                target["rhythm_amount"],
                target["nature_level"],
                target["reverb_amount"],
                target["novelty_amount"],
                target["dissonance_allowance"],
                self.user.relaxation_responsiveness,
                self.user.confidence_sensitivity,
            ],
            dtype=np.float32,
        )
        return pref_vector

    def _baseline_state(self) -> AudioControlState:
        target = self.user.target_profile
        return AudioControlState(
            intensity=target["intensity"],
            density=target["density"],
            brightness=target["brightness"],
            tempo=target["tempo"],
            fade=target["fade"],
            music_mix=target["music_mix"],
            ambient_mix=target["ambient_mix"],
        )

    def _update_proxies(self, safe_action: np.ndarray) -> None:
        target = self.user.target_profile
        state_dict = self.current_state.to_dict()
        distance = np.mean(
            [
                abs(state_dict["intensity"] - target["intensity"]),
                abs(state_dict["density"] - target["density"]),
                abs(state_dict["brightness"] - target["brightness"]),
                abs(state_dict["tempo"] - target["tempo"]),
                abs(state_dict["fade"] - target["fade"]),
                abs(state_dict["music_mix"] - target["music_mix"]),
                abs(state_dict["ambient_mix"] - target["ambient_mix"]),
            ]
        )
        action_size = float(np.mean(np.abs(safe_action)))
        noise = float(self.rng.normal(0.0, 0.015))
        stress_delta = ((distance - 0.18) * 0.18) + (action_size * 0.18) + noise
        self.stress = float(np.clip(self.stress + stress_delta - (0.10 * self.user.relaxation_responsiveness), 0.0, 1.0))

        confidence_noise = float(self.rng.normal(0.0, 0.01))
        confidence_delta = -(action_size * 0.20 * self.user.confidence_sensitivity) - max(0.0, self.stress - 0.55) * 0.08 + confidence_noise
        self.confidence = float(np.clip(self.confidence + confidence_delta + 0.02, 0.0, 1.0))
