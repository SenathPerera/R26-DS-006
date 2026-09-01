import {
  answersToJson,
  onboardingFromRow,
  profileFromRow,
  sessionFromRow,
  submissionFromRow,
} from './databaseMappers';

describe('Supabase database mappers', () => {
  it('maps profile and onboarding rows into app domain objects', () => {
    const profile = profileFromRow({
      id: 'user-1', email: 'ari@example.com', display_name: 'Ari', role: 'participant',
      onboarding_complete: true, preferred_language: 'English',
      created_at: '2026-09-01T00:00:00Z', updated_at: '2026-09-01T00:00:00Z',
    });
    const onboarding = onboardingFromRow({
      user_id: 'user-1', age_range: '25-34', meditation_experience: 'Regular',
      preferred_duration: 20, goals: ['Relaxation'], meditation_style: 'Guided',
      audio_preferences: ['Warm pads'], environment_preferences: ['Ocean'],
      sensitivities: ['Avoid sudden transitions'], consent_accepted: true,
      preferred_illumination: 0.35, preferred_warmth: 0.65,
      preferred_atmospheric_softness: 0.2, preferred_color_richness: 0.75,
      preferred_ambient_motion: 0.4, particle_preference: 'subtle',
      light_sensitivity: 'mild', motion_sensitivity: 0.6,
      research_consent: false, privacy_notice_version: 'mindsync-privacy-v1',
      consented_at: '2026-09-01T00:00:00Z', updated_at: '2026-09-01T00:00:00Z',
    }, profile.name);

    expect(profile).toMatchObject({id: 'user-1', name: 'Ari', onboardingComplete: true});
    expect(onboarding).toMatchObject({
      name: 'Ari', preferredDuration: 20, researchConsent: false,
      preferredIllumination: 0.35, particlePreference: 'subtle',
      lightSensitivity: 'mild', motionSensitivity: 0.6,
    });
  });

  it('maps sessions without changing research values', () => {
    const session = sessionFromRow({
      id: 'session-1', user_id: 'user-1', title: 'Temple Pond', session_date: '2026-09-01',
      duration_minutes: 15, environment: 'Temple Pond', audio_profile: 'Adaptive audio',
      completion_rate: 92, mood_before: 4, mood_after: 7, validation_complete: true,
      session_context: {
        schemaVersion: 'mindsync-session-context-v1',
        collectedAt: '2026-09-01T07:30:00Z', subjectiveStress: 6,
        moodValence: -0.2, fatigue: 0.7, sleepQuality: 0.4,
        headacheOrEyeStrainToday: false, preferenceMode: 'usual',
        sessionPreferredIllumination: null, sessionPreferredWarmth: null,
        sessionPreferredAtmosphericSoftness: null, sessionPreferredColorRichness: null,
        sessionPreferredAmbientMotion: null, timeOfDayMinutes: 780,
        sessionSequenceNumber: 2, daysSincePreviousSession: 3,
      },
      effective_environment_preference: {
        illumination: 0.35, warmth: 0.65, atmosphericSoftness: 0.2,
        colorRichness: 0.75, ambientMotion: 0.4,
      },
      status: 'complete', created_at: '2026-09-01T00:00:00Z', updated_at: '2026-09-01T00:00:00Z',
    });

    expect(session).toEqual({
      id: 'session-1', title: 'Temple Pond', date: '2026-09-01', durationMinutes: 15,
      environment: 'Temple Pond', audioProfile: 'Adaptive audio', completionRate: 92,
      moodBefore: 4, moodAfter: 7, validationComplete: true,
      sessionContext: expect.objectContaining({subjectiveStress: 6, sessionSequenceNumber: 2}),
      effectiveEnvironmentPreference: {
        illumination: 0.35, warmth: 0.65, atmosphericSoftness: 0.2,
        colorRichness: 0.75, ambientMotion: 0.4,
      },
    });
  });

  it('accepts only supported questionnaire answer values from JSON', () => {
    const submission = submissionFromRow({
      id: 'response-1', user_id: 'user-1', template_id: 'post-v1', session_id: 'session-1',
      submitted_at: '2026-09-01T00:00:00Z', export_shape_version: 'component-d-v1',
      answers: {rating: 7, mood: 'Steady', goals: ['Relaxation'], ignored: {nested: true}},
      created_at: '2026-09-01T00:00:00Z', updated_at: '2026-09-01T00:00:00Z',
    });

    expect(submission.answers).toEqual({rating: 7, mood: 'Steady', goals: ['Relaxation']});
    expect(answersToJson(submission.answers)).toEqual(submission.answers);
  });
});
