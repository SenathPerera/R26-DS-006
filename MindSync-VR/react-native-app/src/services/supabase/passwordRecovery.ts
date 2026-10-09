export const PASSWORD_RECOVERY_REDIRECT_URL = 'mindsyncvr://auth/recovery';

export type PasswordRecoveryLink =
  | {kind: 'ignored'}
  | {kind: 'error'; message: string}
  | {kind: 'session'; accessToken: string; refreshToken: string};

export function parsePasswordRecoveryUrl(url: string): PasswordRecoveryLink {
  if (!/^mindsyncvr:\/\/auth\/recovery\/?(?:[?#]|$)/i.test(url)) {
    return {kind: 'ignored'};
  }

  const queryStart = url.indexOf('?');
  const fragmentStart = url.indexOf('#');
  const queryEnd = fragmentStart >= 0 ? fragmentStart : url.length;
  const query = queryStart >= 0 && queryStart < queryEnd
    ? url.slice(queryStart + 1, queryEnd)
    : '';
  const fragment = fragmentStart >= 0 ? url.slice(fragmentStart + 1) : '';
  const params = parseParams(`${query}&${fragment}`);

  const providerError = params.error_description ?? params.error;
  if (providerError) {
    return {kind: 'error', message: providerError};
  }

  if (params.type !== 'recovery') {
    return {kind: 'error', message: 'This is not a valid password recovery link.'};
  }

  if (!params.access_token || !params.refresh_token) {
    return {
      kind: 'error',
      message: 'This password recovery link is incomplete or has expired. Request a new email and try again.',
    };
  }

  return {
    kind: 'session',
    accessToken: params.access_token,
    refreshToken: params.refresh_token,
  };
}

function parseParams(value: string): Record<string, string> {
  const params: Record<string, string> = {};
  for (const part of value.split('&')) {
    if (!part) continue;
    const separator = part.indexOf('=');
    const rawKey = separator >= 0 ? part.slice(0, separator) : part;
    const rawValue = separator >= 0 ? part.slice(separator + 1) : '';
    const key = safelyDecode(rawKey);
    if (key) params[key] = safelyDecode(rawValue);
  }
  return params;
}

function safelyDecode(value: string): string {
  try {
    return decodeURIComponent(value.replace(/\+/g, ' '));
  } catch {
    return value;
  }
}
