import React, {useState} from 'react';
import {Text, View} from 'react-native';
import {Card, ChoiceChip, Field, Header, PrimaryButton, Screen, SecondaryButton, uiStyles} from '../../components/ui';
import {useMindSyncStore} from '../../store/useMindSyncStore';
import {colors} from '../../theme/theme';

const steps = ['Profile', 'Practice', 'Goals', 'Sound', 'Environment', 'Comfort', 'Consent'];
const goals = ['Stress reduction', 'Better focus', 'Relaxation', 'Sleep preparation', 'Emotional balance'];
const audio = ['Nature heavy', 'Soft drone', 'Warm pads', 'Subtle rhythm', 'No vocals', 'Neutral tone'];
const environments = ['Forest', 'Ocean', 'Cave', 'Mountain', 'Abstract calm'];
const sensitivities = ['Avoid intense sounds', 'Avoid sudden transitions', 'Avoid darkness', 'Motion sensitivity'];

export function OnboardingScreen({navigation}: any) {
  const [step, setStep] = useState(0);
  const profile = useMindSyncStore(state => state.onboarding);
  const update = useMindSyncStore(state => state.updateOnboarding);
  const complete = useMindSyncStore(state => state.completeOnboarding);
  const toggle = (key: 'goals' | 'audioPreferences' | 'environmentPreferences' | 'sensitivities', value: string) => {
    const current = profile[key];
    update({[key]: current.includes(value) ? current.filter(item => item !== value) : [...current, value]});
  };
  const finish = () => { complete(); navigation.reset({index: 0, routes: [{name: 'MainTabs'}]}); };
  return (
    <Screen>
      <Header title="Personalize your space" subtitle={`Step ${step + 1} of ${steps.length} · ${steps[step]}`} />
      <View style={{height: 5, backgroundColor: colors.borderSoft, borderRadius: 4}}><View style={{height: 5, width: `${((step + 1) / steps.length) * 100}%`, backgroundColor: colors.teal, borderRadius: 4}} /></View>
      {step === 0 ? <Card><Text style={uiStyles.value}>A little about you</Text><Field label="Preferred name" value={profile.name} onChangeText={name => update({name})} /><Text style={uiStyles.label}>Age range</Text><View style={uiStyles.wrap}>{['18-24', '25-34', '35-44', '45-54', '55+'].map(item => <ChoiceChip key={item} label={item} selected={profile.ageRange === item} onPress={() => update({ageRange: item})} />)}</View></Card> : null}
      {step === 1 ? <Card><Text style={uiStyles.value}>Meditation experience</Text><Text style={uiStyles.body}>Choose the closest fit. There is no preferred answer.</Text><View style={uiStyles.wrap}>{['New to meditation', 'Occasional', 'Regular', 'Experienced'].map(item => <ChoiceChip key={item} label={item} selected={profile.meditationExperience === item} onPress={() => update({meditationExperience: item})} />)}</View><Text style={uiStyles.label}>Preferred duration</Text><View style={uiStyles.wrap}>{[5, 10, 15, 20, 30].map(item => <ChoiceChip key={item} label={`${item} min`} selected={profile.preferredDuration === item} onPress={() => update({preferredDuration: item})} />)}</View></Card> : null}
      {step === 2 ? <Selection title="What would you like support with?" values={goals} selected={profile.goals} onToggle={value => toggle('goals', value)} /> : null}
      {step === 3 ? <Selection title="Your sound profile" values={audio} selected={profile.audioPreferences} onToggle={value => toggle('audioPreferences', value)} /> : null}
      {step === 4 ? <Selection title="Comforting environments" values={environments} selected={profile.environmentPreferences} onToggle={value => toggle('environmentPreferences', value)} /> : null}
      {step === 5 ? <Selection title="Comfort and sensitivity" subtitle="These preferences constrain adaptation. You can change them any time." values={sensitivities} selected={profile.sensitivities} onToggle={value => toggle('sensitivities', value)} /> : null}
      {step === 6 ? <Card><Text style={uiStyles.value}>Consent and privacy</Text><Text style={uiStyles.body}>Physiological readings are used to support the active session and research workflow. You stay in control of session exit and optional research participation.</Text><ChoiceChip label="I acknowledge the privacy notice" selected={profile.consentAccepted} onPress={() => update({consentAccepted: !profile.consentAccepted})} /><ChoiceChip label="I consent to research participation" selected={profile.researchConsent} onPress={() => update({researchConsent: !profile.researchConsent})} /></Card> : null}
      <View style={uiStyles.row}>
        {step > 0 ? <View style={{flex: 1}}><SecondaryButton label="Back" onPress={() => setStep(value => value - 1)} /></View> : null}
        <View style={{flex: 1}}><PrimaryButton label={step === steps.length - 1 ? 'Complete' : 'Continue'} disabled={step === steps.length - 1 && !profile.consentAccepted} onPress={step === steps.length - 1 ? finish : () => setStep(value => value + 1)} /></View>
      </View>
    </Screen>
  );
}

function Selection({title, subtitle, values, selected, onToggle}: {title: string; subtitle?: string; values: string[]; selected: string[]; onToggle: (value: string) => void}) {
  return <Card><Text style={uiStyles.value}>{title}</Text>{subtitle ? <Text style={uiStyles.body}>{subtitle}</Text> : null}<View style={uiStyles.wrap}>{values.map(value => <ChoiceChip key={value} label={value} selected={selected.includes(value)} onPress={() => onToggle(value)} />)}</View></Card>;
}
