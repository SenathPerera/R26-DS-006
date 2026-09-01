import * as Keychain from 'react-native-keychain';

const SERVICE_PREFIX = 'com.mindsyncvr.supabase.auth';

function serviceForKey(key: string) {
  let hash = 7;
  for (let index = 0; index < key.length; index += 1) {
    hash = (hash * 31 + key.charCodeAt(index)) % 2147483647;
  }
  return `${SERVICE_PREFIX}.${hash.toString(16)}`;
}

export const supabaseSecureStorage = {
  async getItem(key: string): Promise<string | null> {
    const credentials = await Keychain.getGenericPassword({service: serviceForKey(key)});
    return credentials && credentials.username === key ? credentials.password : null;
  },

  async setItem(key: string, value: string): Promise<void> {
    await Keychain.setGenericPassword(key, value, {
      service: serviceForKey(key),
      accessible: Keychain.ACCESSIBLE.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
    });
  },

  async removeItem(key: string): Promise<void> {
    await Keychain.resetGenericPassword({service: serviceForKey(key)});
  },
};
