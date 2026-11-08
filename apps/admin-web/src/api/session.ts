import { useSyncExternalStore } from 'react';
import { sessionStore } from './client';
import type { Role } from './contracts';
export const useSession = () =>
  useSyncExternalStore(sessionStore.subscribe, sessionStore.getSnapshot);
export const canManage = (role?: Role) => role === 'OrganizationOwner' || role === 'Manager';
