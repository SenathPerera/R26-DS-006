# Component E Audio RL Agent

## Scope

This implementation controls meditation and fixed scene-ambient playback. It does not generate music inside the RL policy and it does not modify or call the visual LinUCB agent.

The primary runtime policy is the **directly exported PPO actor network** trained with seed 37. Unity evaluates the original 34 -> 64 -> 64 -> 7 tanh MLP without a Python process or ML runtime dependency. The export includes Python-generated verification examples that are checked when Unity loads the network. A PPO-derived nearest-neighbour sample policy remains only as a compatibility fallback. Unity does not retrain either policy online.

## Runtime Flow

```text
Long-term audio preferences + selected VR environment
                         |
                         v
       Audio profile and personalized baseline
                         |
Stress + confidence + signal quality + optional HR/HRV window
                         |
                         v
              34-feature audio RL state
                         |
             +-----------+------------+
             |                        |
             v                        v
      Safe rule action        Trained PPO residual action
             |                        |
             +-----------+------------+
                         |
                         v
              Confidence-aware safety filter
                         |
                         v
 Seven safe targets: intensity, density, brightness, tempo,
             fade, music mix, ambient mix
                         |
                         v
      Smooth Unity playback and Lyria control-frame mapping
                         |
                         v
 Next non-overlapping physiology window -> delayed reward
                         |
                         v
        JSONL transition log + in-memory replay buffer
```

## State

`AudioRLState` contains:

- Stress, confidence, and signal quality.
- Optional Component B heart rate, RMSSD, SDNN, source timestamp, and window boundaries.
- Stress, confidence, heart-rate, and RMSSD trends.
- The seven current audio parameters and personalized baseline.
- A 13-value preference encoding compatible with the simulated-user training environment.
- Recent residual-action history, session progress, time since action, novelty count, and decision index.

The direct PPO policy receives the original 34-feature training contract. Additional production fields stay in the structured state for safety, reward, and logging so the imported model distribution is not silently changed.

## Actions

Every policy decision is a bounded residual vector:

1. `deltaIntensity`
2. `deltaDensity`
3. `deltaBrightness`
4. `deltaTempo`
5. `deltaFade`
6. `deltaMusicMix`
7. `deltaAmbientMix`

`RuleOnly` applies only the safe rule baseline. `PpoResidual` adds the trained PPO actor's residual before safety filtering. The dashboard can switch between these modes for an A/B demonstration.

## Safety

`AudioRLSafetyFilter` is the only parameter-action safety boundary in the new path. It:

- Clamps every action dimension to the configured maximum delta.
- Limits action acceleration relative to the preceding safe action.
- Freezes adaptation under very low confidence or signal quality.
- Dampens adaptation under moderately low confidence or signal quality.
- Recovers toward the personalized baseline when a signal is stale.
- Prevents excessive drift from the personalized baseline.
- Constrains brightness and the music/ambient balance, then normalizes the mix.
- Cancels action while emergency mute is active.

The legacy `ActionSafetyShield` remains only for compatibility with the old controller. It does not clamp parameters a second time when `AudioRLAgent` is active.

## Reward and Learning Boundary

The agent stores a pending transition after an action. Reward is calculated only when the next eligible observation arrives. If real physiology windows are supplied, the next window must not overlap the pre-action window.

Reward includes:

- Stress reduction.
- RMSSD increase and heart-rate reduction when real physiology is present.
- Preference alignment and stable control.
- Penalties for abrupt actions, low-confidence overreaction, excessive novelty, and unnecessary intervention.

Unity does not pretend to retrain PPO online. Each completed transition is written to JSONL and retained in a bounded replay buffer for later offline policy improvement. The small session-strategy bandit receives each delayed reward once.

## Timing

- Simulation decisions default to every 5 seconds so the dashboard can demonstrate behaviour.
- Production physiology decisions are rate-limited to at least 55 seconds and keyed by unique physiology windows.
- One warmup observation is collected before the first action.
- Production signals time out after 120 seconds by default.

These values are serialized fields on `AudioRLAgent` and should be locked in the study protocol before user evaluation.

## Unity Wiring

`AdaptiveAudioVrSceneInstaller` creates and connects:

- `AudioRLAgent` and `AudioRLTransitionLogger` on `ControllerSystem`.
- `PrototypeBootstrap` on `AppRoot`.
- Existing `AudioMixerController`, Lyria services, logger, simulator, safety manager, and dashboard components.

The installer assigns both policy artifacts from `Assets/StreamingAssets/AdaptiveAudioVR/Training`:

- `ppo_seed_37_unity_network.json`: primary direct PPO actor network.
- `ppo_seed_37_unity_policy.json`: sampled nearest-neighbour fallback.

The direct export records source-model SHA-256 `7a40b87a968be05f922df7c58e2a1a9e648c9895dd995d1c24eb366cc22192d9` for traceability.

To regenerate the direct Unity artifact from a trained Stable-Baselines3 PPO model, run `tools/rl_training/src/export/export_unity_ppo_network.py`. The exporter rejects incompatible observation/action sizes and embeds numerical verification cases.

For manual verification, use `AdaptiveAudioVR > Verify Audio RL Agent` in the Unity editor.

## Generated Meditation Session Lifecycle

The Japanese Temple Pond Garden session uses a strict generated-audio start gate:

1. Unity loads long-term preferences and prepares the personalized audio profile and Japanese-temple Lyria prompt.
2. Meditation and fixed-ambient playback remain stopped while a matching generated clip is loaded from the prompt cache or requested from the clip-generation backend.
3. A failed request does not fall back to raw meditation playback. The service retries while the session remains stopped.
4. Once the generated clip is decoded and assigned, Unity starts that meditation clip and the fixed scene ambience together, starts session logging, and enables the RL update loop.
5. During the session, Unity applies safe RL parameter changes immediately. A materially changed state must remain stable for four seconds before a replacement clip is generated in the background.
6. Replacement clips are staged on a second source and crossfaded over eight seconds near a loop boundary. If generation fails, the current generated clip continues playing.

Cache keys include the environment ID, personalized strategy, prompt weights, stress/confidence buckets, and audio-control buckets. A cached startup clip is therefore accepted only when it matches the current Japanese-temple personalization context.

## Component B Boundary

The shared `ComponentBPhysiologyBridge` owns transport, JSON parsing, schema validation, connection recovery, and delivery on Unity's main thread. The audio-side `ComponentBStressSignalReceiver` subscribes to its accepted-payload event and maps each validated window into the existing `SignalPacket` input port. The RL policy therefore remains transport-neutral and never parses backend JSON.

The adapter maps stress as `continuous_score / 3`, confidence, signal quality, heart rate, RMSSD, SDNN, source timestamp, window start/end, and an audio-local monotonically increasing sequence ID. It rejects duplicate or out-of-order window ends before switching `SignalSimulator` to `External` mode. Until the first live window arrives, the configured simulator remains the fallback input. If live delivery stops, the packet timestamp ages normally and `AudioRLAgent` enters stale-signal baseline recovery.

```text
Component B WebSocket -> shared parser/validator -> ComponentBPhysiologyBridge
                                                        |
                                                        v
                                    ComponentBStressSignalReceiver
                                      (map + order guard + sequence)
                                                        |
                                                        v
                            SignalSimulator external input / simulator fallback
                                                        |
                                                        v
                         PrototypeBootstrap -> AudioRLAgent -> safety -> mixer
```
