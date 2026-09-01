// The voice check-in on the "Still Water at Night" system. Every stage has its
// own aurora background + StageHeader (so you always know where you are), the
// pupil-less Sarah avatar, and rich result visuals. No jargon anywhere the user
// can see.

import React, {useCallback, useEffect, useMemo, useRef, useState} from 'react';
import {ScrollView, StyleSheet, Text, View} from 'react-native';
import Svg, {Circle, Defs, Path, RadialGradient, Rect, Stop} from 'react-native-svg';
import {Clock, Lock, Mic, Sparkles} from 'lucide-react-native';
import {AuroraBackground, AuroraVariant} from '../../components/AuroraBackground';
import {GlassCard, InfoRow, PrimaryButton, StageHeader, StageId, TextLink, stressLevel} from '../../components/glass';
import {palette, space, type as T} from '../../theme/design';
import {useMindSyncStore} from '../../store/useMindSyncStore';
import {
  componentDService,
  type AmbientResult,
  type FullSessionResult,
  type StressResult,
} from '../../services/api/componentDService';
import {concatWavs, pickAudioFile, useRecorder, type RecordingResult} from '../../services/audio/nativeRecorder';
import {SarahAvatar, SarahState} from './SarahAvatar';
import {AnalysingLines, LevelBand, LinearVisualizer, MetricTile, NoiseFloorGauge, ScoreGauge} from './voiceUi';
import {ReportView, scoreToPhrase, typeToPhrase} from './VoiceResults';
import {loadSessions, saveSession, type SavedVoiceSession} from './voiceHistory';
import {useSarah} from './useSarah';
import {Users, Waves, Zap} from 'lucide-react-native';
import {createCompleteSessionRecord} from '../../services/session/completeSessionRecord';
import {sessionRecordOutbox} from '../../services/session/sessionRecordOutbox';

type Stage = StageId; // intro | room | pre | vr | post | report
type Phase = 'pre' | 'post';
type Lang = 'english' | 'sinhala' | 'tamil';

// Simple, everyday prompts. The point is only to get the user talking naturally
// for a little while — the stress score reads the AUDIO, not the words — so the
// questions are kept easy and concrete: anyone can answer them without thinking
// hard, which is exactly what keeps them speaking. We shuffle per session so the
// opener varies, ask a few each time, and merge every answer into one clip.
const QUESTION_POOL: Record<Phase, string[]> = {
  pre: [
    'Tell me a little about how your day has been so far.',
    'What did you do today before coming here?',
    'What did you have for your last meal?',
    'Tell me about one thing that happened today.',
    'In simple words, how are you feeling right now?',
    'What have you been up to this week?',
    'Is there something small on your mind today?',
    'What do you usually do to relax?',
  ],
  post: [
    'How do you feel now compared to before?',
    'What did you notice while you were in there?',
    'Tell me one word for how you feel right now.',
    'Did anything feel more relaxed after the session?',
    'What was the session like for you?',
    'How is your body feeling right now?',
    'Would you want to do this again? Why?',
    'What are you going to do after this?',
  ],
};
const ACK = 'Thank you. Can I ask one more thing…';
const SPEECH_BUDGET = 30; // seconds of speech we aim to collect across turns
const MIN_TURNS = 2;      // always hold a short conversation, never one line
const MAX_TURNS = 4;      // cap so it never drags on
const SILENCE_TAIL_SEC = 2.5;
const TEMPLE_POND_SCENE_ID = 'temple-pond';

function shuffle<T>(arr: T[]): T[] {
  const a = [...arr];
  for (let i = a.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [a[i], a[j]] = [a[j], a[i]];
  }
  return a;
}

const HEADER: Record<Stage, {title: string; status: string}> = {
  intro: {title: 'Before we begin', status: 'Sarah is here'},
  room: {title: 'Checking your space', status: 'Listening to the room'},
  pre: {title: 'Before your session', status: 'Sarah is listening'},
  vr: {title: 'Your session', status: 'Ready when you are'},
  post: {title: 'After your session', status: 'Sarah is listening'},
  report: {title: 'Your session report', status: 'Complete'},
};

function speechThreshold(a: AmbientResult | null): number {
  const f = a?.metrics?.noise_floor_rms;
  if (typeof f === 'number' && f > 0) return Math.max(0.012, Math.min(0.08, f * 2.5));
  return 0.02;
}
const delay = (ms: number) => new Promise<void>(r => setTimeout(() => r(), ms));

export function VoiceCheckInScreen({navigation}: {navigation: {goBack: () => void; navigate: (r: string) => void}}) {
  const user = useMindSyncStore(s => s.user);
  const firstName = (user?.name ?? '').split(' ')[0];
  const [stage, setStage] = useState<Stage>('intro');
  const [language, setLanguage] = useState<Lang>('english');
  const sessionId = useMemo(() => `voice-${Date.now()}`, []);
  const sessionStartedAtUnixSeconds = useMemo(() => Date.now() / 1000, []);
  const userId = user?.id ?? 'anonymous';
  const fullSessionStartedRef = useRef(false);
  const finalVisualLogRefreshStartedRef = useRef(false);
  const sessionRecordQueuedRef = useRef(false);

  const [ambient, setAmbient] = useState<AmbientResult | null>(null);
  const [pre, setPre] = useState<StressResult | null>(null);
  const [post, setPost] = useState<StressResult | null>(null);
  const [full, setFull] = useState<FullSessionResult | null>(null);
  const [history, setHistory] = useState<SavedVoiceSession[]>([]);
  const prepareVrSession = useMindSyncStore(s => s.prepareVrSession);
  const refreshVisualLog = useMindSyncStore(s => s.refreshVisualLog);
  const syncNow = useMindSyncStore(s => s.syncNow);
  const relay = useMindSyncStore(s => s.relay);

  // §3.8: warm the model the moment the check-in opens, so the analysing wait
  // is short by the time the first clip is uploaded.
  useEffect(() => { void componentDService.warmup(); loadSessions().then(setHistory).catch(() => {}); }, []);

  useEffect(() => {
    if (
      stage !== 'report'
      || !pre
      || !post
      || full
      || fullSessionStartedRef.current
    ) return;
    fullSessionStartedRef.current = true;
    componentDService.fullSession(sessionId, userId, {useMockHrv: true, language, log: true})
      .then(f => {
        setFull(f);
        const entry: SavedVoiceSession = {
          id: sessionId,
          at: Date.now(),
          participant: firstName,
          language,
          pre,
          post,
          full: f,
          vrSessionId: relay.preparedSession?.sessionId,
        };
        saveSession(entry).then(setHistory).catch(() => undefined);
      })
      .catch(() => { fullSessionStartedRef.current = false; });
  }, [stage, pre, post, full, sessionId, userId, language, firstName, relay.preparedSession?.sessionId]);

  useEffect(() => {
    const relaySessionId = relay.preparedSession?.sessionId;
    const visualLog = relay.visualLogSnapshot;
    if (
      !full
      || !pre
      || !post
      || !relaySessionId
    ) return;
    if (!visualLog?.finalized || !visualLog.deliveryAcknowledged) {
      if (!finalVisualLogRefreshStartedRef.current) {
        finalVisualLogRefreshStartedRef.current = true;
        refreshVisualLog().catch(() => undefined);
      }
      return;
    }
    if (sessionRecordQueuedRef.current) return;

    sessionRecordQueuedRef.current = true;
    const record = createCompleteSessionRecord({
      sessionId,
      participantPseudonym: user?.id ?? 'anonymous',
      startedAtUnixSeconds: sessionStartedAtUnixSeconds,
      completedAtUnixSeconds: Date.now() / 1000,
      language,
      pre,
      post,
      voiceSummary: full,
      relaySessionId,
      sceneId: TEMPLE_POND_SCENE_ID,
      visualLog,
    });
    sessionRecordOutbox.enqueue(record)
      .then(() => syncNow())
      .catch(() => { sessionRecordQueuedRef.current = false; });
  }, [
    full,
    language,
    post,
    pre,
    relay.preparedSession?.sessionId,
    relay.visualLogSnapshot,
    refreshVisualLog,
    syncNow,
    sessionId,
    sessionStartedAtUnixSeconds,
    user?.id,
  ]);

  const commonProps = {onBack: navigation.goBack};

  return (
    <AuroraBackground variant={stage as AuroraVariant}>
      <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
        {stage === 'intro' ? <IntroStage {...commonProps} name={firstName} language={language} setLanguage={setLanguage} onReady={() => setStage('room')} /> : null}
        {stage === 'room' ? <RoomStage {...commonProps} ambient={ambient} setAmbient={setAmbient} onContinue={() => setStage('pre')} /> : null}
        {stage === 'pre' || stage === 'post' ? (
          <CheckInStage key={stage} {...commonProps} phase={stage} sessionId={sessionId} userId={userId} language={language} ambient={ambient}
            result={stage === 'pre' ? pre : post} onScored={r => (stage === 'pre' ? setPre(r) : setPost(r))}
            onContinue={async () => {
              if (stage === 'pre') await prepareVrSession(sessionId);
              setStage(stage === 'pre' ? 'vr' : 'report');
            }} />
        ) : null}
        {stage === 'vr' ? <VrStage {...commonProps} name={firstName} onBack2={async () => { try { await refreshVisualLog(); } catch { /* live messages remain available */ } setStage('post'); }} /> : null}
        {stage === 'report' ? <ReportStage {...commonProps} full={full} pre={pre} post={post} history={history} onHome={() => navigation.navigate('MainTabs')} /> : null}
      </ScrollView>
    </AuroraBackground>
  );
}

/* ---------- Intro ---------- */
function IntroStage({name, language, setLanguage, onReady, onBack}: {name: string; language: Lang; setLanguage: (l: Lang) => void; onReady: () => void; onBack: () => void}) {
  const sarah = useSarah();
  useEffect(() => {
    const hi = name ? `Hi ${name}. I'm Sarah, your AI wellbeing companion.` : "Hi, I'm Sarah, your AI wellbeing companion.";
    void sarah.say(`${hi} For this VR session I'll ask you a few simple questions. I just want to hear your natural voice — that helps me sense how you're really feeling inside. There are no right or wrong answers. If you're okay with this, tap "I agree" and we'll begin.`, language);
    return () => sarah.stop();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return (
    <>
      <StageHeader stage="intro" title={HEADER.intro.title} status={HEADER.intro.status} onBack={onBack} />
      <View style={styles.avatarWrap}><SarahAvatar state={sarah.speaking ? 'speaking' : 'idle'} size="lg" /></View>
      <Text style={styles.sarahIntro}>Hi{name ? ` ${name}` : ''}, I’m Sarah — your AI wellbeing companion. I’ll guide this check-in and stay with you the whole way through.</Text>
      <GlassCard>
        <Text style={styles.display}>A few simple questions.</Text>
        <Text style={styles.body}>For this VR session I’ll ask you some easy questions — once now, once after. I just want to hear your natural voice, because it carries how you’re really feeling inside. There are no right or wrong answers.</Text>
      </GlassCard>
      <GlassCard>
        <InfoRow icon={Clock} text="About a minute" />
        <InfoRow icon={Lock} text="Private — nothing shared" />
        <InfoRow icon={Mic} text="Speak naturally, no script" />
      </GlassCard>
      <Text style={styles.smallLabel}>Which language will you speak?</Text>
      <View style={styles.pillRow}>
        <Pill label="English" active={language === 'english'} onPress={() => setLanguage('english')} />
        <Pill label="සිංහල" active={language === 'sinhala'} onPress={() => setLanguage('sinhala')} />
        <Pill label="தமிழ்" active={language === 'tamil'} onPress={() => setLanguage('tamil')} />
      </View>
      <PrimaryButton label="I agree" onPress={() => { sarah.stop(); onReady(); }} />
      <TextLink label="Not now" onPress={() => { sarah.stop(); onBack(); }} />
    </>
  );
}

/* ---------- Room ---------- */
function RoomStage({ambient, setAmbient, onContinue, onBack}: {ambient: AmbientResult | null; setAmbient: (a: AmbientResult) => void; onContinue: () => void; onBack: () => void}) {
  const sarah = useSarah();
  const rec = useRecorder();
  const [count, setCount] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [paused, setPaused] = useState(false);
  const finishRef = useRef<((r: RecordingResult | null) => void) | null>(null);

  useEffect(() => { if (rec.result && finishRef.current) { const f = finishRef.current; finishRef.current = null; f(rec.result); rec.reset(); } }, [rec.result, rec]);
  useEffect(() => { if (rec.error && finishRef.current) { const f = finishRef.current; finishRef.current = null; f(null); } }, [rec.error]);

  const listen = useCallback(async () => {
    setPaused(false);
    for (let n = 3; n >= 1; n--) { setCount(n); await delay(750); }
    setCount(null);
    const r = await new Promise<RecordingResult | null>(res => { finishRef.current = res; void rec.start({silenceThreshold: 0, minDurationMs: 0, maxDurationMs: 8500}); });
    if (!r) return;
    setBusy(true);
    try {
      const a = await componentDService.ambientCheck({uri: r.uri});
      setAmbient(a);
      void sarah.say(a.ok ? (a.verdict === 'usable' ? "There's a little background sound — a fan, maybe. That's fine, I'll adjust for it." : 'Your room sounds good. Let’s begin.') : roomSuggestion(a), 'english');
    } catch { /* offline */ } finally { setBusy(false); }
  }, [rec, sarah, setAmbient]);

  useEffect(() => {
    void (async () => {
      await sarah.say("Let's make sure the room's on your side. Stay quiet for about eight seconds while I listen — I'm only checking the background, not you.", 'english');
      if (!paused) void listen();
    })();
    return () => { sarah.stop(); void rec.cancel(); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const state: SarahState = rec.isRecording ? 'listening' : busy ? 'thinking' : sarah.speaking ? 'speaking' : 'idle';
  const micLevel = rec.levels[rec.levels.length - 1] ?? 0;

  return (
    <>
      <StageHeader stage="room" title={HEADER.room.title} status={busy ? 'Checking' : HEADER.room.status} statusColor={ambient && !ambient.ok ? palette.high : palette.aqua} done={!!ambient?.ok} onBack={onBack} />
      <View style={styles.avatarWrap}>
        <SarahAvatar state={state} size="lg" level={micLevel} hideCaption={count != null} />
        {count != null ? (
          <View style={styles.countOverlay} pointerEvents="none"><Text style={styles.count}>{count}</Text></View>
        ) : null}
      </View>

      {!ambient ? (
        <GlassCard>
          <Text style={styles.body}>{sarah.visibleText || 'One moment…'}</Text>
        </GlassCard>
      ) : null}
      {rec.isRecording ? <TextLink label="Give me a moment" onPress={() => { void rec.cancel(); setPaused(true); }} /> : null}
      {paused && !rec.isRecording && !ambient ? <PrimaryButton label="I’m ready now" onPress={() => void listen()} /> : null}

      {ambient ? (
        <>
          <VerdictBanner ambient={ambient} />
          <GlassCard>
            <View style={styles.rowStart}><Waves color={palette.textLow} size={16} /><Text style={styles.smallLabel}>HOW LOUD YOUR ROOM IS</Text></View>
            <View style={{alignItems: 'center'}}><NoiseFloorGauge dbfs={ambient.metrics?.noise_floor_dbfs} verdict={ambient.verdict} /></View>
            <Text style={styles.body}>{roomLoudnessLine(ambient)}</Text>
          </GlassCard>
          <View style={styles.tileGrid}>
            <MetricTile icon={Users} label="OTHER VOICES" value={(ambient.metrics?.speech_seconds ?? 0) > 0.3 ? 'Someone nearby' : 'None'} level={(ambient.metrics?.speech_seconds ?? 0) > 0.3 ? 0.9 : 0} tone={(ambient.metrics?.speech_seconds ?? 0) > 0.3 ? palette.high : palette.textHi} />
            <MetricTile icon={Zap} label="SUDDEN SOUNDS" value={(ambient.checks?.find(c => c.id === 'peaks')?.pass ?? true) ? 'None' : 'A few'} level={(ambient.checks?.find(c => c.id === 'peaks')?.pass ?? true) ? 0 : 0.5} tone={(ambient.checks?.find(c => c.id === 'peaks')?.pass ?? true) ? palette.textHi : palette.moderate} />
          </View>
          {ambient.ok ? <PrimaryButton label="Continue" onPress={() => { sarah.stop(); onContinue(); }} /> : <PrimaryButton label="Try again" onPress={() => void listen()} />}
          {!ambient.ok && __DEV__ ? <TextLink label="Continue anyway (dev)" onPress={() => { sarah.stop(); onContinue(); }} /> : null}
        </>
      ) : null}
    </>
  );
}

/* ---------- Check-in loop ---------- */
function CheckInStage({phase, sessionId, userId, language, ambient, result, onScored, onContinue, onBack}: {
  phase: Phase; sessionId: string; userId: string; language: Lang; ambient: AmbientResult | null;
  result: StressResult | null; onScored: (r: StressResult) => void; onContinue: () => void | Promise<void>; onBack: () => void;
}) {
  const sarah = useSarah();
  const rec = useRecorder();
  const [sub, setSub] = useState<'running' | 'processing' | 'result' | 'error'>('running');
  const [turnIndex, setTurnIndex] = useState(0);
  const [error, setError] = useState('');
  const [continuing, setContinuing] = useState(false);
  const [continueError, setContinueError] = useState('');
  const finishRef = useRef<((r: RecordingResult | null) => void) | null>(null);
  const started = useRef(false);
  const abortRef = useRef(false);
  const questions = useMemo(() => shuffle(QUESTION_POOL[phase]).slice(0, MAX_TURNS), [phase]);

  useEffect(() => { if (rec.result && finishRef.current) { const f = finishRef.current; finishRef.current = null; f(rec.result); rec.reset(); } }, [rec.result, rec]);
  useEffect(() => { if (rec.error && finishRef.current) { const f = finishRef.current; finishRef.current = null; f(null); } }, [rec.error]);

  const runLoop = useCallback(async () => {
    const qs = questions;
    const uris: string[] = [];
    let speech = 0;
    // Hold a short conversation: keep asking (up to MAX_TURNS) until we've
    // gathered ~SPEECH_BUDGET seconds of speech, but never stop before MIN_TURNS
    // so a single long answer doesn't cut Sarah off after one question. Every
    // answer is kept and merged into ONE clip that gets scored (§4).
    for (let t = 0; t < qs.length; t++) {
      setTurnIndex(t);
      await sarah.say(t === 0 ? qs[0] : `${ACK} ${qs[t]}`, language);
      await delay(450);
      const r = await new Promise<RecordingResult | null>(res => { finishRef.current = res; void rec.start({minDurationMs: 6000, silenceTailMs: SILENCE_TAIL_SEC * 1000, maxDurationMs: 20000, silenceThreshold: speechThreshold(ambient)}); });
      if (!r) break;
      uris.push(r.uri);
      speech += Math.max(0, r.durationMs / 1000 - SILENCE_TAIL_SEC);
      const turnsDone = t + 1;
      if (turnsDone >= MIN_TURNS && speech >= SPEECH_BUDGET) break;
    }
    if (abortRef.current) return; // a dev file-pick took over
    if (uris.length === 0) { setError('I didn’t catch that — let’s try again.'); setSub('error'); return; }
    setSub('processing');
    const merged = await concatWavs(uris);
    if (!merged) { setError('Something went wrong putting that together.'); setSub('error'); return; }
    try {
      const turn = await componentDService.voiceTurn({uri: merged.uri}, sessionId, {phase, userId, language, log: true, isFinal: true});
      if (turn.analysis) {
        onScored(turn.analysis);
        setSub('result');
        void sarah.say(shortReading(turn.analysis), language);
      } else { setError('That was hard to read — let’s try once more.'); setSub('error'); }
    } catch { setError('I couldn’t reach the companion just now.'); setSub('error'); }
  }, [questions, phase, language, ambient, sarah, rec, sessionId, userId, onScored]);

  // DEV demo: run a picked audio file through the exact same scoring path,
  // bypassing the live mic. Aborts the conversation loop cleanly first.
  const chooseAudioFile = useCallback(async () => {
    abortRef.current = true;
    sarah.stop();
    if (finishRef.current) { const f = finishRef.current; finishRef.current = null; f(null); }
    await rec.cancel();
    const picked = await pickAudioFile();
    if (!picked) { abortRef.current = false; void runLoop(); return; } // cancelled → resume the conversation
    setSub('processing');
    try {
      const turn = await componentDService.voiceTurn({uri: picked.uri, name: picked.name}, sessionId, {phase, userId, language, log: true, isFinal: true});
      if (turn.analysis) { onScored(turn.analysis); setSub('result'); void sarah.say(shortReading(turn.analysis), language); }
      else { setError('That file was hard to read — try another.'); setSub('error'); }
    } catch { setError('I couldn’t process that file just now.'); setSub('error'); }
  }, [runLoop, sarah, rec, sessionId, phase, userId, language, onScored]);

  useEffect(() => {
    if (started.current) return; started.current = true;
    void runLoop();
    return () => { sarah.stop(); void rec.cancel(); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const micLevel = rec.levels[rec.levels.length - 1] ?? 0;

  // Result
  if (sub === 'result' && result) {
    return (
      <>
        <StageHeader stage={phase} title={HEADER[phase].title} status="Complete" done onBack={onBack} />
        <View style={styles.avatarWrap}><SarahAvatar state={sarah.speaking ? 'speaking' : 'idle'} size="md" /></View>
        <Text style={styles.h1}>{phase === 'pre' ? capitalize(scoreToPhrase(result.stress_score)) + '.' : 'Here’s where you’re landing.'}</Text>
        <View style={{alignItems: 'center'}}><ScoreGauge score={result.stress_score} /></View>
        <LevelBand score={result.stress_score} />
        <GlassCard accent={palette.aqua}>
          <View style={styles.rowStart}><Sparkles color={palette.aqua} size={16} /><Text style={styles.sarahName}>Sarah</Text></View>
          <Text style={styles.body}>{typeToPhrase(result.stress_type)}</Text>
        </GlassCard>
        <GlassCard>
          <Text style={styles.body}>{phase === 'pre' ? 'This is a starting point, not a verdict. Let’s see what the session does.' : 'Let’s look at what changed.'}</Text>
        </GlassCard>
        {continueError ? <Text style={styles.errorText}>{continueError}</Text> : null}
        <PrimaryButton
          label={continuing ? 'Preparing your session…' : phase === 'pre' ? 'Start my session' : 'See what changed'}
          disabled={continuing}
          onPress={() => {
            sarah.stop();
            setContinuing(true);
            setContinueError('');
            Promise.resolve(onContinue())
              .catch(() => setContinueError('I couldn’t prepare the headset session. Check the relay connection and try again.'))
              .finally(() => setContinuing(false));
          }} />
      </>
    );
  }

  // Analysing
  if (sub === 'processing') {
    return (
      <>
        <StageHeader stage={phase} title={HEADER[phase].title} status="Reading your voice" onBack={onBack} />
        <View style={styles.avatarWrap}><SarahAvatar state="thinking" size="lg" /></View>
        <Text style={styles.h1Center}>Reading your voice</Text>
        <AnalysingLines />
      </>
    );
  }

  // Running / error
  const state: SarahState = rec.isRecording ? 'listening' : sarah.speaking ? 'speaking' : 'idle';
  return (
    <>
      <StageHeader stage={phase} title={HEADER[phase].title} status={HEADER[phase].status} onBack={onBack} />
      <View style={styles.avatarWrap}><SarahAvatar state={state} size="md" level={micLevel} /></View>
      <GlassCard><Text style={styles.bubble}>{sarah.visibleText || '…'}</Text></GlassCard>
      <TurnDots total={questions.length} done={turnIndex} active={rec.isRecording} />
      <Text style={styles.hintCenter}>Take your time</Text>
      {rec.isRecording ? <LinearVisualizer levels={rec.levels} active /> : null}
      {rec.isRecording ? <TextLink label="That’s all for now" onPress={() => void rec.stop()} /> : null}
      {sub === 'error' ? (<><Text style={styles.errorText}>{error}</Text><PrimaryButton label="Try again" onPress={() => { started.current = false; setSub('running'); setError(''); started.current = true; void runLoop(); }} /></>) : null}
      {__DEV__ ? <Text onPress={() => void chooseAudioFile()} style={styles.devLink}>Use an audio file (dev)</Text> : null}
    </>
  );
}

/* ---------- VR ---------- */
function VrStage({name, onBack2, onBack}: {name: string; onBack2: () => void | Promise<void>; onBack: () => void}) {
  const sarah = useSarah();
  const pairingCode = useMindSyncStore(s => s.pairingCode);
  const relay = useMindSyncStore(s => s.relay);
  useEffect(() => {
    void sarah.say(`Go ahead and put the headset on${name ? `, ${name}` : ''}. I'll be right here when you're back.`, 'english');
    return () => sarah.stop();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return (
    <>
      <StageHeader stage="vr" title={HEADER.vr.title} status={HEADER.vr.status} onBack={onBack} />
      <GlassCard accent={palette.aqua}>
        <Text style={styles.smallLabel}>ONE-TIME HEADSET CODE</Text>
        <Text style={styles.accessCode}>{pairingCode ?? '••••••'}</Text>
        <Text style={styles.body}>
          {relay.connectionState === 'connected'
            ? 'Open MindSync on your Quest and enter this code. It expires in five minutes.'
            : 'Connecting your phone to the session relay…'}
        </Text>
        {relay.lastError ? <Text style={styles.errorText}>Relay unavailable. Return and tap Start my session again.</Text> : null}
      </GlassCard>
      <View style={{alignItems: 'center', paddingVertical: space.xl}}><Headset /></View>
      <Text style={styles.display}>Time to drop in.</Text>
      <Text style={styles.body}>Put on your headset and let the session take over. I’ll be waiting when you’re back — we’ll see what changed.</Text>
      <GlassCard>
        <InfoRow icon={Clock} text="20–30 minutes" />
        <InfoRow icon={Sparkles} text="Sarah will be waiting" />
      </GlassCard>
      <PrimaryButton label="I’m back" onPress={() => { sarah.stop(); void onBack2(); }} />
    </>
  );
}

/* ---------- Report ---------- */
function ReportStage({full, pre, post, history, onHome, onBack}: {full: FullSessionResult | null; pre: StressResult | null; post: StressResult | null; history: SavedVoiceSession[]; onHome: () => void; onBack: () => void}) {
  return (
    <>
      <StageHeader stage="report" title={HEADER.report.title} status="Complete" done onBack={onBack} />
      {!full || !pre || !post ? (
        <>
          <View style={styles.avatarWrap}><SarahAvatar state="thinking" size="lg" /></View>
          <Text style={styles.h1Center}>Putting it together</Text>
          <AnalysingLines />
        </>
      ) : (
        <>
          <ReportView full={full} pre={pre} post={post} history={history} />
          <PrimaryButton label="Done" onPress={onHome} />
        </>
      )}
    </>
  );
}

/* ---------- small pieces ---------- */
function Pill({label, active, onPress}: {label: string; active: boolean; onPress: () => void}) {
  return <Text onPress={onPress} style={[styles.pill, active && styles.pillActive]}>{label}</Text>;
}
function TurnDots({total, done, active}: {total: number; done: number; active: boolean}) {
  return (
    <View style={styles.turnDots}>
      {Array.from({length: total}).map((_, i) => (
        <View key={i} style={{flexDirection: 'row', alignItems: 'center'}}>
          <View style={{width: i < done ? 20 : 8, height: 8, borderRadius: 4, backgroundColor: i < done ? palette.aqua : i === done && active ? palette.mild : 'rgba(255,255,255,0.18)'}} />
          {i < total - 1 ? <View style={{width: 10, height: 2, backgroundColor: 'rgba(255,255,255,0.12)'}} /> : null}
        </View>
      ))}
    </View>
  );
}
function VerdictBanner({ambient}: {ambient: AmbientResult}) {
  const v = ambient.verdict;
  const color = v === 'good' ? palette.calm : v === 'usable' ? palette.moderate : palette.high;
  const text = v === 'good' ? 'Your room sounds calm' : v === 'usable' ? 'A little background sound — that’s fine' : v === 'voices' ? 'I can hear someone nearby' : v === 'clipping' ? 'Something’s very close to the mic' : 'It’s too noisy in here right now';
  return (
    <View style={[styles.banner, {backgroundColor: `${color}22`, borderColor: `${color}66`}]}>
      <View style={{width: 8, height: 8, borderRadius: 4, backgroundColor: color}} />
      <Text style={[styles.bannerText, {color: palette.textHi}]}>{text}</Text>
    </View>
  );
}
function Headset() {
  return (
    <View style={{width: 220, height: 150, alignItems: 'center', justifyContent: 'center'}}>
      <Svg width={220} height={150}>
        <Defs>
          <RadialGradient id="hglow" cx="50%" cy="45%" r="55%"><Stop offset="0" stopColor={palette.aqua} stopOpacity={0.28} /><Stop offset="1" stopColor={palette.aqua} stopOpacity={0} /></RadialGradient>
        </Defs>
        <Rect x={0} y={0} width={220} height={150} fill="url(#hglow)" />
        {/* Quest-style headset, 3/4 */}
        <Path d="M40 62 Q40 44 62 42 L158 42 Q180 44 180 62 L180 92 Q180 112 156 112 L120 112 Q110 116 100 112 L64 112 Q40 112 40 92 Z" fill={palette.bg700} stroke={palette.aqua} strokeWidth={2.5} />
        <Circle cx={80} cy={76} r={17} fill={palette.bg900} stroke={`${palette.mild}99`} strokeWidth={1.5} />
        <Circle cx={140} cy={76} r={17} fill={palette.bg900} stroke={`${palette.mild}99`} strokeWidth={1.5} />
        <Path d="M40 60 Q22 62 26 84" stroke={palette.aqua} strokeWidth={3} fill="none" strokeLinecap="round" />
        <Path d="M180 60 Q198 62 194 84" stroke={palette.aqua} strokeWidth={3} fill="none" strokeLinecap="round" />
        <Rect x={70} y={128} width={80} height={7} rx={4} fill="rgba(0,0,0,0.35)" />
      </Svg>
    </View>
  );
}
const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);
function shortReading(r: StressResult): string {
  const lvl = stressLevel(r.stress_score).word.toLowerCase();
  return `From your voice, you sound ${lvl} right now. ${typeToPhrase(r.stress_type)}`;
}
function roomLoudnessLine(a: AmbientResult): string {
  const kind = a.noise_type === 'hum' ? ' — a low hum, like a fan' : a.noise_type === 'broadband' ? ' — a steady background wash' : a.noise_type === 'hiss' ? ' — a faint hiss' : '';
  switch (a.verdict) {
    case 'good': return 'Nice and quiet in here. Perfect for a clear reading.';
    case 'usable': return `A little background sound${kind}. That’s fine — I’ll adjust for it.`;
    case 'too_noisy': return `It’s quite loud in here${kind}. Somewhere quieter would give a cleaner reading.`;
    case 'voices': return 'I can hear other voices nearby — somewhere more private would help.';
    case 'clipping': return 'Something’s very close to the mic — give it a little space.';
    default: return 'Here’s how your room sounds right now.';
  }
}
function roomSuggestion(a: AmbientResult): string {
  switch (a.verdict) {
    case 'voices': return 'I can hear someone talking nearby — somewhere more private would help.';
    case 'clipping': return 'Something’s very close to the microphone — give it a little space.';
    case 'too_noisy': return a.noise_type === 'hum' ? 'There’s a strong hum — could you switch off the fan for a minute?' : 'Could you close the window, or move away from the road?';
    default: return 'Let’s try that once more.';
  }
}

const styles = StyleSheet.create({
  scroll: {paddingHorizontal: 20, paddingTop: space.md, paddingBottom: 60, gap: space.lg},
  avatarWrap: {alignItems: 'center', paddingVertical: space.md},
  display: {...T.display, color: palette.textHi},
  h1: {...T.h1, color: palette.textHi},
  h1Center: {...T.h1, color: palette.textHi, textAlign: 'center'},
  body: {...T.body, color: palette.textMid},
  bubble: {...T.h2, color: palette.textHi, fontWeight: '500'},
  sarahIntro: {...T.body, color: palette.textHi, textAlign: 'center', paddingHorizontal: space.md},
  smallLabel: {...T.caption, color: palette.textMid, fontWeight: '700'},
  accessCode: {...T.metricXL, color: palette.aqua, textAlign: 'center', fontWeight: '800', letterSpacing: 12},
  hintCenter: {...T.caption, color: palette.textLow, textAlign: 'center'},
  countOverlay: {position: 'absolute', top: 0, bottom: 0, left: 0, right: 0, alignItems: 'center', justifyContent: 'center'},
  count: {...T.metricXL, color: palette.aqua, textShadowColor: 'rgba(0,0,0,0.5)', textShadowRadius: 12},
  errorText: {...T.body, color: palette.high},
  devLink: {...T.caption, color: palette.textLow, textAlign: 'center', paddingVertical: 8, opacity: 0.6},
  rowStart: {flexDirection: 'row', alignItems: 'center', gap: 8},
  sarahName: {...T.caption, color: palette.aqua, fontWeight: '700'},
  pillRow: {flexDirection: 'row', gap: space.sm, flexWrap: 'wrap'},
  pill: {borderWidth: 1, borderColor: palette.hairline, borderRadius: 999, paddingHorizontal: 20, paddingVertical: 10, color: palette.textMid, fontWeight: '700', overflow: 'hidden'},
  pillActive: {borderColor: palette.aqua, backgroundColor: `${palette.aqua}22`, color: palette.aqua},
  tileGrid: {flexDirection: 'row', gap: space.md},
  turnDots: {flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 4},
  banner: {flexDirection: 'row', alignItems: 'center', gap: 10, borderRadius: 16, borderWidth: 1, padding: space.lg},
  bannerText: {...T.bodyMid, fontWeight: '700'},
});
