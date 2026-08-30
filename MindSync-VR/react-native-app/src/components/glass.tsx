// Shared building blocks for the "Still Water at Night" system: glass cards,
// buttons, the per-stage header, status chip, and simple info rows. Depth from
// translucency + top-edge highlight + shadow (no native blur lib needed).

import React, {ReactNode, useEffect} from 'react';
import {Pressable, StyleSheet, Text, View, ViewStyle} from 'react-native';
import Animated, {Easing, interpolate, useAnimatedStyle, useSharedValue, withRepeat, withTiming} from 'react-native-reanimated';
import LinearGradient from 'react-native-linear-gradient';
import {ChevronLeft, type LucideIcon} from 'lucide-react-native';
import {elevation, palette, radius, space, type as T} from '../theme/design';

// ---- stress level helper (score → word + colour), used across results ----
export function stressLevel(score: number): {word: string; color: string} {
  if (score >= 7) return {word: 'HIGH', color: palette.high};
  if (score >= 5) return {word: 'MODERATE', color: palette.moderate};
  if (score >= 2.5) return {word: 'MILD', color: palette.mild};
  return {word: 'LOW', color: palette.calm};
}

export function GlassCard({children, style, accent}: {children: ReactNode; style?: ViewStyle; accent?: string}) {
  return (
    <View style={[styles.card, elevation.card, accent ? {borderWidth: 1, borderColor: `${accent}44`} : null, style]}>
      <View style={styles.cardTopEdge} pointerEvents="none" />
      {accent ? <View style={[styles.cardAccentEdge, {backgroundColor: accent}]} pointerEvents="none" /> : null}
      {children}
    </View>
  );
}

export function PrimaryButton({label, onPress, disabled}: {label: string; onPress: () => void; disabled?: boolean}) {
  return (
    <Pressable onPress={onPress} disabled={disabled} style={({pressed}) => [{borderRadius: radius.pill, overflow: 'hidden', opacity: disabled ? 0.5 : 1, transform: [{scale: pressed ? 0.97 : 1}]}, elevation.glow(palette.aqua)]}>
      <LinearGradient colors={[palette.aqua, palette.violet]} start={{x: 0, y: 0}} end={{x: 1, y: 0}} style={styles.primary}>
        <Text style={styles.primaryText}>{label}</Text>
      </LinearGradient>
    </Pressable>
  );
}

export function GhostButton({label, onPress}: {label: string; onPress: () => void}) {
  return (
    <Pressable onPress={onPress} style={({pressed}) => [styles.ghost, {opacity: pressed ? 0.7 : 1}]}>
      <Text style={styles.ghostText}>{label}</Text>
    </Pressable>
  );
}

export function TextLink({label, onPress}: {label: string; onPress: () => void}) {
  return <Text onPress={onPress} style={styles.link}>{label}</Text>;
}

export function PulseDot({color = palette.aqua, solid = false}: {color?: string; solid?: boolean}) {
  const p = useSharedValue(solid ? 1 : 0);
  useEffect(() => {
    if (solid) return;
    p.value = withRepeat(withTiming(1, {duration: 1200, easing: Easing.inOut(Easing.sin)}), -1, true);
  }, [p, solid]);
  const st = useAnimatedStyle(() => ({opacity: solid ? 1 : interpolate(p.value, [0, 1], [0.4, 1])}));
  return <Animated.View style={[{width: 8, height: 8, borderRadius: 4, backgroundColor: color}, st]} />;
}

export function StatusChip({label, color = palette.aqua, done = false}: {label: string; color?: string; done?: boolean}) {
  const c = done ? palette.calm : color;
  return (
    <View style={[styles.chip, {borderColor: `${c}55`}]}>
      <PulseDot color={c} solid={done} />
      <Text style={[styles.chipText, {color: c}]}>{label}</Text>
    </View>
  );
}

const STAGE_ORDER = ['intro', 'room', 'pre', 'vr', 'post', 'report'] as const;
export type StageId = typeof STAGE_ORDER[number];

export function StageHeader({stage, title, status, statusColor, done, onBack}: {stage: StageId; title: string; status: string; statusColor?: string; done?: boolean; onBack?: () => void}) {
  const idx = STAGE_ORDER.indexOf(stage);
  return (
    <View style={{gap: space.md}}>
      <View style={styles.headerTop}>
        {onBack ? (
          <Pressable onPress={onBack} hitSlop={10} style={styles.back}><ChevronLeft color={palette.textHi} size={24} /></Pressable>
        ) : <View style={{width: 24}} />}
        <View style={styles.dots}>
          {STAGE_ORDER.map((s, i) => (
            <View key={s} style={{width: i === idx ? 22 : 7, height: 7, borderRadius: 4, backgroundColor: i < idx ? palette.aqua : i === idx ? palette.aqua : 'rgba(255,255,255,0.18)'}} />
          ))}
        </View>
      </View>
      <Text style={styles.title}>{title}</Text>
      <StatusChip label={status} color={statusColor} done={done} />
    </View>
  );
}

export function InfoRow({icon: Icon, text}: {icon: LucideIcon; text: string}) {
  return (
    <View style={styles.infoRow}>
      <Icon color={palette.aqua} size={18} />
      <Text style={styles.infoText}>{text}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {backgroundColor: palette.cardFill, borderRadius: radius.lg, padding: space.xl, gap: space.md, overflow: 'hidden'},
  cardTopEdge: {position: 'absolute', top: 0, left: 0, right: 0, height: 1, backgroundColor: 'rgba(255,255,255,0.12)'},
  cardAccentEdge: {position: 'absolute', top: 0, bottom: 0, left: 0, width: 3},
  primary: {height: 56, alignItems: 'center', justifyContent: 'center', paddingHorizontal: space.xl},
  primaryText: {...T.bodyMid, color: palette.textHi, fontWeight: '700'},
  ghost: {height: 52, borderRadius: radius.pill, borderWidth: 1, borderColor: palette.hairline, alignItems: 'center', justifyContent: 'center'},
  ghostText: {...T.bodyMid, color: palette.textMid},
  link: {...T.caption, color: palette.textLow, textAlign: 'center', paddingVertical: 12},
  chip: {alignSelf: 'flex-start', flexDirection: 'row', alignItems: 'center', gap: 8, borderWidth: 1, backgroundColor: palette.surface, borderRadius: radius.pill, paddingHorizontal: 12, paddingVertical: 6},
  chipText: {...T.caption, fontWeight: '700'},
  headerTop: {flexDirection: 'row', alignItems: 'center', gap: space.md},
  back: {width: 24, height: 24, alignItems: 'center', justifyContent: 'center'},
  dots: {flexDirection: 'row', alignItems: 'center', gap: 6},
  title: {...T.h1, color: palette.textHi},
  infoRow: {flexDirection: 'row', alignItems: 'center', gap: space.md, paddingVertical: 4},
  infoText: {...T.body, color: palette.textMid, flex: 1},
});
