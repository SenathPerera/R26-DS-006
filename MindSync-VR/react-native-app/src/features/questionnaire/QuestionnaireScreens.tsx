import React, {useCallback, useMemo, useState} from 'react';
import {ScrollView, StyleSheet, Text, View} from 'react-native';
import {useFocusEffect} from '@react-navigation/native';
import {ChevronRight} from 'lucide-react-native';
import Svg, {Circle as SvgCircle, Polyline} from 'react-native-svg';
import {Card, ChoiceChip, Field, Header, PrimaryButton, Screen, SecondaryButton, uiStyles} from '../../components/ui';
import {questionnaireTemplates, useMindSyncStore} from '../../store/useMindSyncStore';
import {QuestionnaireQuestion} from '../../types/domain';
import {colors} from '../../theme/theme';
import {AuroraBackground} from '../../components/AuroraBackground';
import {GlassCard, stressLevel} from '../../components/glass';
import {palette, radius, space, type as T} from '../../theme/design';
import {loadSessions, setSelfReport, type SavedVoiceSession} from '../voice/voiceHistory';
import {ResearchDetails, TrendChart} from '../voice/VoiceResults';

// The Validate tab, reworked onto the "Still Water at Night" system. It's the
// researcher's-eye surface: a trend across sessions, each check-in as a rich
// card, and — the key addition — a self-report rating per session so we can see
// how closely the voice reading matches how the person actually felt.
export function QuestionnaireHomeScreen() {
  const [sessions, setSessions] = useState<SavedVoiceSession[]>([]);
  // Reload every time the tab gains focus, so a session completed elsewhere in
  // the app shows up immediately (tab screens stay mounted, so a plain mount
  // effect would only run once).
  useFocusEffect(useCallback(() => { loadSessions().then(setSessions).catch(() => {}); }, []));
  const fmt = (ts: number) => new Date(ts).toLocaleString([], {month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit'});
  const rate = (id: string, v: number) => { setSelfReport(id, v).then(setSessions).catch(() => {}); };

  return (
    <AuroraBackground variant="report">
      <ScrollView contentContainerStyle={vs.scroll}>
        <Text style={vs.title}>Your validation record</Text>
        <Text style={vs.subtitle}>Every check-in, its before → after change, and how the voice reading lines up with how you actually felt.</Text>

        <TrendsSummary sessions={sessions} />

        <Text style={vs.section}>YOUR CHECK-INS</Text>
        {sessions.length
          ? sessions.map(s => <SessionCard key={s.id} s={s} fmt={fmt} onRate={rate} />)
          : <GlassCard><Text style={vs.body}>Your check-in readings will appear here after your first session.</Text></GlassCard>}

        <AgreementSummary sessions={sessions} />

        <Text style={vs.section}>OTHER ASSESSMENTS</Text>
        {questionnaireTemplates.map(template => (
          <GlassCard key={template.id}>
            <Text style={vs.cardTitle}>{template.title}</Text>
            <Text style={vs.body}>{template.description}</Text>
          </GlassCard>
        ))}
      </ScrollView>
    </AuroraBackground>
  );
}

function TrendsSummary({sessions}: {sessions: SavedVoiceSession[]}) {
  if (sessions.length <= 1) {
    return <GlassCard><Text style={vs.cardTitle}>Your trend</Text><Text style={vs.body}>Your before/after trend appears once you have a couple of sessions.</Text></GlassCard>;
  }
  const changes = sessions.map(s => (s.post?.stress_score ?? 0) - (s.pre?.stress_score ?? 0));
  const avg = changes.reduce((a, b) => a + b, 0) / changes.length;
  const best = Math.min(0, ...changes);
  return (
    <GlassCard>
      <Text style={vs.cardTitle}>Your trend</Text>
      <View style={{alignItems: 'center'}}><TrendChart sessions={sessions} /></View>
      <View style={vs.statRow}>
        <Stat label="Sessions" value={String(sessions.length)} />
        <Stat label="Avg change" value={`${avg <= 0 ? '↓' : '↑'} ${Math.abs(avg).toFixed(1)}`} color={avg <= 0 ? palette.calm : palette.high} />
        <Stat label="Best drop" value={`↓ ${Math.abs(best).toFixed(1)}`} color={palette.calm} />
      </View>
    </GlassCard>
  );
}

function SessionCard({s, fmt, onRate}: {s: SavedVoiceSession; fmt: (n: number) => string; onRate: (id: string, v: number) => void}) {
  const before = s.pre?.stress_score ?? 0;
  const after = s.post?.stress_score ?? 0;
  const change = after - before;
  return (
    <GlassCard>
      <View style={vs.rowBetween}>
        <Text style={vs.cardTitle}>{fmt(s.at)}</Text>
        <Text style={[vs.change, {color: change < 0 ? palette.calm : change > 0 ? palette.high : palette.textLow}]}>{change < 0 ? '↓' : change > 0 ? '↑' : '→'} {Math.abs(change).toFixed(1)}</Text>
      </View>
      <View style={vs.metaRow}>
        <Text style={vs.meta}>Before {before.toFixed(1)}</Text>
        <ChevronRight color={palette.textLow} size={14} />
        <Text style={vs.meta}>After {after.toFixed(1)}</Text>
        {s.language ? <Text style={vs.meta}>· {s.language}</Text> : null}
      </View>
      <MiniBeforeAfter before={before} after={after} />
      <SelfReport s={s} onRate={onRate} />
      <ResearchDetails pre={s.pre} post={s.post} full={s.full} />
    </GlassCard>
  );
}

function MiniBeforeAfter({before, after}: {before: number; after: number}) {
  const W = 280, H = 46, padX = 10, padY = 8;
  const y = (v: number) => padY + (1 - Math.max(0, Math.min(10, v)) / 10) * (H - padY * 2);
  const col = after < before ? palette.calm : after > before ? palette.high : palette.textLow;
  return (
    <Svg width="100%" height={H} viewBox={`0 0 ${W} ${H}`}>
      <Polyline points={`${padX},${y(before)} ${W - padX},${y(after)}`} fill="none" stroke={col} strokeWidth={2.5} strokeLinecap="round" />
      <SvgCircle cx={padX} cy={y(before)} r={4} fill={stressLevel(before).color} />
      <SvgCircle cx={W - padX} cy={y(after)} r={4} fill={stressLevel(after).color} />
    </Svg>
  );
}

function SelfReport({s, onRate}: {s: SavedVoiceSession; onRate: (id: string, v: number) => void}) {
  const voice = s.post?.stress_score ?? 0;
  if (s.selfPost == null) {
    return (
      <View style={vs.selfBox}>
        <Text style={vs.selfPrompt}>How stressed did you actually feel?  <Text style={vs.selfHint}>0 calm · 10 very stressed</Text></Text>
        <View style={vs.chipRow}>
          {Array.from({length: 11}).map((_, n) => (
            <Text key={n} onPress={() => onRate(s.id, n)} style={vs.rateChip}>{n}</Text>
          ))}
        </View>
      </View>
    );
  }
  const gap = Math.abs(s.selfPost - voice);
  const label = gap <= 1.5 ? 'Close match' : gap <= 3 ? 'Some gap' : 'Diverges';
  const col = gap <= 1.5 ? palette.calm : gap <= 3 ? palette.moderate : palette.high;
  return (
    <View style={vs.selfBox}>
      <View style={vs.rowBetween}>
        <Text style={vs.meta}>You felt {s.selfPost}/10 · Voice {voice.toFixed(1)}/10</Text>
        <Text style={[vs.agree, {color: col}]}>{label}</Text>
      </View>
    </View>
  );
}

function AgreementSummary({sessions}: {sessions: SavedVoiceSession[]}) {
  const rated = sessions.filter(s => s.selfPost != null && s.post);
  if (rated.length === 0) {
    return (
      <GlassCard accent={palette.aqua}>
        <Text style={vs.cardTitle}>Voice vs self-report</Text>
        <Text style={vs.body}>Rate how you felt on any session above, and this tracks how closely the voice reading matches your own sense of your stress — the core of the validation.</Text>
      </GlassCard>
    );
  }
  const gaps = rated.map(s => Math.abs((s.selfPost as number) - (s.post as NonNullable<typeof s.post>).stress_score));
  const mean = gaps.reduce((a, b) => a + b, 0) / gaps.length;
  const within = gaps.filter(g => g <= 1.5).length;
  return (
    <GlassCard accent={palette.aqua}>
      <Text style={vs.cardTitle}>Voice vs self-report</Text>
      <View style={vs.statRow}>
        <Stat label="Rated" value={String(rated.length)} />
        <Stat label="Mean gap" value={mean.toFixed(1)} color={mean <= 1.5 ? palette.calm : mean <= 3 ? palette.moderate : palette.high} />
        <Stat label="Close" value={`${within}/${rated.length}`} color={palette.calm} />
      </View>
      <Text style={vs.body}>On average the voice reading is within {mean.toFixed(1)} points of how you said you felt — {within} of {rated.length} were a close match.</Text>
    </GlassCard>
  );
}

function Stat({label, value, color}: {label: string; value: string; color?: string}) {
  return <View style={vs.stat}><Text style={[vs.statValue, color ? {color} : null]}>{value}</Text><Text style={vs.statLabel}>{label}</Text></View>;
}

const vs = StyleSheet.create({
  scroll: {paddingHorizontal: 20, paddingTop: space.md, paddingBottom: 80, gap: space.lg},
  title: {...T.h1, color: palette.textHi},
  subtitle: {...T.body, color: palette.textMid},
  section: {...T.label, color: palette.textLow, marginTop: space.sm},
  cardTitle: {...T.h2, color: palette.textHi},
  body: {...T.body, color: palette.textMid},
  rowBetween: {flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between'},
  change: {...T.bodyMid, fontWeight: '800'},
  metaRow: {flexDirection: 'row', alignItems: 'center', gap: 6},
  meta: {...T.caption, color: palette.textMid},
  statRow: {flexDirection: 'row', justifyContent: 'space-between', marginTop: space.sm},
  stat: {alignItems: 'center', gap: 2, flex: 1},
  statValue: {...T.h2, color: palette.textHi, fontWeight: '700'},
  statLabel: {...T.caption, color: palette.textLow},
  selfBox: {gap: 8, paddingTop: space.sm, borderTopWidth: 1, borderTopColor: palette.hairline},
  selfPrompt: {...T.caption, color: palette.textMid},
  selfHint: {...T.caption, color: palette.textLow},
  chipRow: {flexDirection: 'row', flexWrap: 'wrap', gap: 6},
  rateChip: {minWidth: 30, textAlign: 'center', color: palette.textHi, borderWidth: 1, borderColor: palette.hairline, borderRadius: radius.sm, paddingVertical: 6, paddingHorizontal: 8, overflow: 'hidden'},
  agree: {...T.caption, fontWeight: '700'},
});

export function QuestionnaireFormScreen({navigation, route}: any) {
  const template = questionnaireTemplates.find(item => item.id === route.params?.templateId) ?? questionnaireTemplates[0];
  const activeSession = useMindSyncStore(state => state.activeSession);
  const submit = useMindSyncStore(state => state.submitQuestionnaire);
  const [answers, setAnswers] = useState<Record<string, string | number | string[]>>({});
  const [index, setIndex] = useState(0);
  const visible = useMemo(() => template.questions.filter(question => branchVisible(question, answers)), [template.questions, answers]);
  const question = visible[index] ?? visible[visible.length - 1];
  const value = answers[question.id];
  const valid = !question.required || value !== undefined && value !== '';
  const finish = () => { submit(template.id, activeSession?.id ?? null, answers); navigation.navigate('MainTabs', {screen: 'Validate'}); };
  return (
    <Screen>
      <Header title={template.title} subtitle={`Question ${index + 1} of ${visible.length}`} onBack={navigation.goBack} />
      <View style={{height: 5, backgroundColor: colors.borderSoft, borderRadius: 4}}><View style={{height: 5, width: `${((index + 1) / visible.length) * 100}%`, backgroundColor: colors.teal, borderRadius: 4}} /></View>
      <Card>
        <Text style={uiStyles.value}>{question.prompt}</Text>
        {question.helperText ? <Text style={uiStyles.body}>{question.helperText}</Text> : null}
        <QuestionInput question={question} value={value} onChange={next => setAnswers(current => ({...current, [question.id]: next}))} />
      </Card>
      <View style={uiStyles.row}>
        {index > 0 ? <View style={{flex: 1}}><SecondaryButton label="Back" onPress={() => setIndex(value => value - 1)} /></View> : null}
        <View style={{flex: 1}}><PrimaryButton label={index === visible.length - 1 ? 'Submit securely' : 'Continue'} disabled={!valid} onPress={index === visible.length - 1 ? finish : () => setIndex(value => value + 1)} /></View>
      </View>
      <Text style={[uiStyles.label, {textAlign: 'center'}]}>Your responses are saved privately on this device.</Text>
    </Screen>
  );
}

function QuestionInput({question, value, onChange}: {question: QuestionnaireQuestion; value: unknown; onChange: (value: string | number | string[]) => void}) {
  if (question.type === 'text') return <Field label="Your response" multiline value={typeof value === 'string' ? value : ''} onChangeText={onChange} />;
  if (question.type === 'single') return <View style={uiStyles.wrap}>{question.options?.map(option => <ChoiceChip key={option} label={option} selected={value === option} onPress={() => onChange(option)} />)}</View>;
  const min = question.min ?? 1;
  const max = question.max ?? 7;
  if (question.type === 'likert' || question.type === 'slider' || question.type === 'numeric') return <View style={uiStyles.wrap}>{Array.from({length: max - min + 1}, (_, i) => i + min).map(option => <ChoiceChip key={option} label={String(option)} selected={value === option} onPress={() => onChange(option)} />)}</View>;
  return <Text style={uiStyles.body}>This response type will be available in a future study template.</Text>;
}

function branchVisible(question: QuestionnaireQuestion, answers: Record<string, unknown>) {
  if (!question.branch) return true;
  const source = answers[question.branch.whenQuestionId];
  if (question.branch.equals !== undefined) return source === question.branch.equals;
  if (question.branch.includes !== undefined) return typeof source === 'string' && ['Moderate', 'Severe'].includes(source);
  return true;
}
