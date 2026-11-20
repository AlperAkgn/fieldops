import { execFileSync } from "node:child_process";
import { randomUUID } from "node:crypto";
import { setTimeout as delay } from "node:timers/promises";

export async function verifySlowDownloads({ api, account, request, fixture }) {
  const proxy = "fieldops-phase7-stream-proxy";
  const networks = JSON.parse(
    execFileSync(
      "docker",
      ["inspect", "--format", "{{json .NetworkSettings.Networks}}", proxy],
      { encoding: "utf8" },
    ),
  );
  if (!networks.fieldops_phase7_default && !networks["fieldops-phase7_default"])
    throw new Error(
      "The stream fixture must use the isolated Phase 7 network.",
    );
  const mode = (value) =>
    execFileSync("docker", [
      "exec",
      proxy,
      "node",
      "-e",
      'require("node:fs").writeFileSync("/tmp/fieldops-stream-mode",process.argv[1])',
      value,
    ]);
  const { tokens: auth } = await account("download-drill");
  const outside = (await account("download-outsider")).tokens;
  const path = await fixture(auth);
  async function upload(size) {
    const body = Buffer.alloc(size, 17);
    Buffer.from("%PDF-1.7\n").copy(body);
    const attachment = await request("POST", path, {
      auth,
      body,
      binary: true,
      status: 201,
      headers: {
        "Content-Type": "application/pdf",
        "Idempotency-Key": randomUUID(),
        "X-File-Name": "stream-fixture.pdf",
      },
    });
    return { url: `${api}${path}/${attachment.id}`, body };
  }
  const fetchFile = (url, token, signal) =>
    fetch(url, {
      signal: signal ?? AbortSignal.timeout(14000),
      headers: { Authorization: `Bearer ${token.accessToken}` },
    });
  async function verifyFile(file) {
    const response = await fetchFile(file.url, auth);
    if (
      response.status !== 200 ||
      !Buffer.from(await response.arrayBuffer()).equals(file.body)
    ) {
      throw new Error("The completed download did not match the stored file.");
    }
  }
  mode("normal");
  const small = await upload(156);
  const large = await upload(6 * 1024 * 1024);
  await verifyFile(small);
  await verifyFile(large);
  if ((await fetchFile(large.url, outside)).status !== 404)
    throw new Error("Tenant isolation failed.");
  const results = [];
  const observedSince = new Date().toISOString();
  try {
    mode("headers");
    let started = performance.now();
    const unavailable = await fetchFile(large.url, auth);
    const body = await unavailable.json();
    if (
      unavailable.status !== 503 ||
      body.code !== "object_storage_unavailable"
    )
      throw new Error("Expected safe HTTP 503 before headers.");
    results.push({
      case: "provider stalled before headers",
      status: 503,
      elapsedMs: Math.round(performance.now() - started),
    });

    mode("body");
    started = performance.now();
    const partial = await fetchFile(large.url, auth);
    const reader = partial.body.getReader();
    const first = await reader.read();
    if (partial.status !== 200 || first.done || first.value.length !== 8)
      throw new Error("Expected the first eight bytes before the stall.");
    let interrupted = false;
    try {
      await reader.read();
    } catch (error) {
      interrupted = error.name === "TypeError";
    }
    if (!interrupted || performance.now() - started >= 10000)
      throw new Error("The partial response outlived the server deadline.");
    results.push({
      case: "provider stalled after eight bytes",
      status: 200,
      transportInterrupted: true,
      elapsedMs: Math.round(performance.now() - started),
    });

    const client = new AbortController();
    const disconnect = await fetchFile(large.url, auth, client.signal);
    await disconnect.body.getReader().read();
    client.abort();
    results.push({ case: "downstream client disconnected" });
  } finally {
    mode("normal");
  }
  await verifyFile(large);
  let upstreamClosures;
  const deadline = Date.now() + 3000;
  do {
    upstreamClosures = execFileSync(
      "docker",
      ["logs", "--since", observedSince, proxy],
      { encoding: "utf8" },
    )
      .trim()
      .split("\n")
      .filter(Boolean)
      .map((line) => JSON.parse(line));
    if (upstreamClosures.length >= 3) break;
    await delay(100);
  } while (Date.now() < deadline);
  if (
    upstreamClosures.length !== 3 ||
    upstreamClosures.some((closure) => closure.completed) ||
    !upstreamClosures.some(
      (closure) => closure.mode === "body" && closure.elapsedMs < 2000,
    )
  ) {
    throw new Error(
      "The stalled provider streams did not all close, including prompt cancellation on client disconnect.",
    );
  }
  return {
    normalBytes: [small.body.length, large.body.length],
    crossTenantStatus: 404,
    recovered: true,
    results,
    upstreamClosures,
  };
}
