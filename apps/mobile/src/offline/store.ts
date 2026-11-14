import type { Checklist, Comment, WorkOrder } from '../api/contracts';

export interface SqlConnection {
  execAsync(sql: string): Promise<void>;
  runAsync(sql: string, ...params: (string | number | null)[]): Promise<unknown>;
  getAllAsync<T>(sql: string, ...params: (string | number | null)[]): Promise<T[]>;
}

export type SyncOrder = { order: WorkOrder; checklist: Checklist; notes: Comment[] };
export type PullPage = {
  orders: SyncOrder[];
  cursor: string;
  hasMore: boolean;
  serverTime: string;
};
export type Intent =
  | { kind: 'checklist'; itemId: string; isCompleted: boolean }
  | { kind: 'note'; body: string }
  | { kind: 'start' | 'pause' | 'resume' | 'complete' };
export type Command = Intent & {
  id: string;
  orderId: string;
  expectedVersion: number;
  createdAt: string;
};
export type OperationState =
  'pending' | 'sending' | 'retry' | 'conflict' | 'rejected' | 'acknowledged';
export type QueueRow = {
  sequence: number;
  id: string;
  orderId: string;
  command: Command;
  state: OperationState;
  attempts: number;
  due: number;
  code: string | null;
  version: number | null;
};
export type OfflineSnapshot = {
  orders: SyncOrder[];
  operations: QueueRow[];
  cursor: string | null;
  lastSync: number | null;
  removed: number;
  rebuilding: boolean;
  retryAt: number;
};

const initial: OfflineSnapshot = {
  orders: [],
  operations: [],
  cursor: null,
  lastSync: null,
  removed: 0,
  rebuilding: false,
  retryAt: 0,
};
export const offlineWindow = 24 * 60 * 60 * 1000;
const terminal = new Set(['Completed', 'Cancelled']);

function applyIntent(snapshot: SyncOrder, command: Command): SyncOrder {
  const { order, checklist, notes } = snapshot;
  if (order.version !== command.expectedVersion || terminal.has(order.status))
    throw new Error('The work order changed. Review it in Sync.');
  const version = order.version + 1;
  if (command.kind === 'checklist') {
    if (!checklist.items.some((item) => item.id === command.itemId))
      throw new Error('The checklist item is unavailable.');
    const items = checklist.items.map((item) =>
      item.id === command.itemId
        ? { ...item, isCompleted: command.isCompleted, completedAt: null }
        : item,
    );
    const completed = items.filter((item) => item.isCompleted).length;
    return {
      order: { ...order, version },
      checklist: {
        ...checklist,
        items,
        workOrderVersion: version,
        completedItems: completed,
        progressPercent: Math.floor((completed * 100) / items.length),
        requiredItemsComplete: items.every((item) => !item.isRequired || item.isCompleted),
      },
      notes,
    };
  }
  if (command.kind === 'note') {
    const body = command.body.trim();
    if (!body || body.length > 4000) throw new Error('Enter a note of 1 to 4,000 characters.');
    return {
      order: { ...order, version },
      checklist: { ...checklist, workOrderVersion: version },
      notes: [
        {
          id: command.id,
          body,
          authorDisplayName: 'Local edit',
          visibility: 'TechnicianVisible',
          createdAt: command.createdAt,
        },
        ...notes,
      ],
    };
  }
  const transitions = {
    start: ['Assigned', 'InProgress'],
    pause: ['InProgress', 'OnHold'],
    resume: ['OnHold', 'InProgress'],
    complete: ['InProgress', 'Completed'],
  } as const;
  const [from, to] = transitions[command.kind];
  if (order.status !== from || (command.kind === 'complete' && !checklist.requiredItemsComplete))
    throw new Error('This action is unavailable. Check the order status and required items.');
  return {
    order: { ...order, version, status: to },
    checklist: { ...checklist, workOrderVersion: version },
    notes,
  };
}

function minimized({ order, checklist, notes }: SyncOrder): SyncOrder {
  const {
    id,
    workOrderNumber,
    title,
    priority,
    status,
    plannedStartAt,
    plannedEndAt,
    actualStartAt,
    actualEndAt,
    createdAt,
    updatedAt,
    version,
  } = order;
  return {
    order: {
      id,
      workOrderNumber,
      title,
      priority,
      status,
      plannedStartAt,
      plannedEndAt,
      actualStartAt,
      actualEndAt,
      createdAt,
      updatedAt,
      version,
      description: order.description,
    },
    checklist: {
      workOrderId: id,
      workOrderVersion: version,
      totalItems: checklist.totalItems,
      completedItems: checklist.completedItems,
      progressPercent: checklist.progressPercent,
      requiredItemsComplete: checklist.requiredItemsComplete,
      items: checklist.items.map(
        ({ id: itemId, label, position, isRequired, isCompleted, completedAt }) => ({
          id: itemId,
          label,
          position,
          isRequired,
          isCompleted,
          completedAt,
        }),
      ),
    },
    notes: notes
      .filter((note) => note.visibility === 'TechnicianVisible')
      .slice(0, 10)
      .map(({ id: noteId, body, authorDisplayName, createdAt: at }) => ({
        id: noteId,
        body,
        authorDisplayName,
        createdAt: at,
        visibility: 'TechnicianVisible',
      })),
  };
}

export class OfflineStore {
  private tail: Promise<unknown> = Promise.resolve();
  private closed = false;
  private dispatch<T>(operation: () => Promise<T>): Promise<T> {
    const result = this.tail.then(() => {
      if (this.closed) throw new Error('Offline workspace closed.');
      return operation();
    });
    this.tail = result.catch(() => undefined);
    return result;
  }

  constructor(
    private db: SqlConnection,
    private now: () => number = Date.now,
  ) {}

  async close() {
    this.closed = true;
    await this.tail;
  }

  initialize() {
    return this.transaction(async () => {
      const schema = await this.db.getAllAsync<{ user_version: number }>('PRAGMA user_version');
      if (schema[0]?.user_version === 0)
        await this.db.execAsync(`
        CREATE TABLE offline_orders (id TEXT PRIMARY KEY, value TEXT NOT NULL, version INTEGER NOT NULL);
        CREATE TABLE outbox (sequence INTEGER PRIMARY KEY AUTOINCREMENT, id TEXT UNIQUE NOT NULL,
          orderId TEXT NOT NULL, command TEXT NOT NULL, state TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0,
          due INTEGER NOT NULL DEFAULT 0, code TEXT, version INTEGER);
        CREATE INDEX outbox_order_sequence ON outbox(orderId, sequence);
        CREATE TABLE sync_state (id INTEGER PRIMARY KEY CHECK (id = 1), cursor TEXT, lastSync INTEGER,
          removed INTEGER NOT NULL DEFAULT 0, rebuilding INTEGER NOT NULL DEFAULT 0,
          retryAt INTEGER NOT NULL DEFAULT 0);
        INSERT INTO sync_state(id) VALUES(1);
        PRAGMA user_version = 1;
      `);
      else if (schema[0]?.user_version !== 1)
        throw new Error('Unsupported offline storage version.');
      await this.db.runAsync("UPDATE outbox SET state = 'retry' WHERE state = 'sending'");
    });
  }

  private transaction<T>(operation: () => Promise<T>) {
    return this.dispatch(async () => {
      await this.db.execAsync('BEGIN IMMEDIATE');
      try {
        const result = await operation();
        await this.db.execAsync('COMMIT');
        return result;
      } catch (error) {
        await this.db.execAsync('ROLLBACK');
        throw error;
      }
    });
  }

  private async read(): Promise<OfflineSnapshot> {
    const [state] = await this.db.getAllAsync<
      Omit<OfflineSnapshot, 'orders' | 'operations' | 'rebuilding'> & { rebuilding: number }
    >('SELECT cursor, lastSync, removed, rebuilding, retryAt FROM sync_state');
    const rows = await this.db.getAllAsync<{ value: string }>(
      'SELECT value FROM offline_orders ORDER BY id',
    );
    const queued = await this.db.getAllAsync<Omit<QueueRow, 'command'> & { command: string }>(
      'SELECT * FROM outbox ORDER BY sequence',
    );
    const operations = queued.map((row) => ({
      ...row,
      command: JSON.parse(row.command) as Command,
    }));
    if (!state) return initial;
    const orders = rows.map(({ value }) => {
      let snapshot = JSON.parse(value) as SyncOrder;
      for (const row of operations.filter((item) => item.orderId === snapshot.order.id)) {
        if (row.state === 'acknowledged' && (row.version ?? 0) <= snapshot.order.version) continue;
        if (row.state === 'conflict' || row.state === 'rejected') break;
        try {
          snapshot = applyIntent(snapshot, row.command);
        } catch {
          break;
        }
      }
      return snapshot;
    });
    return {
      ...state,
      rebuilding: !!state.rebuilding,
      operations,
      orders: state.rebuilding ? [] : orders,
    };
  }

  snapshot() {
    return this.dispatch(() => this.read());
  }

  deferSync(until: number) {
    return this.dispatch(() =>
      this.db.runAsync('UPDATE sync_state SET retryAt = max(retryAt, ?) WHERE id = 1', until),
    );
  }

  applyPage(page: PullPage) {
    return this.transaction(async () => {
      const stamp = Date.parse(page.serverTime);
      if (!page.cursor || !Number.isFinite(stamp) || Math.abs(this.now() - stamp) > 5 * 60_000)
        throw new Error('The server time or sync cursor could not be verified.');
      for (const item of page.orders) {
        if (
          item.order.version !== item.checklist.workOrderVersion ||
          item.order.id !== item.checklist.workOrderId
        )
          throw new Error('The server supplied an inconsistent work order.');
        const cached = minimized(item);
        await this.db.runAsync(
          `
          INSERT INTO offline_orders(id, value, version) VALUES (?, ?, ?)
          ON CONFLICT(id) DO UPDATE SET value=excluded.value, version=excluded.version
          WHERE excluded.version >= offline_orders.version
        `,
          cached.order.id,
          JSON.stringify(cached),
          cached.order.version,
        );
      }
      await this.db.runAsync(`
        DELETE FROM outbox WHERE state = 'acknowledged' AND EXISTS (
          SELECT 1 FROM offline_orders WHERE id = outbox.orderId AND version >= outbox.version
        )
      `);
      if (!page.hasMore) {
        const missing = await this.db.getAllAsync<{ count: number }>(
          'SELECT count(*) AS count FROM outbox WHERE orderId NOT IN (SELECT id FROM offline_orders)',
        );
        await this.db.runAsync(
          'DELETE FROM outbox WHERE orderId NOT IN (SELECT id FROM offline_orders)',
        );
        await this.db.runAsync(
          'UPDATE sync_state SET removed = removed + ?, rebuilding = 0, lastSync = ? WHERE id = 1',
          missing[0]?.count ?? 0,
          Math.min(stamp, this.now()),
        );
      }
      await this.db.runAsync('UPDATE sync_state SET cursor = ? WHERE id = 1', page.cursor);
    });
  }

  resnapshot() {
    return this.transaction(async () => {
      await this.db.execAsync(
        'DELETE FROM offline_orders; UPDATE sync_state SET cursor = NULL, lastSync = NULL, rebuilding = 1 WHERE id = 1;',
      );
    });
  }

  purgeOrder(orderId: string) {
    return this.transaction(async () => {
      const rows = await this.db.getAllAsync<{ count: number }>(
        'SELECT count(*) AS count FROM outbox WHERE orderId = ?',
        orderId,
      );
      await this.db.runAsync('DELETE FROM outbox WHERE orderId = ?', orderId);
      await this.db.runAsync('DELETE FROM offline_orders WHERE id = ?', orderId);
      await this.db.runAsync(
        'UPDATE sync_state SET removed = removed + ? WHERE id = 1',
        rows[0]?.count ?? 0,
      );
    });
  }

  enqueue(id: string, orderId: string, expectedVersion: number, intent: Intent) {
    return this.transaction(async () => {
      const current = await this.read();
      if (
        current.lastSync === null ||
        this.now() - current.lastSync >= offlineWindow ||
        this.now() < current.lastSync
      )
        throw new Error('Connect to refresh access before saving offline.');
      if (current.operations.length >= 200)
        throw new Error('Synchronize the pending queue before adding more edits.');
      if (
        current.operations.some(
          (row) =>
            row.orderId === orderId && (row.state === 'conflict' || row.state === 'rejected'),
        )
      )
        throw new Error('Resolve the previous operation in Sync.');
      const target = current.orders.find((item) => item.order.id === orderId);
      if (!target) throw new Error('Download this work order before editing offline.');
      const command = {
        ...intent,
        id,
        orderId,
        expectedVersion,
        createdAt: new Date(this.now()).toISOString(),
      };
      applyIntent(target, command);
      // The visible projection is derived from the same committed outbox rows.
      await this.db.runAsync(
        "INSERT INTO outbox(id, orderId, command, state) VALUES (?, ?, ?, 'pending')",
        id,
        orderId,
        JSON.stringify(command),
      );
    });
  }

  mark(
    id: string,
    state: OperationState,
    attempts: number,
    due = 0,
    code: string | null = null,
    version: number | null = null,
  ) {
    return this.transaction(() =>
      this.db.runAsync(
        'UPDATE outbox SET state = ?, attempts = ?, due = ?, code = ?, version = ? WHERE id = ?',
        state,
        attempts,
        due,
        code,
        version,
        id,
      ),
    );
  }

  discard(id: string) {
    return this.transaction(async () => {
      const row = (await this.read()).operations.find((item) => item.id === id);
      if (!row || !['conflict', 'rejected'].includes(row.state))
        throw new Error('Select an operation that needs review.');
      await this.db.runAsync(
        'DELETE FROM outbox WHERE orderId = ? AND sequence >= ?',
        row.orderId,
        row.sequence,
      );
    });
  }

  resolve(id: string, replacement: string, reviewedVersion: number) {
    return this.transaction(async () => {
      const snapshot = await this.read();
      const row = snapshot.operations.find((item) => item.id === id);
      if (
        !row ||
        row.state !== 'conflict' ||
        !['work_order_version_conflict', 'sync_predecessor_review'].includes(row.code ?? '')
      )
        throw new Error('This operation cannot be reapplied.');
      if (
        snapshot.operations.some(
          (item) =>
            item.orderId === row.orderId &&
            item.sequence < row.sequence &&
            item.state !== 'acknowledged',
        )
      )
        throw new Error('Resolve the earlier operation first.');
      const target = snapshot.orders.find((item) => item.order.id === row.orderId);
      if (
        !target ||
        target.order.version !== reviewedVersion ||
        reviewedVersion < (row.version ?? 0)
      )
        throw new Error('Review the latest server version first.');
      const command = {
        ...row.command,
        id: replacement,
        expectedVersion: reviewedVersion,
        createdAt: new Date(this.now()).toISOString(),
      };
      applyIntent(target, command);
      await this.db.runAsync(
        "UPDATE outbox SET id = ?, command = ?, state = 'pending', attempts = 0, due = 0, code = NULL, version = NULL WHERE id = ?",
        replacement,
        JSON.stringify(command),
        id,
      );
      await this.db.runAsync(
        "UPDATE outbox SET state = 'conflict', code = 'sync_predecessor_review' WHERE orderId = ? AND sequence > ? AND state <> 'acknowledged'",
        row.orderId,
        row.sequence,
      );
    });
  }
}
