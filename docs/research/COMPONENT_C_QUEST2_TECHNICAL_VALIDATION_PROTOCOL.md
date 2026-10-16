# Component C Quest 2 Technical Validation Protocol

**Status:** frozen procedure awaiting execution  
**Protocol date:** 2026-10-15  
**Target:** Meta Quest 2 standalone Android  
**Unity project:** `unity/`  
**Production scene:** `JapaneseTemplePondGarden.unity`  
**Package ID:** `com.mindsyncvr.templepond`

## 1. Purpose and claim boundary

This protocol supplies the non-participant technical evidence required by the
Component C simulation evaluation protocol. It validates compilation, tests,
Android packaging, standalone Quest 2 operation, performance, failure
behavior, and local telemetry durability.

It does not evaluate comfort, cybersickness, relaxation, meditation quality,
preference, clinical safety, or human efficacy. A developer wearing the
headset to operate the application is not a research participant and must not
provide outcome data for the paper.

Do not mark technical validation complete from Editor tests or an APK build
alone. The required evidence level is an installed application exercised on a
physical Quest 2.

## 2. Frozen validation baseline

The repository currently specifies:

| Concern | Expected value |
|---|---|
| Git baseline before device testing | record at execution time |
| Unity | `6000.0.82f1` |
| URP | `17.0.4` |
| OpenXR | `1.16.1` |
| XR Interaction Toolkit | `3.0.11` |
| XR Management | `4.5.4` |
| Android scripting backend | IL2CPP |
| Android architecture | ARM64 only |
| Enabled production scene | `Assets/Laminar VR/Scenes/JapaneseTemplePondGarden.unity` |
| OpenXR Android features | Meta Quest Support and Oculus Touch Controller Profile |
| Application version | `0.1.0` |
| Development package ID | `com.mindsyncvr.templepond` |
| Full configured session | 120 s acclimatization, 900 s adaptation, 150 s stabilization |
| Local emergency input | left Touch controller menu button |
| Local telemetry directory | `AdaptiveMeditationTelemetry/<sessionId>.jsonl` |

The disabled `TestHarness.unity` scene is not included in the APK. Forest Lake
is outside this paper's frozen single-scene evaluation and must not be claimed
as device-validated from this run.

## 3. Evidence and naming convention

Create one evidence directory per validation attempt:

`analysis/component-c-bandit-evaluation/device-validation/<UTC_RUN_ID>/`

Use a UTC run identifier such as `20261015T120000Z-quest2-validation`. Keep:

- EditMode and PlayMode XML results;
- their Unity log files;
- a build log and APK SHA-256 record, but not the APK itself;
- device and package metadata;
- pre- and post-run thermal snapshots;
- the OVR Metrics Tool CSV for the baseline soak;
- Android logcat output;
- pulled Component C telemetry JSONL;
- screenshots only where they clarify a visible result; and
- a completed copy of the results matrix in Section 12.

Do not commit the APK, secrets, pairing codes, participant identifiers, or
unredacted files containing personal data. Use only synthetic/developer
session identifiers, for example `quest2-validation-q1`.

## 4. Prerequisites

1. Charge the Quest 2 and controllers. Keep the headset connected to the PC by
   USB during the fault tests so ADB remains available when Wi-Fi is disabled.
2. Enable Developer Mode for the Quest organization and accept the USB
   debugging prompt inside the headset.
3. Install Android Build Support, OpenJDK, Android SDK, and Android NDK for
   Unity `6000.0.82f1` through Unity Hub.
4. Install Meta's OVR Metrics Tool from the Horizon Store on the headset.
5. Put the PC, phone, and Quest on the same Wi-Fi for the connected runs.
6. Configure the current PC LAN host through `MINDSYNC_DEVELOPMENT_HOST` as
   described in `docs/LOCAL_FULL_SYSTEM_DEMO.md`.
7. Have Component B, the session relay, mobile application, and any other
   services required by the fixed integrated build ready. Record whether the
   audio component is enabled; keep that state unchanged across performance
   runs.
8. Close other GPU-heavy PC applications when using the Unity Profiler. Do not
   record or cast the headset during the primary OVR Metrics capture because
   recording can affect performance.

Verify ADB from PowerShell:

```powershell
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
& $adb version
& $adb devices -l
```

Pass condition: exactly one intended Quest 2 appears as `device`, not
`unauthorized` or `offline`. At the time this protocol was written, ADB was
installed at that path but no device was connected.

## 5. Preserve the pre-test baseline

Do not clear the Console or logcat until their initial state is recorded.

From the repository root:

```powershell
$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') + '-quest2-validation'
$evidence = Join-Path (Resolve-Path '.').Path "analysis/component-c-bandit-evaluation/device-validation/$runId"
New-Item -ItemType Directory -Path $evidence -Force

git rev-parse HEAD | Out-File "$evidence/git-commit.txt" -Encoding utf8
git status --short --untracked-files=all | Out-File "$evidence/git-status-before.txt" -Encoding utf8
Copy-Item 'unity/ProjectSettings/ProjectVersion.txt' "$evidence/ProjectVersion.txt"
Copy-Item 'unity/Packages/manifest.json' "$evidence/package-manifest.json"

& $adb logcat -d -v threadtime | Out-File "$evidence/logcat-before.txt" -Encoding utf8
& $adb shell getprop ro.product.model | Out-File "$evidence/device-model.txt" -Encoding utf8
& $adb shell getprop ro.build.version.release | Out-File "$evidence/android-version.txt" -Encoding utf8
& $adb shell getprop ro.build.version.incremental | Out-File "$evidence/os-build.txt" -Encoding utf8
& $adb shell dumpsys thermalservice | Out-File "$evidence/thermal-before.txt" -Encoding utf8
```

Record the active Unity scene, Android target, existing Unity Console errors and
warnings, existing failed tests, headset OS version, headset battery level, and
room/ambient conditions in the results record.

## 6. Produce fresh machine-readable Unity test evidence

Close the interactive Unity Editor before using command-line test execution.
Run EditMode first and PlayMode second:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.0.82f1\Editor\Unity.exe'
$project = Join-Path (Resolve-Path '.').Path 'unity'

& $unity -batchmode -projectPath $project -runTests -testPlatform EditMode `
  -testResults "$evidence/editmode-results.xml" `
  -logFile "$evidence/editmode-unity.log"
$editModeExit = $LASTEXITCODE

& $unity -batchmode -projectPath $project -runTests -testPlatform PlayMode `
  -testResults "$evidence/playmode-results.xml" `
  -logFile "$evidence/playmode-unity.log"
$playModeExit = $LASTEXITCODE

"EditMode exit code: $editModeExit" | Out-File "$evidence/test-exit-codes.txt" -Encoding utf8
"PlayMode exit code: $playModeExit" | Out-File "$evidence/test-exit-codes.txt" -Encoding utf8 -Append
```

Pass conditions:

- both commands return exit code `0`;
- both XML files exist and are non-empty;
- zero failed tests;
- zero unexpected skipped or inconclusive tests; and
- the Research Evaluation tests, including both deterministic invariant wrapper
  tests, pass in the fresh result.

Record the exact passed, failed, skipped, and inconclusive counts from the XML;
do not copy an older inventory count into the paper.

If command-line PlayMode behavior differs from the Editor, preserve both logs
and classify the failure. Do not disable a failing test.

## 7. Inspect and build the Android player

Open the project in Unity `6000.0.82f1` and wait for import/compilation to
finish. Preserve and classify Console messages before clearing anything.

### 7.1 Scene and runtime configuration

1. Open `JapaneseTemplePondGarden.unity`.
2. Confirm the scene is the only enabled production scene in **File > Build
   Profiles > Scene List**. `TestHarness.unity` must remain disabled.
3. Confirm the production bootstrap, visual-session boundary, environment
   adapter, physiology bridge, session relay, pairing controller, local
   emergency-stop controller, and telemetry configuration have no missing
   serialized references or missing scripts.
4. If the development host changed, run **Adaptive Meditation > Sync Local
   Development Host From Environment**. Do not put a secret or pairing code in
   a scene or ScriptableObject.
5. Open **Edit > Project Settings > XR Plug-in Management > OpenXR** for
   Android. Run project validation and record every unresolved critical issue.
   Meta Quest Support and Oculus Touch Controller Profile must be enabled.

### 7.2 Build settings

In **File > Build Profiles**, select Android. Unity `6000.0` may show Android
rather than a dedicated Meta Quest platform. Verify:

- Scripting Backend: IL2CPP;
- Target Architectures: ARM64 only;
- package ID: `com.mindsyncvr.templepond`;
- app version: `0.1.0` and version code `1` for this validation attempt;
- OpenXR is the Android XR provider;
- Internet access has not been removed because the development build uses LAN
  WebSockets;
- Development Build: enabled;
- Autoconnect Profiler: enabled only for the diagnostic profiler run;
- Script Debugging: disabled unless diagnosing a specific failure; and
- output is outside `Assets/`, for example
  `C:\Temp\MindSyncQuestValidation\<RUN_ID>\mindsync-temple-pond-dev.apk`.

Select the connected Quest 2 as the run device and choose **Build and Run**.
The build preprocessor will reject a missing development host.

After a successful build, copy the Unity Editor log and record the APK hash:

```powershell
$apk = 'C:\Temp\MindSyncQuestValidation\<RUN_ID>\mindsync-temple-pond-dev.apk'
Copy-Item "$env:LOCALAPPDATA\Unity\Editor\Editor.log" "$evidence/android-build-editor.log"
Get-Item $apk | Select-Object FullName,Length,LastWriteTimeUtc |
  Format-List | Out-File "$evidence/apk-metadata.txt" -Encoding utf8
Get-FileHash $apk -Algorithm SHA256 |
  Format-List | Out-File "$evidence/apk-sha256.txt" -Encoding utf8
& $adb shell dumpsys package com.mindsyncvr.templepond |
  Out-File "$evidence/installed-package.txt" -Encoding utf8
```

Build/install pass conditions:

- build completes without error;
- the APK exists and has a retained SHA-256 value;
- installation succeeds without uninstalling or erasing existing app data;
- the package dump reports the expected package/version; and
- the application launches to head-tracked VR or a VR loading indicator,
  without an Android crash or ANR.

## 8. Configure performance capture

Use OVR Metrics Tool **Report Mode**. Choose the Basic preset, then add the
following if the installed version exposes them without switching to a
high-overhead diagnostic preset:

- heat or thermal level;
- CPU throttling and GPU throttling;
- max consecutive stale frames; and
- application memory/RSS.

At minimum retain battery level, CPU level, GPU level, average FPS, stale frame
count, CPU utilization, GPU utilization, and application GPU time. Enable CSV
recording before the application starts. The ADB equivalent is:

```powershell
& $adb shell am start omms://app
& $adb shell am broadcast `
  -n com.oculus.ovrmonitormetricsservice/.SettingsBroadcastReceiver `
  -a com.oculus.ovrmonitormetricsservice.ENABLE_CSV
```

Do not use screen recording or casting during the primary performance run.
The headset's observed requested refresh rate must be recorded. For a 72 Hz
run, the nominal frame budget is 13.9 ms. The current Meta technical criterion
requires immersive applications to maintain at least 60 FPS and avoid extended
periods below that value, except during loading, full-screen fades, system-menu
overlays, or recording. Also report time at requested refresh, stale frames,
and transition-associated spikes rather than reducing the result to a single
pass/fail label.

## 9. Device run matrix

Use synthetic/developer sessions only.

### Q1: cold offline launch

1. Force-stop the application.
2. Disable Quest Wi-Fi.
3. Launch the application from the Quest library.
4. Confirm the Temple Pond scene renders, head tracking remains stable,
   controllers are tracked, and the user is not moved artificially.
5. Confirm the application remains in a safe waiting state instead of
   fabricating physiology or beginning adaptive decisions.
6. Keep it open for at least two minutes, then close it.

Pass: no crash/ANR, unsafe automatic transition, or loss of local tracking.
Network-dependent pairing may remain unavailable and should communicate that
state rather than starting a session.

### Q2: uninterrupted full-duration soak and performance run

1. Re-enable Wi-Fi and start the required services using
   `docs/LOCAL_FULL_SYSTEM_DEMO.md`.
2. Start OVR Metrics CSV recording.
3. Launch the Quest application, pair it with a synthetic/developer mobile
   session, and verify exactly one start command.
4. Complete acclimatization, the 900-second adaptive phase, and stabilization
   without opening the system menu or recording video.
5. During adaptation, visually inspect every environmental dimension at least
   once when it changes. Record any shader, fog, water, particle,
   post-processing, transition, controller, or tracking defect.
6. Complete the session normally and stop OVR Metrics recording.

Pass: the complete configured session finishes without crash, ANR, freeze,
unexpected scene reload, or missing terminal telemetry. The OVR CSV must show
no extended interval below 60 FPS outside declared exceptions. Thermal,
throttling, memory, stale-frame, and requested-refresh results are reported
descriptively even when the minimum criterion passes.

For this protocol, a transition-associated repeated frame-loss cluster means
at least two consecutive one-second OVR samples below 60 FPS within five
seconds of a transition start or completion, observed for two or more
transitions. Report isolated dips as observations, but fail the cluster
criterion when this repeated definition is met. Fail the thermal criterion if
OVR Metrics or the OS reports CPU/GPU thermal throttling during Q2, or if an
observed thermal event coincides with sustained performance degradation.

### Q3: network loss and reconnection

1. Start a new adaptive developer session while USB ADB remains connected.
2. Record the current normalized environment and UTC time.
3. During the adaptive phase, disable Quest Wi-Fi for 120 seconds using Quest
   Quick Settings. Do not stop the application.
4. Confirm rendering and head tracking continue, the current safe environment
   is preserved, and no new adaptive decision or model update is initiated
   from unavailable/stale physiology.
5. Re-enable Wi-Fi. Confirm reconnect attempts are visible in logs/telemetry.
6. Confirm adaptation resumes only after valid fresh physiology is received;
   otherwise record the exact recovery failure.

Pass: no unsafe environment change or invalid update occurs during the outage,
the application remains locally responsive, and reconnection behavior matches
the observed telemetry. A failure to reconnect is a failed criterion, not a
reason to omit the run.

### Q4: disconnected local emergency stop

1. Start another session and wait until the adaptive phase. Preferably trigger
   the test while a gradual environment transition is active.
2. Disable Quest Wi-Fi while keeping USB connected.
3. Press the **left Touch controller menu button** once. The right Meta/system
   button is not the application emergency control.
4. Confirm the application locally enters `Aborted`, cancels any active
   transition, stops decisions and reward attribution, and preserves or moves
   to the defined safe state without requiring the relay or Component B.
5. Press the button again and confirm no duplicate emergency transition is
   created.

Pass: the first press produces one local emergency outcome while disconnected;
the action is idempotent and no cloud/network response is required.

### Q5: pause/focus and telemetry restart durability

1. Complete or abort a uniquely named developer session.
2. Note the session ID and verify its JSONL file exists on the headset.
3. Pull the telemetry file and record its line count and SHA-256 hash.
4. Force-stop and relaunch the app without uninstalling it or clearing app
   data. Exercise headset sleep/focus loss once and return to the application.
5. Pull the original session file again and confirm it still exists. Run a
   second session and confirm it writes a different session-specific file.

Example retrieval commands:

```powershell
$package = 'com.mindsyncvr.templepond'
$remote = "/sdcard/Android/data/$package/files/AdaptiveMeditationTelemetry"
& $adb shell ls -la $remote |
  Out-File "$evidence/telemetry-device-listing.txt" -Encoding utf8
& $adb pull $remote "$evidence/quest-telemetry"

Get-ChildItem "$evidence/quest-telemetry" -File |
  Get-FileHash -Algorithm SHA256 |
  Format-Table -AutoSize |
  Out-File "$evidence/telemetry-sha256.txt" -Encoding utf8

Get-ChildItem "$evidence/quest-telemetry" -Filter '*.jsonl' |
  ForEach-Object {
    [pscustomobject]@{
      File = $_.Name
      Lines = (Get-Content -LiteralPath $_.FullName).Count
    }
  } | Export-Csv "$evidence/telemetry-line-counts.csv" -NoTypeInformation

& $adb shell am force-stop $package
# Relaunch from the Quest library, then repeat the listing, pull, hashes,
# and line counts into files whose names end in "-after-restart".
```

Pass: the first session file survives force-stop, application restart, and
focus/sleep handling; existing records are not silently replaced; the second
session uses a distinct file; and the files contain only pseudonymous test
identifiers.

## 10. Collect post-run evidence

After the final run:

```powershell
& $adb logcat -d -v threadtime |
  Out-File "$evidence/logcat-after.txt" -Encoding utf8
& $adb shell dumpsys thermalservice |
  Out-File "$evidence/thermal-after.txt" -Encoding utf8

& $adb pull `
  '/sdcard/Android/data/com.oculus.ovrmonitormetricsservice/files/CapturedMetrics/' `
  "$evidence/ovr-metrics"

git status --short --untracked-files=all |
  Out-File "$evidence/git-status-after.txt" -Encoding utf8
```

Copy only the OVR CSV corresponding to Q2 into the final evidence package, or
identify it unambiguously in the results record. Do not mix earlier captures.
Search logcat and telemetry for crashes, ANRs, unhandled exceptions, missing
references, emergency events, transport status, physiology rejection,
decisions, model updates, and session completion/abort.

## 11. Performance analysis fields

Calculate and report, without removing poor intervals:

- capture duration and number of samples;
- observed display refresh rate;
- FPS mean, median, 5th percentile, minimum, and proportion below 60 FPS;
- longest continuous interval below 60 FPS, excluding declared loading/system
  exceptions;
- stale-frame total and maximum consecutive stale frames;
- application GPU-time median, 95th percentile, and maximum;
- CPU and GPU utilization summaries;
- CPU/GPU performance levels and any throttling indication;
- heat/thermal values or events exposed by the tool;
- battery percentage at start and end;
- application memory/RSS at early stable state and session end, if available;
- frame behavior around each environmental transition; and
- any visual defect or tracking loss with its timestamp.

Do not report Editor FPS as Quest performance. Do not claim absence of a memory
leak or thermal problem if the relevant metric was unavailable. If Q2 used a
Development Build, say so in the paper because profiler instrumentation may
affect performance.

## 12. Results matrix to complete

Copy this table into a run-specific results file and fill every row with
`Passed`, `Failed`, `Blocked`, or `Not run`.

| Criterion | Status | Evidence file and observation |
|---|---|---|
| Fresh EditMode suite | Not run | |
| Fresh PlayMode suite | Not run | |
| Android ARM64/IL2CPP build | Not run | |
| APK installed and version verified | Not run | |
| Cold offline launch | Not run | |
| OpenXR/head/controller tracking | Not run | |
| Full configured session completed | Not run | |
| Minimum 60 FPS criterion | Not run | |
| Requested-refresh behavior reported | Not run | |
| Thermal/throttling evidence retained | Not run | |
| Memory behavior reported | Not run | |
| No transition-associated repeated frame-loss cluster | Not run | |
| Wi-Fi loss freezes adaptation | Not run | |
| Reconnection requires fresh physiology | Not run | |
| Disconnected local emergency stop | Not run | |
| Emergency during transition cancels safely | Not run | |
| Pause/focus/sleep handling | Not run | |
| Telemetry survives restart | Not run | |
| Sessions use separate pseudonymous files | Not run | |
| No new unhandled exception/crash/ANR | Not run | |

The overall status is:

- **Ready** only if every required row passes;
- **Ready with limitations** only when all required rows pass and a clearly
  non-blocking optional measurement is unavailable;
- **Failed** when a required criterion was executed and failed;
- **Blocked** when a device, tool, service, build, or configuration problem
  prevents required execution; or
- **Not validated** before meaningful target-device evidence exists.

## 13. Paper update rule

Only after the evidence is reviewed should the manuscript's technical
validation section be changed. Report exact build/test counts, device/OS,
capture duration, performance summaries, faults exercised, and failures.

Permitted wording is specific, for example: "In one 20-minute Quest 2
development-build soak, the application completed all configured phases; the
OVR Metrics capture contained [values]." Do not generalize a single device run
to all Quest 2 devices, comfort, clinical safety, or participant outcomes.

## 14. External tool references

- Meta Quest build configuration:
  <https://developers.meta.com/vr/documentation/unity/unity-build/>
- Meta OVR Metrics Tool:
  <https://developers.meta.com/vr/documentation/native/android/ts-ovrmetricstool/>
- Meta Quest performance criterion:
  <https://developers.meta.com/vr/resources/vrc-quest-performance-1/>
- Meta Quest VRC guidelines:
  <https://developers.meta.com/vr/resources/publish-quest-req/>
- Meta Unity performance analysis:
  <https://developers.meta.com/vr/documentation/unity/unity-perf/>

