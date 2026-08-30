from __future__ import annotations

from dataclasses import dataclass
from typing import List, Tuple

import numpy as np


@dataclass(frozen=True)
class BanditAction:
    name: str
    vector: np.ndarray


class LinearUCBBandit:
    def __init__(self, context_dim: int, action_library: List[BanditAction], alpha: float = 0.8) -> None:
        self.context_dim = context_dim
        self.action_library = action_library
        self.alpha = alpha
        self.A = [np.eye(context_dim, dtype=np.float64) for _ in action_library]
        self.b = [np.zeros(context_dim, dtype=np.float64) for _ in action_library]

    def select_action(self, context: np.ndarray) -> Tuple[int, BanditAction]:
        x = context.astype(np.float64)
        scores = []
        for index, action in enumerate(self.action_library):
            A_inv = np.linalg.inv(self.A[index])
            theta = A_inv @ self.b[index]
            mean = theta.T @ x
            uncertainty = self.alpha * np.sqrt(x.T @ A_inv @ x)
            scores.append(mean + uncertainty)
        action_index = int(np.argmax(scores))
        return action_index, self.action_library[action_index]

    def update(self, action_index: int, context: np.ndarray, reward: float) -> None:
        x = context.astype(np.float64)
        self.A[action_index] += np.outer(x, x)
        self.b[action_index] += reward * x


def build_action_library(max_delta: float) -> List[BanditAction]:
    def vec(*values: float) -> np.ndarray:
        return np.array(values, dtype=np.float32) * max_delta

    return [
        BanditAction("NoChange", vec(0, 0, 0, 0, 0, 0, 0)),
        BanditAction("Soothe", vec(-0.6, -0.5, -0.4, -0.4, 0.3, -0.4, 0.4)),
        BanditAction("Activate", vec(0.6, 0.5, 0.4, 0.4, -0.2, 0.4, -0.4)),
        BanditAction("Brighten", vec(0.0, 0.1, 0.6, 0.1, -0.1, 0.1, -0.1)),
        BanditAction("Darken", vec(-0.1, -0.1, -0.6, -0.1, 0.2, -0.1, 0.1)),
        BanditAction("IncreaseAmbient", vec(-0.1, -0.1, -0.1, -0.1, 0.1, -0.6, 0.6)),
        BanditAction("IncreaseMusic", vec(0.1, 0.1, 0.1, 0.1, -0.1, 0.6, -0.6)),
    ]
