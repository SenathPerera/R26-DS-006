import type {OnboardingProfile} from '../../types/domain';
import {
  createSessionContext,
  effectiveEnvironmentPreference,
} from './preferenceProfile';

const profile: OnboardingProfile = {
  name: 'Ari', ageRange: '25-34', meditationExperience: 'Regular',
  preferredDuration: 20, goals: [], meditationStyle: 'Guided',
  audioPreferences: [], environmentPreferences: [], sensitivities: [],
  preferredIllumination: 0.25, preferredWarmth: 0.6,
  preferredAtmosphericSoftness: 0.1, preferredColorRichness: 0.7,
  preferredAmbientMotion: 0.35, particlePreference: 'subtle',
  lightSensitivity: 'mild', motionSensitivity: 0.4,
  consentAccepted: true, researchConsent: false,
};

describe('preference profile', () => {
  test('uses persistent preferences when the session selects usual values', () => {
    const context = createSessionContext({
      subjectiveStress: 6,
      moodValence: -0.2,
      fatigue: 0.7,
      sleepQuality: 0.3,
      headacheOrEyeStrainToday: false,
      preferenceMode: 'usual',
      temporaryPreference: null,
    }, [], new Date('2026-09-01T08:30:00'));

    expect(effectiveEnvironmentPreference(profile, context)).toEqual({
      illumination: 0.25,
      warmth: 0.6,
      atmosphericSoftness: 0.1,
      colorRichness: 0.7,
      ambientMotion: 0.35,
    });
    expect(context.sessionPreferredIllumination).toBeNull();
  });

  test('keeps temporary preferences separate and clamps normalized values', () => {
    const context = createSessionContext({
      subjectiveStress: 12,
      moodValence: -2,
      fatigue: 1.2,
      sleepQuality: -0.1,
      headacheOrEyeStrainToday: true,
      preferenceMode: 'adjust',
      temporaryPreference: {
        illumination: 0.8,
        warmth: 0.2,
        atmosphericSoftness: 1.2,
        colorRichness: -0.5,
        ambientMotion: 0.1,
      },
    }, [], new Date('2026-09-01T08:30:00'));

    expect(context).toMatchObject({
      subjectiveStress: 10,
      moodValence: -1,
      fatigue: 1,
      sleepQuality: 0,
      sessionPreferredAtmosphericSoftness: 1,
      sessionPreferredColorRichness: 0,
    });
    expect(effectiveEnvironmentPreference(profile, context).illumination).toBe(0.8);
    expect(profile.preferredIllumination).toBe(0.25);
  });

  test('derives automatic session context from account history', () => {
    const context = createSessionContext({
      subjectiveStress: 4, moodValence: 0, fatigue: 0.5, sleepQuality: 0.5,
      headacheOrEyeStrainToday: false, preferenceMode: 'usual', temporaryPreference: null,
    }, [{
      id: 'session-1', title: 'Temple Pond', date: '2026-08-29', durationMinutes: 20,
      environment: 'Temple Pond', audioProfile: 'Adaptive audio', completionRate: 100,
      moodBefore: 0, moodAfter: 0, validationComplete: true,
    }, {
      id: 'voice-1', title: 'Adaptive VR meditation', date: '2026-08-29', durationMinutes: 20,
      environment: 'temple-pond', audioProfile: 'Adaptive audio', completionRate: 100,
      moodBefore: 0, moodAfter: 0, validationComplete: true,
    }], new Date('2026-09-01T08:30:00'));

    expect(context.timeOfDayMinutes).toBe(510);
    expect(context.sessionSequenceNumber).toBe(2);
    expect(context.daysSincePreviousSession).toBe(3);
  });
});
