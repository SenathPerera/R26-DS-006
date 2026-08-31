# Temple Pond Quest Pilot Readiness

**Last reviewed:** 2026-08-31  
**Scene:** Japanese Temple Pond Garden only  
**Overall status:** Development integration and on-device pairing validated; full pilot validation pending

This checklist is the final hardening gate for Steps 13 and 14 of the adaptive
VR implementation plan. Editor success is not treated as Quest 2 or end-to-end
pilot validation.

## 1. Verified baseline

- Unity version is `6000.0.82f1` with URP, OpenXR, XR Interaction Toolkit, and
  the Input System installed.
- The Android player uses IL2CPP and ARM64.
- `JapaneseTemplePondGarden.unity` is the only enabled production build scene.
- The Android OpenXR configuration enables the Meta Quest feature and Oculus
  Touch controller profile. Mock-runtime and runtime-debug features are not
  enabled for Android.
- The Temple Pond scene contains the production application bootstrap, session
  coordinator, visual boundary, Component B physiology bridge, scene adapter,
  and global color volume. No missing-script reference was found in the scene
  YAML during this review.
- The latest machine-readable Unity result contains 17 passing PlayMode tests
  and no failures. The user also reported the relevant EditMode suite passing.
- Durable visual telemetry can be read by the relay bridge, published in
  configured batches, and retained after a failed send.
- Component B remains a separate physiology stream. Audio-agent integration is
  intentionally out of scope until its owner freezes that contract.

## 2. Development configuration and later production hardening

To unblock full-system testing, the current implementation uses provisional,
clearly identified development values:

- Local relay endpoint: `ws://172.20.10.4:8080/realtime?role=quest`.
- Schema identifier: `mindsync-session-v1`.
- Pairing-code lifetime: five minutes.
- Readiness-to-start initialization delay: 30 seconds.
- Maximum inbound message size: 65,536 bytes.
- Maximum telemetry batch: 32 events.
- Pseudonymous Quest identity: a locally persisted random `quest-<guid>` value.
- Development Android package ID: `com.mindsyncvr.templepond`.

Before participant deployment, replace or formally approve:

- Relay `wss://` endpoint.
- Relay schema identifier/version.
- Maximum inbound relay message size and maximum telemetry events per batch.
- Pairing-code lifetime and the relay response for expired/reused codes.
- Atomic mobile behavior for `Start my session`: create the prepared session,
  upload its configuration, request the one-time code, and expose failure/retry
  state without generating multiple active sessions.
- Renewable resume credential and lifetime, or an explicit decision to require
  a new mobile-issued code after disconnect.
- Source and persistence policy for a pseudonymous Quest installation/client
  ID. It must not contain participant identity.
- Delivery acknowledgement for the final visual log and retry/idempotency
  behavior.

The installer replaces Unity's template Android identifier with the provisional
development identifier above. Confirm the team's permanent reverse-domain
package ID before distributing a participant APK; changing it later creates a
different Android application identity and upgrade path.

The current local Component B endpoint uses `ws://172.20.10.4:8000/stream`.
Confirm Android cleartext-network behavior in a development Quest build, or
serve Component B through TLS. Production relay communication must use WSS.

## 3. Unity development scene wiring

1. Open `JapaneseTemplePondGarden.unity`.
2. Run `Adaptive Meditation > Configure Temple Pond Development Relay`.
3. Save the scene after the installer selects `AdaptiveEnvironment`.
4. Confirm that `SessionRelayBridge`, `SessionRelayPairingController`, and
   `QuestPairingRuntimePanel` appear on that object and that all references are
   assigned.
5. Enter Play Mode and confirm the controller-ray keypad appears, the bootstrap still
   initializes, and the Console has no errors before attempting live pairing.

The installer creates only a non-secret connection profile. The one-time code
is entered at runtime and the pseudonymous Quest client ID is stored in
`PlayerPrefs`, never in the scene or ScriptableObject.

## 4. Required end-to-end checks

### Relay and mobile

- Complete the voice-companion flow, tap `Start my session`, and verify that a
  single one-time code appears in `Your session` directly below
  `Ready when you are`.
- Verify that tapping `Start my session` prepares the relay session but does
  not begin the Quest timer or adaptive phase before code redemption.
- Pair one mobile client and one Quest client to one session.
- Verify Quest publishes `ready`, the relay waits 30 seconds, and exactly one
  `start` command begins the session.
- Reject wrong, expired, and reused pairing codes without leaking credentials.
- Reject a second active client or participant binding.
- Accept one valid configuration and reject wrong-scene, wrong-schema,
  malformed, oversized, and duplicate messages.
- Verify start, pause, resume, stop, and emergency-stop idempotency.
- Verify Quest readiness and phase updates reach mobile.
- Verify telemetry batches preserve ordering and are not lost after a failed
  publish.
- Verify final delivery acknowledgement before local data is considered
  transferable or eligible for cleanup.
- Force a stale final-log receipt and confirm the relay rejects it without
  replacing the locally durable log.

### Component B and safety

- Receive fresh valid Component B payloads on Quest at the expected roughly
  60-second source cadence.
- Reject stale, invalid, duplicate, and low-quality physiology without
  fabricating replacements.
- Freeze new adaptation decisions during physiology or relay loss while
  preserving the current safe environment.
- Confirm pause stops decisions and reward attribution.
- Confirm emergency stop remains local and works without either network.

### Quest 2

- Build and install an ARM64 IL2CPP APK using the final package ID.
- Confirm OpenXR startup, controllers, scene loading, and pairing on the
  standalone headset.
- Run the full configured session duration, including acclimatization,
  adaptation, and stabilization.
- Check frame timing, thermal behavior, memory growth, water transparency,
  post-processing, and action-transition spikes on device.
- Interrupt Wi-Fi and restart the application to validate safe recovery and
  durable local-log behavior.
- Complete and abort separate sessions, then verify that each session exports
  only its own pseudonymous visual telemetry.

## 5. Freeze criteria

Step 14 can be marked complete only when:

- all blocking deployment inputs above are versioned and approved;
- the production scene contains the reviewed relay composition;
- current EditMode and PlayMode suites pass after that wiring;
- the complete mobile-relay-Quest flow passes on a Quest 2;
- one full-duration session shows acceptable performance and recoverability;
- feature schema, reward configuration, safety/action/timing profiles, package
  versions, and the final Android package ID are recorded; and
- an ADR records the frozen study configuration and any accepted limitations.

Until then, the correct status is **Step 13 development implementation
complete; Step 14 in progress and awaiting Unity plus Quest 2 evidence**.
