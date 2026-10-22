# Component E: Online Personalized Audio Control

Updated: 2026-10-18. This is the current audio learning architecture in the shared
`R26-DS-006/unity` project. Earlier PPO residual descriptions are superseded.
The separate original `ComponentE_AdaptiveAudioVR` checkout is not changed.

## Relationship To Component C

The integrated runtime now reserves one shared adaptation turn across C, E and
audio content changes. A turn covers the action, transition, settling interval,
and a complete reliable post-action physiology window. The other agent holds its
current output and does not learn a reward during that turn. C and E retain their
own policies, action spaces, safety checks and reward functions.

The coordinator is enabled when the integrated audio bootstrap attaches to the
production session. Visual-only runtimes retain their existing scheduling.
New visual scheduling events replace older queued opportunities; a held event
uses the current monotonic time when admitted. Audio retries a denied reservation
without reusing a completed decision context or requiring another publication.
Both agents require a clean pre-window after the latest transition or interruption.
An accepted post-window can serve as the next agent's pre-window but cannot be
credited as two separate rewards. Model updates commit under the reservation lock.
Coordinated audio snapshots include `shared-adaptation-v1` in their configuration
key, so weights learned under the previous independent schedule are not reused.

After a turn, the peer receives a five-second admission preference. A missing or
ineligible peer does not block the remaining agent indefinitely. Each turn also
has a bounded deadline. Pause, disconnection, emergency mute, unexpected content
or scene changes, and stale reservations prevent pending model updates. Resuming
requires a new window collected after recovery and settling.

Generated clips may be prepared while an agent measures an outcome; applying
them is deferred into the existing standby queue. Crossfades reserve a content
turn until playback finishes changing. Initial/manual realtime syncs reserve a
content turn until acknowledgement and a conservative playback-buffer delay.
An RL-selected density/BPM update executes inside the **existing audio turn**,
not a second content reservation. A content change requires five seconds of settling before collecting
a clean pre-window. Further content changes wait for a completed agent turn or
a 240-second recovery deadline, preventing repeated generation from starving
learning. These timing values remain provisional and need headset validation.

The live-agent monitor reports the reservation owner, stage, eligibility time,
deadline, completed turns and cancelled turns. These observations do not control
the policies. Standalone verification can run on .NET 8 using:

```sh
dotnet run --project tests/audio_online/AudioOnlineTests.csproj -- /tmp/mindsync-audio-tests
```

This exercises the shared core and audio logic with explicit Unity test doubles;
the Unity Editor and headset playback checks remain separate requirements.

Both components follow the same learning process:

1. Initialize an output from participant preferences.
2. Select one whole controller condition for the session.
3. Collect reliable baseline physiology windows.
4. Observe a fresh context during the adaptive session phase.
5. Select one eligible adjustment and independently validate safety.
6. Apply a smooth transition and allow a settling period.
7. Wait for a new, non-overlapping post-action physiology window.
8. Calculate reward and log the outcome.
9. Update the personalized contextual bandit online, only for a valid outcome.

Component C changes the visual environment. Component E changes audio controls.
The audio implementation has its own policy, action catalogue, features, model
files and logs. It does not modify the visual LinUCB implementation or weights.

The precise algorithm name is **disjoint LinUCB contextual bandit**. It learns
context-to-action outcome estimates online. It does not perform online PPO
gradient updates or claim to solve long-horizon sequential credit assignment.

## Complete Controller Conditions

| Unity mode | Behaviour | Online weight updates |
| --- | --- | --- |
| `StaticPersonalized` | Hold the initialized personalized audio parameters | None |
| `RuleBasedAdaptive` | Select a conservative stress-aware action from the catalogue | None |
| `PersonalizedBandit` | Select an eligible action using personalized LinUCB scores | Valid delayed outcomes only |

All conditions share the initial preference mapping, action catalogue, safety,
playback, physiology validation, reward calculation and outcome telemetry.
The static condition selects NoChange. The rule condition chooses a catalogue
action closest to its conservative target adjustment. The bandit chooses the
complete action; a rule action is not added to it.

The initial content strategy uses preference priors deterministically. It is
not a second learner receiving study rewards. This preserves the same initial
personalized configuration across controller conditions for identical inputs.

Serialized mode values are migrated deliberately: old rule mode `0` becomes
`RuleBasedAdaptive`, and old PPO residual mode `1` becomes `PersonalizedBandit`.
Static personalized is `2`. Old Unity button callbacks named
`SetPpoResidualMode` delegate to the new bandit mode for scene compatibility.
New UI buttons display the correct controller names.

## Music And Scene Initialization

The existing generated-meditation start gate remains active. The session must
have its personalized Lyria meditation clip before playback begins. Preferences
and the selected Japanese Temple Pond Garden inform content generation.
The environment ambient clip is fixed and stays separate from generated music.
Lyria remains a separate service, but live density/BPM steering is executed and
attributed as part of the audio agent's selected action.

The mobile app now saves structured long-term sound choices during onboarding
and exposes them for later editing in Settings. Before each VR session it asks
whether to use those usual choices or a session-only adjustment. The mobile
request sends both profiles, the selected mode and the signed-in account's
stable pseudonymous ID to the session relay. The relay validates the audio
bundle and includes it in the paired Quest configuration. Unity maps the
effective sound profile into Component E, with the temple scene supplying the
musical setting. The fixed scene ambience remains its own audio source.

The integrated audio installer waits for this configuration. A missing or
invalid audio bundle cannot load a personalized model or trigger generated
music; `sample_user_preferences.json` remains for explicitly configured
standalone demonstrations. Audio transition logs use the relay session ID,
and learned weights remain keyed by participant and environment. A new
participant session resets clip readiness and pending generation, so an old
clip cannot satisfy the next session's start gate. The mobile database change
requires `202610130004_audio_control_preferences.sql` before using the updated
mobile build with Supabase. Existing broad sound tags are mapped to initial
structured values until the participant saves explicit choices in Settings.

Replacing a meditation clip invalidates the pending audio outcome. Crossfading
content temporarily suspends decisions and attribution. This prevents a new
composition from being incorrectly credited to a small mixer adjustment.

## Action Catalogue

`AudioActionCatalog` version `audio-micro-actions-v1` has 13 actions:

- NoChange.
- Increase or reduce intensity.
- Increase or reduce density.
- Increase brightness or darken.
- Increase or reduce tempo.
- Lengthen or shorten fade.
- Increase music while reducing ambience, or the inverse.

An action changes one independent control dimension. Mix is one coupled
dimension, so the seven logged output values contain six independent controls.
The prototype micro-step is 0.03. The final filter retains an absolute 0.08
per-dimension limit, range limits, mix normalization and a baseline drift limit.

Candidates that would be altered by the safety filter are masked before bandit
selection. NoChange remains available. The final action still passes through
the filter. A safety replacement is never credited to the proposed arm; an
altered/non-catalogue execution cannot update this fixed-action bandit.

## Local And Generated Controls

| Control | Execution |
| --- | --- |
| Intensity and music/ambience balance | Local Unity source gains |
| Brightness | Local music low-pass cutoff |
| Fade | Local smoothing/crossfade behaviour and existing reverb mapping |
| Density | Lyria generation `density`, not ambience playback pitch |
| Tempo | Lyria generation `bpm`, not music playback pitch |

Both source pitches remain 1.0, so the fixed ambience is not detuned. Tempo maps
to `round(60 + 52 * normalizedTempo) + strategyOffset`, clamped to 60-200 BPM.
Participant tempo preferences already inform the normalized initial parameters.
Tempo actions that round to the same BPM are masked. Brightness/config/prompts
unrelated to a selected generation action are held at the last synced baseline;
raw stress fluctuations cannot independently trigger a new sync during a session.

Density and tempo actions are masked while using cached clips, disconnected or
paused streaming, disabled automatic steering, or a bridge without
`mindsync-audio-playback-v1` support. Local actions and Hold remain available.
The model configuration includes `audio-musical-controls-v2` and live generation
capability, isolating weights learned under the old pitch mappings or a different
execution capability. Density/tempo applied values only advance with playback
confirmation; changing controller condition does not silently reset those controls.

For a generation action, the audio reservation covers this sequence:

1. Send the full last generation config with only selected density/BPM updated,
   and an opaque action request ID. Revalidate the audio lease before sending.
2. The bridge applies the config, resets context only if BPM/scale changed, and
   echoes the ID in its acknowledgement and subsequent PCM envelopes.
3. Wait for matching PCM to pass through the existing backlog, plus a two-second
   PCM guard. Enqueueing or acknowledgement alone is not playback confirmation.
4. After that queue position is read, allow a streaming-clip look-ahead plus DSP
   buffer margin. Only then confirm the applied control and start normal settling.
5. Learn only from a complete, reliable, non-overlapping post-settling window.

Queue loss, playback gaps, pause, transport failures, and cancellation prevent
learning. An already-sent cancelled action is reconciled at playback **without**
a reward while both agents wait; a late callback cannot confirm a newer action.
After a playback gap, the system needs stable buffering and clean physiology.
An irrecoverable lost marker or failed command triggers automatic fallback and
reconnection in the normal session flow, rather than guessing that the action
was heard. Manual-mode streams can be stopped/restarted explicitly.

The request ID identifies the bridge's accepted configuration revision. The
provider does not attach that ID to generated audio; the guard and engine queue
evidence are conservative attribution estimates, not acoustic verification of
the exact moment a composition adopted a setting. Confirm buffer timing and
audibility on Quest before using these timings as study evidence. Settings are
generation targets, not measured musical density or measured BPM.

### Deploy And Verify

Realtime is now the default active-session music path (`Prefer Realtime Music`
on the installer). The initial personalized clip remains the start gate and
fallback. Six seconds of PCM are buffered before an eight-second equal-power
source crossfade. Automatic reconnect uses cached music while respecting session
lifecycle and manual Stop/Pause. Revision changes can blend one second of future
queued PCM without discarding the two-second reserve; reward settling still
starts only after the marked revision reaches the playback confirmation gate.
See `services/lyria_backend/README.md` for live smoke/20-minute soak commands and
the remaining limits of buffering, musical continuity, and headset validation.

Restart `services/lyria_backend/app.py` on the Windows host and rebuild/redeploy
Unity/Quest from this checkout. No firmware, mobile schema or mobile APK change
is required for these two improvements. An older bridge keeps generation actions
disabled and reports a compatibility warning rather than timing out silently.

Run offline bridge tests from the repository root in the backend environment:

```powershell
python -m unittest services.lyria_backend.test_realtime -v
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-audio-online.ps1
```

The bridge tests use fake provider sessions and make no paid generation calls.
The standalone tests exercise real agent/mixer/PCM-queue logic with explicit
Unity test doubles; they do not establish actual speaker output. On the headset,
check a selected density/tempo action stays Transitioning until PCM playback,
then Measuring, with one policy update only after an eligible outcome. A real
policy may select Hold or a local action; it does not force generation every turn.

## Context Features

`AudioBanditFeatures` version `audio-context-v1-40` uses the following fixed order:

| Indices | Contents |
| --- | --- |
| 0 | Bias |
| 1-13 | Existing preference encoding |
| 14-20 | Current applied audio parameters |
| 21-27 | Current parameters minus personalized baseline |
| 28-30 | Stress, confidence, signal quality |
| 31-32 | Recent stress and confidence trends |
| 33-34 | Optional heart rate / 200 and RMSSD / 200 |
| 35-36 | Heart-rate and RMSSD availability flags |
| 37-39 | Session progress, novelty count / 20, recent action magnitude |

Values are finite and bounded to [-1, 1]. Missing optional physiology has an
explicit availability flag and contributes no invented HRV reward. The old
34-value PPO observation schema remains only for archived export compatibility.

## Online Learning

Each user/environment model contains one ridge-regression arm per action.
At a scheduled decision the model evaluates:

```text
theta[a] = solve(A[a], b[a])
score[a] = dot(theta[a], context) + alpha * sqrt(context' solve(A[a], context))

After a valid delayed outcome for the executed arm:
A[a] = A[a] + context * context'
b[a] = b[a] + reward * context
```

The prototype uses ridge 1 and exploration alpha 0.25. Cholesky solves avoid
explicit inversion. Feature dimensions, matrix symmetry, finite values and
positive definiteness are validated. Ties deterministically favour NoChange,
then the lower action ID. Only the selected, executed arm is updated once.

Online learning can be frozen using `allowOnlineLearning = false` for evaluation;
frozen evaluation also removes the exploration bonus and ranks expected rewards.
`allowSimulationLearning` separately controls synthetic demonstration updates.
Simulation and Component B window-based models have distinct persistence keys.

Snapshots are saved under `Application.persistentDataPath/AudioOnlineModels`.
Keys separate participant ID, environment, source domain and relevant reward/
control settings. Feature/action schema and matrix dimensions are validated on
load. Corrupt or incompatible models are rejected without partially restoring
weights. Snapshot writes replace files atomically where supported.

Supply a stable, distinct pseudonymous `AudioProfile.userId` for each participant.
A sample preference identity reused for different people is not personalization.
These local audio session IDs are separate from shared relay session IDs; use
recorded UTC windows when joining logs unless an explicit study session identity
is supplied by a future shared boundary extension.

## Delayed Outcome Validation

The audio adapter continues receiving accepted Component B windows through the
existing bridge. The payload can contain stress/confidence, signal quality,
heart rate and optional RMSSD/SDNN, without adding audio fields to Component B.

The agent requires two reliable baseline observations before adapting. Baseline
standard deviations normalize stress, RMSSD and heart-rate changes, with floors
of 0.05 normalized stress, 5 ms and 5 bpm respectively.

The applied mixer state must reach the target within tolerance before the
settling timer starts. A post-window must start after transition completion plus
settling and after the pre-window ends. Its sequence and end time must be new.
The same post-window is never used to reward multiple decisions, but can become
the next decision's pre-window.

Prototype timing and validity settings:

| Setting | Value |
| --- | --- |
| Minimum production decision interval | 75 seconds |
| Simulated demonstration interval | 5 seconds |
| Settling | 5 seconds |
| Outcome timeout | 240 seconds |
| Accepted window duration | 30-120 seconds |
| Maximum source-window age | 90 seconds |
| Future timestamp tolerance | 2 seconds |
| Minimum learning confidence | 0.45 |
| Minimum learning signal quality | 0.60 |

With 60-second Component B windows, a valid full post-window often arrives two
window deliveries after a decision. There is no reward every Unity frame.
Invalid observations or cancelled attribution mean no model update, rather
than an invented negative or zero physiological reward.

## Lifecycle And Attribution Protection

Audio reads the existing coordinator's phase and connection state:

- Acclimatization collects baseline observations without policy decisions.
- Adaptive permits decisions with fresh live windows.
- Pause, completion, abort and network interruption cancel pending outcomes.
- Stabilization holds the current safe audio state without exploration.
- Resume requires a fresh full window rather than reusing paused observations.
- Emergency mute immediately holds parameters, mutes output and cancels learning.

The audio bootstrap reads visual state and transition status without changing C.
By default, visual changes invalidate a pending audio outcome and visual
transitions suspend audio attribution. Simultaneous independent adaptive agents
can therefore yield fewer usable audio outcomes. Evaluate E with C held static,
then evaluate integrated operation separately. Do not infer independent causal
audio effects from sessions where uncontrolled visual changes occur.

## Reward And Telemetry

The configurable reward retains stress improvement, optional RMSSD and heart
rate improvement, preference alignment and smoothness. Penalties cover abrupt
changes, low-confidence overreaction, excessive novelty and unnecessary changes.
Reliability uses both pre- and post-window confidence/quality. Optional missing
HRV contributes nothing to that component. These weights are research prototype
settings, not clinically calibrated physiological efficacy measures.

Version-2 JSONL records under `RLTransitions` include decision and outcome events,
controller condition/segment, context schema/vector, proposed and executed arms,
safe action, predicted reward, uncertainty, reward validity, update status,
window identities and cancellation reasons. Invalid outcomes have no reward.
A bounded replay buffer retains completed and cancelled outcomes.

## Running And Evaluating

Open `R26-DS-006/unity`. On `AdaptiveAudioIntegration`, choose `Audio Study
Condition` in `AdaptiveAudioVrSceneInstaller`. Existing runtime installation
adds/configures `AudioRLAgent` and `AudioRLTransitionLogger` on `ControllerSystem`.
The dashboard has Static Personalized, Rule Based and Online Personalized
buttons. Switching is a demo feature: it cancels a pending outcome, returns to
the personalized baseline and starts a new labelled condition segment. For a
research comparison, use one fixed condition per session and counterbalance order.

Run standalone verification from the common repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-audio-online.ps1
python -m unittest discover -s tests -p test_audio_online_evaluation.py -v
```

Pass `-UnityEditorData` if the installed editor is in another location. The
script rebuilds shared references without editing visual sources, compiles all
audio files against Unity APIs, and executes deterministic logic tests with
explicit Unity test doubles. It does not launch or upgrade the Unity project.
In Unity, run `AdaptiveAudioVR > Verify Audio RL Agent` for engine-side checks.

After real sessions in all conditions, summarize logs:

```powershell
python tools/evaluate-audio-online.py --logs "C:\path\to\RLTransitions" --output results/audio_online
```

The script writes per-session/participant CSV files, comparison CSV/JSON and a
Markdown report. It aggregates repeated sessions within participant before
calculating between-participant statistics. Invalid-only sessions are retained
without invented reward means. Simulated rows are excluded by default and, if
explicitly included, are reported in a separate group.

## Historical Experiments And Thesis Correction

PPO, TD3 and SAC models and the earlier simulated-user comparison are retained
as historical offline experiments. They are not the current online audio agent,
and their scores cannot be presented as results for the new LinUCB architecture.
No PPO retraining is required to initialize LinUCB: a new participant starts
with ridge priors and improves from their valid delayed outcomes.

The previously prepared Component E thesis DOCX still describes the earlier
PPO residual design. Revise its abstract, methodology, architecture, deployment,
figures, study conditions and conclusions before presenting it as current.
Keep its numerical simulation results explicitly historical until new
experiments are conducted. Component C's thesis and agent are unchanged.

Engine playback, Android/headset persistence, end-to-end wearable delivery,
participant comfort and longitudinal efficacy still require validation on the
actual deployment. Automated logic checks do not establish these outcomes.
