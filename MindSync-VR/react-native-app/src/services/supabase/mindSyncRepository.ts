import type {User} from '@supabase/supabase-js';
import type {
  MeditationSession,
  OnboardingProfile,
  QuestionnaireSubmission,
  UserProfile,
  WearableDevice,
} from '../../types/domain';
import type {CompleteSessionRecord} from '../session/completeSessionRecord';
import type {Database, Json} from './database.types';
import {
  answersToJson,
  onboardingFromRow,
  profileFromRow,
  sessionFromRow,
  submissionFromRow,
} from './databaseMappers';
import {getSupabaseClient} from './supabaseClient';

type ProfileInsert = Database['public']['Tables']['profiles']['Insert'];
type OnboardingInsert = Database['public']['Tables']['onboarding_profiles']['Insert'];
type SessionInsert = Database['public']['Tables']['meditation_sessions']['Insert'];
type SubmissionInsert = Database['public']['Tables']['questionnaire_submissions']['Insert'];

export interface SupabaseAccountData {
  profile: UserProfile;
  onboarding: OnboardingProfile | null;
  sessions: MeditationSession[];
  questionnaireSubmissions: QuestionnaireSubmission[];
}

type AccountIdentity = Pick<User, 'id' | 'email' | 'user_metadata'>;

class MindSyncRepository {
  async ensureProfile(user: AccountIdentity): Promise<UserProfile> {
    const client = getSupabaseClient();
    const existing = await client.from('profiles').select('*').eq('id', user.id).maybeSingle();
    if (existing.error) throw existing.error;
    if (existing.data) return profileFromRow(existing.data);

    const displayName = typeof user.user_metadata?.display_name === 'string'
      ? user.user_metadata.display_name.trim()
      : '';
    const row: ProfileInsert = {
      id: user.id,
      email: user.email ?? '',
      display_name: displayName || user.email?.split('@')[0] || 'Participant',
    };
    const created = await client.from('profiles').upsert(row).select('*').single();
    if (created.error) throw created.error;
    return profileFromRow(created.data);
  }

  async loadAccount(user: AccountIdentity): Promise<SupabaseAccountData> {
    const client = getSupabaseClient();
    const profile = await this.ensureProfile(user);
    const [onboarding, sessions, submissions] = await Promise.all([
      client.from('onboarding_profiles').select('*').eq('user_id', user.id).maybeSingle(),
      client.from('meditation_sessions').select('*').eq('user_id', user.id).order('session_date', {ascending: false}),
      client.from('questionnaire_submissions').select('*').eq('user_id', user.id).order('submitted_at', {ascending: false}),
    ]);
    if (onboarding.error) throw onboarding.error;
    if (sessions.error) throw sessions.error;
    if (submissions.error) throw submissions.error;
    return {
      profile,
      onboarding: onboarding.data ? onboardingFromRow(onboarding.data, profile.name) : null,
      sessions: (sessions.data ?? []).map(sessionFromRow),
      questionnaireSubmissions: (submissions.data ?? []).map(submissionFromRow),
    };
  }

  async saveOnboarding(userId: string, profile: OnboardingProfile): Promise<UserProfile> {
    const client = getSupabaseClient();
    const now = new Date().toISOString();
    const row: OnboardingInsert = {
      user_id: userId,
      age_range: profile.ageRange,
      meditation_experience: profile.meditationExperience,
      preferred_duration: profile.preferredDuration,
      goals: profile.goals,
      meditation_style: profile.meditationStyle,
      audio_preferences: profile.audioPreferences,
      environment_preferences: profile.environmentPreferences,
      sensitivities: profile.sensitivities,
      preferred_illumination: profile.preferredIllumination,
      preferred_warmth: profile.preferredWarmth,
      preferred_atmospheric_softness: profile.preferredAtmosphericSoftness,
      preferred_color_richness: profile.preferredColorRichness,
      preferred_ambient_motion: profile.preferredAmbientMotion,
      particle_preference: profile.particlePreference,
      light_sensitivity: profile.lightSensitivity,
      motion_sensitivity: profile.motionSensitivity,
      consent_accepted: profile.consentAccepted,
      research_consent: profile.researchConsent,
      consented_at: profile.consentAccepted ? now : null,
    };
    const saved = await client.from('onboarding_profiles').upsert(row).select('user_id').single();
    if (saved.error) throw saved.error;
    const consents = await client.from('participant_consents').insert([
      {
        user_id: userId,
        consent_type: 'privacy_notice',
        document_version: 'mindsync-privacy-v1',
        granted: profile.consentAccepted,
      },
      {
        user_id: userId,
        consent_type: 'research_participation',
        document_version: 'mindsync-research-v1',
        granted: profile.researchConsent,
      },
    ]);
    if (consents.error) throw consents.error;
    const updated = await client.from('profiles').update({
      display_name: profile.name,
      onboarding_complete: true,
    }).eq('id', userId).select('*').single();
    if (updated.error) throw updated.error;
    return profileFromRow(updated.data);
  }

  async saveSession(userId: string, session: MeditationSession, status = 'ready'): Promise<void> {
    const row: SessionInsert = {
      id: session.id,
      user_id: userId,
      title: session.title,
      session_date: session.date,
      duration_minutes: session.durationMinutes,
      environment: session.environment,
      audio_profile: session.audioProfile,
      completion_rate: session.completionRate,
      mood_before: session.moodBefore,
      mood_after: session.moodAfter,
      validation_complete: session.validationComplete,
      session_context: session.sessionContext
        ? JSON.parse(JSON.stringify(session.sessionContext)) as Json
        : null,
      effective_environment_preference: session.effectiveEnvironmentPreference
        ? JSON.parse(JSON.stringify(session.effectiveEnvironmentPreference)) as Json
        : null,
      status,
    };
    const {error} = await getSupabaseClient().from('meditation_sessions').upsert(row);
    if (error) throw error;
  }

  async saveSessions(userId: string, sessions: MeditationSession[]): Promise<void> {
    for (const session of sessions) await this.saveSession(userId, session);
  }

  async saveQuestionnaire(submission: QuestionnaireSubmission): Promise<void> {
    const row: SubmissionInsert = {
      id: submission.id,
      user_id: submission.userId,
      template_id: submission.templateId,
      session_id: submission.sessionId,
      submitted_at: submission.submittedAt,
      export_shape_version: submission.exportShapeVersion,
      answers: answersToJson(submission.answers),
    };
    const {error} = await getSupabaseClient().from('questionnaire_submissions').upsert(row);
    if (error) throw error;
  }

  async savePendingQuestionnaires(submissions: QuestionnaireSubmission[]): Promise<void> {
    for (const submission of submissions.filter(item => !item.synced)) {
      await this.saveQuestionnaire(submission);
    }
  }

  async registerWearable(userId: string, device: WearableDevice): Promise<void> {
    const {error} = await getSupabaseClient().from('wearable_devices').upsert({
      user_id: userId,
      device_identifier: device.id,
      display_name: device.name,
      firmware: device.firmware ?? null,
      last_connected_at: new Date().toISOString(),
    }, {onConflict: 'user_id,device_identifier'});
    if (error) throw error;
  }

  async saveCompleteSessionRecord(userId: string, record: CompleteSessionRecord): Promise<void> {
    const durationMinutes = Math.max(0, Math.ceil(
      (record.completedAtUnixSeconds - record.startedAtUnixSeconds) / 60,
    ));
    await this.saveSession(userId, {
      id: record.sessionId,
      title: 'Adaptive VR meditation',
      date: new Date(record.startedAtUnixSeconds * 1000).toISOString().slice(0, 10),
      durationMinutes,
      environment: record.visual.sceneId,
      audioProfile: 'Adaptive audio',
      completionRate: record.visual.completionPhase === 'completed' ? 100 : 0,
      moodBefore: 0,
      moodAfter: 0,
      validationComplete: true,
    }, record.visual.completionPhase === 'completed' ? 'complete' : 'aborted');
    const {error} = await getSupabaseClient().from('complete_session_records').upsert({
      record_id: record.recordId,
      user_id: userId,
      session_id: record.sessionId,
      schema_version: record.schemaVersion,
      started_at: new Date(record.startedAtUnixSeconds * 1000).toISOString(),
      completed_at: new Date(record.completedAtUnixSeconds * 1000).toISOString(),
      record: JSON.parse(JSON.stringify(record)) as Json,
    });
    if (error) throw error;
  }
}

export const mindSyncRepository = new MindSyncRepository();
