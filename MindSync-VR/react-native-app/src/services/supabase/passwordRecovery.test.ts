import {
  parsePasswordRecoveryUrl,
  PASSWORD_RECOVERY_REDIRECT_URL,
} from './passwordRecovery';

describe('parsePasswordRecoveryUrl', () => {
  it('reads a Supabase recovery session from the URL fragment', () => {
    expect(parsePasswordRecoveryUrl(
      `${PASSWORD_RECOVERY_REDIRECT_URL}#access_token=access-token&refresh_token=refresh-token&type=recovery`,
    )).toEqual({
      kind: 'session',
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
    });
  });

  it('reads recovery parameters from the query string', () => {
    expect(parsePasswordRecoveryUrl(
      `${PASSWORD_RECOVERY_REDIRECT_URL}?type=recovery&access_token=access%20token&refresh_token=refresh%2Btoken`,
    )).toEqual({
      kind: 'session',
      accessToken: 'access token',
      refreshToken: 'refresh+token',
    });
  });

  it('returns the provider message for an expired link', () => {
    expect(parsePasswordRecoveryUrl(
      `${PASSWORD_RECOVERY_REDIRECT_URL}#error=access_denied&error_code=otp_expired&error_description=Email+link+is+invalid+or+has+expired&type=recovery`,
    )).toEqual({
      kind: 'error',
      message: 'Email link is invalid or has expired',
    });
  });

  it('rejects an incomplete recovery callback', () => {
    expect(parsePasswordRecoveryUrl(
      `${PASSWORD_RECOVERY_REDIRECT_URL}#type=recovery&access_token=access-token`,
    )).toEqual({
      kind: 'error',
      message: 'This password recovery link is incomplete or has expired. Request a new email and try again.',
    });
  });

  it('ignores URLs that do not target the recovery route', () => {
    expect(parsePasswordRecoveryUrl('https://example.com/auth/recovery#type=recovery')).toEqual({kind: 'ignored'});
  });
});
