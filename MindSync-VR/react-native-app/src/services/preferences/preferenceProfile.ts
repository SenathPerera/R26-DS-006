import type {
  MeditationSession,
  OnboardingProfile,
  PreferredEnvironment,
  SessionContext,
  SessionPreferenceMode,
} from '../../types/domain';

export const SESSION_CONTEXT_SCHEMA_VERSION =
  'mindsync-session-context-v1' as const;

export const TEMPLE_POND_SAFE_DEFAULT: PreferredEnvironment = {
  illumination: 0.319,
  warmth: 0.5,
  atmosphericSoftness: 0,
  colorRichness: 0.5,
  ambientMotion: 0.75,
};

export type SessionContextDraft = {
  subjectiveStress: number;
  moodValence: number;
  fatigue: number;
  sleepQuality: number;
  headacheOrEyeStrainToday: boolean;
  preferenceMode: SessionPreferenceMode;
  temporaryPreference: PreferredEnvironment | null;
};

export function preferredEnvironmentFromOnboarding(
  profile: OnboardingProfile,
): PreferredEnvironment {
  return normalizePreferredEnvironment({
    illumination: profile.preferredIllumination,
    warmth: profile.preferredWarmth,
    atmosphericSoftness: profile.preferredAtmosphericSoftness,
    colorRichness: profile.preferredColorRichness,
    ambientMotion: profile.preferredAmbientMotion,
  });
}

export function effectiveEnvironmentPreference(
  profile: OnboardingProfile,
  context: SessionContext | null,
): PreferredEnvironment {
  if (context?.preferenceMode !== 'adjust') {
    return preferredEnvironmentFromOnboarding(profile);
  }
  return normalizePreferredEnvironment({
    illumination: context.sessionPreferredIllumination ?? profile.preferredIllumination,
    warmth: context.sessionPreferredWarmth ?? profile.preferredWarmth,
    atmosphericSoftness: context.sessionPreferredAtmosphericSoftness
      ?? profile.preferredAtmosphericSoftness,
    colorRichness: context.sessionPreferredColorRichness
      ?? profile.preferredColorRichness,
    ambientMotion: context.sessionPreferredAmbientMotion
      ?? profile.preferredAmbientMotion,
  });
}

export function createSessionContext(
  draft: SessionContextDraft,
  previousSessions: MeditationSession[],
  now = new Date(),
): SessionContext {
  const previousVrSessions = previousSessions.filter(
    session => !session.id.startsWith('voice-'),
  );
  const temporary = draft.preferenceMode === 'adjust'
    ? normalizePreferredEnvironment(
      draft.temporaryPreference ?? TEMPLE_POND_SAFE_DEFAULT,
    )
    : null;
  return {
    schemaVersion: SESSION_CONTEXT_SCHEMA_VERSION,
    collectedAt: now.toISOString(),
    subjectiveStress: clamp(draft.subjectiveStress, 0, 10),
    moodValence: clamp(draft.moodValence, -1, 1),
    fatigue: normalized(draft.fatigue),
    sleepQuality: normalized(draft.sleepQuality),
    headacheOrEyeStrainToday: draft.headacheOrEyeStrainToday,
    preferenceMode: draft.preferenceMode,
    sessionPreferredIllumination: temporary?.illumination ?? null,
    sessionPreferredWarmth: temporary?.warmth ?? null,
    sessionPreferredAtmosphericSoftness: temporary?.atmosphericSoftness ?? null,
    sessionPreferredColorRichness: temporary?.colorRichness ?? null,
    sessionPreferredAmbientMotion: temporary?.ambientMotion ?? null,
    timeOfDayMinutes: now.getHours() * 60 + now.getMinutes(),
    sessionSequenceNumber: previousVrSessions.length + 1,
    daysSincePreviousSession: daysSincePrevious(previousVrSessions, now),
  };
}

export function normalizePreferredEnvironment(
  preference: PreferredEnvironment,
): PreferredEnvironment {
  return {
    illumination: normalized(preference.illumination),
    warmth: normalized(preference.warmth),
    atmosphericSoftness: normalized(preference.atmosphericSoftness),
    colorRichness: normalized(preference.colorRichness),
    ambientMotion: normalized(preference.ambientMotion),
  };
}

export function isPreferredEnvironment(value: unknown): value is PreferredEnvironment {
  if (!isRecord(value)) return false;
  return isNormalized(value.illumination)
    && isNormalized(value.warmth)
    && isNormalized(value.atmosphericSoftness)
    && isNormalized(value.colorRichness)
    && isNormalized(value.ambientMotion);
}

export function isSessionContext(value: unknown): value is SessionContext {
  if (!isRecord(value)) return false;
  const nullableNormalized = (item: unknown) => item === null || isNormalized(item);
  const temporaryValues = [
    value.sessionPreferredIllumination,
    value.sessionPreferredWarmth,
    value.sessionPreferredAtmosphericSoftness,
    value.sessionPreferredColorRichness,
    value.sessionPreferredAmbientMotion,
  ];
  const preferenceValuesMatchMode = value.preferenceMode === 'usual'
    ? temporaryValues.every(item => item === null)
    : value.preferenceMode === 'adjust'
      && temporaryValues.every(isNormalized);
  return value.schemaVersion === SESSION_CONTEXT_SCHEMA_VERSION
    && typeof value.collectedAt === 'string'
    && typeof value.subjectiveStress === 'number'
    && value.subjectiveStress >= 0 && value.subjectiveStress <= 10
    && typeof value.moodValence === 'number'
    && value.moodValence >= -1 && value.moodValence <= 1
    && isNormalized(value.fatigue)
    && isNormalized(value.sleepQuality)
    && typeof value.headacheOrEyeStrainToday === 'boolean'
    && (value.preferenceMode === 'usual' || value.preferenceMode === 'adjust')
    && nullableNormalized(value.sessionPreferredIllumination)
    && nullableNormalized(value.sessionPreferredWarmth)
    && nullableNormalized(value.sessionPreferredAtmosphericSoftness)
    && nullableNormalized(value.sessionPreferredColorRichness)
    && nullableNormalized(value.sessionPreferredAmbientMotion)
    && preferenceValuesMatchMode
    && typeof value.timeOfDayMinutes === 'number'
    && Number.isInteger(value.timeOfDayMinutes)
    && value.timeOfDayMinutes >= 0 && value.timeOfDayMinutes <= 1439
    && typeof value.sessionSequenceNumber === 'number'
    && Number.isInteger(value.sessionSequenceNumber)
    && value.sessionSequenceNumber >= 1
    && (value.daysSincePreviousSession === null
      || (typeof value.daysSincePreviousSession === 'number'
        && Number.isInteger(value.daysSincePreviousSession)
        && value.daysSincePreviousSession >= 0));
}

function daysSincePrevious(
  sessions: MeditationSession[],
  now: Date,
): number | null {
  const previousTimes = sessions
    .map(session => Date.parse(`${session.date}T00:00:00`))
    .filter(timestamp => Number.isFinite(timestamp) && timestamp <= now.getTime());
  if (previousTimes.length === 0) return null;
  const latest = Math.max(...previousTimes);
  return Math.max(0, Math.floor((now.getTime() - latest) / 86_400_000));
}

function normalized(value: number): number {
  return clamp(value, 0, 1);
}

function clamp(value: number, minimum: number, maximum: number): number {
  if (!Number.isFinite(value)) return minimum;
  return Math.min(maximum, Math.max(minimum, value));
}

function isNormalized(value: unknown): value is number {
  return typeof value === 'number'
    && Number.isFinite(value)
    && value >= 0
    && value <= 1;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
