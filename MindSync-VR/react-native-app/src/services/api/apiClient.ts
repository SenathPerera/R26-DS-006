import {environment} from '../../config/environment';

export class ApiError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
  }
}

class ApiClient {
  private accessToken: string | null = null;

  setAccessToken(token: string | null) {
    this.accessToken = token;
  }

  async request<T>(path: string, init: RequestInit = {}, retries = 1): Promise<T> {
    const response = await fetch(`${environment.apiBaseUrl}${path}`, {
      ...init,
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
        ...(this.accessToken ? {Authorization: `Bearer ${this.accessToken}`} : {}),
        ...init.headers,
      },
    });
    if (!response.ok) {
      if (response.status >= 500 && retries > 0) return this.request(path, init, retries - 1);
      throw new ApiError((await response.text()) || `Request failed (${response.status})`, response.status);
    }
    if (response.status === 204) return undefined as T;
    return response.json() as Promise<T>;
  }
}

export const apiClient = new ApiClient();
