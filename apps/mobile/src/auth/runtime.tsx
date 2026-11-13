import {
  createContext,
  useContext,
  useEffect,
  useSyncExternalStore,
  type PropsWithChildren,
} from 'react';
import { AppState, Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';
import { QueryClient } from '@tanstack/react-query';
import { NativeTransport, nativeOrigin } from '../api/transport';
import { MobileSession } from './session';

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
);
export const SessionContext = createContext(mobileSession);
export function SessionProvider({ children }: PropsWithChildren) {
  useEffect(() => {
    void mobileSession.bootstrap().catch(() => undefined);
    const subscription = AppState.addEventListener('change', (state) => {
      if (state === 'active' && mobileSession.snapshot().status === 'authenticated') {
        void mobileSession
          .profile()
          .then(() => queryClient.invalidateQueries())
          .catch(() => undefined);
      }
    });
    return () => subscription.remove();
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
