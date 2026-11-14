import {
  createContext,
  useContext,
  useEffect,
  useSyncExternalStore,
  type PropsWithChildren,
} from 'react';
import { AppState, Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';
import * as Network from 'expo-network';
import { QueryClient } from '@tanstack/react-query';
import { NativeTransport, nativeOrigin } from '../api/transport';
import { fetchHealth } from '../api/health';
import { MobileSession } from './session';
import { removeOfflineVault } from '../offline/vault';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: false, staleTime: 15_000, refetchOnWindowFocus: false },
    mutations: { retry: false },
  },
});
const key = 'fieldops.session.v1';
const options = {
  keychainService: 'com.fieldops.session',
  keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
};
const secureStore = {
  async get() {
    if (Platform.OS === 'web') throw new Error('Use the admin web application in a browser.');
    return SecureStore.getItemAsync(key, options);
  },
  async set(value: string) {
    if (Platform.OS === 'web') throw new Error('Protected storage requires a native device.');
    await SecureStore.setItemAsync(key, value, options);
  },
  async remove() {
    if (Platform.OS === 'web') throw new Error('Protected storage requires a native device.');
    await SecureStore.deleteItemAsync(key, options);
  },
};
const apiConfig = (() => {
  try {
    return {
      origin: nativeOrigin(
        process.env.EXPO_PUBLIC_API_BASE_URL || 'https://localhost:5443',
        __DEV__ && process.env.EXPO_PUBLIC_ALLOW_INSECURE_HTTP === 'true',
      ),
      error: null,
    };
  } catch {
    return {
      origin: 'https://invalid.example',
      error:
        'Set EXPO_PUBLIC_API_BASE_URL to a trusted HTTPS API origin without credentials, path, query or fragment. Private local HTTP requires the explicit debug setting.',
    };
  }
})();
export const configurationError = apiConfig.error;
export const mobileSession = new MobileSession(
  new NativeTransport(apiConfig.origin),
  secureStore,
  () => {
    void queryClient.cancelQueries();
    queryClient.clear();
  },
  Date.now,
  () => (Platform.OS === 'web' ? Promise.resolve() : removeOfflineVault()),
);
export const SessionContext = createContext(mobileSession);
async function serviceReachable(signal?: AbortSignal) {
  try {
    if (configurationError) return false;
    const network = await Network.getNetworkStateAsync();
    if (network.isConnected === false || network.isInternetReachable === false) return false;
    return (await fetchHealth(apiConfig.origin, 'ready', signal)).status === 'Healthy';
  } catch {
    return false;
  }
}
export function SessionProvider({ children }: PropsWithChildren) {
  useEffect(() => {
    const controller = new AbortController();
    void serviceReachable(controller.signal)
      .then((reachable) => {
        if (controller.signal.aborted) return;
        return reachable ? mobileSession.bootstrap() : mobileSession.restoreOffline();
      })
      .catch(() => undefined);
    const subscription = AppState.addEventListener('change', (state) => {
      const access = mobileSession.snapshot().access;
      if (state !== 'active' || !access || access.role === 'Technician') return;
      void serviceReachable(controller.signal)
        .then(async (reachable) => {
          if (!reachable || controller.signal.aborted || mobileSession.snapshot().access !== access)
            return;
          await mobileSession.profile(controller.signal);
          await queryClient.invalidateQueries();
        })
        .catch(() => undefined);
    });
    return () => {
      controller.abort();
      subscription.remove();
    };
  }, []);
  return <SessionContext.Provider value={mobileSession}>{children}</SessionContext.Provider>;
}
export function useSession() {
  const session = useContext(SessionContext);
  return {
    session,
    state: useSyncExternalStore(session.subscribe, session.snapshot),
  };
}
