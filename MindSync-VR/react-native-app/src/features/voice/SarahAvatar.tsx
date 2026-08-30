// Sarah — a "living orb", NOT an eye. No pupil, no central dot. A gradient-mesh
// core (aqua↔violet liquid), a breathing aura, a slowly rotating segmented orbit
// ring, a specular highlight, and an amplitude ring that reacts to the mic —
// pulling INWARD when listening and pushing OUTWARD when speaking, so the two
// states are unmistakable. SVG (gradients) + Reanimated worklets (motion).

import React, {useEffect} from 'react';
import {AccessibilityInfo, Image, StyleSheet, Text, View} from 'react-native';
import Animated, {
  cancelAnimation,
  Easing,
  interpolate,
  type SharedValue,
  useAnimatedStyle,
  useSharedValue,
  withRepeat,
  withTiming,
} from 'react-native-reanimated';
import Svg, {Circle, Defs, RadialGradient, Stop} from 'react-native-svg';
import {palette, radius} from '../../theme/design';
import {SARAH_IMAGE} from './sarahImage';

export type SarahState = 'idle' | 'listening' | 'speaking' | 'thinking';
type SizeName = 'sm' | 'md' | 'lg';
const SIZES: Record<SizeName, number> = {sm: 80, md: 140, lg: 200};
const BARS = 32;

const COLOR: Record<SarahState, {a: string; b: string; aura: string}> = {
  idle: {a: palette.aqua, b: palette.violet, aura: palette.aqua},
  listening: {a: '#5FF0E2', b: palette.aqua, aura: palette.aqua},
  speaking: {a: '#FFFFFF', b: palette.aqua, aura: palette.aqua},
  thinking: {a: palette.violet, b: palette.violetDeep, aura: palette.violet},
};
const CAPTION: Record<SarahState, string> = {idle: 'Sarah', listening: 'Listening', speaking: 'Sarah', thinking: 'Thinking'};

export function SarahAvatar({state, size = 'md', level = 0, hideCaption = false}: {state: SarahState; size?: SizeName | number; level?: number; hideCaption?: boolean}) {
  const S = typeof size === 'number' ? size : SIZES[size];
  const breath = useSharedValue(0);
  const spin = useSharedValue(0);
  const wave = useSharedValue(0);
  const mesh = useSharedValue(0);
  const amp = useSharedValue(0);
  const [reduced, setReduced] = React.useState(false);

  useEffect(() => { AccessibilityInfo.isReduceMotionEnabled().then(setReduced).catch(() => {}); }, []);
  useEffect(() => { amp.value = withTiming(Math.max(0, Math.min(1, level)), {duration: 110}); }, [level, amp]);

  useEffect(() => {
    if (reduced) { breath.value = 0.5; return; }
    breath.value = withRepeat(withTiming(1, {duration: state === 'idle' ? 4000 : 1600, easing: Easing.inOut(Easing.sin)}), -1, true);
    spin.value = withRepeat(withTiming(1, {duration: state === 'thinking' ? 6000 : 40000, easing: Easing.linear}), -1, false);
    wave.value = withRepeat(withTiming(1, {duration: 1500, easing: Easing.linear}), -1, false);
    mesh.value = withRepeat(withTiming(1, {duration: 7000, easing: Easing.inOut(Easing.sin)}), -1, true);
    return () => { cancelAnimation(breath); cancelAnimation(spin); cancelAnimation(wave); cancelAnimation(mesh); };
  }, [state, reduced, breath, spin, wave, mesh]);

  const c = COLOR[state];
  const reactive = state === 'listening' || state === 'speaking';
  const outward = state === 'speaking';

  const auraStyle = useAnimatedStyle(() => {
    const s = interpolate(breath.value, [0, 1], [0.9, 1.1]) * (state === 'speaking' ? 1 + amp.value * 0.12 : 1);
    return {transform: [{scale: s}], opacity: interpolate(breath.value, [0, 1], [0.18, 0.32]) + (reactive ? amp.value * 0.15 : 0)};
  });
  const orbitStyle = useAnimatedStyle(() => ({transform: [{rotate: `${interpolate(spin.value, [0, 1], [0, 360])}deg`}]}));
  const coreStyle = useAnimatedStyle(() => {
    const base = state === 'listening' ? interpolate(amp.value, [0, 1], [0.9, 0.78])
      : state === 'speaking' ? interpolate(amp.value, [0, 1], [0.98, 1.1])
      : interpolate(breath.value, [0, 1], [0.94, 1.0]);
    return {transform: [{scale: base}]};
  });
  const auraR = S * 0.5;
  const coreR = S * 0.24;

  // Flowing aurora cluster: two soft blobs drift over a bright, edgeless center
  // so the orb reads as living light — no dark core, no pupil, no eye.
  const blobA = useAnimatedStyle(() => ({
    transform: [
      {translateX: interpolate(mesh.value, [0, 1], [-coreR * 0.24, coreR * 0.24])},
      {translateY: interpolate(breath.value, [0, 1], [coreR * 0.16, -coreR * 0.16])},
    ],
  }));
  const blobB = useAnimatedStyle(() => ({
    transform: [
      {translateX: interpolate(mesh.value, [0, 1], [coreR * 0.26, -coreR * 0.26])},
      {translateY: interpolate(breath.value, [0, 1], [-coreR * 0.18, coreR * 0.18])},
    ],
    opacity: interpolate(mesh.value, [0, 1], [0.55, 0.95]),
  }));

  return (
    <View style={{alignItems: 'center', gap: 10}}>
      <View style={{width: S, height: S, alignItems: 'center', justifyContent: 'center'}} accessible accessibilityLabel={`Sarah, ${state}`}>
        {/* 1 · aura bloom */}
        <Animated.View style={[StyleSheet.absoluteFill, styles.center, auraStyle]}>
          <Svg width={S} height={S}>
            <Defs>
              <RadialGradient id="aura" cx="50%" cy="50%" r="50%">
                <Stop offset="0%" stopColor={c.aura} stopOpacity={0.7} />
                <Stop offset="100%" stopColor={c.aura} stopOpacity={0} />
              </RadialGradient>
            </Defs>
            <Circle cx={S / 2} cy={S / 2} r={auraR} fill="url(#aura)" />
          </Svg>
        </Animated.View>

        {/* 2 · orbit ring (segmented, rotating) */}
        <Animated.View style={[StyleSheet.absoluteFill, styles.center, orbitStyle]}>
          <Svg width={S} height={S}>
            <Circle cx={S / 2} cy={S / 2} r={S * 0.42} stroke={`${c.a}88`} strokeWidth={1.5} fill="none"
              strokeDasharray={state === 'thinking' ? `${S * 0.5} ${S * 0.12}` : `${S * 0.08} ${S * 0.06}`} strokeLinecap="round" />
          </Svg>
        </Animated.View>

        {/* 3 · amplitude ring (listening/speaking) */}
        {reactive ? (
          <View style={[StyleSheet.absoluteFill, styles.center]}>
            {Array.from({length: BARS}).map((_, i) => (
              <AmpBar key={i} i={i} S={S} color={c.a} wave={wave} amp={amp} outward={outward} />
            ))}
          </View>
        ) : null}

        {/* 4 · core — Sarah's portrait if provided, else a flowing aurora cluster (never an eye) */}
        <Animated.View style={[styles.center, coreStyle]}>
          {SARAH_IMAGE ? (
            <View style={{width: S * 0.62, height: S * 0.62, borderRadius: S * 0.31, overflow: 'hidden', borderWidth: 2, borderColor: `${c.a}99`}}>
              <Image source={SARAH_IMAGE} style={{width: '100%', height: '100%'}} resizeMode="cover" />
            </View>
          ) : (
          <View style={{width: coreR * 2.8, height: coreR * 2.8, alignItems: 'center', justifyContent: 'center'}}>
            {/* drifting blob A (aqua) */}
            <Animated.View style={[StyleSheet.absoluteFill, styles.center, blobA]}>
              <Svg width={coreR * 2.8} height={coreR * 2.8}>
                <Defs>
                  <RadialGradient id="sBlobA" cx="50%" cy="50%" r="50%">
                    <Stop offset="0%" stopColor={c.a} stopOpacity={0.95} />
                    <Stop offset="60%" stopColor={c.a} stopOpacity={0.3} />
                    <Stop offset="100%" stopColor={c.a} stopOpacity={0} />
                  </RadialGradient>
                </Defs>
                <Circle cx={coreR * 1.4} cy={coreR * 1.4} r={coreR * 0.95} fill="url(#sBlobA)" />
              </Svg>
            </Animated.View>
            {/* drifting blob B (violet) */}
            <Animated.View style={[StyleSheet.absoluteFill, styles.center, blobB]}>
              <Svg width={coreR * 2.8} height={coreR * 2.8}>
                <Defs>
                  <RadialGradient id="sBlobB" cx="50%" cy="50%" r="50%">
                    <Stop offset="0%" stopColor={c.b} stopOpacity={0.85} />
                    <Stop offset="70%" stopColor={c.b} stopOpacity={0.25} />
                    <Stop offset="100%" stopColor={c.b} stopOpacity={0} />
                  </RadialGradient>
                </Defs>
                <Circle cx={coreR * 1.4} cy={coreR * 1.4} r={coreR * 0.8} fill="url(#sBlobB)" />
              </Svg>
            </Animated.View>
            {/* bright, edgeless center — glows from within, no hard rim */}
            <Svg width={coreR * 2.8} height={coreR * 2.8} style={StyleSheet.absoluteFill}>
              <Defs>
                <RadialGradient id="sHot" cx="50%" cy="50%" r="50%">
                  <Stop offset="0%" stopColor="#FFFFFF" stopOpacity={state === 'thinking' ? 0.35 : 0.72} />
                  <Stop offset="35%" stopColor={c.a} stopOpacity={0.5} />
                  <Stop offset="100%" stopColor={c.a} stopOpacity={0} />
                </RadialGradient>
              </Defs>
              <Circle cx={coreR * 1.4} cy={coreR * 1.4} r={coreR * 0.62} fill="url(#sHot)" />
            </Svg>
          </View>
          )}
        </Animated.View>
      </View>

      {hideCaption ? null : (
        <View style={styles.chip}>
          <PulseDot color={c.a} />
          <Text style={[styles.chipText, {color: c.a}]}>{CAPTION[state]}</Text>
        </View>
      )}
    </View>
  );
}

function AmpBar({i, S, color, wave, amp, outward}: {i: number; S: number; color: string; wave: SharedValue<number>; amp: SharedValue<number>; outward: boolean}) {
  const ringR = S * 0.36;
  const angle = (i / BARS) * Math.PI * 2;
  const style = useAnimatedStyle(() => {
    'worklet';
    const travelling = Math.sin(wave.value * Math.PI * 2 + i * 0.55) * 0.5 + 0.5; // 0..1
    const idleRipple = 0.25 + 0.15 * Math.sin(i * 0.8);
    const len = (idleRipple + travelling * (0.3 + amp.value * 0.9)) * (S * 0.14);
    return {height: Math.max(3, len)};
  });
  const cx = S / 2 + Math.cos(angle) * ringR;
  const cy = S / 2 + Math.sin(angle) * ringR;
  const deg = (angle * 180) / Math.PI + (outward ? 90 : -90);
  return (
    <Animated.View
      style={[
        {position: 'absolute', left: cx - 1.25, top: cy, width: 2.5, borderRadius: 2, backgroundColor: color, transform: [{rotate: `${deg}deg`}]},
        style,
      ]}
    />
  );
}

function PulseDot({color}: {color: string}) {
  const p = useSharedValue(0);
  useEffect(() => { p.value = withRepeat(withTiming(1, {duration: 1200, easing: Easing.inOut(Easing.sin)}), -1, true); return () => cancelAnimation(p); }, [p]);
  const st = useAnimatedStyle(() => ({opacity: interpolate(p.value, [0, 1], [0.4, 1])}));
  return <Animated.View style={[{width: 7, height: 7, borderRadius: 4, backgroundColor: color}, st]} />;
}

const styles = StyleSheet.create({
  center: {alignItems: 'center', justifyContent: 'center'},
  chip: {flexDirection: 'row', alignItems: 'center', gap: 8, borderWidth: 1, borderColor: palette.hairline, backgroundColor: palette.surface, borderRadius: radius.pill, paddingHorizontal: 12, paddingVertical: 6},
  chipText: {fontSize: 13, fontWeight: '700'},
});
