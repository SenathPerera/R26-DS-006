import React, {useState} from 'react';
import {Switch, Text, View} from 'react-native';
import {Bluetooth, Database, Headset, HelpCircle, LogOut, RotateCcw, Shield, UserRound} from 'lucide-react-native';
import {Card, Header, SecondaryButton, Screen, SectionHeader, uiStyles} from '../../components/ui';
import {useMindSyncStore} from '../../store/useMindSyncStore';
import {colors} from '../../theme/theme';
import {AudioPreferenceFields} from '../../components/AudioPreferenceFields';
import {audioPreferencesFromOnboarding} from '../../services/preferences/audioPreferences';
import type {AudioControlPreferences} from '../../types/domain';

export function SettingsScreen({navigation}: any) {
  const [reminders, setReminders] = useState(true);
  const [recoveryPrompts, setRecoveryPrompts] = useState(true);
  const user = useMindSyncStore(state => state.user);
  const logout = useMindSyncStore(state => state.logout);
  const reset = useMindSyncStore(state => state.resetDemo);
  const supabaseConfigured = useMindSyncStore(state => state.supabaseConfigured);
  const dataSyncStatus = useMindSyncStore(state => state.dataSyncStatus);
  const dataSyncError = useMindSyncStore(state => state.dataSyncError);
  const lastSyncedAt = useMindSyncStore(state => state.lastSyncedAt);
  const syncNow = useMindSyncStore(state => state.syncNow);
  const onboarding = useMindSyncStore(state => state.onboarding);
  const saveUsualAudioPreferences = useMindSyncStore(state => state.saveUsualAudioPreferences);
  const [editingSound, setEditingSound] = useState(false);
  const [usualSound, setUsualSound] = useState<AudioControlPreferences>(() => audioPreferencesFromOnboarding(onboarding));
  const [soundSaving, setSoundSaving] = useState(false);
  const [soundMessage, setSoundMessage] = useState<string | null>(null);
  const row = (label: string, detail: string, Icon: any) => <View style={uiStyles.row}><Icon color={colors.teal} size={22} /><View style={{flex: 1}}><Text style={uiStyles.value}>{label}</Text><Text style={uiStyles.label}>{detail}</Text></View></View>;
  return (
    <Screen>
      <Header title="Settings" subtitle="Account, devices, privacy, and study support." />
      <Card>{row(user?.name ?? 'Participant', user?.email ?? 'Not signed in', UserRound)}</Card>
      <SectionHeader title="Usual sound preferences" />
      <Card>
        <Text style={uiStyles.body}>Your saved sound choices shape each new meditation clip and your personal audio controller. The selected VR scene provides the musical setting.</Text>
        <SecondaryButton label={editingSound ? 'Cancel editing' : 'Edit usual sound'} onPress={() => {
          setUsualSound(audioPreferencesFromOnboarding(onboarding));
          setSoundMessage(null);
          setEditingSound(!editingSound);
        }} />
      </Card>
      {editingSound ? <AudioPreferenceFields value={usualSound} onChange={setUsualSound} /> : null}
      {editingSound ? <SecondaryButton label={soundSaving ? 'Saving...' : 'Save usual sound'} disabled={soundSaving} onPress={() => {
        setSoundSaving(true);
        saveUsualAudioPreferences(usualSound)
          .then(() => { setEditingSound(false); setSoundMessage('Usual sound preferences saved.'); })
          .catch(error => setSoundMessage(error instanceof Error ? error.message : 'Could not save sound preferences'))
          .finally(() => setSoundSaving(false));
      }} /> : null}
      {soundMessage ? <Text style={uiStyles.label}>{soundMessage}</Text> : null}
      <SectionHeader title="Devices" />
      <Card><SecondaryButton label="Manage wearable" icon={Bluetooth} onPress={() => navigation.navigate('Wearable')} /><SecondaryButton label="Manage VR" icon={Headset} onPress={() => navigation.navigate('VR')} /></Card>
      <SectionHeader title="Notifications" />
      <Card><ToggleRow label="Session reminders" value={reminders} onChange={setReminders} /><ToggleRow label="Grounding and recovery prompts" value={recoveryPrompts} onChange={setRecoveryPrompts} /></Card>
      <SectionHeader title="Privacy and research" />
      <Card>{row('Privacy controls', 'Consent, local storage, and physiological data handling', Shield)}{row('Export research data', 'Structured response export placeholder', Database)}{row('Support', 'Troubleshooting and study contact', HelpCircle)}</Card>
      <SectionHeader title="Database" />
      <Card>
        {row('Supabase', supabaseConfigured ? `Sync ${dataSyncStatus}` : 'Demo mode; no cloud database configured', Database)}
        {lastSyncedAt ? <Text style={uiStyles.label}>Last synchronized: {new Date(lastSyncedAt).toLocaleString()}</Text> : null}
        {dataSyncError ? <Text style={[uiStyles.label, {color: colors.rose}]}>{dataSyncError}</Text> : null}
        {supabaseConfigured ? <SecondaryButton label={dataSyncStatus === 'syncing' ? 'Synchronizing...' : 'Synchronize now'} disabled={dataSyncStatus === 'syncing'} onPress={() => { syncNow().catch(() => undefined); }} /> : null}
      </Card>
      {!supabaseConfigured ? <SecondaryButton label="Reset demo state" icon={RotateCcw} onPress={reset} /> : null}
      <SecondaryButton label="Log out" danger icon={LogOut} onPress={() => { logout().catch(() => undefined); }} />
      <Text style={[uiStyles.label, {textAlign: 'center'}]}>MindSync VR 1.0.0 · Research participant build</Text>
    </Screen>
  );
}

function ToggleRow({label, value, onChange}: {label: string; value: boolean; onChange: (value: boolean) => void}) {
  return <View style={uiStyles.rowBetween}><Text style={[uiStyles.body, {color: colors.text, flex: 1}]}>{label}</Text><Switch value={value} onValueChange={onChange} trackColor={{false: colors.borderSoft, true: colors.teal}} thumbColor={colors.white} /></View>;
}
