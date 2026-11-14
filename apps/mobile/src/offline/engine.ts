import { ApiError } from '../api/transport';
import type { Command, OfflineStore, PullPage, QueueRow } from './store';

export type PushResult = {
  id: string;
  status: 'applied' | 'conflict' | 'rejected' | 'blocked';
  version?: number | null;
  code?: string | null;
};
export type SyncApi = {
  pull(cursor: string | null): Promise<PullPage>;
  push(command: Command): Promise<PushResult>;
};
export class SyncEngine {
  private running: Promise<void> | null = null;

  constructor(
    private store: OfflineStore,
    private api: SyncApi,
    private now = Date.now,
    private random = Math.random,
  ) {}

  run(active: () => boolean = () => true, changed: () => Promise<void> = async () => {}) {
    this.running ??= this.drain(active, changed)
      .catch(async (error: unknown) => {
        if (error instanceof ApiError && error.status === 429)
          await this.store.deferSync(this.now() + Math.max(1000, (error.retryAfter ?? 30) * 1000));
        throw error;
      })
      .finally(() => {
        this.running = null;
      });
    return this.running;
  }

  private async pull(active: () => boolean, changed: () => Promise<void>) {
    let { cursor } = await this.store.snapshot();
    let restarted = false;
    for (let page = 0; page < 20 && active(); page++) {
      try {
        const result = await this.api.pull(cursor);
        if (!active()) return false;
        await this.store.applyPage(result);
        await changed();
        if (!result.hasMore) return true;
        cursor = result.cursor;
      } catch (error) {
        if (
          active() &&
          !restarted &&
          error instanceof ApiError &&
          error.code === 'sync_resnapshot_required'
        ) {
          await this.store.resnapshot();
          await changed();
          restarted = true;
          cursor = null;
          continue;
        }
        throw error;
      }
    }
    return false;
  }

  private async drain(active: () => boolean, changed: () => Promise<void>) {
    if ((await this.store.snapshot()).retryAt > this.now()) return;
    if (!(await this.pull(active, changed))) return;
    let acknowledged = false;
    for (let attempt = 0; attempt < 20 && active(); attempt++) {
      const { operations } = await this.store.snapshot();
      const first = new Map<string, QueueRow>();
      for (const row of operations)
        if (row.state !== 'acknowledged' && !first.has(row.orderId)) first.set(row.orderId, row);
      const row = [...first.values()].find(
        (item) => ['pending', 'retry'].includes(item.state) && item.due <= this.now(),
      );
      if (!row) break;
      await this.store.mark(row.id, 'sending', row.attempts + 1);
      await changed();
      try {
        if (!active()) {
          await this.store.mark(row.id, 'retry', row.attempts + 1);
          return;
        }
        const result = await this.api.push(row.command);
        if (!active()) {
          await this.store.mark(row.id, 'retry', row.attempts + 1);
          return;
        }
        if (result.id !== row.id) throw new Error('Invalid synchronization response.');
        if (result.status === 'applied' && result.version === row.command.expectedVersion + 1) {
          await this.store.mark(row.id, 'acknowledged', row.attempts + 1, 0, null, result.version);
          acknowledged = true;
        } else if (result.code === 'work_order_not_found') {
          await this.store.purgeOrder(row.orderId);
        } else if (result.status === 'conflict' || result.status === 'rejected') {
          await this.store.mark(
            row.id,
            result.status,
            row.attempts + 1,
            0,
            result.code ?? 'sync_rejected',
            result.version ?? null,
          );
        } else {
          throw new Error('Invalid synchronization response.');
        }
      } catch (error) {
        const tries = row.attempts + 1;
        if (error instanceof ApiError && [401, 403].includes(error.status)) {
          if (active()) await this.store.mark(row.id, 'retry', tries);
          throw error;
        }
        if (error instanceof ApiError && [400, 404, 413, 422, 426].includes(error.status)) {
          await this.store.mark(row.id, 'rejected', tries, 0, error.code);
          await changed();
          continue;
        }
        const delay =
          Math.min(300_000, 1000 * 2 ** Math.min(tries, 8)) * (0.5 + this.random() * 0.5);
        const retryAfter = error instanceof ApiError ? (error.retryAfter ?? 0) * 1000 : 0;
        await this.store.mark(
          row.id,
          'retry',
          tries,
          this.now() + Math.max(delay, retryAfter),
          'sync_retry_waiting',
        );
        await changed();
        if (error instanceof ApiError && error.status === 429) throw error;
        if (retryAfter > 0) {
          await this.store.deferSync(this.now() + retryAfter);
          throw error;
        }
        if (!(error instanceof ApiError) || error.status === 0) throw error;
      }
      await changed();
    }
    if (acknowledged && active()) await this.pull(active, changed);
  }
}
