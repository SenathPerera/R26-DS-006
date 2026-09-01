import React, {ReactNode, useEffect} from 'react';
import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleProp,
  StyleSheet,
  Text,
  TextInput,
  TextInputProps,
  View,
  ViewStyle,
} from 'react-native';
import Animated, {
  Easing,
  interpolate,
  useAnimatedStyle,
  useSharedValue,
  withRepeat,
  withTiming,
} from 'react-native-reanimated';
import LinearGradient from 'react-native-linear-gradient';
import {SafeAreaView} from 'react-native-safe-area-context';
import {Circle} from 'react-native-svg';
import Svg from 'react-native-svg';
import {ChevronLeft, LucideIcon} from 'lucide-react-native';
import {colors, radii, spacing, typography} from '../theme/theme';

export function Screen({children, scroll = true, style}: {children: ReactNode; scroll?: boolean; style?: StyleProp<ViewStyle>}) {
  const content = <View style={[styles.screenContent, style]}>{children}</View>;
  return (
    <LinearGradient colors={[colors.midnight, colors.deep, '#151A3B']} style={styles.fill}>
      <SafeAreaView style={styles.fill} edges={['top', 'left', 'right']}>
        {scroll ? <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">{content}</ScrollView> : content}
      </SafeAreaView>
    </LinearGradient>
  );
}

export function Header({title, subtitle, onBack}: {title: string; subtitle?: string; onBack?: () => void}) {
  return (
    <View style={styles.header}>
      {onBack ? (
        <Pressable accessibilityRole="button" accessibilityLabel="Go back" onPress={onBack} style={styles.backButton}>
          <ChevronLeft color={colors.text} size={24} />
        </Pressable>
      ) : null}
      <View style={styles.headerCopy}>
        <Text style={styles.title}>{title}</Text>
        {subtitle ? <Text style={styles.subtitle}>{subtitle}</Text> : null}
      </View>
    </View>
  );
}

export function Card({children, style}: {children: ReactNode; style?: StyleProp<ViewStyle>}) {
  return <View style={[styles.card, style]}>{children}</View>;
}

export function SectionHeader({title, subtitle}: {title: string; subtitle?: string}) {
  return (
    <View style={styles.sectionHeader}>
      <Text style={styles.sectionTitle}>{title}</Text>
      {subtitle ? <Text style={styles.sectionSubtitle}>{subtitle}</Text> : null}
    </View>
  );
}

export function PrimaryButton({label, onPress, disabled, loading, icon: Icon}: {label: string; onPress: () => void; disabled?: boolean; loading?: boolean; icon?: LucideIcon}) {
  return (
    <Pressable accessibilityRole="button" disabled={disabled || loading} onPress={onPress} style={({pressed}) => [styles.buttonShell, pressed && styles.pressed, (disabled || loading) && styles.disabled]}>
      <LinearGradient colors={[colors.cyan, colors.violet, colors.teal]} start={{x: 0, y: 0}} end={{x: 1, y: 0}} style={styles.primaryButton}>
        {loading ? <ActivityIndicator color={colors.white} /> : Icon ? <Icon color={colors.white} size={20} /> : null}
        <Text style={styles.primaryButtonText}>{label}</Text>
      </LinearGradient>
    </Pressable>
  );
}

export function SecondaryButton({label, onPress, danger = false, disabled = false, icon: Icon}: {label: string; onPress: () => void; danger?: boolean; disabled?: boolean; icon?: LucideIcon}) {
  return (
    <Pressable accessibilityRole="button" disabled={disabled} onPress={onPress} style={({pressed}) => [styles.secondaryButton, danger && styles.dangerButton, pressed && styles.pressed, disabled && styles.disabled]}>
      {Icon ? <Icon color={danger ? colors.rose : colors.text} size={20} /> : null}
      <Text style={[styles.secondaryButtonText, danger && {color: colors.rose}]}>{label}</Text>
    </Pressable>
  );
}

export function StatusPill({label, tone = 'neutral'}: {label: string; tone?: 'good' | 'warning' | 'danger' | 'neutral'}) {
  const tint = tone === 'good' ? colors.green : tone === 'warning' ? colors.amber : tone === 'danger' ? colors.rose : colors.cyan;
  return (
    <View style={[styles.pill, {borderColor: `${tint}88`, backgroundColor: `${tint}18`}]}>
      <View style={[styles.dot, {backgroundColor: tint}]} />
      <Text style={[styles.pillText, {color: tint}]}>{label}</Text>
    </View>
  );
}

export function Metric({label, value, accent = colors.text}: {label: string; value: string | number; accent?: string}) {
  return (
    <View style={styles.metric}>
      <Text numberOfLines={1} adjustsFontSizeToFit style={[styles.metricValue, {color: accent}]}>{value}</Text>
      <Text style={styles.metricLabel}>{label}</Text>
    </View>
  );
}

export function ProgressRing({value, label, size = 94}: {value: number; label: string; size?: number}) {
  const stroke = 8;
  const radius = (size - stroke) / 2;
  const circumference = 2 * Math.PI * radius;
  const safeValue = Math.max(0, Math.min(100, value));
  return (
    <View style={{width: size, height: size, alignItems: 'center', justifyContent: 'center'}}>
      <Svg width={size} height={size} style={StyleSheet.absoluteFill}>
        <Circle cx={size / 2} cy={size / 2} r={radius} stroke={colors.borderSoft} strokeWidth={stroke} fill="none" />
        <Circle cx={size / 2} cy={size / 2} r={radius} stroke={colors.teal} strokeWidth={stroke} fill="none" strokeLinecap="round" strokeDasharray={`${circumference} ${circumference}`} strokeDashoffset={circumference * (1 - safeValue / 100)} rotation="-90" origin={`${size / 2}, ${size / 2}`} />
      </Svg>
      <Text style={styles.ringValue}>{safeValue}</Text>
      <Text style={styles.ringLabel}>{label}</Text>
    </View>
  );
}

export function BreathingVisual({size = 138}: {size?: number}) {
  const pulse = useSharedValue(0);
  useEffect(() => {
    pulse.value = withRepeat(withTiming(1, {duration: 4200, easing: Easing.inOut(Easing.sin)}), -1, true);
  }, [pulse]);
  const outerStyle = useAnimatedStyle(() => ({
    transform: [{scale: interpolate(pulse.value, [0, 1], [0.88, 1.08])}],
    opacity: interpolate(pulse.value, [0, 1], [0.28, 0.58]),
  }));
  const innerStyle = useAnimatedStyle(() => ({
    transform: [{scale: interpolate(pulse.value, [0, 1], [1, 0.9])}],
  }));
  return (
    <View style={{width: size, height: size, alignItems: 'center', justifyContent: 'center'}} accessible accessibilityLabel="Slow breathing animation">
      <Animated.View style={[styles.aura, {width: size, height: size, borderRadius: size / 2}, outerStyle]} />
      <Animated.View style={[styles.auraCore, {width: size * 0.56, height: size * 0.56, borderRadius: size}, innerStyle]} />
    </View>
  );
}

export function Field(props: TextInputProps & {label: string; error?: string}) {
  return (
    <View style={styles.fieldWrap}>
      <Text style={styles.fieldLabel}>{props.label}</Text>
      <TextInput placeholderTextColor={colors.faint} {...props} style={[styles.input, props.multiline && styles.multiline, props.style]} />
      {props.error ? <Text style={styles.errorText}>{props.error}</Text> : null}
    </View>
  );
}

export function ChoiceChip({label, selected, onPress}: {label: string; selected: boolean; onPress: () => void}) {
  return (
    <Pressable accessibilityRole="checkbox" accessibilityState={{checked: selected}} onPress={onPress} style={[styles.choice, selected && styles.choiceSelected]}>
      <Text style={[styles.choiceText, selected && {color: colors.teal}]}>{label}</Text>
    </Pressable>
  );
}

export const uiStyles = StyleSheet.create({
  row: {flexDirection: 'row', alignItems: 'center', gap: spacing.sm},
  rowBetween: {flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.md},
  wrap: {flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm},
  body: {fontSize: typography.body, color: colors.muted, lineHeight: 24},
  label: {fontSize: typography.small, color: colors.muted},
  value: {fontSize: typography.section, fontWeight: '700', color: colors.text},
});

const styles = StyleSheet.create({
  fill: {flex: 1},
  scroll: {flexGrow: 1},
  screenContent: {paddingHorizontal: spacing.lg, paddingTop: spacing.md, paddingBottom: 112, gap: spacing.md},
  header: {flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm, marginBottom: spacing.xs},
  backButton: {width: 42, height: 42, alignItems: 'center', justifyContent: 'center', borderRadius: radii.md, borderWidth: 1, borderColor: colors.borderSoft, marginTop: 2},
  headerCopy: {flex: 1},
  title: {fontSize: typography.title, color: colors.text, fontWeight: '800', letterSpacing: 0},
  subtitle: {fontSize: typography.body, color: colors.muted, lineHeight: 23, marginTop: spacing.xs},
  card: {backgroundColor: 'rgba(35, 52, 75, 0.82)', borderWidth: 1, borderColor: colors.border, borderRadius: radii.lg, padding: spacing.md, gap: spacing.md},
  sectionHeader: {gap: 4, marginTop: spacing.sm},
  sectionTitle: {fontSize: typography.section, color: colors.text, fontWeight: '800'},
  sectionSubtitle: {fontSize: typography.small, color: colors.muted, lineHeight: 19},
  buttonShell: {borderRadius: radii.md, overflow: 'hidden'},
  primaryButton: {height: 56, flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: spacing.sm, paddingHorizontal: spacing.md},
  primaryButtonText: {fontSize: typography.body, color: colors.white, fontWeight: '800'},
  secondaryButton: {minHeight: 52, borderRadius: radii.md, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.panelStrong, flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: spacing.sm, paddingHorizontal: spacing.md},
  dangerButton: {borderColor: `${colors.rose}88`, backgroundColor: `${colors.rose}12`},
  secondaryButtonText: {fontSize: typography.body, color: colors.text, fontWeight: '700'},
  pressed: {opacity: 0.78},
  disabled: {opacity: 0.46},
  pill: {alignSelf: 'flex-start', minHeight: 34, flexDirection: 'row', alignItems: 'center', gap: 8, borderWidth: 1, borderRadius: radii.pill, paddingHorizontal: 12, paddingVertical: 7},
  dot: {width: 7, height: 7, borderRadius: 4},
  pillText: {fontSize: 12, fontWeight: '800'},
  metric: {flex: 1, minWidth: 76, minHeight: 86, borderRadius: radii.md, borderWidth: 1, borderColor: colors.borderSoft, backgroundColor: 'rgba(7,21,37,0.36)', alignItems: 'center', justifyContent: 'center', padding: spacing.sm},
  metricValue: {fontSize: 22, fontWeight: '800', maxWidth: '100%'},
  metricLabel: {fontSize: 11, color: colors.muted, marginTop: 4, textAlign: 'center'},
  ringValue: {fontSize: 20, color: colors.text, fontWeight: '800'},
  ringLabel: {fontSize: 10, color: colors.muted},
  aura: {position: 'absolute', backgroundColor: colors.cyan, borderWidth: 1, borderColor: `${colors.teal}88`},
  auraCore: {backgroundColor: colors.violet, borderWidth: 2, borderColor: `${colors.white}33`, shadowColor: colors.teal, shadowRadius: 18, shadowOpacity: 0.6},
  fieldWrap: {gap: spacing.xs},
  fieldLabel: {fontSize: 13, color: colors.muted, fontWeight: '700'},
  input: {minHeight: 52, borderWidth: 1, borderColor: colors.border, borderRadius: radii.md, backgroundColor: 'rgba(7,21,37,0.54)', paddingHorizontal: spacing.md, color: colors.text, fontSize: typography.body},
  multiline: {minHeight: 104, paddingTop: 14, textAlignVertical: 'top'},
  errorText: {color: colors.rose, fontSize: 12},
  choice: {minHeight: 40, justifyContent: 'center', borderWidth: 1, borderColor: colors.border, borderRadius: radii.pill, paddingHorizontal: 14, paddingVertical: 8, backgroundColor: 'rgba(7,21,37,0.4)'},
  choiceSelected: {borderColor: colors.teal, backgroundColor: `${colors.teal}18`},
  choiceText: {fontSize: 13, color: colors.muted, fontWeight: '700'},
});
