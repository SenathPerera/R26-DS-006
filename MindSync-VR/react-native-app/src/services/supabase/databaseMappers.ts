import type {Database, Json} from './database.types';
import type {
  MeditationSession,
  OnboardingProfile,
  QuestionnaireSubmission,
  UserProfile,
} from '../../types/domain';
import {
  isPreferredEnvironment,
  isSessionContext,
  TEMPLE_POND_SAFE_DEFAULT,
} from '../preferences/preferenceProfile';

type ProfileRow = Database['public']['Tables']['profiles']['Row'];
type OnboardingRow = Database['public']['Tables']['onboarding_profiles']['Row'];
type SessionRow = Database['public']['Tables']['meditation_sessions']['Row'];
type SubmissionRow = Database['public']['Tables']['questionnaire_submissions']['Row'];

export function profileFromRow(row: ProfileRow): UserProfile {
  return {
    id: row.id,
    email: row.email,
    name: row.display_name,
    role: row.role,
    onboardingComplete: row.onboarding_complete,
    preferredLanguage: row.preferred_language,
  };
}

export function onboardingFromRow(row: OnboardingRow, name: string): OnboardingProfile {
  return {
    name,
    ageRange: row.age_range,
    meditationExperience: row.meditation_experience,
    preferredDuration: row.preferred_duration,
    goals: row.goals,
    meditationStyle: row.meditation_style,
    audioPreferences: row.audio_preferences,
    environmentPreferences: row.environment_preferences,
    sensitivities: row.sensitivities,
    preferredIllumination: row.preferred_illumination ?? TEMPLE_POND_SAFE_DEFAULT.illumination,
    preferredWarmth: row.preferred_warmth ?? TEMPLE_POND_SAFE_DEFAULT.warmth,
    preferredAtmosphericSoftness: row.preferred_atmospheric_softness ?? TEMPLE_POND_SAFE_DEFAULT.atmosphericSoftness,
    preferredColorRichness: row.preferred_color_richness ?? TEMPLE_POND_SAFE_DEFAULT.colorRichness,
    preferredAmbientMotion: row.preferred_ambient_motion ?? TEMPLE_POND_SAFE_DEFAULT.ambientMotion,
    particlePreference: row.particle_preference,
    lightSensitivity: row.light_sensitivity,
    motionSensitivity: row.motion_sensitivity ?? 0.5,
    consentAccepted: row.consent_accepted,
    researchConsent: row.research_consent,
  };
}

export function sessionFromRow(row: SessionRow): MeditationSession {
  return {
    id: row.id,
    title: row.title,
    date: row.session_date,
    durationMinutes: row.duration_minutes,
    environment: row.environment,
    audioProfile: row.audio_profile,
    completionRate: row.completion_rate,
    moodBefore: row.mood_before,
    moodAfter: row.mood_after,
    validationComplete: row.validation_complete,
    sessionContext: isSessionContext(row.session_context) ? row.session_context : null,
    effectiveEnvironmentPreference: isPreferredEnvironment(row.effective_environment_preference)
      ? row.effective_environment_preference
      : null,
  };
}

export function submissionFromRow(row: SubmissionRow): QuestionnaireSubmission {
  return {
    id: row.id,
    templateId: row.template_id,
    sessionId: row.session_id,
    userId: row.user_id,
    submittedAt: row.submitted_at,
    synced: true,
    exportShapeVersion: 'component-d-v1',
    answers: answersFromJson(row.answers),
  };
}

export function answersToJson(answers: QuestionnaireSubmission['answers']): Json {
  return JSON.parse(JSON.stringify(answers)) as Json;
}

function answersFromJson(value: Json): QuestionnaireSubmission['answers'] {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return {};
  const answers: QuestionnaireSubmission['answers'] = {};
  Object.entries(value).forEach(([key, item]) => {
    if (typeof item === 'string' || typeof item === 'number') answers[key] = item;
    else if (Array.isArray(item) && item.every(entry => typeof entry === 'string')) {
      answers[key] = item;
    }
  });
  return answers;
}
