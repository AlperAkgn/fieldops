import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { QueryClientProvider } from '@tanstack/react-query';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { configurationError, queryClient, SessionProvider, useSession } from '../auth/runtime';
import { Screen, ErrorBox } from '../components/ui';

function Navigation() {
  const { state } = useSession();
  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Screen name="index" />
      <Stack.Protected guard={state.status !== 'authenticated'}>
        <Stack.Screen name="sign-in" />
      </Stack.Protected>
      <Stack.Protected guard={state.status === 'authenticated'}>
        <Stack.Screen name="(tabs)" />
      </Stack.Protected>
      <Stack.Protected
        guard={state.status === 'authenticated' && state.access?.role === 'Technician'}
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
          <Navigation />
        </SessionProvider>
      </QueryClientProvider>
    </SafeAreaProvider>
  );
}
