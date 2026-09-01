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
      research_consent: false, privacy_notice_version: 'mindsync-privacy-v1',
      consented_at: '2026-09-01T00:00:00Z', updated_at: '2026-09-01T00:00:00Z',
    }, profile.name);

    expect(profile).toMatchObject({id: 'user-1', name: 'Ari', onboardingComplete: true});
    expect(onboarding).toMatchObject({name: 'Ari', preferredDuration: 20, researchConsent: false});
  });

  it('maps sessions without changing research values', () => {
    const session = sessionFromRow({
      id: 'session-1', user_id: 'user-1', title: 'Temple Pond', session_date: '2026-09-01',
      duration_minutes: 15, environment: 'Temple Pond', audio_profile: 'Adaptive audio',
      completion_rate: 92, mood_before: 4, mood_after: 7, validation_complete: true,
      status: 'complete', created_at: '2026-09-01T00:00:00Z', updated_at: '2026-09-01T00:00:00Z',
    });

    expect(session).toEqual({
      id: 'session-1', title: 'Temple Pond', date: '2026-09-01', durationMinutes: 15,
      environment: 'Temple Pond', audioProfile: 'Adaptive audio', completionRate: 92,
      moodBefore: 4, moodAfter: 7, validationComplete: true,
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
