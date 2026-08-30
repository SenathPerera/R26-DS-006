// The report + research surface on the "Still Water at Night" system.
// User report (§4.6): hero with level words, before→after curve with reliability
// band, what-changed cards, voice+body chart, anomaly distribution card, DERIVED
// recommendations, trend. Research surface (§4.7): structured labelled rows +
// "Copy raw data" — never a JSON blob on screen.

import React, {useState} from 'react';
import {NativeModules, Pressable, StyleSheet, Text, View} from 'react-native';
import Svg, {Circle, Defs, LinearGradient as SvgLinear, Path, Polyline, Rect, Stop, Text as SvgTextNative} from 'react-native-svg';
import LinearGradient from 'react-native-linear-gradient';
import {Activity, ChevronDown, ChevronRight, Clock, Copy, HeartPulse, Repeat, Sparkles, Wind} from 'lucide-react-native';
import type {Anomaly, Comparison, CrossModal, FullSessionResult, StressResult} from '../../services/api/componentDService';
import type {SavedVoiceSession} from './voiceHistory';
import {palette, radius, space, type as T} from '../../theme/design';
import {GlassCard, stressLevel} from '../../components/glass';

// ---- plain-language translators (shared with the check-in result card) ----
export function scoreToPhrase(score: number): string {
  if (score >= 7) return "you're carrying a lot right now";
  if (score >= 4.5) return "there's a fair bit on you right now";
  if (score >= 2.5) return "you're a little tense, but holding steady";
  return "you're fairly settled right now";
}
export function typeToPhrase(type?: string): string {
  switch (type) {
    case 'withdrawn':
    case 'shutdown':
      return 'Your voice sounds withdrawn rather than agitated — more worn down than wound up.';
    case 'activated':
      return 'Your voice sounds wound up and running hot rather than shut down.';
    case 'positive':
      return 'Your voice sounds like you’re in a genuinely lighter place.';
    default:
      return 'Your voice sits somewhere in the middle right now.';
  }
}

// ================= charts =================
function BeforeAfterCurve({before, after, reliable}: {before: number; after: number; reliable: boolean}) {
  const W = 320, H = 150, padX = 40, padY = 24;
  const x0 = padX, x1 = W - padX;
  const y = (v: number) => padY + (1 - Math.max(0, Math.min(10, v)) / 10) * (H - padY * 2);
  const band = reliable ? 0.6 : Math.max(1.0, Math.abs(after - before) + 0.4);
  const improved = after < before;
  const line = improved ? palette.calm : reliable ? palette.high : palette.textLow;
  const mx = (x0 + x1) / 2;
  const path = `M ${x0} ${y(before)} C ${mx} ${y(before)}, ${mx} ${y(after)}, ${x1} ${y(after)}`;
  const area = `${path} L ${x1} ${H - padY} L ${x0} ${H - padY} Z`;
  return (
    <Svg width={W} height={H}>
      <Defs>
        <SvgLinear id="area" x1="0" y1="0" x2="0" y2="1">
          <Stop offset="0" stopColor={line} stopOpacity={0.28} />
          <Stop offset="1" stopColor={line} stopOpacity={0} />
        </SvgLinear>
      </Defs>
      <Rect x={x0} y={y(before + band)} width={x1 - x0} height={Math.abs(y(before - band) - y(before + band))} fill="rgba(255,255,255,0.06)" rx={8} />
      <Path d={area} fill="url(#area)" />
      <Path d={path} stroke={line} strokeWidth={3} fill="none" strokeLinecap="round" />
      <Circle cx={x0} cy={y(before)} r={7} fill={stressLevel(before).color} />
      <Circle cx={x1} cy={y(after)} r={7} fill={stressLevel(after).color} />
      <SvgTextNative x={x0} y={y(before) - 14} fill={palette.textHi} fontSize={11} fontWeight="bold" textAnchor="middle">{stressLevel(before).word}</SvgTextNative>
      <SvgTextNative x={x1} y={y(after) - 14} fill={palette.textHi} fontSize={11} fontWeight="bold" textAnchor="middle">{stressLevel(after).word}</SvgTextNative>
    </Svg>
  );
}

function VoiceBodyChart({cross}: {cross: CrossModal}) {
  const W = 300, H = 120, padX = 40, padY = 18;
  const x0 = padX, x1 = W - padX;
  const vy = (v?: number) => padY + (1 - Math.max(0, Math.min(10, v ?? 0)) / 10) * (H - padY * 2);
  const {voice, body} = cross;
  return (
    <Svg width={W} height={H}>
      {body?.pre != null && body?.post != null ? <Polyline points={`${x0},${vy(body.pre)} ${x1},${vy(body.post)}`} fill="none" stroke={palette.violet} strokeWidth={2.5} strokeLinecap="round" /> : null}
      {voice?.pre != null && voice?.post != null ? <Polyline points={`${x0},${vy(voice.pre)} ${x1},${vy(voice.post)}`} fill="none" stroke={palette.aqua} strokeWidth={2.5} strokeLinecap="round" /> : null}
      {[[x0, voice?.pre, palette.aqua], [x1, voice?.post, palette.aqua], [x0, body?.pre, palette.violet], [x1, body?.post, palette.violet]].map(([x, v, c], i) => v != null ? <Circle key={i} cx={x as number} cy={vy(v as number)} r={5} fill={c as string} /> : null)}
    </Svg>
  );
}

export function TrendChart({sessions}: {sessions: SavedVoiceSession[]}) {
  const data = [...sessions].reverse().slice(-8);
  const W = 320, H = 150, padX = 24, padY = 20;
  const n = data.length;
  const x = (i: number) => padX + (n <= 1 ? (W - padX * 2) / 2 : (i / (n - 1)) * (W - padX * 2));
  const y = (v: number) => padY + (1 - Math.max(0, Math.min(10, v)) / 10) * (H - padY * 2);
  return (
    <View style={{gap: 8}}>
      <Svg width={W} height={H}>
        {[0, 5, 10].map(g => <Rect key={g} x={padX} y={y(g)} width={W - padX * 2} height={1} fill="rgba(255,255,255,0.06)" />)}
        <Polyline points={data.map((s, i) => `${x(i)},${y(s.pre?.stress_score ?? 0)}`).join(' ')} fill="none" stroke={palette.moderate} strokeWidth={2.5} />
        <Polyline points={data.map((s, i) => `${x(i)},${y(s.post?.stress_score ?? 0)}`).join(' ')} fill="none" stroke={palette.calm} strokeWidth={2.5} />
        {data.map((s, i) => <React.Fragment key={i}><Circle cx={x(i)} cy={y(s.pre?.stress_score ?? 0)} r={4} fill={palette.moderate} /><Circle cx={x(i)} cy={y(s.post?.stress_score ?? 0)} r={4} fill={palette.calm} /></React.Fragment>)}
      </Svg>
      <View style={styles.legendRow}>
        <Legend color={palette.moderate} label="Before" /><Legend color={palette.calm} label="After" />
      </View>
    </View>
  );
}

function AnomalyDistribution({markerScore}: {markerScore: number}) {
  const W = 300, H = 90, pad = 20;
  const mid = W / 2;
  // simple bell curve
  const pts: string[] = [];
  for (let i = 0; i <= 40; i++) {
    const t = i / 40;
    const xx = pad + t * (W - pad * 2);
    const g = Math.exp(-Math.pow((t - 0.5) * 5, 2));
    const yy = H - pad - g * (H - pad * 2);
    pts.push(`${xx},${yy}`);
  }
  const markerX = pad + Math.max(0, Math.min(1, markerScore / 10)) * (W - pad * 2);
  return (
    <Svg width={W} height={H}>
      <Polyline points={pts.join(' ')} fill="none" stroke={`${palette.aqua}88`} strokeWidth={2} />
      <Rect x={markerX - 1} y={pad * 0.5} width={2} height={H - pad} fill={palette.aqua} />
      <Circle cx={markerX} cy={pad * 0.5} r={4} fill={palette.aqua} />
    </Svg>
  );
}
function Legend({color, label}: {color: string; label: string}) {
  return <View style={styles.legendItem}><View style={[styles.swatch, {backgroundColor: color}]} /><Text style={styles.legendText}>{label}</Text></View>;
}

// ================= recommendations (§4.6.6, derived) =================
type Rec = {icon: any; title: string; text: string};
function buildRecommendations(c: Comparison, post: StressResult, cross: CrossModal | null | undefined, sessions: number): Rec[] {
  const recs: Rec[] = [];
  const improved = c.reliable && c.direction === 'improved';
  const worsened = c.reliable && c.direction === 'worsened';
  const bigDrop = improved && Math.abs(c.delta) >= 3;
  const type = post.stress_type;
  if (bigDrop) recs.push({icon: Repeat, title: 'Try the same again', text: 'This session type worked for you today — worth repeating.'});
  if (!c.reliable && (type === 'shutdown' || type === 'withdrawn')) recs.push({icon: Wind, title: 'A little movement', text: 'Gentle movement may help more than stillness when you feel worn down.'});
  if (!c.reliable && type === 'activated') recs.push({icon: Clock, title: 'A longer session', text: 'Try a longer or breath-led session to let the system settle.'});
  if (worsened) recs.push({icon: Clock, title: 'Keep it shorter', text: 'A shorter session, and check the time of day — evenings may suit you better.'});
  if (cross && !cross.validated && !cross.low_confidence) recs.push({icon: HeartPulse, title: 'A wind-down', text: 'Your body settled but the mind is still busy — a wind-down could close the gap.'});
  if (sessions < 5) recs.push({icon: Sparkles, title: 'Keep going', text: 'Accuracy improves with each session — a few more and this gets sharper.'});
  return recs.slice(0, 3);
}

// ================= §4.6 The report =================
export function ReportView({full, pre, post, history = []}: {full: FullSessionResult; pre: StressResult; post: StressResult; history?: SavedVoiceSession[]}) {
  const c: Comparison = full.comparison;
  const reliable = c.reliable;
  const improved = c.direction === 'improved';
  const worsened = c.direction === 'worsened';
  const preScore = c.pre_stress ?? pre.stress_score;
  const postScore = c.post_stress ?? post.stress_score;
  const delta = c.delta ?? postScore - preScore;
  const cross = full.crossmodal;
  const anomaly: Anomaly | null = full.anomaly;

  const headline = !reliable ? "You're holding steady."
    : improved && Math.abs(delta) >= 3 ? 'That was a real shift.'
    : improved && Math.abs(delta) >= 1.5 ? "You've come down a long way."
    : improved ? 'Something eased.'
    : worsened ? "Something's still sitting with you."
    : "You're holding steady.";
  const heroColor = improved && reliable ? palette.calm : worsened && reliable ? palette.high : palette.mild;

  const anomalyClaims = reliable && !!anomaly && anomaly.anomaly;
  const anomalyGood = !anomalyClaims || anomaly?.anomaly_direction === 'unusual_improvement';
  const anomalyText = !reliable ? 'This session looked typical for you.'
    : !anomaly || !anomaly.anomaly ? 'This session looked like your usual pattern.'
    : anomaly.anomaly_direction === 'unusual_improvement' ? 'This was an unusually good session for you — a strong result.'
    : "This one looked different from your usual. Worth keeping an eye on, nothing more.";
  const recs = buildRecommendations(c, post, cross, history.length);

  return (
    <View style={{gap: space.lg}}>
      {/* 1 · hero */}
      <View style={[styles.hero, {borderColor: `${heroColor}44`}]}>
        <LinearGradient colors={[`${heroColor}22`, 'transparent']} style={StyleSheet.absoluteFill} />
        <Text style={[styles.heroDelta, {color: heroColor}]}>{delta < 0 ? '↓' : delta > 0 ? '↑' : ''}{Math.abs(delta).toFixed(1)}</Text>
        <Text style={styles.heroTitle}>{headline}</Text>
        <View style={styles.heroRow}>
          <MiniStat score={preScore} label="before" />
          <ChevronRight color={palette.textLow} size={20} />
          <MiniStat score={postScore} label="after" />
          <View style={styles.miniStat}><Text style={[styles.miniWord, {color: heroColor}]}>{reliable ? 'REAL' : 'STEADY'}</Text><Text style={styles.miniLabel}>change</Text></View>
        </View>
      </View>

      {/* 2 · curve */}
      <GlassCard>
        <Text style={styles.section}>Before → after</Text>
        <View style={{alignItems: 'center'}}><BeforeAfterCurve before={preScore} after={postScore} reliable={reliable} /></View>
        {!reliable ? <Text style={styles.caption}>The change sits inside the grey band — too small to call a real difference yet.</Text> : null}
      </GlassCard>

      {/* 3 · what changed */}
      <GlassCard>
        <Text style={styles.section}>What changed</Text>
        <ChangeRow accent={improved && reliable ? palette.calm : worsened && reliable ? palette.high : palette.mild} title="Did the session help?"
          text={reliable ? (improved ? 'Your stress came down by a real, measurable amount.' : 'Your stress read a little higher afterwards.') : "The change was small enough that we can't call it real yet."} />
        <ChangeRow accent={palette.violet} title="Your voice and your body" text={bodySentence(cross)} />
        <ChangeRow accent={anomalyGood ? palette.calm : palette.moderate} title="Compared with your usual" text={anomalyText} />
      </GlassCard>

      {/* 4 · voice and body */}
      {cross && (cross.voice?.pre != null || cross.body?.pre != null) ? (
        <GlassCard>
          <Text style={styles.section}>Your voice and your body</Text>
          <View style={{alignItems: 'center'}}><VoiceBodyChart cross={cross} /></View>
          <View style={styles.legendRow}><Legend color={palette.aqua} label="Voice" /><Legend color={palette.violet} label="Body" /></View>
          <Text style={styles.caption}>Your voice tells us how you feel; your wristband tells us how activated your body is. Together they give a fuller picture than either alone.</Text>
        </GlassCard>
      ) : null}

      {/* 5 · anomaly distribution */}
      <GlassCard>
        <Text style={styles.section}>Compared with your usual</Text>
        <View style={{alignItems: 'center'}}><AnomalyDistribution markerScore={postScore} /></View>
        <View style={styles.rowStart}>
          <Sparkles color={anomalyGood ? palette.calm : palette.moderate} size={18} />
          <Text style={[styles.changeText, {flex: 1}]}>{anomalyText}</Text>
        </View>
        {history.length < 5 ? (
          <View style={{gap: 6, marginTop: 4}}>
            <Text style={styles.caption}>Getting to know you</Text>
            <View style={styles.progTrack}><View style={[styles.progFill, {width: `${Math.min(100, (history.length / 5) * 100)}%`}]} /></View>
            <Text style={styles.caption}>{Math.min(history.length, 5)} of 5 sessions</Text>
          </View>
        ) : null}
      </GlassCard>

      {/* 6 · recommendations */}
      {recs.length ? (
        <GlassCard>
          <Text style={styles.section}>What might help next</Text>
          {recs.map((r, i) => <ChangeRow key={i} accent={palette.aqua} title={r.title} text={r.text} icon={r.icon} />)}
        </GlassCard>
      ) : null}

      {/* 7 · trend */}
      <GlassCard>
        <Text style={styles.section}>Your trend</Text>
        {history.length <= 1 ? <Text style={styles.body}>Your trend will appear after a few sessions.</Text> : <TrendChart sessions={history} />}
      </GlassCard>

      <Text style={styles.disclaimer}>This is a wellbeing estimate to help you reflect — not a medical diagnosis.</Text>
    </View>
  );
}

function MiniStat({score, label}: {score: number; label: string}) {
  const {word, color} = stressLevel(score);
  return <View style={styles.miniStat}><Text style={styles.miniScore}>{score.toFixed(1)}</Text><Text style={[styles.miniWord, {color}]}>{word}</Text><Text style={styles.miniLabel}>{label}</Text></View>;
}
function ChangeRow({accent, title, text, icon: Icon}: {accent: string; title: string; text: string; icon?: any}) {
  return (
    <View style={styles.changeRow}>
      <View style={[styles.accentBar, {backgroundColor: accent}]} />
      <View style={{flex: 1, gap: 4}}>
        <View style={styles.rowStart}>{Icon ? <Icon color={accent} size={16} /> : null}<Text style={styles.changeTitle}>{title}</Text></View>
        <Text style={styles.changeText}>{text}</Text>
      </View>
    </View>
  );
}
function bodySentence(cross?: CrossModal | null): string {
  if (!cross) return 'We didn’t get a wristband reading this time, so this is based on your voice alone.';
  if (cross.low_confidence) return 'Your voice was hard to read this time, so we leaned on your heart signal instead.';
  if (cross.validated) return 'Your voice and your body agreed — a consistent picture.';
  return 'Your body settled, but your voice still sounds tense — that’s common, and it usually catches up.';
}

// ================= §4.7 Research surface (Validate) — structured, no JSON =================
export function ResearchDetails({pre, post, full}: {pre?: StressResult; post?: StressResult; full?: FullSessionResult}) {
  const [open, setOpen] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const toggle = (k: string) => setOpen(o => (o === k ? null : k));
  const copyRaw = () => {
    const mod = NativeModules.AudioRecorder as {copyToClipboard?: (t: string) => void} | undefined;
    mod?.copyToClipboard?.(JSON.stringify({pre, post, full}, null, 2));
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  };
  const rows = (r?: StressResult) => r ? [
    ['Stress', r.stress_score?.toFixed(2)], ['Level', stressLevel(r.stress_score ?? 0).word], ['Type', r.stress_type ?? '—'],
    ['Valence', r.valence?.toFixed(3)], ['Arousal', r.arousal?.toFixed(3)], ['Confidence', r.confidence?.toFixed(3)],
  ] as [string, string][] : [];
  const cross = full?.crossmodal;
  return (
    <View style={{gap: space.sm}}>
      <Group label="Voice readings" open={open === 'v'} onToggle={() => toggle('v')}>
        <TwoCol title="BEFORE" rows={rows(pre)} />
        <TwoCol title="AFTER" rows={rows(post)} />
      </Group>
      <Group label="Voice × heart rate" open={open === 'x'} onToggle={() => toggle('x')}>
        <TwoCol title="" rows={cross ? [['Agreement', String(cross.agreement ?? '—')], ['Validated', String(cross.validated ?? '—')], ['Low confidence', String(cross.low_confidence ?? '—')], ['Mismatch', cross.mismatch_type ?? 'none']] : [['—', 'no data']]} />
      </Group>
      <Group label="Session pattern" open={open === 'a'} onToggle={() => toggle('a')}>
        <TwoCol title="" rows={full?.anomaly ? [['Unusual', String(full.anomaly.anomaly)], ['Direction', full.anomaly.anomaly_direction ?? '—'], ['Severity', full.anomaly.severity ?? '—']] : [['—', 'warming up']]} />
      </Group>
      <Group label="Your baseline" open={open === 'b'} onToggle={() => toggle('b')}>
        <TwoCol title="" rows={full?.personal_baseline ? [['Personalised', String(full.personal_baseline.personalised)], ['Band', full.personal_baseline.relative_band ?? '—']] : [['—', 'learning']]} />
      </Group>
      <Pressable onPress={copyRaw} style={styles.copyBtn}><Copy color={palette.aqua} size={15} /><Text style={styles.copyText}>{copied ? 'Copied' : 'Copy raw data'}</Text></Pressable>
    </View>
  );
}
function Group({label, open, onToggle, children}: {label: string; open: boolean; onToggle: () => void; children: React.ReactNode}) {
  return (
    <View style={styles.group}>
      <Pressable onPress={onToggle} style={styles.groupHead}>
        <Text style={styles.groupLabel}>{label}</Text>
        {open ? <ChevronDown color={palette.textLow} size={16} /> : <ChevronRight color={palette.textLow} size={16} />}
      </Pressable>
      {open ? <View style={{gap: space.sm, marginTop: space.sm}}>{children}</View> : null}
    </View>
  );
}
function TwoCol({title, rows}: {title: string; rows: [string, string][]}) {
  return (
    <View style={{gap: 3}}>
      {title ? <Text style={styles.twoColTitle}>{title}</Text> : null}
      {rows.map(([k, v]) => <View key={k} style={styles.twoColRow}><Text style={styles.twoColKey}>{k}</Text><Text style={styles.twoColVal}>{v}</Text></View>)}
    </View>
  );
}

const styles = StyleSheet.create({
  section: {...T.h2, color: palette.textHi},
  body: {...T.body, color: palette.textMid},
  caption: {...T.caption, color: palette.textLow},
  disclaimer: {...T.caption, color: palette.textLow, textAlign: 'center', paddingHorizontal: space.md},
  hero: {borderRadius: radius.lg, borderWidth: 1, padding: space.xl, gap: space.sm, overflow: 'hidden', alignItems: 'center'},
  heroDelta: {...T.metricXL},
  heroTitle: {...T.h1, color: palette.textHi, textAlign: 'center'},
  heroRow: {flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: space.md, marginTop: space.md},
  miniStat: {alignItems: 'center', gap: 2},
  miniScore: {...T.h2, color: palette.textHi, fontWeight: '700'},
  miniWord: {...T.label, fontSize: 11},
  miniLabel: {...T.caption, color: palette.textLow, fontSize: 11},
  rowStart: {flexDirection: 'row', alignItems: 'center', gap: 8},
  changeRow: {flexDirection: 'row', gap: space.md, paddingVertical: 4},
  accentBar: {width: 3, borderRadius: 2, alignSelf: 'stretch'},
  changeTitle: {...T.bodyMid, color: palette.textHi, fontWeight: '700'},
  changeText: {...T.caption, color: palette.textMid, lineHeight: 19},
  legendRow: {flexDirection: 'row', justifyContent: 'center', gap: space.xl},
  legendItem: {flexDirection: 'row', alignItems: 'center', gap: 6},
  swatch: {width: 12, height: 12, borderRadius: 3},
  legendText: {...T.caption, color: palette.textMid},
  progTrack: {height: 8, borderRadius: 5, backgroundColor: 'rgba(255,255,255,0.08)', overflow: 'hidden'},
  progFill: {height: '100%', backgroundColor: palette.aqua, borderRadius: 5},
  group: {backgroundColor: 'rgba(255,255,255,0.04)', borderRadius: radius.md, padding: space.md},
  groupHead: {flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center'},
  groupLabel: {...T.bodyMid, color: palette.textHi, fontWeight: '600'},
  twoColTitle: {...T.label, color: palette.aqua, fontSize: 10},
  twoColRow: {flexDirection: 'row', justifyContent: 'space-between'},
  twoColKey: {...T.caption, color: palette.textMid},
  twoColVal: {...T.caption, color: palette.textHi, fontFamily: 'monospace'},
  copyBtn: {flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 8, paddingVertical: 10, borderRadius: radius.md, borderWidth: 1, borderColor: palette.hairline},
  copyText: {...T.caption, color: palette.aqua, fontWeight: '700'},
});
