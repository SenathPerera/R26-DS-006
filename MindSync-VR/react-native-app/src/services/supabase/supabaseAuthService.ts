import type {Session, User} from '@supabase/supabase-js';
import {apiClient} from '../api/apiClient';
import {getSupabaseClient, startSupabaseAuthRefresh} from './supabaseClient';

export type AuthSessionListener = (session: Session | null) => Promise<void> | void;

class SupabaseAuthService {
  async initialize(listener: AuthSessionListener): Promise<() => void> {
    const client = getSupabaseClient();
    const stopRefresh = startSupabaseAuthRefresh();
    const {data, error} = await client.auth.getSession();
    if (error) {
      stopRefresh();
      throw error;
    }
    apiClient.setAccessToken(data.session?.access_token ?? null);
    await listener(data.session);

    const {data: authListener} = client.auth.onAuthStateChange((_event, session) => {
      apiClient.setAccessToken(session?.access_token ?? null);
      setTimeout(() => {
        Promise.resolve(listener(session)).catch(() => undefined);
      }, 0);
    });

    return () => {
      authListener.subscription.unsubscribe();
      stopRefresh();
      apiClient.setAccessToken(null);
    };
  }

  async signIn(email: string, password: string): Promise<Session> {
    const {data, error} = await getSupabaseClient().auth.signInWithPassword({
      email: email.trim().toLowerCase(),
      password,
    });
    if (error) throw error;
    if (!data.session) throw new Error('Supabase did not return a session');
    return data.session;
  }

  async signUp(name: string, email: string, password: string): Promise<{user: User; session: Session | null}> {
    const {data, error} = await getSupabaseClient().auth.signUp({
      email: email.trim().toLowerCase(),
      password,
      options: {data: {display_name: name.trim()}},
    });
    if (error) throw error;
    if (!data.user) throw new Error('Supabase did not create the user');
    return {user: data.user, session: data.session};
  }

  async sendPasswordReset(email: string): Promise<void> {
    const {error} = await getSupabaseClient().auth.resetPasswordForEmail(
      email.trim().toLowerCase(),
    );
    if (error) throw error;
  }

  async signOut(): Promise<void> {
    const {error} = await getSupabaseClient().auth.signOut();
    if (error) throw error;
    apiClient.setAccessToken(null);
  }
}

export const supabaseAuthService = new SupabaseAuthService();
