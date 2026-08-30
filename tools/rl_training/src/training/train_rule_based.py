from __future__ import annotations

from typing import Dict, List

import numpy as np

from ..evaluation.evaluate import evaluate_policy_on_users


def build_rule_only_action_fn(env):
    def action_fn(_obs):
        return np.zeros(env.action_space.shape[0], dtype=np.float32)
    return action_fn
