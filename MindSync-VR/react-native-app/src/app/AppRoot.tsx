import React, {useEffect} from 'react';
import {Linking, StatusBar} from 'react-native';
import {GestureHandlerRootView} from 'react-native-gesture-handler';
import {SafeAreaProvider} from 'react-native-safe-area-context';
import {QueryClient, QueryClientProvider} from '@tanstack/react-query';
import {AppNavigation} from './navigation';
import {DesignPreview} from '../features/voice/DesignPreview';
import {useMindSyncStore} from '../store/useMindSyncStore';

// TEMP: flip to true to show the design preview (theme + aurora + Sarah states)
// for approval. Set back to false before shipping.
const SHOW_DESIGN_PREVIEW = false;

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {retry: 2, staleTime: 30_000},
    mutations: {retry: 1},
  },
});

export function AppRoot() {
  const hydrated = useMindSyncStore(state => state.hydrated);
  const initializeAuth = useMindSyncStore(state => state.initializeAuth);
  const handlePasswordRecoveryUrl = useMindSyncStore(state => state.handlePasswordRecoveryUrl);

  useEffect(() => {
    if (!hydrated) return;
    let disposed = false;
    let cleanup: (() => void) | undefined;
    initializeAuth().then(nextCleanup => {
      if (disposed) nextCleanup();
      else cleanup = nextCleanup;
    }).catch(() => undefined);
    return () => {
      disposed = true;
      cleanup?.();
    };
  }, [hydrated, initializeAuth]);

  useEffect(() => {
    if (!hydrated) return;
    let active = true;
    const handledUrls = new Set<string>();
    const handleUrl = (url: string | null | undefined) => {
      if (!active || !url || handledUrls.has(url)) return;
      handledUrls.add(url);
      void handlePasswordRecoveryUrl(url);
    };

    const subscription = Linking.addEventListener('url', event => handleUrl(event.url));
    Linking.getInitialURL().then(handleUrl).catch(() => undefined);
    return () => {
      active = false;
      subscription.remove();
    };
  }, [handlePasswordRecoveryUrl, hydrated]);

  return (
    <GestureHandlerRootView style={{flex: 1}}>
      <SafeAreaProvider>
        <QueryClientProvider client={queryClient}>
          <StatusBar barStyle="light-content" />
          {SHOW_DESIGN_PREVIEW ? <DesignPreview /> : <AppNavigation />}
        </QueryClientProvider>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  );
}
