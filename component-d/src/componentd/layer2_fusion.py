"""Layer 2b: the trained fusion model - the core contribution of PP2.

Fuses two views of the same voice:
  - emotion embedding (from the frozen pretrained encoder): WHAT emotion
  - prosody vector (from layer2_prosody): HOW the voice is behaving

A learned gate decides, per input, how much to trust each view, and a
small MLP head regresses valence + arousal. Every parameter in this file
is trained by us - this replaces PP1's hand-written lookup table.
"""

import torch
import torch.nn as nn

#need to work on this part


class GatedFusionModel(nn.Module):
    """emb (B, emb_dim) + pros (B, pros_dim) -> valence, arousal in [-1, 1]."""

    def __init__(self, emb_dim: int, pros_dim: int,
                 proj_dim: int = 128, hidden_dim: int = 64, dropout: float = 0.3):
        super().__init__()
        self.emb_dim = emb_dim
        self.pros_dim = pros_dim

        # Project both branches to the same width so they can be mixed.
        # LayerNorm keeps the two branches on comparable scales.
        self.proj_emb = nn.Sequential(
            nn.Linear(emb_dim, proj_dim), nn.LayerNorm(proj_dim), nn.ReLU(),
        )
        self.proj_pros = nn.Sequential(
            nn.Linear(pros_dim, proj_dim), nn.LayerNorm(proj_dim), nn.ReLU(),
        )

        # THE KEY IDEA: the gate sees both projected branches and outputs
        # a weight in (0,1) per dimension. fused = g*emb + (1-g)*prosody.
        # The model LEARNS when to trust emotion content vs voice physics.
        self.gate = nn.Sequential(
            nn.Linear(proj_dim * 2, proj_dim), nn.Sigmoid(),
        )

        # Regression head. Tanh bounds output to the valid V/A range [-1,1].
        self.head = nn.Sequential(
            nn.Linear(proj_dim, hidden_dim), nn.ReLU(), nn.Dropout(dropout),
            nn.Linear(hidden_dim, 2), nn.Tanh(),
        )

    def forward(self, emb: torch.Tensor, pros: torch.Tensor):
        e = self.proj_emb(emb)
        p = self.proj_pros(pros)
        g = self.gate(torch.cat([e, p], dim=-1))
        fused = g * e + (1.0 - g) * p
        out = self.head(fused)
        valence, arousal = out[:, 0], out[:, 1]
        # g is returned for explainability: mean(g) near 1 means the
        # decision leaned on emotion content, near 0 on prosody.
        return valence, arousal, g


def ccc(pred: torch.Tensor, target: torch.Tensor) -> torch.Tensor:
    """Concordance Correlation Coefficient - the standard metric for
    valence/arousal regression. 1 = perfect agreement. Unlike Pearson
    correlation it also punishes mean shifts and scale errors."""
    pred_mean, target_mean = pred.mean(), target.mean()
    pred_var = pred.var(unbiased=False)
    target_var = target.var(unbiased=False)
    covariance = ((pred - pred_mean) * (target - target_mean)).mean()
    return (2 * covariance) / (
        pred_var + target_var + (pred_mean - target_mean) ** 2 + 1e-8)


def ccc_loss(pred_v, pred_a, true_v, true_a) -> torch.Tensor:
    """Training loss: (1 - mean CCC) plus a small MSE term.
    The MSE term stabilises the first epochs, when batch CCC gradients
    are noisy; 0.2 weighting keeps CCC as the dominant objective."""
    loss_ccc = 1.0 - 0.5 * (ccc(pred_v, true_v) + ccc(pred_a, true_a))
    loss_mse = 0.5 * ((pred_v - true_v) ** 2 + (pred_a - true_a) ** 2).mean()
    return loss_ccc + 0.2 * loss_mse
