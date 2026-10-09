import React, {useEffect, useState} from 'react';
import {Alert, Text, View} from 'react-native';
import {CirclePause, CirclePlay, ShieldAlert, Square} from 'lucide-react-native';
import {BreathingVisual, Card, Header, Metric, PrimaryButton, Screen, SecondaryButton, StatusPill, uiStyles} from '../../components/ui';
import {useMindSyncStore} from '../../store/useMindSyncStore';
import {colors} from '../../theme/theme';

export function PreSessionScreen({navigation}: any) {
  const wearable = useMindSyncStore(state => state.wearableState);
  const vr = useMindSyncStore(state => state.vrStatus);
  const active = useMindSyncStore(state => state.activeSession);
  const create = useMindSyncStore(state => state.createSession);
  useEffect(() => { if (!active) create(); }, [active, create]);
  return (
    <Screen>
      <Header title="Ready to begin" subtitle="Take a moment to confirm your setup. You can leave at any point." onBack={navigation.goBack} />
      <Card><Text style={uiStyles.value}>{active?.title ?? 'Adaptive meditation'}</Text><Text style={uiStyles.body}>{active?.durationMinutes ?? 20} minutes · {active?.environment ?? 'Temple Pond'} · {active?.audioProfile ?? 'Adaptive audio'}</Text></Card>
      <Card>
        <Text style={uiStyles.value}>System readiness</Text>
        <StatusPill label={wearable === 'connected' ? 'Wearable connected' : 'Wearable optional but not connected'} tone={wearable === 'connected' ? 'good' : 'warning'} />
        <StatusPill label={vr === 'ready' ? 'VR ready' : 'VR not paired'} tone={vr === 'ready' ? 'good' : 'warning'} />
        <StatusPill label="Grounding controls available" tone="good" />
      </Card>
      <PrimaryButton label="Start meditation" icon={CirclePlay} onPress={() => navigation.replace('LiveSession')} />
      <SecondaryButton label="Return home" onPress={() => navigation.navigate('MainTabs')} />
    </Screen>
  );
}

export function LiveSessionScreen({navigation}: any) {
  const [elapsed, setElapsed] = useState(0);
  const status = useMindSyncStore(state => state.sessionStatus);
  const setStatus = useMindSyncStore(state => state.setSessionStatus);
  const sendVrCommand = useMindSyncStore(state => state.sendVrCommand);
  const wearable = useMindSyncStore(state => state.wearableState);
  const telemetry = useMindSyncStore(state => state.ble.telemetry);
  const vr = useMindSyncStore(state => state.vrStatus);
  useEffect(() => { setStatus('active'); }, [setStatus]);
  useEffect(() => {
    if (status !== 'active') return;
    const timer = setInterval(() => setElapsed(value => value + 1), 1000);
    return () => clearInterval(timer);
  }, [status]);
  const stop = () => Alert.alert('End this session?', 'The VR environment will stop gently and your post-session check-in will remain available.', [
    {text: 'Continue session', style: 'cancel'},
    {text: 'End session', style: 'destructive', onPress: () => { setStatus('complete'); sendVrCommand('stop'); navigation.replace('SessionComplete'); }},
  ]);
  const pause = () => { const next = status === 'paused' ? 'active' : 'paused'; setStatus(next); sendVrCommand(next === 'paused' ? 'pause' : 'resume'); };
  const emergency = () => { setStatus('complete'); sendVrCommand('emergency_stop'); navigation.replace('SessionComplete'); };
  const minutes = String(Math.floor(elapsed / 60)).padStart(2, '0');
  const seconds = String(elapsed % 60).padStart(2, '0');
  return (
    <Screen scroll={false} style={{justifyContent: 'space-between', paddingBottom: 38}}>
      <View style={{gap: 10}}><Text style={[uiStyles.label, {textAlign: 'center'}]}>ADAPTIVE SESSION</Text><Text style={{fontSize: 42, color: colors.text, fontWeight: '300', textAlign: 'center'}}>{minutes}:{seconds}</Text></View>
      <View style={{alignItems: 'center', gap: 16}}><BreathingVisual size={220} /><StatusPill label={status === 'paused' ? 'Paused gently' : 'Environment adapting'} tone={status === 'paused' ? 'warning' : 'good'} /></View>
      <Card>
        <View style={uiStyles.row}><Metric label="stress band" value={telemetry ? 'Live' : 'Awaiting'} accent={telemetry ? colors.teal : colors.muted} /><Metric label="confidence" value={telemetry ? 'Sensor live' : '--'} /><Metric label="audio" value="Adaptive" accent={colors.violet} /></View>
        <View style={uiStyles.rowBetween}><StatusPill label={wearable === 'connected' ? 'Wearable' : 'No wearable'} tone={wearable === 'connected' ? 'good' : 'warning'} /><StatusPill label={vr === 'ready' ? 'VR linked' : 'VR waiting'} tone={vr === 'ready' ? 'good' : 'warning'} /></View>
      </Card>
      <View style={uiStyles.row}>
        <View style={{flex: 1}}><SecondaryButton label={status === 'paused' ? 'Resume' : 'Pause'} icon={status === 'paused' ? CirclePlay : CirclePause} onPress={pause} /></View>
        <View style={{flex: 1}}><SecondaryButton label="Stop" danger icon={Square} onPress={stop} /></View>
      </View>
      <SecondaryButton label="Ground and exit" danger icon={ShieldAlert} onPress={emergency} />
    </Screen>
  );
}

export function SessionCompleteScreen({navigation}: any) {
  const active = useMindSyncStore(state => state.activeSession);
  const visualLogStatus = useMindSyncStore(state => state.relay.visualLogDeliveryStatus);
  const visualLogMessageCount = useMindSyncStore(state => state.relay.visualLogMessageCount);
  const refreshVisualLog = useMindSyncStore(state => state.refreshVisualLog);
  const logStatusLabel = visualLogStatus === 'acknowledged'
    ? `Session data secured · ${visualLogMessageCount} messages`
    : visualLogStatus === 'error'
      ? 'Session data transfer needs retry'
      : 'Securing session data…';
  return (
    <Screen style={{justifyContent: 'center'}}>
      <View style={{alignItems: 'center'}}><BreathingVisual size={150} /></View>
      <Card><Text style={[uiStyles.value, {textAlign: 'center'}]}>Session complete</Text><Text style={[uiStyles.body, {textAlign: 'center'}]}>Take your time before moving. Your reflection helps validate this session without judging how you felt.</Text><Text style={[uiStyles.label, {textAlign: 'center'}]}>{active?.title ?? 'Adaptive meditation'}</Text><StatusPill label={logStatusLabel} tone={visualLogStatus === 'error' ? 'warning' : 'good'} /></Card>
      {visualLogStatus === 'error' ? <SecondaryButton label="Retry session data transfer" onPress={() => { refreshVisualLog().catch(() => undefined); }} /> : null}
      <PrimaryButton label="Complete post-session validation" onPress={() => navigation.replace('QuestionnaireForm', {templateId: 'component-d-post-v1'})} />
      <SecondaryButton label="Return home" onPress={() => navigation.navigate('MainTabs')} />
    </Screen>
  );
}
