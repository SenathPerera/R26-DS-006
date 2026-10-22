begin;

alter table public.onboarding_profiles
  add column if not exists audio_control_preferences jsonb;
alter table public.meditation_sessions
  add column if not exists effective_audio_preference jsonb;

alter table public.onboarding_profiles
  drop constraint if exists onboarding_profiles_audio_control_preferences_check;
alter table public.onboarding_profiles
  add constraint onboarding_profiles_audio_control_preferences_check
    check (audio_control_preferences is null or jsonb_typeof(audio_control_preferences) = 'object');
alter table public.meditation_sessions
  drop constraint if exists meditation_sessions_effective_audio_preference_check;
alter table public.meditation_sessions
  add constraint meditation_sessions_effective_audio_preference_check
    check (effective_audio_preference is null or jsonb_typeof(effective_audio_preference) = 'object');

-- Existing account tags are retained and mapped by the mobile client until
-- the participant saves the structured sound form. Temporary choices stay
-- in meditation_sessions.session_context, never in onboarding_profiles.
commit;
