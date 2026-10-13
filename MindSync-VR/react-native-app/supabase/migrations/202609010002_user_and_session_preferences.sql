begin;

alter table public.onboarding_profiles
  add column if not exists preferred_illumination real,
  add column if not exists preferred_warmth real,
  add column if not exists preferred_atmospheric_softness real,
  add column if not exists preferred_color_richness real,
  add column if not exists preferred_ambient_motion real,
  add column if not exists particle_preference text,
  add column if not exists light_sensitivity text,
  add column if not exists motion_sensitivity real;

alter table public.onboarding_profiles
  drop constraint if exists onboarding_profiles_preferred_illumination_check,
  drop constraint if exists onboarding_profiles_preferred_warmth_check,
  drop constraint if exists onboarding_profiles_preferred_atmospheric_softness_check,
  drop constraint if exists onboarding_profiles_preferred_color_richness_check,
  drop constraint if exists onboarding_profiles_preferred_ambient_motion_check,
  drop constraint if exists onboarding_profiles_particle_preference_check,
  drop constraint if exists onboarding_profiles_light_sensitivity_check,
  drop constraint if exists onboarding_profiles_motion_sensitivity_check;

alter table public.onboarding_profiles
  add constraint onboarding_profiles_preferred_illumination_check
    check (preferred_illumination between 0 and 1),
  add constraint onboarding_profiles_preferred_warmth_check
    check (preferred_warmth between 0 and 1),
  add constraint onboarding_profiles_preferred_atmospheric_softness_check
    check (preferred_atmospheric_softness between 0 and 1),
  add constraint onboarding_profiles_preferred_color_richness_check
    check (preferred_color_richness between 0 and 1),
  add constraint onboarding_profiles_preferred_ambient_motion_check
    check (preferred_ambient_motion between 0 and 1),
  add constraint onboarding_profiles_particle_preference_check
    check (particle_preference in ('none', 'subtle', 'moderate')),
  add constraint onboarding_profiles_light_sensitivity_check
    check (light_sensitivity in ('none', 'mild', 'high')),
  add constraint onboarding_profiles_motion_sensitivity_check
    check (motion_sensitivity between 0 and 1);

alter table public.meditation_sessions
  add column if not exists session_context jsonb,
  add column if not exists effective_environment_preference jsonb;

alter table public.meditation_sessions
  drop constraint if exists meditation_sessions_session_context_check,
  drop constraint if exists meditation_sessions_effective_environment_preference_check;

alter table public.meditation_sessions
  add constraint meditation_sessions_session_context_check
    check (session_context is null or jsonb_typeof(session_context) = 'object'),
  add constraint meditation_sessions_effective_environment_preference_check
    check (
      effective_environment_preference is null
      or jsonb_typeof(effective_environment_preference) = 'object'
    );

commit;
