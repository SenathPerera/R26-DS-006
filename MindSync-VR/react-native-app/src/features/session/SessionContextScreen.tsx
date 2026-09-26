import React, {useMemo, useState} from 'react';
import {Text, View} from 'react-native';
import {
  Card,
  ChoiceChip,
  Header,
  PreferenceSlider,
  PrimaryButton,
  Screen,
  uiStyles,
} from '../../components/ui';
import {
  createSessionContext,
  preferredEnvironmentFromOnboarding,
} from '../../services/preferences/preferenceProfile';
import {useMindSyncStore} from '../../store/useMindSyncStore';
import type {
  PreferredEnvironment,
  SessionPreferenceMode,
} from '../../types/domain';

export function SessionContextScreen({navigation}: any) {
  const onboarding = useMindSyncStore(state => state.onboarding);
  const sessions = useMindSyncStore(state => state.sessions);
  const saveContext = useMindSyncStore(state => state.setSessionContext);
  const createSession = useMindSyncStore(state => state.createSession);
  const usualPreference = useMemo(
    () => preferredEnvironmentFromOnboarding(onboarding),
    [onboarding],
  );
  const [subjectiveStress, setSubjectiveStress] = useState(5);
  const [moodValence, setMoodValence] = useState(0);
  const [fatigue, setFatigue] = useState(0.5);
  const [sleepQuality, setSleepQuality] = useState(0.5);
  const [headacheOrEyeStrainToday, setHeadacheOrEyeStrainToday] = useState<boolean | null>(null);
  const [preferenceMode, setPreferenceMode] = useState<SessionPreferenceMode | null>(null);
  const [temporaryPreference, setTemporaryPreference] = useState<PreferredEnvironment>(usualPreference);

  const updateTemporary = (patch: Partial<PreferredEnvironment>) => {
    setTemporaryPreference(current => ({...current, ...patch}));
  };
  const continueToCheckIn = () => {
    if (headacheOrEyeStrainToday === null || preferenceMode === null) return;
    const context = createSessionContext({
      subjectiveStress,
      moodValence,
      fatigue,
      sleepQuality,
      headacheOrEyeStrainToday,
      preferenceMode,
      temporaryPreference: preferenceMode === 'adjust' ? temporaryPreference : null,
    }, sessions);
    saveContext(context);
    createSession();
    navigation.navigate('VoiceCheckIn');
  };

  return (
    <Screen>
      <Header
        title="Before today's session"
        subtitle="These answers apply only to this session and will not change your usual garden preferences."
        onBack={navigation.goBack}
      />
      <Card>
        <Text style={uiStyles.value}>How are you feeling right now?</Text>
        <PreferenceSlider
          label="Subjective stress"
          value={subjectiveStress}
          minimum={0}
          maximum={10}
          step={1}
          leftLabel="Not stressed"
          rightLabel="Extremely stressed"
          displayValue={value => String(Math.round(value))}
          onChange={setSubjectiveStress}
        />
        <PreferenceSlider
          label="Mood"
          value={moodValence}
          minimum={-1}
          maximum={1}
          step={0.1}
          leftLabel="Very Negative"
          rightLabel="Very Positive"
          displayValue={value => value === 0 ? 'Neutral' : value.toFixed(1)}
          onChange={setMoodValence}
        />
        <PreferenceSlider label="Fatigue" value={fatigue} leftLabel="Not Tired" rightLabel="Very Tired" onChange={setFatigue} />
        <PreferenceSlider label="Most recent sleep quality" value={sleepQuality} leftLabel="Very Poor" rightLabel="Very Good" onChange={setSleepQuality} />
        <Text style={uiStyles.label}>Headache or eye strain today?</Text>
        <View style={uiStyles.wrap}>
          <ChoiceChip label="No" selected={headacheOrEyeStrainToday === false} onPress={() => setHeadacheOrEyeStrainToday(false)} />
          <ChoiceChip label="Yes" selected={headacheOrEyeStrainToday === true} onPress={() => setHeadacheOrEyeStrainToday(true)} />
        </View>
      </Card>
      <Card>
        <Text style={uiStyles.value}>Would you like to use your usual garden preferences today?</Text>
        <View style={uiStyles.wrap}>
          <ChoiceChip label="Use usual preferences" selected={preferenceMode === 'usual'} onPress={() => setPreferenceMode('usual')} />
          <ChoiceChip label="Adjust for today" selected={preferenceMode === 'adjust'} onPress={() => setPreferenceMode('adjust')} />
        </View>
        {preferenceMode === 'adjust' ? (
          <>
            <Text style={uiStyles.body}>These adjustments are temporary and apply only to the next VR session.</Text>
            <PreferenceSlider label="Illumination" value={temporaryPreference.illumination} leftLabel="Dimmer" rightLabel="Brighter" onChange={illumination => updateTemporary({illumination})} />
            <PreferenceSlider label="Warmth" value={temporaryPreference.warmth} leftLabel="Cooler" rightLabel="Warmer" onChange={warmth => updateTemporary({warmth})} />
            <PreferenceSlider label="Atmospheric softness" value={temporaryPreference.atmosphericSoftness} leftLabel="Clearer" rightLabel="Mistier" onChange={atmosphericSoftness => updateTemporary({atmosphericSoftness})} />
            <PreferenceSlider label="Color richness" value={temporaryPreference.colorRichness} leftLabel="More Muted" rightLabel="More Vivid" onChange={colorRichness => updateTemporary({colorRichness})} />
            <PreferenceSlider label="Ambient motion" value={temporaryPreference.ambientMotion} leftLabel="More Still" rightLabel="More Active" onChange={ambientMotion => updateTemporary({ambientMotion})} />
          </>
        ) : null}
      </Card>
      <PrimaryButton
        label="Continue to voice check-in"
        disabled={headacheOrEyeStrainToday === null || preferenceMode === null}
        onPress={continueToCheckIn}
      />
    </Screen>
  );
}
