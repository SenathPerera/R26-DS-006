// Sarah's visible presence — an abstract AI companion, not a coloured dot.
// A focal core (iris + a pupil that gently wanders, so it reads as *looking at
// you*), an inner glow, and two orbiting rings at different rates/opacities.
//
// Four states, unmistakable at a glance, with listening and speaking moving in
// OPPOSITE directions so a conversation never looks stuck:
//   idle      — slow 4s breathe
//   listening — outer ring reacts to live mic level, with an inward pull
//   speaking  — pulse radiating OUTWARD, synced to playback
//   thinking  — calm indeterminate orbit
// Pure react-native + Reanimated (UI-thread worklets) + linear-gradient — no
// native code, no images. Respects reduce-motion via the `reducedMotion` prop.

import React, {useEffect} from 'react';
import {AccessibilityInfo, StyleSheet, Text, View} from 'react-native';
import Animated, {
  cancelAnimation,
  Easing,
  interpolate,
  useAnimatedStyle,
  useSharedValue,
  withRepeat,
  withTiming,
} from 'react-native-reanimated';
import LinearGradient from 'react-native-linear-gradient';
import {colors, radii} from '../../theme/theme';

export type CompanionState = 'idle' | 'listening' | 'speaking' | 'thinking';

const TINT: Record<CompanionState, string> = {
  idle: colors.cyan,
  listening: colors.teal,
  speaking: colors.green,
  thinking: colors.violet,
};
const CAPTION: Record<CompanionState, string> = {
  idle: 'Sarah',
  listening: 'Listening',
  speaking: 'Sarah',
  thinking: 'Thinking',
};

export function CompanionAvatar({
  state,
  size = 132,
  level = 0,
}: {
  state: CompanionState;
  size?: number;
  level?: number; // 0..1 live mic amplitude, used by the listening state
}) {
  const breathe = useSharedValue(0);
  const orbit = useSharedValue(0);
  const pulse = useSharedValue(0);
  const gaze = useSharedValue(0);
  const amp = useSharedValue(0);
  const [reduced, setReduced] = React.useState(false);

  useEffect(() => {
    AccessibilityInfo.isReduceMotionEnabled().then(setReduced).catch(() => {});
  }, []);

  // Push the live mic level onto the UI thread for the listening ring.
  useEffect(() => {
    amp.value = withTiming(Math.max(0, Math.min(1, level)), {duration: 120});
  }, [level, amp]);

  useEffect(() => {
    if (reduced) {
      cancelAnimation(breathe);
      cancelAnimation(orbit);
      cancelAnimation(pulse);
      cancelAnimation(gaze);
      breathe.value = 0.5;
      return;
    }
    // Breathing rate differs per state; speaking/listening are quicker.
    const period = state === 'thinking' ? 1100 : state === 'listening' ? 1500 : state === 'speaking' ? 1000 : 4200;
    breathe.value = withRepeat(withTiming(1, {duration: period, easing: Easing.inOut(Easing.sin)}), -1, true);
    orbit.value = withRepeat(withTiming(1, {duration: state === 'thinking' ? 3200 : 9000, easing: Easing.linear}), -1, false);
    pulse.value = withRepeat(withTiming(1, {duration: 1400, easing: Easing.out(Easing.ease)}), -1, false);
    gaze.value = withRepeat(withTiming(1, {duration: 5200, easing: Easing.inOut(Easing.sin)}), -1, true);
    return () => {
      cancelAnimation(breathe);
      cancelAnimation(orbit);
      cancelAnimation(pulse);
    };
  }, [state, reduced, breathe, orbit, pulse, gaze]);

  const tint = TINT[state];

  // Outer aura: breathes; in listening it also reacts to mic level (inward pull
  // = smaller when louder); in speaking it radiates outward via `pulse`.
  const auraStyle = useAnimatedStyle(() => {
    const base = interpolate(breathe.value, [0, 1], [0.9, 1.08]);
    const listen = state === 'listening' ? 1 - amp.value * 0.16 : 1;
    const speak = state === 'speaking' ? interpolate(pulse.value, [0, 1], [0.94, 1.22]) : 1;
    const opacity = state === 'speaking'
      ? interpolate(pulse.value, [0, 1], [0.5, 0])
      : interpolate(breathe.value, [0, 1], [0.22, 0.5]);
    return {transform: [{scale: base * listen * speak}], opacity};
  });

  // Ring 1 orbits and tilts; ring 2 orbits opposite for depth.
  const ring1Style = useAnimatedStyle(() => ({
    transform: [{rotate: `${interpolate(orbit.value, [0, 1], [0, 360])}deg`}, {scale: interpolate(breathe.value, [0, 1], [0.98, 1.04])}],
  }));
  const ring2Style = useAnimatedStyle(() => ({
    transform: [{rotate: `${interpolate(orbit.value, [0, 1], [0, -360])}deg`}, {scale: interpolate(breathe.value, [0, 1], [1.02, 0.97])}],
  }));

  // Core: in listening it draws slightly inward; in speaking it swells outward.
  const coreStyle = useAnimatedStyle(() => {
    const s = state === 'listening'
      ? interpolate(amp.value, [0, 1], [0.9, 0.74])
      : state === 'speaking'
        ? interpolate(breathe.value, [0, 1], [0.92, 1.06])
        : interpolate(breathe.value, [0, 1], [0.98, 0.9]);
    return {transform: [{scale: s}]};
  });

  // Pupil wanders a little — the thing that makes it read as "looking".
  const pupilStyle = useAnimatedStyle(() => ({
    transform: [
      {translateX: interpolate(gaze.value, [0, 1], [-size * 0.03, size * 0.03])},
      {translateY: interpolate(gaze.value, [0, 1], [size * 0.02, -size * 0.02])},
    ],
  }));

  const core = size * 0.42;
  const pupil = size * 0.16;

  return (
    <View style={{alignItems: 'center', gap: 10}}>
      <View style={{width: size, height: size, alignItems: 'center', justifyContent: 'center'}} accessible accessibilityLabel={`Sarah, ${state}`}>
        <Animated.View style={[styles.aura, {width: size, height: size, borderRadius: size / 2, backgroundColor: tint}, auraStyle]} />
        <Animated.View style={[styles.ring, {width: size * 0.82, height: size * 0.82, borderRadius: size, borderColor: `${tint}66`}, ring1Style]} />
        <Animated.View style={[styles.ring, {width: size * 0.64, height: size * 0.64, borderRadius: size, borderColor: `${tint}44`, borderStyle: 'dashed'}, ring2Style]} />
        <Animated.View style={[{width: core, height: core, borderRadius: core}, coreStyle]}>
          <LinearGradient
            colors={[`${colors.white}`, tint, `${tint}00`]}
            start={{x: 0.3, y: 0.2}}
            end={{x: 0.8, y: 1}}
            style={{width: core, height: core, borderRadius: core, alignItems: 'center', justifyContent: 'center'}}>
            <Animated.View style={[{width: pupil, height: pupil, borderRadius: pupil, backgroundColor: colors.midnight, opacity: state === 'thinking' ? 0.5 : 0.82}, pupilStyle]}>
              <View style={{position: 'absolute', top: pupil * 0.18, left: pupil * 0.2, width: pupil * 0.3, height: pupil * 0.3, borderRadius: pupil, backgroundColor: `${colors.white}cc`}} />
            </Animated.View>
          </LinearGradient>
        </Animated.View>
      </View>
      <View style={[styles.caption, {borderColor: `${tint}66`, backgroundColor: `${tint}18`}]}>
        <View style={[styles.dot, {backgroundColor: tint}]} />
        <Text style={[styles.captionText, {color: tint}]}>{CAPTION[state]}</Text>
      </View>
    </View>
  );
}

// Live microphone waveform strip (kept for the recording screens).
export function Waveform({levels, active}: {levels: number[]; active: boolean}) {
  return (
    <View style={styles.waveRow}>
      {levels.map((v, i) => (
        <View
          key={i}
          style={{
            width: 3,
            borderRadius: 2,
            height: 6 + Math.round(v * 40),
            backgroundColor: active ? colors.teal : colors.borderSoft,
            opacity: active ? 0.5 + v * 0.5 : 0.5,
          }}
        />
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  aura: {position: 'absolute'},
  ring: {position: 'absolute', borderWidth: 1.5},
  caption: {flexDirection: 'row', alignItems: 'center', gap: 8, borderWidth: 1, borderRadius: radii.pill, paddingHorizontal: 12, paddingVertical: 6},
  dot: {width: 7, height: 7, borderRadius: 4},
  captionText: {fontSize: 12, fontWeight: '800'},
  waveRow: {flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 3, height: 52},
});
