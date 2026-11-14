import { DatabaseSync } from 'node:sqlite';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { OfflineStore, type SqlConnection, type SyncOrder } from './store';
import { SyncEngine } from './engine';
import { ApiError } from '../api/transport';

const time = Date.parse('2026-11-13T12:00:00Z');
const now = () => time;
const roots: string[] = [];
const handles = new Set<DatabaseSync>();
function open(file = ':memory:') {
  const db = new DatabaseSync(file);
  handles.add(db);
  const sql: SqlConnection = {
    execAsync: async (source) => db.exec(source),
    runAsync: async (source, ...params) => {
      db.prepare(source).run(...params);
    },
    getAllAsync: async <T>(source: string) => db.prepare(source).all() as T[],
  };
  return {
    db,
    store: new OfflineStore(sql, now),
    close: () => {
      db.close();
      handles.delete(db);
    },
  };
}
afterEach(() => {
  for (const db of handles) db.close();
  handles.clear();
  for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
});
function order(id = 'a', version = 1): SyncOrder {
  return {
    order: {
      id,
      workOrderNumber: id,
      title: 'Pump',
      description: null,
      priority: 'High',
      status: 'Assigned',
      plannedStartAt: null,
      plannedEndAt: null,
      actualStartAt: null,
      actualEndAt: null,
      createdAt: '',
      updatedAt: '',
      version,
    },
    checklist: {
      workOrderId: id,
      workOrderVersion: version,
      totalItems: 1,
      completedItems: 0,
      progressPercent: 0,
      requiredItemsComplete: false,
      items: [
        {
          id: 'item',
          label: 'Inspect',
          position: 0,
          isRequired: true,
          isCompleted: false,
          completedAt: null,
        },
      ],
    },
    notes: [],
  };
}
async function seed(store: OfflineStore, orders = [order()]) {
  await store.initialize();
  await store.applyPage({
    orders,
    cursor: 'cursor-1',
    hasMore: false,
    serverTime: new Date(time).toISOString(),
  });
}

describe('offline SQLite and synchronization', () => {
  it('keeps checklist and ordered commands across close and reopen', async () => {
    const root = mkdtempSync(join(tmpdir(), 'fieldops-sqlite-'));
    roots.push(root);
    const file = join(root, 'fieldops.db');
    const first = open(file);
    await seed(first.store);
    await first.store.enqueue('first', 'a', 1, {
      kind: 'checklist',
      itemId: 'item',
      isCompleted: true,
    });
    await first.store.enqueue('second', 'a', 2, { kind: 'start' });
    await first.store.mark('first', 'sending', 1);
    first.close();
    const second = open(file);
    await second.store.initialize();
    const snapshot = await second.store.snapshot();
    expect(snapshot.operations[0]).toMatchObject({ state: 'retry', attempts: 1 });
    expect(snapshot.orders[0]?.checklist.requiredItemsComplete).toBe(true);
    expect(snapshot.orders[0]?.order.status).toBe('InProgress');
    expect(snapshot.operations.map(({ id, command }) => [id, command.expectedVersion])).toEqual([
      ['first', 1],
      ['second', 2],
    ]);
  });

  it('honors a persisted shared rate limit before retrying or sending independent writes', async () => {
    const { store } = open();
    await seed(store, [order(), order('b')]);
    await store.enqueue('first', 'a', 1, { kind: 'start' });
    await store.enqueue('other', 'b', 1, { kind: 'start' });
    const sent: string[] = [];
    let clock = time;
    const api = {
      pull: async () => ({
        cursor: 'cursor-2',
        hasMore: false,
        serverTime: new Date(clock).toISOString(),
        orders: [],
      }),
      push: async (command: { id: string }) => {
        sent.push(command.id);
        if (sent.length === 1) throw new ApiError(429, 'rate_limit', 'Wait', 60);
        return { id: command.id, status: 'applied' as const, version: 2 };
      },
    };
    await expect(new SyncEngine(store, api, () => clock).run()).rejects.toMatchObject({
      status: 429,
    });
    clock += 30_000;
    await new SyncEngine(store, api, () => clock).run();
    expect(sent).toEqual(['first']);
    clock += 30_000;
    await new SyncEngine(store, api, () => clock).run();
    expect(sent).toEqual(['first', 'first', 'other']);
  });

  it('rolls back a failed pull page and its cursor', async () => {
    const { db, store } = open();
    await seed(store);
    db.exec(
      "CREATE TRIGGER refuse_order BEFORE INSERT ON offline_orders WHEN NEW.id = 'b' BEGIN SELECT RAISE(ABORT, 'test'); END",
    );
    await expect(
      store.applyPage({
        cursor: 'advanced',
        hasMore: false,
        serverTime: new Date(time).toISOString(),
        orders: [order('a', 2), order('b')],
      }),
    ).rejects.toThrow();
    expect((await store.snapshot()).orders[0]?.order.version).toBe(1);
    expect((await store.snapshot()).cursor).toBe('cursor-1');
  });

  it('replays the identical operation after losing a response and keeps the tail ordered', async () => {
    const { store } = open();
    await seed(store);
    await store.enqueue('first', 'a', 1, { kind: 'start' });
    await store.enqueue('second', 'a', 2, { kind: 'pause' });
    const sent: string[] = [];
    let clock = time;
    const engine = new SyncEngine(
      store,
      {
        pull: async () => ({
          cursor: 'cursor-2',
          hasMore: false,
          serverTime: new Date(clock).toISOString(),
          orders: [],
        }),
        push: async (command) => {
          sent.push(command.id);
          if (sent.length === 1) throw new TypeError('Network request failed');
          return { id: command.id, status: 'applied', version: command.expectedVersion + 1 };
        },
      },
      () => clock,
      () => 0,
    );
    await expect(engine.run()).rejects.toThrow('Network request failed');
    expect(sent).toEqual(['first']);
    expect((await store.snapshot()).operations[0]?.state).toBe('retry');
    clock += 30_000;
    await engine.run();
    expect(sent).toEqual(['first', 'first', 'second']);
    expect((await store.snapshot()).operations.every((item) => item.state === 'acknowledged')).toBe(
      true,
    );
  });

  it('holds a conflicting tail while an independent order proceeds, and resolves explicitly', async () => {
    const { store } = open();
    await seed(store, [order(), order('b')]);
    await store.enqueue('first', 'a', 1, { kind: 'start' });
    await store.enqueue('second', 'a', 2, { kind: 'pause' });
    await store.enqueue('other', 'b', 1, { kind: 'start' });
    const sent: string[] = [];
    await new SyncEngine(
      store,
      {
        pull: async () => ({
          cursor: 'cursor-2',
          hasMore: false,
          serverTime: new Date(time).toISOString(),
          orders: [],
        }),
        push: async (command) => {
          sent.push(command.id);
          return command.orderId === 'a'
            ? {
                id: command.id,
                status: 'conflict',
                code: 'work_order_version_conflict',
                version: 2,
              }
            : { id: command.id, status: 'applied', version: 2 };
        },
      },
      now,
    ).run();
    expect(sent).toEqual(['first', 'other']);
    expect((await store.snapshot()).operations.find((item) => item.id === 'first')?.state).toBe(
      'conflict',
    );
    await expect(store.resolve('first', 'replacement', 1)).rejects.toThrow('latest server version');
    await store.applyPage({
      orders: [order('a', 2)],
      cursor: 'latest',
      hasMore: false,
      serverTime: new Date(time).toISOString(),
    });
    await store.resolve('first', 'replacement', 2);
    expect(
      (await store.snapshot()).operations.find((item) => item.id === 'replacement')?.command
        .expectedVersion,
    ).toBe(2);
    expect((await store.snapshot()).operations.find((item) => item.id === 'second')?.state).toBe(
      'conflict',
    );
  });

  it('purges assignment data and orphaned commands after an authorization resnapshot', async () => {
    const { store } = open();
    await seed(store, [order(), order('b')]);
    await store.enqueue('private', 'a', 1, { kind: 'note', body: 'Private draft' });
    let reset = false;
    await new SyncEngine(
      store,
      {
        pull: async (cursor) => {
          if (cursor) throw new ApiError(409, 'sync_resnapshot_required', 'Access changed');
          reset = true;
          expect((await store.snapshot()).orders).toEqual([]);
          return {
            cursor: 'new-scope',
            hasMore: false,
            serverTime: new Date(time).toISOString(),
            orders: [order('b')],
          };
        },
        push: async () => {
          throw new Error('Unauthorized operation must not be sent');
        },
      },
      now,
    ).run();
    const snapshot = await store.snapshot();
    expect(reset).toBe(true);
    expect(snapshot.orders.map(({ order: row }) => row.id)).toEqual(['b']);
    expect(snapshot.operations).toEqual([]);
    expect(snapshot.removed).toBe(1);
  });
});
