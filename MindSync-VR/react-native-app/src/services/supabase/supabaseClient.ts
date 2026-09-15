import 'react-native-url-polyfill/auto';

import {AppState, type AppStateStatus} from 'react-native';
import {createClient, type SupabaseClient} from '@supabase/supabase-js';
import {environment} from '../../config/environment';
import type {Database} from './database.types';
import {supabaseSecureStorage} from './supabaseStorage';

export const isSupabaseConfigured = environment.supabase.enabled
  && environment.supabase.url.startsWith('https://')
  && environment.supabase.publishableKey.length > 0;

const client: SupabaseClient<Database> | null = isSupabaseConfigured
  ? createClient<Database>(
      environment.supabase.url,
      environment.supabase.publishableKey,
      {
        auth: {
          storage: supabaseSecureStorage,
          autoRefreshToken: true,
          persistSession: true,
          detectSessionInUrl: false,
        },
      },
    )
  : null;

export function getSupabaseClient(): SupabaseClient<Database> {
  if (!client) {
    throw new Error('Supabase is not configured for this build');
  }
  return client;
}

export function startSupabaseAuthRefresh(): () => void {
  if (!client) return () => undefined;

  const applyState = (state: AppStateStatus) => {
    if (state === 'active') client.auth.startAutoRefresh();
    else client.auth.stopAutoRefresh();
  };
  applyState((AppState.currentState ?? 'active') as AppStateStatus);
  const subscription = AppState.addEventListener('change', applyState);
  return () => {
    subscription.remove();
    client.auth.stopAutoRefresh();
  };
}
