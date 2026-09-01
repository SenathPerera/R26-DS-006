from __future__ import annotations

from dataclasses import dataclass
from typing import Dict
import numpy as np

from .audio_state import AudioControlState
from ..users.simulated_user import SimulatedUser


@dataclass
class RewardBreakdown:
    total_reward: float
    preference_match_score: float
    calmness_score: float
    stability_score: float
    abrupt_change_penalty: float
    low_confidence_overreaction_penalty: float
    excessive_novelty_penalty: float
    unnecessary_intervention_penalty: float


class RewardCalculator:
    def __init__(self, weights: Dict[str, float]) -> None:
        self.weights = weights

    def compute(
        self,
        user: SimulatedUser,
        current_state: AudioControlState,
        previous_state: AudioControlState,
        stress: float,
        confidence: float,
        action: np.ndarray,
        novelty_count: int,
    ) -> RewardBreakdown:
        preference_match = self._preference_match(user, current_state)
        calmness_score = self._calmness_score(user, current_state, stress)
        stability_score = 1.0 - float(np.mean(np.abs(current_state.as_vector() - previous_state.as_vector())))
        abrupt_change_penalty = float(np.mean(np.abs(action)))
        low_confidence_overreaction_penalty = abrupt_change_penalty * (1.0 - confidence)
        excessive_novelty_penalty = min(1.0, novelty_count / 25.0)
        unnecessary_intervention_penalty = abrupt_change_penalty * max(0.0, 0.45 - stress)

        total = (
            self.weights["preference_match_weight"] * preference_match
            + self.weights["calmness_weight"] * calmness_score
            + self.weights["stability_weight"] * stability_score
            - self.weights["abrupt_change_penalty_weight"] * abrupt_change_penalty
            - self.weights["low_confidence_overreaction_penalty_weight"] * low_confidence_overreaction_penalty
            - self.weights["excessive_novelty_penalty_weight"] * excessive_novelty_penalty
            - self.weights["unnecessary_intervention_penalty_weight"] * unnecessary_intervention_penalty
        )

        return RewardBreakdown(
            total_reward=float(total),
            preference_match_score=float(preference_match),
            calmness_score=float(calmness_score),
            stability_score=float(stability_score),
            abrupt_change_penalty=float(abrupt_change_penalty),
            low_confidence_overreaction_penalty=float(low_confidence_overreaction_penalty),
            excessive_novelty_penalty=float(excessive_novelty_penalty),
            unnecessary_intervention_penalty=float(unnecessary_intervention_penalty),
        )

    def _preference_match(self, user: SimulatedUser, state: AudioControlState) -> float:
        weights = {
            "intensity": 1.0,
            "density": 0.85,
            "brightness": 1.0,
            "tempo": 0.95,
            "fade": 0.65,
            "music_mix": 1.0,
            "ambient_mix": 1.0,
        }
        score_sum = 0.0
        weight_sum = 0.0
        state_dict = state.to_dict()
        for key, weight in weights.items():
            target = user.target_profile[key]
            tolerance = user.tolerance_widths[key]
            distance = abs(state_dict[key] - target)
            match = float(np.exp(-((distance ** 2) / max(1e-6, 2.0 * tolerance ** 2))))
            score_sum += match * weight
            weight_sum += weight
        return score_sum / weight_sum

    def _calmness_score(self, user: SimulatedUser, state: AudioControlState, stress: float) -> float:
        calm_prior = (
            (1.0 - state.intensity) * 0.25
            + (1.0 - state.density) * 0.20
            + (1.0 - state.brightness) * 0.15
            + state.ambient_mix * 0.15
            + state.fade * 0.10
            + (1.0 - state.tempo) * 0.15
        )
        responsiveness_bonus = (1.0 - stress) * user.relaxation_responsiveness * 0.15
        return float(np.clip(calm_prior + responsiveness_bonus, 0.0, 1.0))
