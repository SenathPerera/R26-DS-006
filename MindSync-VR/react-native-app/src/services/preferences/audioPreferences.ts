import type {
  AudioControlPreferences,
  AudioPreferenceBundle,
  OnboardingProfile,
  SessionContext,
} from '../../types/domain';

export const AUDIO_PREFERENCE_SCHEMA = 'mindsync-audio-preferences-v1' as const;
export const AUDIO_INSTRUMENTS = ['piano', 'flute', 'pad', 'strings', 'bells', 'drone'] as const;

export function defaultAudioPreferences(): AudioControlPreferences {
  return {
    preferredInstruments: ['piano', 'flute'], mood: 'calm', tempoPreference: 'slow',
    audioIntensity: 0.3, ambientMusicBalance: 'balanced', brightnessPreference: 'soft_dark',
    noveltyTolerance: 0.2, avoidDissonance: true, rhythmPreference: 'minimal',
    natureSoundPreference: 'medium', reverbPreference: 'spacious', volumePreference: 'low',
  };
}

// Old accounts keep their broad tags until they save the structured sound form.
export function audioPreferencesFromOnboarding(profile: OnboardingProfile): AudioControlPreferences {
  if (isAudioControlPreferences(profile.audioControlPreferences)) {
    return normalizeAudioPreferences(profile.audioControlPreferences);
  }
  const result = defaultAudioPreferences();
  const tags = profile.audioPreferences ?? [];
  if (tags.includes('Soft drone')) result.preferredInstruments = ['drone', 'pad'];
  else if (tags.includes('Warm pads')) result.preferredInstruments = ['pad', 'piano'];
  if (tags.includes('Nature heavy')) result.ambientMusicBalance = 'mostly_ambience';
  if (tags.includes('Subtle rhythm')) result.rhythmPreference = 'gentle_pulse';
  if (tags.includes('Neutral tone')) result.brightnessPreference = 'neutral';
  return result;
}

export function effectiveAudioPreference(
  profile: OnboardingProfile, context: SessionContext | null,
): AudioControlPreferences {
  return context?.audioPreferenceMode === 'adjust'
    && isAudioControlPreferences(context.sessionAudioPreference)
    ? normalizeAudioPreferences(context.sessionAudioPreference)
    : audioPreferencesFromOnboarding(profile);
}

export function buildAudioPreferenceBundle(
  profile: OnboardingProfile, context: SessionContext | null,
): AudioPreferenceBundle {
  const adjusting = context?.audioPreferenceMode === 'adjust';
  if (adjusting && !isAudioControlPreferences(context.sessionAudioPreference)) {
    throw new Error('session-audio-preferences-required');
  }
  return {
    schemaVersion: AUDIO_PREFERENCE_SCHEMA,
    longTerm: audioPreferencesFromOnboarding(profile),
    preferenceMode: adjusting ? 'adjust' : 'usual',
    sessionOverride: adjusting ? normalizeAudioPreferences(context.sessionAudioPreference!) : null,
  };
}

export function requireParticipantPseudonym(value: string | undefined): string {
  const id = value?.trim();
  if (!id || ['anonymous', 'U001', 'FallbackUser', 'participant-demo'].includes(id) || id.length > 128) {
    throw new Error('participant-identity-required');
  }
  return id;
}

export function normalizeAudioPreferences(value: AudioControlPreferences): AudioControlPreferences {
  return {
    ...value,
    preferredInstruments: [...new Set(value.preferredInstruments)],
    audioIntensity: bounded(value.audioIntensity, 0.3),
    noveltyTolerance: bounded(value.noveltyTolerance, 0.2),
  };
}

export function isAudioControlPreferences(value: unknown): value is AudioControlPreferences {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
  const p = value as Record<string, unknown>;
  const oneOf = (field: string, choices: readonly string[]) =>
    typeof p[field] === 'string' && choices.includes(p[field] as string);
  const normalized = (v: unknown) => typeof v === 'number' && Number.isFinite(v) && v >= 0 && v <= 1;
  return Array.isArray(p.preferredInstruments)
    && p.preferredInstruments.length > 0 && p.preferredInstruments.length <= AUDIO_INSTRUMENTS.length
    && p.preferredInstruments.every(item => typeof item === 'string' && AUDIO_INSTRUMENTS.includes(item as typeof AUDIO_INSTRUMENTS[number]))
    && new Set(p.preferredInstruments).size === p.preferredInstruments.length
    && oneOf('mood', ['calm', 'focused', 'sleepy', 'energized'])
    && oneOf('tempoPreference', ['slow', 'medium', 'fast'])
    && normalized(p.audioIntensity) && normalized(p.noveltyTolerance)
    && typeof p.avoidDissonance === 'boolean'
    && oneOf('ambientMusicBalance', ['mostly_ambience', 'balanced', 'mostly_music'])
    && oneOf('brightnessPreference', ['soft_dark', 'neutral', 'bright_clear'])
    && oneOf('rhythmPreference', ['minimal', 'gentle_pulse', 'more_motion'])
    && oneOf('natureSoundPreference', ['none', 'low', 'medium', 'high'])
    && oneOf('reverbPreference', ['dry', 'balanced', 'spacious'])
    && oneOf('volumePreference', ['low', 'medium', 'high']);
}

function bounded(value: number, fallback: number): number {
  return Number.isFinite(value) ? Math.max(0, Math.min(1, value)) : fallback;
}
