import React from 'react';
import {Text} from 'react-native';
import {Headset} from 'lucide-react-native';
import {Card, Header, PrimaryButton, Screen, StatusPill, uiStyles} from '../../components/ui';
import {useMindSyncStore} from '../../store/useMindSyncStore';
import {colors} from '../../theme/theme';

export function VrScreen({navigation}: any) {
  const status = useMindSyncStore(state => state.vrStatus);
  const code = useMindSyncStore(state => state.pairingCode);
  return (
    <Screen>
      <Header title="VR connection" subtitle="Pair the Unity meditation environment with this controller." onBack={navigation.goBack} />
      <Card>
        <Headset color={colors.violet} size={32} />
        <Text style={uiStyles.value}>Setup guide</Text>
        <Text style={uiStyles.body}>Open MindSync in the headset, keep both devices on the same research network, then enter the pairing code.</Text>
        <PrimaryButton label="Begin pre-session check-in" onPress={() => navigation.navigate('VoiceCheckIn')} />
      </Card>
      <Card>
        <Text style={{fontSize: 38, color: colors.text, fontWeight: '900', textAlign: 'center', letterSpacing: 8}}>{code ?? '------'}</Text>
        <StatusPill label={status === 'ready' ? 'Ready for handoff' : 'Waiting for pairing'} tone={status === 'ready' ? 'good' : 'warning'} />
        <Text style={uiStyles.label}>Transport boundary: native Unity module or backend-mediated session bridge.</Text>
      </Card>
      <PrimaryButton label="Return to your session" disabled={!code} onPress={() => navigation.navigate('VoiceCheckIn')} />
    </Screen>
  );
}
