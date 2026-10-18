# Temple garden sky motion

`JapaneseTemplePondGarden` contains **Temple Sky and Distant Birds** at world
position `(15, 38, 22)`. In Play Mode it creates seven distant bird silhouettes
in a loose V formation, with slow wingbeats and a circular flight path. All
birds use one opaque URP Unlit mesh/material with no physics or shadows.

The existing environment adapter forwards its validated, transitioned
`AmbientMotion` value to this visual component. Sky and flight speeds come from
`TemplePondSkyMotionProfile.asset`; the preview uses normalized motion 0.5 until
the adapter applies its first state. The original skybox material is cloned at
runtime and restored on disable. Lighting and reflections are not rebaked each
frame. No audio is added.

Preview settings: sky rotation 0.02–0.08 degrees/second, bird flight 0.6–1.2
meters/second, orbit radius 55 meters, wingspan 1.6 meters and wingbeats 0.3 Hz.
Pause, headset suspension, completion and abort stop the animation clock.
Stabilization retains the selected speed, just as the pond retains its ripple
setting. Network loss does not change motion speed or create new decisions.

TODO(RESEARCH_DECISION): These are configurable artistic preview values, not
validated comfort limits. Review the sky/flight ranges, flock size and placement
on Quest 2 before study use. Record the motion profile with the study's frozen
scene configuration; the existing approved water/lighting profile is unchanged.

## Manual validation

1. Let Unity import the scripts, then reload `JapaneseTemplePondGarden` if it
   was already open. Enter Play Mode. No Inspector wiring is required.
2. Look above the far garden wall. Confirm seven birds move continuously without
   popping or approaching the seated viewpoint. Observe sky drift over a minute.
   The flock is generated in Play Mode, so it is not visible in Edit Mode.
3. Exercise low/high ambient-motion preferences through the existing session
   controls; confirm gradual speed changes along with the pond mapping.
4. Pause/resume, suspend the headset, then emergency-stop. Confirm motion freezes
   at its current position when paused/aborted and resumes without a time jump.
5. Exit/re-enter Play Mode and switch scenes. Confirm the shared sky material's
   rotation is unchanged and no duplicate flock remains.
6. Run `TemplePondSkyMotionPlayModeTests` in Unity Test Runner. On Quest 2 verify
   stereo rendering, perceived motion comfort, visibility and GPU/frame timing.
