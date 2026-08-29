import React from 'react';
import {Pressable, StyleSheet, Text, View} from 'react-native';
import {Activity, BarChart3, Bluetooth, ClipboardCheck, Headset, Mic2, Settings, Sparkles} from 'lucide-react-native';
import {BreathingVisual, Card, PrimaryButton, ProgressRing, Screen, SectionHeader, StatusPill, uiStyles} from '../../components/ui';
import {useMindSyncStore} from '../../store/useMindSyncStore';
import {colors, radii, spacing, typography} from '../../theme/theme';

const actions = [
  {label: 'Wearable', icon: Bluetooth, route: 'Wearable'},
  {label: 'VR setup', icon: Headset, route: 'VR'},
  {label: 'Check-in', icon: Mic2, route: 'VoiceCheckIn'},
  {label: 'Validate', icon: ClipboardCheck, route: 'QuestionnaireForm'},
  {label: 'Trends', icon: BarChart3, route: 'MainTabs'},
  {label: 'Settings', icon: Settings, route: 'MainTabs'},
] as const;

export function HomeScreen({navigation}: any) {
  const user = useMindSyncStore(state => state.user);
  const wearableState = useMindSyncStore(state => state.wearableState);
  const telemetry = useMindSyncStore(state => state.ble.telemetry);
  const vrStatus = useMindSyncStore(state => state.vrStatus);
  const pending = useMindSyncStore(state => state.pendingValidationCount);
  const isStreaming = wearableState === 'connected' && telemetry !== null;
  return (
    <Screen>
      <View style={uiStyles.rowBetween}>
        <View><Text style={styles.eyebrow}>Good to see you</Text><Text style={styles.name}>{user?.name ?? 'Participant'}</Text></View>
        <StatusPill label={pending ? `${pending} validation pending` : 'Study flow complete'} tone={pending ? 'warning' : 'good'} />
      </View>
      <Card>
        <View style={uiStyles.rowBetween}>
          <View style={{flex: 1, gap: spacing.xs}}><Text style={styles.cardTitle}>Current inner state</Text><Text style={uiStyles.body}>{isStreaming ? `Live IR ${telemetry.ir}, RED ${telemetry.red}, and ambient noise ${telemetry.noiseAverage}.` : 'Connect your wearable for a live physiological snapshot.'}</Text></View>
          <ProgressRing value={isStreaming ? 100 : 0} label={isStreaming ? 'live' : 'waiting'} />
        </View>
        <View style={uiStyles.row}>
          <BreathingVisual size={118} />
          <View style={{flex: 1, gap: spacing.sm}}>
            <StatusPill label={isStreaming ? 'Wearable streaming' : 'Wearable needed'} tone={isStreaming ? 'good' : 'warning'} />
            <StatusPill label={vrStatus === 'ready' ? 'VR ready' : 'VR setup needed'} tone={vrStatus === 'ready' ? 'good' : 'warning'} />
            <StatusPill label="Audio adaptation ready" tone="good" />
          </View>
        </View>
        <PrimaryButton label="Begin session" icon={Sparkles} onPress={() => navigation.navigate('VoiceCheckIn')} />
      </Card>
      <Card>
        <View style={uiStyles.row}><Activity color={colors.teal} size={22} /><Text style={styles.cardTitle}>Recommended focus</Text></View>
        <Text style={uiStyles.body}>15-minute Ocean Dusk with warm pads, restrained motion, and post-session Component D validation.</Text>
      </Card>
      <SectionHeader title="Control hub" subtitle="Device setup, session control, validation, and research insights." />
      <View style={styles.grid}>
        {actions.map(({label, icon: Icon, route}, index) => (
          <Pressable key={label} style={styles.action} onPress={() => {
            if (label === 'Trends') navigation.navigate('MainTabs', {screen: 'Trends'});
            else if (label === 'Settings') navigation.navigate('MainTabs', {screen: 'Settings'});
            else if (label === 'Validate') navigation.navigate('QuestionnaireForm', {templateId: 'component-d-post-v1'});
            else navigation.navigate(route);
          }}>
            <Icon color={index % 2 ? colors.violet : colors.teal} size={24} />
            <Text style={styles.actionLabel}>{label}</Text>
          </Pressable>
        ))}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  eyebrow: {fontSize: typography.small, color: colors.muted},
  name: {fontSize: 31, color: colors.text, fontWeight: '900'},
  cardTitle: {fontSize: typography.section, color: colors.text, fontWeight: '800', flexShrink: 1},
  grid: {flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm},
  action: {width: '48%', minHeight: 86, borderRadius: radii.md, borderWidth: 1, borderColor: colors.border, backgroundColor: 'rgba(34,52,75,0.72)', padding: spacing.md, justifyContent: 'space-between'},
  actionLabel: {color: colors.text, fontSize: 15, fontWeight: '800'},
});
