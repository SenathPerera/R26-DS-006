begin;

create or replace function public.set_updated_at()
returns trigger
language plpgsql
security invoker
set search_path = ''
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

create table if not exists public.profiles (
  id uuid primary key references auth.users(id) on delete cascade,
  email text not null,
  display_name text not null check (char_length(display_name) between 1 and 100),
  role text not null default 'participant' check (role in ('participant', 'clinician', 'researcher')),
  onboarding_complete boolean not null default false,
  preferred_language text not null default 'English',
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table if not exists public.onboarding_profiles (
  user_id uuid primary key references public.profiles(id) on delete cascade,
  age_range text not null default '',
  meditation_experience text not null default '',
  preferred_duration integer not null default 15 check (preferred_duration between 1 and 180),
  goals text[] not null default '{}',
  meditation_style text not null default 'Guided',
  audio_preferences text[] not null default '{}',
  environment_preferences text[] not null default '{}',
  sensitivities text[] not null default '{}',
  consent_accepted boolean not null default false,
  research_consent boolean not null default false,
  privacy_notice_version text not null default 'mindsync-privacy-v1',
  consented_at timestamptz,
  updated_at timestamptz not null default now()
);

create table if not exists public.participant_consents (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles(id) on delete cascade,
  consent_type text not null check (consent_type in ('privacy_notice', 'research_participation')),
  document_version text not null,
  granted boolean not null,
  recorded_at timestamptz not null default now()
);

create table if not exists public.meditation_sessions (
  id text primary key,
  user_id uuid not null references public.profiles(id) on delete cascade,
  title text not null,
  session_date date not null,
  duration_minutes integer not null check (duration_minutes between 0 and 480),
  environment text not null,
  audio_profile text not null,
  completion_rate integer not null default 0 check (completion_rate between 0 and 100),
  mood_before integer not null default 0 check (mood_before between 0 and 10),
  mood_after integer not null default 0 check (mood_after between 0 and 10),
  validation_complete boolean not null default false,
  status text not null default 'ready' check (status in ('ready', 'active', 'paused', 'complete', 'aborted')),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table if not exists public.questionnaire_submissions (
  id text primary key,
  user_id uuid not null references public.profiles(id) on delete cascade,
  template_id text not null,
  session_id text references public.meditation_sessions(id) on delete set null,
  submitted_at timestamptz not null,
  export_shape_version text not null,
  answers jsonb not null check (jsonb_typeof(answers) = 'object'),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table if not exists public.wearable_devices (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles(id) on delete cascade,
  device_identifier text not null,
  display_name text not null,
  firmware text,
  last_connected_at timestamptz not null default now(),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (user_id, device_identifier)
);

create table if not exists public.complete_session_records (
  record_id text primary key,
  user_id uuid not null references public.profiles(id) on delete cascade,
  session_id text not null references public.meditation_sessions(id) on delete cascade,
  schema_version text not null,
  started_at timestamptz not null,
  completed_at timestamptz not null,
  record jsonb not null check (jsonb_typeof(record) = 'object'),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  check (completed_at >= started_at)
);

create index if not exists meditation_sessions_user_date_idx
  on public.meditation_sessions (user_id, session_date desc);
create index if not exists participant_consents_user_time_idx
  on public.participant_consents (user_id, recorded_at desc);
create index if not exists questionnaire_submissions_user_time_idx
  on public.questionnaire_submissions (user_id, submitted_at desc);
create index if not exists complete_session_records_user_time_idx
  on public.complete_session_records (user_id, completed_at desc);

drop trigger if exists profiles_set_updated_at on public.profiles;
create trigger profiles_set_updated_at before update on public.profiles
for each row execute function public.set_updated_at();
drop trigger if exists onboarding_profiles_set_updated_at on public.onboarding_profiles;
create trigger onboarding_profiles_set_updated_at before update on public.onboarding_profiles
for each row execute function public.set_updated_at();
drop trigger if exists meditation_sessions_set_updated_at on public.meditation_sessions;
create trigger meditation_sessions_set_updated_at before update on public.meditation_sessions
for each row execute function public.set_updated_at();
drop trigger if exists questionnaire_submissions_set_updated_at on public.questionnaire_submissions;
create trigger questionnaire_submissions_set_updated_at before update on public.questionnaire_submissions
for each row execute function public.set_updated_at();
drop trigger if exists wearable_devices_set_updated_at on public.wearable_devices;
create trigger wearable_devices_set_updated_at before update on public.wearable_devices
for each row execute function public.set_updated_at();
drop trigger if exists complete_session_records_set_updated_at on public.complete_session_records;
create trigger complete_session_records_set_updated_at before update on public.complete_session_records
for each row execute function public.set_updated_at();

create or replace function public.set_consent_recorded_at()
returns trigger
language plpgsql
security invoker
set search_path = ''
as $$
begin
  new.recorded_at = now();
  return new;
end;
$$;

drop trigger if exists participant_consents_set_recorded_at on public.participant_consents;
create trigger participant_consents_set_recorded_at before insert on public.participant_consents
for each row execute function public.set_consent_recorded_at();

create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer set search_path = ''
as $$
begin
  insert into public.profiles (id, email, display_name)
  values (
    new.id,
    coalesce(new.email, ''),
    coalesce(nullif(new.raw_user_meta_data ->> 'display_name', ''), split_part(coalesce(new.email, 'Participant'), '@', 1))
  )
  on conflict (id) do nothing;
  return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
after insert on auth.users
for each row execute function public.handle_new_user();

alter table public.profiles enable row level security;
alter table public.onboarding_profiles enable row level security;
alter table public.participant_consents enable row level security;
alter table public.meditation_sessions enable row level security;
alter table public.questionnaire_submissions enable row level security;
alter table public.wearable_devices enable row level security;
alter table public.complete_session_records enable row level security;

revoke all on table public.profiles from anon, authenticated;
revoke all on table public.onboarding_profiles from anon, authenticated;
revoke all on table public.participant_consents from anon, authenticated;
revoke all on table public.meditation_sessions from anon, authenticated;
revoke all on table public.questionnaire_submissions from anon, authenticated;
revoke all on table public.wearable_devices from anon, authenticated;
revoke all on table public.complete_session_records from anon, authenticated;

grant select, insert, update, delete on table public.profiles to authenticated;
grant select, insert, update, delete on table public.onboarding_profiles to authenticated;
grant select, insert on table public.participant_consents to authenticated;
grant select, insert, update, delete on table public.meditation_sessions to authenticated;
grant select, insert, update, delete on table public.questionnaire_submissions to authenticated;
grant select, insert, update, delete on table public.wearable_devices to authenticated;
grant select, insert, update, delete on table public.complete_session_records to authenticated;

do $$
declare
  table_name text;
begin
  foreach table_name in array array[
    'profiles', 'onboarding_profiles', 'participant_consents', 'meditation_sessions',
    'questionnaire_submissions', 'wearable_devices', 'complete_session_records'
  ] loop
    execute format('drop policy if exists "owner_select" on public.%I', table_name);
    execute format('drop policy if exists "owner_insert" on public.%I', table_name);
    execute format('drop policy if exists "owner_update" on public.%I', table_name);
    execute format('drop policy if exists "owner_delete" on public.%I', table_name);
  end loop;
end;
$$;

create policy "owner_select" on public.profiles for select to authenticated
using ((select auth.uid()) = id);
create policy "owner_insert" on public.profiles for insert to authenticated
with check ((select auth.uid()) = id and role = 'participant');
create policy "owner_update" on public.profiles for update to authenticated
using ((select auth.uid()) = id) with check ((select auth.uid()) = id and role = 'participant');
create policy "owner_delete" on public.profiles for delete to authenticated
using ((select auth.uid()) = id);

create policy "owner_select" on public.onboarding_profiles for select to authenticated
using ((select auth.uid()) = user_id);
create policy "owner_insert" on public.onboarding_profiles for insert to authenticated
with check ((select auth.uid()) = user_id);
create policy "owner_update" on public.onboarding_profiles for update to authenticated
using ((select auth.uid()) = user_id) with check ((select auth.uid()) = user_id);
create policy "owner_delete" on public.onboarding_profiles for delete to authenticated
using ((select auth.uid()) = user_id);

create policy "owner_select" on public.participant_consents for select to authenticated
using ((select auth.uid()) = user_id);
create policy "owner_insert" on public.participant_consents for insert to authenticated
with check ((select auth.uid()) = user_id);

create policy "owner_select" on public.meditation_sessions for select to authenticated
using ((select auth.uid()) = user_id);
create policy "owner_insert" on public.meditation_sessions for insert to authenticated
with check ((select auth.uid()) = user_id);
create policy "owner_update" on public.meditation_sessions for update to authenticated
using ((select auth.uid()) = user_id) with check ((select auth.uid()) = user_id);
create policy "owner_delete" on public.meditation_sessions for delete to authenticated
using ((select auth.uid()) = user_id);

create policy "owner_select" on public.questionnaire_submissions for select to authenticated
using ((select auth.uid()) = user_id);
create policy "owner_insert" on public.questionnaire_submissions for insert to authenticated
with check ((select auth.uid()) = user_id);
create policy "owner_update" on public.questionnaire_submissions for update to authenticated
using ((select auth.uid()) = user_id) with check ((select auth.uid()) = user_id);
create policy "owner_delete" on public.questionnaire_submissions for delete to authenticated
using ((select auth.uid()) = user_id);

create policy "owner_select" on public.wearable_devices for select to authenticated
using ((select auth.uid()) = user_id);
create policy "owner_insert" on public.wearable_devices for insert to authenticated
with check ((select auth.uid()) = user_id);
create policy "owner_update" on public.wearable_devices for update to authenticated
using ((select auth.uid()) = user_id) with check ((select auth.uid()) = user_id);
create policy "owner_delete" on public.wearable_devices for delete to authenticated
using ((select auth.uid()) = user_id);

create policy "owner_select" on public.complete_session_records for select to authenticated
using ((select auth.uid()) = user_id);
create policy "owner_insert" on public.complete_session_records for insert to authenticated
with check ((select auth.uid()) = user_id);
create policy "owner_update" on public.complete_session_records for update to authenticated
using ((select auth.uid()) = user_id) with check ((select auth.uid()) = user_id);
create policy "owner_delete" on public.complete_session_records for delete to authenticated
using ((select auth.uid()) = user_id);

commit;
