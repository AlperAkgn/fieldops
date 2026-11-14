import * as SQLite from 'expo-sqlite';
import * as SecureStore from 'expo-secure-store';
import { getRandomBytesAsync } from 'expo-crypto';
import type { OfflineIdentity } from '../auth/session';
import { OfflineStore } from './store';

const filename = 'fieldops-offline-v1.db';
const keyName = 'fieldops.offline.key.v1';
const options = {
  keychainService: 'com.fieldops.offline',
  keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
};
type OpenVault = { scope: string; database: SQLite.SQLiteDatabase; store: OfflineStore };
let current: OpenVault | null = null;
let tail: Promise<unknown> = Promise.resolve();
function serialize<T>(action: () => Promise<T>): Promise<T> {
  const next = tail.then(action);
  tail = next.catch(() => undefined);
  return next;
}

async function close() {
  const vault = current;
  current = null;
  if (vault) {
    await vault.store.close();
    await vault.database.closeAsync();
  }
}

async function erase() {
  // Delete the encryption key before a filesystem operation that can be interrupted.
  try {
    await SecureStore.deleteItemAsync(keyName, options);
  } finally {
    try {
      await close();
    } finally {
      await SQLite.deleteDatabaseAsync(filename);
    }
  }
}

export function removeOfflineVault() {
  return serialize(erase);
}

export function openOfflineVault(
  origin: string,
  identity: OfflineIdentity,
  allowCreate: boolean,
  active: () => boolean,
) {
  return serialize(async () => {
    if (!active() || !origin.startsWith('https:') || identity.role !== 'Technician') return null;
    const scope = JSON.stringify([
      origin,
      identity.tenantId,
      identity.userId,
      identity.sessionId,
      identity.role,
    ]);
    if (current?.scope === scope) return current.store;
    await close();
    const record = await SecureStore.getItemAsync(keyName, options);
    let saved: { scope: string; key: string } | null = record ? JSON.parse(record) : null;
    if (saved && saved.scope !== scope) {
      if (!allowCreate) return null;
      await erase();
      saved = null;
    }
    if (!saved && !allowCreate) return null;
    if (!saved) await SQLite.deleteDatabaseAsync(filename);

    const database = await SQLite.openDatabaseAsync(filename, { useNewConnection: true });
    try {
      const cipher = await database.getFirstAsync<{ cipher_version: string }>(
        'PRAGMA cipher_version',
      );
      if (!cipher?.cipher_version)
        throw new Error('Offline storage requires a native build with SQLCipher.');
      const key =
        saved?.key ??
        Array.from(await getRandomBytesAsync(32), (byte) =>
          byte.toString(16).padStart(2, '0'),
        ).join('');
      if (!/^[0-9a-f]{64}$/.test(key)) throw new Error('The encrypted workspace key is invalid.');
      await database.execAsync(`PRAGMA key = "x'${key}'";`);
      await database.getFirstAsync('SELECT count(*) FROM sqlite_master');
      await database.execAsync('PRAGMA secure_delete = ON; PRAGMA journal_mode = DELETE;');
      if (!saved) await SecureStore.setItemAsync(keyName, JSON.stringify({ scope, key }), options);
      const store = new OfflineStore(database);
      await store.initialize();
      current = { scope, database, store };
      if (!active()) {
        await close();
        return null;
      }
      return store;
    } catch (error) {
      if (current?.database === database) await close();
      else await database.closeAsync().catch(() => undefined);
      throw error;
    }
  });
}
