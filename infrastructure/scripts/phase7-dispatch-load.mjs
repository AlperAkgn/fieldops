import { execFile } from "node:child_process";
import { setTimeout as delay } from "node:timers/promises";
import { promisify } from "node:util";

const docker = promisify(execFile);
const postgres = "fieldops-phase7-postgres-1";

export async function measureDispatch({ tenant, orders, update, connections }) {
  if (!/^[\da-f-]{36}$/i.test(tenant))
    throw new Error("Expected a synthetic tenant ID.");
  const query = async (sql) =>
    (
      await docker("docker", [
        "exec",
        postgres,
        "sh",
        "-c",
        'exec psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -XAt -v ON_ERROR_STOP=1 -c "$1"',
        "phase7-load",
        sql,
      ])
    ).stdout.trim();
  const queue = async () =>
    (
      await query(`SELECT count(*) FILTER (WHERE "FailedAt" IS NULL AND "DispatchedAt" IS NULL),
    count(*) FILTER (WHERE "DispatchedAt" IS NOT NULL), count(*) FILTER (WHERE "FailedAt" IS NOT NULL),
    coalesce(sum("Attempts"), 0) FROM "WorkOrderEvents" WHERE "TenantId" = '${tenant}'`)
    )
      .split("|")
      .map(Number);
  const database = async () =>
    (
      await query(
        "SELECT xact_commit, blks_read, blks_hit FROM pg_stat_database WHERE datname = current_database()",
      )
    )
      .split("|")
      .map(Number);
  const samples = new Map();
  let duplicates = 0;
  connections.forEach((connection, index) =>
    connection.on("OrderChanged", (event) => {
      const sample = samples.get(`${event.workOrderId}:${event.version}`);
      if (!sample) return;
      if (sample.seen.has(index)) duplicates++;
      sample.seen.add(index);
      if (sample.seen.size === connections.length)
        sample.completed ??= performance.now();
    }),
  );

  async function until(condition, timeout = 40000) {
    const limit = performance.now() + timeout;
    while (performance.now() < limit) {
      if (await condition()) return;
      await delay(50);
    }
    throw new Error(
      "The isolated dispatch workload exceeded its observation window.",
    );
  }

  async function run(name, count, pacer) {
    samples.clear();
    duplicates = 0;
    const before = await queue();
    const dbBefore = await database();
    const start = performance.now();
    const resources = [];
    let sampling = true;
    let peakPending = 0;
    const observe = async () => {
      while (sampling) {
        peakPending = Math.max(peakPending, (await queue())[0]);
        await delay(175);
      }
    };
    const resourceSample = async () => {
      while (sampling) {
        const { stdout } = await docker("docker", [
          "stats",
          "--no-stream",
          "--format",
          "{{json .}}",
          "fieldops-phase7-api-1",
          postgres,
        ]);
        resources.push(
          ...stdout
            .trim()
            .split("\n")
            .map((line) => JSON.parse(line)),
        );
        if (sampling) await delay(1000);
      }
    };
    const observation = Promise.all([observe(), resourceSample()]);
    let productionEnd;
    try {
      for (let index = 0; index < count; index++) {
        if (pacer)
          await delay(Math.max(0, start + index * pacer - performance.now()));
        const order = orders[index % orders.length];
        const sample = { seen: new Set(), start: performance.now() };
        samples.set(`${order.id}:${order.version + 1}`, sample);
        order.version = (await update(order)).version;
        productionEnd = performance.now();
        if (!pacer) {
          await until(() => sample.completed !== undefined);
          if (index + 1 < count) await delay(173);
        }
      }
      await until(() =>
        [...samples.values()].every((sample) => sample.completed !== undefined),
      );
      await until(async () => (await queue())[0] === 0);
    } finally {
      sampling = false;
      await observation;
    }
    const after = await queue();
    const dbAfter = await database();
    const durations = [...samples.values()]
      .map((sample) => sample.completed - sample.start)
      .sort((a, b) => a - b);
    const last = Math.max(
      ...[...samples.values()].map((sample) => sample.completed),
    );
    const round = (number) => Number(number.toFixed(2));
    const percentile = (rank) => round(durations[Math.ceil(rank * count) - 1]);
    const containers = Object.groupBy(resources, (row) => row.Name);
    return {
      name,
      samples: count,
      pacingMs: pacer,
      connections: connections.length,
      peakPending,
      productionPerSecond: round((count * 1000) / (productionEnd - start)),
      dispatchPerSecond: round(
        ((after[1] - before[1]) * 1000) / (last - start),
      ),
      drainAfterProducerMs: round(Math.max(0, last - productionEnd)),
      p50Ms: percentile(0.5),
      p95Ms: percentile(0.95),
      p99Ms: percentile(0.99),
      duplicates,
      failed: after[2] - before[2],
      retries: after[3] - before[3],
      database: Object.fromEntries(
        ["commits", "blocksRead", "blocksHit"].map((name, index) => [
          name,
          dbAfter[index] - dbBefore[index],
        ]),
      ),
      containers: Object.fromEntries(
        Object.entries(containers).map(([name, rows]) => [
          name,
          {
            samples: rows.length,
            meanCpuPercent: round(
              rows.reduce(
                (sum, row) => sum + Number.parseFloat(row.CPUPerc),
                0,
              ) / rows.length,
            ),
            lastMemoryUsage: rows.at(-1).MemUsage,
          },
        ]),
      ),
    };
  }

  await until(async () => (await queue())[0] === 0);
  return [await run("low", 20, 0), await run("burst", 160, 95)];
}
