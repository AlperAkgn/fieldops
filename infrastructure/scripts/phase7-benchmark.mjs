import { randomUUID } from "node:crypto";
import { execFile, execFileSync } from "node:child_process";
import { availableParallelism, totalmem } from "node:os";
import { setTimeout as delay } from "node:timers/promises";
import { promisify } from "node:util";
import {
  HubConnectionBuilder,
  HttpTransportType,
  LogLevel,
} from "@microsoft/signalr";

// Fixed ports belong to the disposable Phase 7 Compose project documented in operations.md.
const api = "https://localhost:5466/api/v1";
const mail = "http://localhost:8066";
if (process.env.FIELDOPS_PHASE7_BENCHMARK !== "isolated") {
  throw new Error(
    "Start the isolated fieldops-phase7 stack before running this benchmark.",
  );
}
const run = randomUUID();
const password = `Local-Synthetic-${run}!`;
const workerCount = 4;
const results = [];
const image = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=",
  "base64",
);

async function request(
  method,
  path,
  { auth, body, headers, status = 200, binary = false } = {},
) {
  const reply = await fetch(api + path, {
    method,
    redirect: "error",
    signal: AbortSignal.timeout(12000),
    headers: {
      ...(auth && { Authorization: `Bearer ${auth.accessToken}` }),
      ...(body !== undefined &&
        !binary && { "Content-Type": "application/json" }),
      ...headers,
    },
    body: binary ? body : JSON.stringify(body),
  });
  if (reply.status !== status)
    throw new Error(`${method} ${path.split("?")[0]} returned ${reply.status}`);
  if (reply.status === 204 || reply.status === 202) return undefined;
  return reply.headers.get("content-type") === "image/png"
    ? reply.arrayBuffer()
    : reply.json();
}

async function mailbox(email, subject) {
  const end = Date.now() + 20000;
  while (Date.now() < end) {
    const data = await (
      await fetch(`${mail}/api/v1/messages?limit=100`, {
        signal: AbortSignal.timeout(5000),
      })
    ).json();
    const entry = data.messages?.find(
      (item) =>
        item.Subject.includes(subject) &&
        item.To.some((to) => to.Address === email),
    );
    if (entry) {
      const { Text } = await (
        await fetch(`${mail}/api/v1/message/${entry.ID}`, {
          signal: AbortSignal.timeout(5000),
        })
      ).json();
      return {
        userId: /^User ID:\s*(\S+)/m.exec(Text)?.[1],
        token: /^Token:\s*(\S+)/m.exec(Text)?.[1],
      };
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error("Isolated Mailpit delivery did not complete.");
}

async function account(label) {
  const email = `${label}-${run}@fieldops.test`;
  await request("POST", "/auth/register", {
    body: {
      email,
      displayName: label,
      organizationName: `Benchmark ${label} ${run}`,
      timeZoneId: "UTC",
    },
    status: 202,
  });
  await request("POST", "/auth/confirm-email", {
    body: { ...(await mailbox(email, "Confirm")), password },
    status: 204,
  });
  return {
    email,
    tokens: await request("POST", "/auth/login", { body: { email, password } }),
  };
}

async function measure(name, count, operation, concurrency = workerCount) {
  const samples = [];
  let errors = 0;
  let index = 0;
  const wall = performance.now();
  await Promise.all(
    Array.from({ length: concurrency }, async () => {
      while (index < count) {
        const current = index++;
        const start = performance.now();
        try {
          await operation(current);
        } catch (error) {
          errors++;
          if (errors === 1) process.stderr.write(`${name}: ${error.message}\n`);
        }
        samples.push(performance.now() - start);
      }
    }),
  );
  samples.sort((a, b) => a - b);
  const percentile = (rank) =>
    Number(samples[Math.ceil(rank * count) - 1].toFixed(2));
  results.push({
    name,
    count,
    concurrency,
    errors,
    errorRate: errors / count,
    operationsPerSecond: Number(
      ((count * 1000) / (performance.now() - wall)).toFixed(2),
    ),
    p50Ms: percentile(0.5),
    p95Ms: percentile(0.95),
    p99Ms: percentile(0.99),
  });
}

function isolatedContainer(service) {
  const container = `fieldops-phase7-${service}-1`;
  const target = JSON.parse(
    execFileSync("docker", ["inspect", container], { encoding: "utf8" }),
  )[0];
  if (
    target.Config.Labels["com.docker.compose.project"] !== "fieldops-phase7" ||
    target.Config.Labels["com.docker.compose.service"] !== service ||
    !target.State.Running ||
    target.State.Paused
  ) {
    throw new Error(
      `The expected isolated ${service} container is unavailable.`,
    );
  }
  return container;
}

async function waitUntil(check, deadlineMs) {
  const end = Date.now() + deadlineMs;
  while (Date.now() < end) {
    if (await check()) return;
    await delay(100);
  }
  throw new Error("The isolated recovery check timed out.");
}

async function verifyStorageRecovery(restartApi) {
  const container = isolatedContainer("minio");
  const apiContainer = restartApi ? isolatedContainer("api") : null;
  const database = restartApi ? isolatedContainer("postgres") : null;
  const { tokens: auth } = await account("storage-drill");
  const post = (path, body) =>
    request("POST", path, { auth, body, status: 201 });
  const customer = await post("/customers", {
    customerType: "Business",
    name: "Storage drill",
  });
  const location = await post("/service-locations", {
    customerId: customer.id,
    name: "Storage drill",
    addressLine: "20 Test Street",
    city: "Istanbul",
    country: "TR",
  });
  const order = await post("/work-orders", {
    customerId: customer.id,
    serviceLocationId: location.id,
    title: "Storage drill",
    priority: "High",
  });
  const path = `/work-orders/${order.id}/attachments`;
  const upload = (status) =>
    request("POST", path, { auth, status, body: image, binary: true, headers });
  const headers = {
    "Content-Type": "image/png",
    "Idempotency-Key": randomUUID(),
    "X-File-Name": "fixture.png",
  };

  const start = performance.now();
  execFileSync("docker", ["pause", container]);
  try {
    const attemptedUpload = upload(503);
    if (apiContainer && database) {
      // Observe the committed reservation before sending SIGTERM to the API.
      attemptedUpload.catch(() => {});
      const sql = `SELECT EXISTS (SELECT 1 FROM "WorkOrderAttachments" WHERE "OperationId" = '${headers["Idempotency-Key"]}' AND NOT "Ready")`;
      await waitUntil(
        () =>
          execFileSync(
            "docker",
            [
              "exec",
              database,
              "sh",
              "-c",
              'exec psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -XAt -c "$1"',
              "fieldops-drill",
              sql,
            ],
            { encoding: "utf8" },
          ).trim() === "t",
        5000,
      );
      await promisify(execFile)("docker", [
        "restart",
        "--time",
        "30",
        apiContainer,
      ]);
    }
    const error = await attemptedUpload;
    if (error.code !== "object_storage_unavailable")
      throw new Error("Expected a safe storage-unavailable problem.");
    await waitUntil(async () => {
      try {
        return (
          await fetch(new URL("/health/ready", api), {
            signal: AbortSignal.timeout(1000),
          })
        ).ok;
      } catch {
        return false;
      }
    }, 20000);
    if ((await request("GET", path, { auth })).length !== 0)
      throw new Error("Failed upload became visible.");
  } finally {
    execFileSync("docker", ["unpause", container]);
  }
  const unavailableMs = Math.round(performance.now() - start);
  const file = await upload(201);
  if (
    (await upload(201)).id !== file.id ||
    (await request("GET", path, { auth })).length !== 1 ||
    !Buffer.from(await request("GET", `${path}/${file.id}`, { auth })).equals(
      image,
    )
  ) {
    throw new Error(
      "Storage recovery did not yield exactly one downloadable file.",
    );
  }
  console.log(
    JSON.stringify({
      storageUnavailableStatus: 503,
      readinessStatus: 200,
      apiRestartedDuringUpload: restartApi,
      unavailableMs,
      recoveredFiles: 1,
    }),
  );
}

if (process.argv.includes("--storage-recovery")) {
  await verifyStorageRecovery(process.argv.includes("--restart-api"));
  process.exit(0);
}

if (process.argv.includes("--slow-download")) {
  const { verifySlowDownloads } = await import("./phase7-stream-drill.mjs");
  const verified = await verifySlowDownloads({
    api,
    account,
    request,
    fixture: async (auth) => {
      const create = (path, body) =>
        request("POST", path, { auth, body, status: 201 });
      const customer = await create("/customers", {
        customerType: "Business",
        name: "Download drill",
      });
      const location = await create("/service-locations", {
        customerId: customer.id,
        name: "Download drill",
        addressLine: "20 Test Street",
        city: "Istanbul",
        country: "TR",
      });
      const order = await create("/work-orders", {
        customerId: customer.id,
        serviceLocationId: location.id,
        title: "Download drill",
        priority: "High",
      });
      return `/work-orders/${order.id}/attachments`;
    },
  });
  console.log(JSON.stringify(verified, null, 2));
  process.exit(0);
}

const { email, tokens: owner } = await account("owner");
const technician = await account("technician");
const post = (path, body, status = 200) =>
  request("POST", path, { auth: owner, body, status });
await post(
  `/organizations/${owner.tenantId}/invitations`,
  { email: technician.email, role: "Technician" },
  201,
);
await request("POST", "/invitations/accept", {
  auth: technician.tokens,
  body: { token: (await mailbox(technician.email, "invitation")).token },
});
const tech = await request("POST", `/organizations/${owner.tenantId}/select`, {
  auth: technician.tokens,
  body: { refreshToken: technician.tokens.refreshToken },
});
const customer = await post(
  "/customers",
  { customerType: "Business", name: "Benchmark customer" },
  201,
);
const location = await post(
  "/service-locations",
  {
    customerId: customer.id,
    name: "Benchmark worksite",
    addressLine: "20 Test Street",
    city: "Istanbul",
    country: "TR",
  },
  201,
);
const template = await post(
  "/checklist-templates",
  { name: "Benchmark checklist" },
  201,
);
await post(`/checklist-templates/${template.id}/items`, {
  expectedVersion: template.version,
  label: "Verify indicator",
  position: 1,
  isRequired: true,
});

const orders = [];
for (let index = 0; index < 64; index++) {
  const order = await post(
    "/work-orders",
    {
      customerId: customer.id,
      serviceLocationId: location.id,
      title: `Benchmark order ${index}`,
      priority: "High",
    },
    201,
  );
  const path = `/work-orders/${order.id}`;
  if (index >= 16) {
    orders.push({ ...order, path });
    continue;
  }
  const checklist = await post(`${path}/checklist/apply`, {
    templateId: template.id,
    expectedVersion: order.version,
  });
  const start = Date.UTC(2027, 0, 11, 10) + index * 7200000;
  const scheduled = await post(`${path}/schedule`, {
    expectedVersion: checklist.workOrderVersion,
    plannedStartAt: new Date(start),
    plannedEndAt: new Date(start + 3600000),
  });
  const assigned = await post(`${path}/assign`, {
    expectedVersion: scheduled.version,
    technicianUserIds: [tech.userId],
    primaryTechnicianUserId: tech.userId,
  });
  orders.push({ ...assigned, path, item: checklist.items[0].id });
}

if (process.argv.includes("--dispatch-load")) {
  const { measureDispatch } = await import("./phase7-dispatch-load.mjs");
  const connections = [owner, tech].map((token) =>
    new HubConnectionBuilder()
      .withUrl(`${api}/live`, {
        transport: HttpTransportType.WebSockets,
        accessTokenFactory: () => token.accessToken,
      })
      .configureLogging(LogLevel.None)
      .build(),
  );
  try {
    await Promise.all(connections.map((connection) => connection.start()));
    const measured = await measureDispatch({
      tenant: owner.tenantId,
      orders: orders.slice(0, 16),
      connections,
      update: (order) =>
        request("PATCH", order.path, {
          auth: owner,
          body: {
            expectedVersion: order.version,
            description: `Measured update v${order.version + 1}`,
          },
        }),
    });
    console.log(JSON.stringify(measured, null, 2));
  } finally {
    await Promise.all(connections.map((connection) => connection.stop()));
  }
  process.exit(0);
}

await measure("concurrent sign-in", 16, () =>
  request("POST", "/auth/login", {
    body: { email, password, organizationId: owner.tenantId },
  }),
);
await measure("paged filtered list", 64, (index) =>
  request(
    "GET",
    `/work-orders?page=${(index % 4) + 1}&pageSize=16&priority=High`,
    { auth: owner },
  ),
);
await measure("work-order detail", 64, (index) =>
  request("GET", orders[index].path, { auth: owner }),
);
await measure("schedule query", 32, () =>
  request(
    "GET",
    "/work-orders?plannedFrom=2027-01-01T00%3A00%3A00Z&plannedTo=2027-02-01T00%3A00%3A00Z&pageSize=100",
    { auth: owner },
  ),
);
await measure("technician checklist mutation", 16, async (index) => {
  const row = orders[index];
  const changed = await request("PATCH", `${row.path}/checklist/${row.item}`, {
    auth: tech,
    body: { expectedVersion: row.version, isCompleted: true },
  });
  row.version = changed.workOrderVersion;
});
await measure("sync submission", 8, async (index) => {
  const row = orders[index];
  const batch = await request("POST", "/sync/push", {
    auth: tech,
    body: {
      protocol: 1,
      operations: [
        {
          id: randomUUID(),
          orderId: row.id,
          expectedVersion: row.version,
          kind: "note",
          createdAt: new Date(),
          body: "Synthetic check",
        },
      ],
    },
  });
  if (batch.results[0]?.status !== "applied")
    throw new Error("Expected one applied sync operation.");
});
await measure("concurrent version conflict pair", 8, async (index) => {
  const row = orders[index + 16];
  const attempts = await Promise.allSettled(
    [0, 1].map((attempt) =>
      request("PATCH", row.path, {
        auth: owner,
        body: {
          expectedVersion: row.version,
          title: `Synthetic revision ${attempt}`,
        },
      }),
    ),
  );
  if (
    attempts.filter((entry) => entry.status === "fulfilled").length !== 1 ||
    !attempts.some(
      (entry) =>
        entry.status === "rejected" && entry.reason.message.endsWith(" 409"),
    )
  )
    throw new Error("Expected exactly one successful write and one HTTP 409.");
});
await measure("private upload + byte-for-byte download", 8, async (index) => {
  const path = `${orders[index].path}/attachments`;
  const attachment = await request("POST", path, {
    auth: tech,
    body: image,
    binary: true,
    status: 201,
    headers: {
      "Content-Type": "image/png",
      "Idempotency-Key": randomUUID(),
      "X-File-Name": "fixture.png",
    },
  });
  const output = await request("GET", `${path}/${attachment.id}`, {
    auth: tech,
  });
  if (!Buffer.from(output).equals(image))
    throw new Error("Attachment bytes differed.");
});

const connections = [owner, tech].map((token) =>
  new HubConnectionBuilder()
    .withUrl(`${api}/live`, {
      transport: HttpTransportType.WebSockets,
      accessTokenFactory: () => token.accessToken,
    })
    .configureLogging(LogLevel.None)
    .build(),
);
try {
  await Promise.all(connections.map((connection) => connection.start()));
  await measure(
    "SignalR commit to two clients",
    4,
    async (index) => {
      const row = orders[12 + index];
      const handlers = [];
      let timeout;
      const changed = Promise.all(
        connections.map(
          (connection) =>
            new Promise((resolve) => {
              const handler = (event) => {
                if (
                  event.workOrderId === row.id &&
                  event.version === row.version + 1
                )
                  resolve();
              };
              handlers.push([connection, handler]);
              connection.on("OrderChanged", handler);
            }),
        ),
      );
      try {
        await request("PATCH", row.path, {
          auth: owner,
          body: {
            expectedVersion: row.version,
            description: "SignalR fixture change",
          },
        });
        await Promise.race([
          changed,
          new Promise((_, reject) => {
            timeout = setTimeout(
              () => reject(new Error("SignalR event deadline expired.")),
              25000,
            );
          }),
        ]);
      } finally {
        clearTimeout(timeout);
        handlers.forEach(([connection, handler]) =>
          connection.off("OrderChanged", handler),
        );
      }
    },
    1,
  );
} finally {
  await Promise.all(connections.map((connection) => connection.stop()));
}

console.log(
  JSON.stringify(
    {
      clients: workerCount,
      logicalCpus: availableParallelism(),
      memoryBytes: totalmem(),
      records: {
        workOrders: orders.length,
        assigned: 16,
        attachmentBytes: image.length,
      },
      results,
    },
    null,
    2,
  ),
);
if (results.some((row) => row.errors)) process.exitCode = 1;
