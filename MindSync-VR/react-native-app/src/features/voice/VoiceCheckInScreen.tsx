import React, {useEffect} from 'react';
import {Text, View} from 'react-native';
import {Headphones, Mic2, ShieldCheck, Sparkles} from 'lucide-react-native';
import {BreathingVisual, Card, ChoiceChip, Field, Header, PrimaryButton, Screen, SecondaryButton, StatusPill, uiStyles} from '../../components/ui';
import {useMindSyncStore} from '../../store/useMindSyncStore';
import {colors} from '../../theme/theme';

const stages = ['intro', 'environment', 'pre', 'vr', 'post', 'report'] as const;

export function VoiceCheckInScreen({navigation}: any) {
  const voice = useMindSyncStore(state => state.voice);
  const start = useMindSyncStore(state => state.startVoiceCheckIn);
  const update = useMindSyncStore(state => state.updateVoice);
  useEffect(() => { void start(); }, [start]);
  const next = () => update({stage: stages[Math.min(stages.indexOf(voice.stage) + 1, stages.length - 1)]});
  return (
    <Screen>
      <Header title="Voice companion" subtitle="A supported pre-to-post reflection for Component D." onBack={navigation.goBack} />
      <View style={uiStyles.rowBetween}><Text style={uiStyles.label}>{voice.stage.toUpperCase()}</Text><StatusPill label={voice.backendHealthy === true ? 'Companion ready' : voice.backendHealthy === false ? 'Service offline' : 'Checking service'} tone={voice.backendHealthy === true ? 'good' : voice.backendHealthy === false ? 'danger' : 'warning'} /></View>
      {voice.stage === 'intro' ? <Card><Text style={uiStyles.value}>Before we begin</Text><Text style={uiStyles.body}>Choose how the companion should address you and the language you will use.</Text><Field label="Preferred name" value={voice.personName} onChangeText={personName => update({personName})} /><View style={uiStyles.wrap}><ChoiceChip label="English" selected={voice.language === 'english'} onPress={() => update({language: 'english'})} /><ChoiceChip label="සිංහල" selected={voice.language === 'sinhala'} onPress={() => update({language: 'sinhala'})} /></View><PrimaryButton label="Check the room" icon={Mic2} onPress={next} /></Card> : null}
      {voice.stage === 'environment' ? <CompanionStage icon={Headphones} title="Let's check the room" body="Sit comfortably and allow a few quiet seconds. The production recorder bridge sends this acoustic check to Component D before voice analysis begins." action="Continue to check-in" onPress={next} /> : null}
      {voice.stage === 'pre' ? <CompanionStage icon={Mic2} title="How are you arriving?" body="Speak naturally about how you feel. The companion uses the change between pre and post readings as the primary signal, never as a diagnosis." action="Continue to VR" onPress={next} /> : null}
      {voice.stage === 'vr' ? <CompanionStage icon={Sparkles} title="Your VR session" body="Your check-in is linked to this session. Continue when the headset experience is complete." action="Return for post-session check-in" onPress={next} /> : null}
      {voice.stage === 'post' ? <CompanionStage icon={Mic2} title="How are you now?" body="Take your time. Describe what shifted, what stayed the same, and any discomfort you noticed." action="View session report" onPress={next} /> : null}
      {voice.stage === 'report' ? <Card><View style={{alignItems: 'center'}}><BreathingVisual size={150} /></View><Text style={[uiStyles.value, {textAlign: 'center'}]}>Reflection complete</Text><Text style={[uiStyles.body, {textAlign: 'center'}]}>The live Component D service will provide the within-speaker comparison, confidence, cross-modal agreement, and anomaly context here.</Text><StatusPill label="No clinical diagnosis" tone="good" /><PrimaryButton label="Return home" icon={ShieldCheck} onPress={() => navigation.navigate('MainTabs')} /></Card> : null}
      {voice.error ? <Card><Text style={[uiStyles.value, {color: colors.rose}]}>Companion unavailable</Text><Text style={uiStyles.body}>{voice.error}</Text><Text style={uiStyles.label}>The rest of MindSync remains available. Configure the Component D URL for live voice processing.</Text></Card> : null}
      {voice.stage !== 'report' ? <SecondaryButton label="Exit check-in" danger onPress={navigation.goBack} /> : null}
    </Screen>
  );
}

function CompanionStage({icon: Icon, title, body, action, onPress}: {icon: any; title: string; body: string; action: string; onPress: () => void}) {
  return <Card><View style={{alignItems: 'center', gap: 12}}><BreathingVisual size={150} /><Icon color={colors.teal} size={28} /></View><Text style={[uiStyles.value, {textAlign: 'center'}]}>{title}</Text><Text style={[uiStyles.body, {textAlign: 'center'}]}>{body}</Text><PrimaryButton label={action} onPress={onPress} /></Card>;
}
