begin;

alter table public.onboarding_profiles
  drop constraint if exists onboarding_profiles_particle_preference_check;

alter table public.onboarding_profiles
  drop column if exists particle_preference;

commit;
