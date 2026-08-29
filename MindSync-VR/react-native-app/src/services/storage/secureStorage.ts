import * as Keychain from 'react-native-keychain';

const TOKEN_SERVICE = 'com.mindsyncvr.auth';

export const secureStorage = {
  async saveTokens(accessToken: string, refreshToken: string) {
    await Keychain.setGenericPassword('mindsync-session', JSON.stringify({accessToken, refreshToken}), {
      service: TOKEN_SERVICE,
      accessible: Keychain.ACCESSIBLE.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
    });
  },
  async getTokens(): Promise<{accessToken: string; refreshToken: string} | null> {
    const credentials = await Keychain.getGenericPassword({service: TOKEN_SERVICE});
    if (!credentials) return null;
    try {
      return JSON.parse(credentials.password) as {accessToken: string; refreshToken: string};
    } catch {
      await this.clearTokens();
      return null;
    }
  },
  async clearTokens() {
    await Keychain.resetGenericPassword({service: TOKEN_SERVICE});
  },
};
