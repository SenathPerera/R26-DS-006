import {describeSupabaseError} from './supabaseError';

describe('Supabase error messages', () => {
  it('identifies missing onboarding columns', () => {
    expect(describeSupabaseError({
      code: 'PGRST204',
      message: "Could not find the 'preferred_illumination' column",
    })).toContain('database setup is incomplete (PGRST204)');
  });

  it('identifies row-level security failures', () => {
    expect(describeSupabaseError({
      code: '42501',
      message: 'new row violates row-level security policy',
    })).toContain('Check the Supabase policies');
  });

  it('preserves other structured error messages and codes', () => {
    expect(describeSupabaseError({code: '23514', message: 'Value violates a check constraint'}))
      .toBe('Value violates a check constraint (23514)');
  });

  it('handles ordinary and unknown errors', () => {
    expect(describeSupabaseError(new Error('Network request failed'))).toBe('Network request failed');
    expect(describeSupabaseError(null)).toBe('Unable to save right now. Please try again.');
  });
});
