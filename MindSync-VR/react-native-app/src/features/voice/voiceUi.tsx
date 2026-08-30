// Result + feedback visuals on the new design system: ScoreGauge (radial arc +
// the mandatory large level word), the level band, MetricTile (room results),
// LinearVisualizer (live voice), and the full-screen AnalysingOverlay.

import React, {useEffect, useState} from 'react';
import {StyleSheet, Text, View} from 'react-native';
import Animated, {Easing, useAnimatedProps, useSharedValue, withTiming} from 'react-native-reanimated';
import Svg, {Circle, Defs, LinearGradient as SvgLinear, Rect, Stop, Text as SvgTextNative} from 'react-native-svg';
import {type LucideIcon} from 'lucide-react-native';
import {palette, radius, space, type as T} from '../../theme/design';
import {stressLevel} from '../../components/glass';

const AnimatedCircle = Animated.createAnimatedComponent(Circle);

// ---------------- ScoreGauge: 270° arc, level word large ----------------
export function ScoreGauge({score, size = 200}: {score: number; size?: number}) {
  const {word, color} = stressLevel(score);
  const stroke = 14;
  const r = (size - stroke) / 2;
  const C = 2 * Math.PI * r;
  const arc = 0.75 * C; // 270°
  const prog = useSharedValue(0);
  useEffect(() => { prog.value = withTiming(Math.max(0, Math.min(1, score / 10)), {duration: 900, easing: Easing.out(Easing.cubic)}); }, [score, prog]);
  const animatedProps = useAnimatedProps(() => ({strokeDashoffset: arc * (1 - prog.value)}));
  return (
    <View style={{width: size, height: size, alignItems: 'center', justifyContent: 'center'}}>
      <Svg width={size} height={size} style={{transform: [{rotate: '135deg'}]}}>
        <Defs>
          <SvgLinear id="gauge" x1="0" y1="0" x2="1" y2="1">
            <Stop offset="0" stopColor={color} stopOpacity={0.5} />
            <Stop offset="1" stopColor={color} stopOpacity={1} />
          </SvgLinear>
        </Defs>
        <Circle cx={size / 2} cy={size / 2} r={r} stroke="rgba(255,255,255,0.08)" strokeWidth={stroke} fill="none" strokeDasharray={`${arc} ${C}`} strokeLinecap="round" />
        <AnimatedCircle cx={size / 2} cy={size / 2} r={r} stroke="url(#gauge)" strokeWidth={stroke} fill="none" strokeDasharray={`${arc} ${C}`} strokeLinecap="round" animatedProps={animatedProps} />
      </Svg>
      <View style={styles.gaugeCenter}>
        <Text style={[styles.metricXL, {color}]}>{score.toFixed(1)}</Text>
        <Text style={styles.gaugeOutOf}>/ 10</Text>
        <Text style={[styles.gaugeWord, {color}]}>{word}</Text>
      </View>
    </View>
  );
}

export function LevelBand({score}: {score: number}) {
  const segs = [
    {label: 'LOW', color: palette.calm},
    {label: 'MILD', color: palette.mild},
    {label: 'MOD', color: palette.moderate},
    {label: 'HIGH', color: palette.high},
  ];
  const pos = Math.max(0, Math.min(1, score / 10));
  return (
    <View style={{gap: 6}}>
      <View style={{flexDirection: 'row', gap: 4}}>
        {segs.map((s, i) => {
          const active = score >= i * 2.5 && score < (i + 1) * 2.5;
          return <View key={s.label} style={{flex: 1, height: 8, borderRadius: 4, backgroundColor: active ? s.color : 'rgba(255,255,255,0.08)'}} />;
        })}
      </View>
      <View style={{height: 10, justifyContent: 'center'}}>
        <View style={{position: 'absolute', left: `${pos * 100}%`, marginLeft: -5, width: 0, height: 0, borderLeftWidth: 5, borderRightWidth: 5, borderTopWidth: 7, borderLeftColor: 'transparent', borderRightColor: 'transparent', borderTopColor: palette.textHi}} />
      </View>
      <View style={{flexDirection: 'row', justifyContent: 'space-between'}}>
        {segs.map(s => <Text key={s.label} style={styles.segLabel}>{s.label}</Text>)}
      </View>
    </View>
  );
}

// ---------------- NoiseFloorGauge: how loud the room is ----------------
// Maps the measured noise floor (dBFS) onto a quiet→loud track with the two
// decision thresholds marked, so the room reading looks measured, not empty.
// good ≤ -45 dBFS · usable -45..-30 · too noisy > -30.
export function NoiseFloorGauge({dbfs, verdict}: {dbfs?: number; verdict?: string}) {
  const W = 300, H = 74, padX = 14, trackY = 40, trackH = 12;
  const trackW = W - padX * 2;
  // clamp dBFS to a readable window and map to 0..1 (quiet left → loud right)
  const LO = -70, HI = -10;
  const v = typeof dbfs === 'number' ? Math.max(LO, Math.min(HI, dbfs)) : LO;
  const pos = (v - LO) / (HI - LO);
  const mark = (db: number) => ((Math.max(LO, Math.min(HI, db)) - LO) / (HI - LO)) * trackW + padX;
  const markerX = pos * trackW + padX;
  const tone = verdict === 'good' ? palette.calm : verdict === 'usable' ? palette.moderate : palette.high;
  const prog = useSharedValue(0);
  useEffect(() => { prog.value = withTiming(1, {duration: 700, easing: Easing.out(Easing.cubic)}); }, [prog]);
  return (
    <View style={{gap: 8}}>
      <Svg width={W} height={H}>
        <Defs>
          <SvgLinear id="noise" x1="0" y1="0" x2="1" y2="0">
            <Stop offset="0" stopColor={palette.calm} stopOpacity={0.85} />
            <Stop offset="0.5" stopColor={palette.moderate} stopOpacity={0.85} />
            <Stop offset="1" stopColor={palette.high} stopOpacity={0.9} />
          </SvgLinear>
        </Defs>
        <Rect x={padX} y={trackY} width={trackW} height={trackH} rx={6} fill="url(#noise)" opacity={0.9} />
        {/* threshold ticks */}
        {[-45, -30].map(db => <Rect key={db} x={mark(db) - 1} y={trackY - 6} width={2} height={trackH + 12} fill="rgba(255,255,255,0.55)" />)}
        {/* measured marker */}
        <Circle cx={markerX} cy={trackY + trackH / 2} r={9} fill={palette.bg900} stroke={tone} strokeWidth={3} />
        <SvgTextNative x={padX} y={22} fill={palette.textLow} fontSize={11}>Quiet</SvgTextNative>
        <SvgTextNative x={W - padX} y={22} fill={palette.textLow} fontSize={11} textAnchor="end">Loud</SvgTextNative>
      </Svg>
    </View>
  );
}

// ---------------- MetricTile: room results ----------------
export function MetricTile({icon: Icon, label, value, level, tone = palette.textHi}: {icon: LucideIcon; label: string; value: string; level: number; tone?: string}) {
  return (
    <View style={styles.tile}>
      <View style={styles.tileTopEdge} pointerEvents="none" />
      <View style={styles.tileHead}><Icon color={palette.textLow} size={16} /><Text style={styles.tileLabel}>{label}</Text></View>
      <Text style={[styles.tileValue, {color: tone}]}>{value}</Text>
      <View style={{flexDirection: 'row', gap: 3, marginTop: 4}}>
        {[0, 1, 2, 3].map(i => <View key={i} style={{flex: 1, height: 5, borderRadius: 3, backgroundColor: i < Math.round(level * 4) ? tone : 'rgba(255,255,255,0.08)'}} />)}
      </View>
    </View>
  );
}

// ---------------- LinearVisualizer: mirrored, live ----------------
export function LinearVisualizer({levels, active}: {levels: number[]; active: boolean}) {
  return (
    <View style={styles.viz}>
      {levels.map((v, i) => {
        const h = 4 + v * 28;
        return (
          <View key={i} style={{width: 3, alignItems: 'center', justifyContent: 'center'}}>
            <View style={{width: 3, height: h, borderRadius: 2, backgroundColor: active ? (i % 2 ? palette.aqua : palette.violet) : 'rgba(255,255,255,0.14)', opacity: active ? 0.55 + v * 0.45 : 0.5}} />
          </View>
        );
      })}
    </View>
  );
}

// ---------------- AnalysingOverlay ----------------
const ANALYSING_LINES = [
  'Listening to the tone underneath your words',
  'Reading your pitch, your pauses, the steadiness in your voice',
  'Comparing this with how you usually sound',
  'Putting it together',
];
export function AnalysingLines() {
  const [i, setI] = useState(0);
  useEffect(() => {
    const id = setInterval(() => setI(v => (v + 1) % ANALYSING_LINES.length), 3500);
    return () => clearInterval(id);
  }, []);
  return (
    <View style={{alignItems: 'center', gap: space.md}}>
      <Text style={styles.analysingLine}>{ANALYSING_LINES[i]}</Text>
      <View style={{flexDirection: 'row', gap: 8}}>
        {ANALYSING_LINES.map((_, k) => <View key={k} style={{width: 7, height: 7, borderRadius: 4, backgroundColor: k === i ? palette.aqua : 'rgba(255,255,255,0.2)'}} />)}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  gaugeCenter: {position: 'absolute', alignItems: 'center'},
  metricXL: {...T.metricXL},
  gaugeOutOf: {...T.caption, color: palette.textLow, marginTop: -6},
  gaugeWord: {...T.h2, fontWeight: '700', letterSpacing: 2, marginTop: 6},
  segLabel: {...T.label, color: palette.textLow, fontSize: 10},
  tile: {flex: 1, backgroundColor: palette.surface, borderRadius: radius.md, padding: space.lg, gap: 6, overflow: 'hidden', minHeight: 96},
  tileTopEdge: {position: 'absolute', top: 0, left: 0, right: 0, height: 1, backgroundColor: 'rgba(255,255,255,0.10)'},
  tileHead: {flexDirection: 'row', alignItems: 'center', gap: 6},
  tileLabel: {...T.label, color: palette.textLow, fontSize: 10},
  tileValue: {...T.h2, fontWeight: '700'},
  viz: {flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 2, height: 34},
  analysingLine: {...T.body, color: palette.textMid, textAlign: 'center', minHeight: 48, paddingHorizontal: space.lg},
});
