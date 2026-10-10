alter table public.onboarding_profiles
  drop column if exists light_sensitivity,
  drop column if exists motion_sensitivity;
