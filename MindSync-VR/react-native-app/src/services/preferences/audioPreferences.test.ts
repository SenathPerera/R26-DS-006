import type {OnboardingProfile, SessionContext} from '../../types/domain';
import {
  audioPreferencesFromOnboarding,
  buildAudioPreferenceBundle,
  defaultAudioPreferences,
  effectiveAudioPreference,
  isAudioControlPreferences,
  requireParticipantPseudonym,
} from './audioPreferences';

const onboarding: OnboardingProfile = {
  name: 'Participant', ageRange: '25-34', meditationExperience: 'Regular',
  preferredDuration: 20, goals: [], meditationStyle: 'Guided',
  audioPreferences: ['Warm pads', 'Nature heavy'], environmentPreferences: [], sensitivities: [],
  preferredIllumination: 0.5, preferredWarmth: 0.5,
  preferredAtmosphericSoftness: 0.5, preferredColorRichness: 0.5,
  preferredAmbientMotion: 0.5,
  consentAccepted: true, researchConsent: true,
};

describe('mobile audio preference contract', () => {
  test('maps old sound tags and keeps saved structured choices', () => {
    expect(audioPreferencesFromOnboarding(onboarding)).toMatchObject({
      preferredInstruments: ['pad', 'piano'], ambientMusicBalance: 'mostly_ambience',
    });
    const structured = {...defaultAudioPreferences(), mood: 'focused' as const};
    expect(audioPreferencesFromOnboarding({...onboarding, audioControlPreferences: structured}).mood).toBe('focused');
  });

  test('session adjustment does not change long-term sound', () => {
    const usual = defaultAudioPreferences();
    const profile = {...onboarding, audioControlPreferences: usual};
    const temporary = {...usual, preferredInstruments: ['bells'] as Array<'bells'>, mood: 'sleepy' as const};
    const context = {audioPreferenceMode: 'adjust', sessionAudioPreference: temporary} as SessionContext;
    const bundle = buildAudioPreferenceBundle(profile, context);
    expect(bundle.longTerm.preferredInstruments).toEqual(['piano', 'flute']);
    expect(bundle.sessionOverride?.preferredInstruments).toEqual(['bells']);
    expect(effectiveAudioPreference(profile, context).mood).toBe('sleepy');
    expect(profile.audioControlPreferences?.mood).toBe('calm');
    expect(() => buildAudioPreferenceBundle(profile, {
      audioPreferenceMode: 'adjust', sessionAudioPreference: null,
    } as SessionContext)).toThrow('session-audio-preferences-required');
  });

  test('rejects unknown audio values and demo identities', () => {
    expect(isAudioControlPreferences({...defaultAudioPreferences(), audioIntensity: 2})).toBe(false);
    expect(isAudioControlPreferences({...defaultAudioPreferences(), preferredInstruments: ['piano', 'piano']})).toBe(false);
    expect(() => requireParticipantPseudonym('U001')).toThrow('participant-identity-required');
    expect(() => requireParticipantPseudonym('participant-demo')).toThrow('participant-identity-required');
    expect(requireParticipantPseudonym('  participant-7  ')).toBe('participant-7');
  });
});
