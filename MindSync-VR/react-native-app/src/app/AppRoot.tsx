import React from 'react';
import {StatusBar} from 'react-native';
import {GestureHandlerRootView} from 'react-native-gesture-handler';
import {SafeAreaProvider} from 'react-native-safe-area-context';
import {QueryClient, QueryClientProvider} from '@tanstack/react-query';
import {AppNavigation} from './navigation';
import {DesignPreview} from '../features/voice/DesignPreview';

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
