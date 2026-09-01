# Confidence-Aware Personalized Adaptive Audio Controller - RL Training Pipeline

This folder contains the Phase 1 simulated-user preference pretraining pipeline for Component E.

The pipeline compares:

- Rule-based baseline
- Contextual bandit baseline
- PPO
- TD3
- SAC

SAC is the recommended primary model.

This training setup uses simulated users generated from long-term audio preferences only.
It does not use real physiology. Stress and confidence are simulated proxies inside the environment.

## Export a Unity-readable trained policy

The Unity runtime does not load Stable-Baselines3 `.zip` files directly.
Instead, export a sampled policy surface that Unity can query at runtime:

```bash
cd tools/rl_training
python -m src.export.export_unity_policy --root . --algorithm ppo --seed 37
```

That writes a JSON sample set to:

`unity/Assets/StreamingAssets/AdaptiveAudioVR/Training/ppo_seed_37_unity_policy.json`

The current Unity bridge is designed around the PPO export because PPO is the strongest learned RL model in the current experiment results.
