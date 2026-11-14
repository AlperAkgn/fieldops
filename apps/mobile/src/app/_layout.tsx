import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { QueryClientProvider } from '@tanstack/react-query';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { configurationError, queryClient, SessionProvider, useSession } from '../auth/runtime';
import { Screen, ErrorBox } from '../components/ui';
import { OfflineProvider } from '../offline/runtime';

function Navigation() {
  const { state } = useSession();
  const admitted = state.status === 'authenticated' || state.status === 'offline';
  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Screen name="index" />
      <Stack.Protected guard={!admitted}>
        <Stack.Screen name="sign-in" />
      </Stack.Protected>
      <Stack.Protected guard={admitted}>
        <Stack.Screen name="(tabs)" />
      </Stack.Protected>
      <Stack.Protected
        guard={admitted && (state.access?.role ?? state.offline?.role) === 'Technician'}
      >
        <Stack.Screen name="work-orders/[id]" />
      </Stack.Protected>
    </Stack>
  );
}

export default function RootLayout() {
  if (configurationError)
    return (
      <SafeAreaProvider>
        <Screen>
          <ErrorBox text={configurationError} />
        </Screen>
      </SafeAreaProvider>
    );
  return (
    <SafeAreaProvider>
      <QueryClientProvider client={queryClient}>
        <SessionProvider>
          <StatusBar style="dark" />
          <OfflineProvider>
            <Navigation />
          </OfflineProvider>
        </SessionProvider>
      </QueryClientProvider>
    </SafeAreaProvider>
  );
}
