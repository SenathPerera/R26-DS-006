"""Layer 2b tests: architecture sanity - shapes, output ranges, loss
behaviour, and the overfit-one-batch check that catches most training
bugs before any GPU time is spent."""

import sys
from pathlib import Path

import torch

sys.path.insert(0, str(Path(__file__).parent.parent))
from componentd.layer2_fusion import GatedFusionModel, ccc, ccc_loss

EMB_DIM, PROS_DIM = 1024, 21


def make_model():
    torch.manual_seed(0)
    return GatedFusionModel(emb_dim=EMB_DIM, pros_dim=PROS_DIM)


def test_forward_shapes_and_ranges():
    model = make_model()
    v, a, g = model(torch.randn(8, EMB_DIM), torch.randn(8, PROS_DIM))
    assert v.shape == (8,) and a.shape == (8,)
    assert v.abs().max() <= 1.0 and a.abs().max() <= 1.0   # tanh bound
    assert g.min() >= 0.0 and g.max() <= 1.0               # sigmoid bound


def test_ccc_perfect_agreement_is_one():
    x = torch.randn(100)
    assert float(ccc(x, x)) > 0.999


def test_ccc_penalises_mean_shift():
    x = torch.randn(100)
    assert float(ccc(x + 2.0, x)) < 0.5


def test_model_can_overfit_one_batch():
    """A healthy model must drive the loss down hard on one small
    fixed batch. If it cannot, something is broken in the architecture
    or the loss - cheapest possible pre-training check."""
    torch.manual_seed(0)
    model = make_model()
    emb, pros = torch.randn(16, EMB_DIM), torch.randn(16, PROS_DIM)
    tv, ta = torch.rand(16) * 2 - 1, torch.rand(16) * 2 - 1
    opt = torch.optim.Adam(model.parameters(), lr=1e-3)

    first = None
    for _ in range(300):
        opt.zero_grad()
        pv, pa, _ = model(emb, pros)
        loss = ccc_loss(pv, pa, tv, ta)
        loss.backward()
        opt.step()
        if first is None:
            first = float(loss.detach())
    last = float(loss.detach())
    assert last < first * 0.3, f"loss barely moved: {first} -> {last}"


def test_gradients_reach_both_branches():
    """Both inputs must receive gradient - otherwise one branch is dead."""
    model = make_model()
    emb = torch.randn(4, EMB_DIM, requires_grad=True)
    pros = torch.randn(4, PROS_DIM, requires_grad=True)
    v, a, _ = model(emb, pros)
    (v.sum() + a.sum()).backward()
    assert emb.grad.abs().sum() > 0
    assert pros.grad.abs().sum() > 0
