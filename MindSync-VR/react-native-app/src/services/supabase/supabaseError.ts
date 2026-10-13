type SupabaseErrorShape = {
  code?: unknown;
  message?: unknown;
};

export function describeSupabaseError(error: unknown): string {
  const value = error && typeof error === 'object' ? error as SupabaseErrorShape : null;
  const code = typeof value?.code === 'string' ? value.code : null;
  const message = typeof value?.message === 'string'
    ? value.message
    : error instanceof Error ? error.message : null;

  if (code && ['42P01', '42703', 'PGRST204', 'PGRST205'].includes(code)) {
    return `The app database setup is incomplete (${code}). Apply the Supabase migrations, then try again.`;
  }
  if (code === '42501') {
    return 'This account cannot save to the app database (42501). Check the Supabase policies and sign-in session.';
  }
  return message ? `${message}${code ? ` (${code})` : ''}` : 'Unable to save right now. Please try again.';
}
