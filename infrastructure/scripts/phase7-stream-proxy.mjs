import { readFileSync } from "node:fs";
import http from "node:http";

// Run only on the isolated Compose network. Preserve the signed Host and URI when forwarding to MinIO.
http
  .createServer((request, response) => {
    const mode = readFileSync("/tmp/fieldops-stream-mode", "utf8").trim();
    const fault =
      request.method === "GET" &&
      request.url.startsWith("/fieldops-private/") &&
      mode !== "normal";
    const started = performance.now();
    let timer;
    let source;
    const forward = http.request(
      {
        hostname: "minio",
        port: 9000,
        path: request.url,
        method: request.method,
        headers: request.headers,
      },
      (upstream) => {
        source = upstream;
        upstream.on("error", () => response.destroy());
        const headers = () =>
          response.writeHead(upstream.statusCode, upstream.headers);
        if (!fault) {
          headers();
          upstream.pipe(response);
          return;
        }
        if (mode === "body") {
          const firstChunk = () => {
            const first = upstream.read(8);
            if (!first) return;
            upstream.removeListener("readable", firstChunk);
            headers();
            response.flushHeaders();
            response.write(first);
          };
          upstream.on("readable", firstChunk);
        }
        timer = setTimeout(() => {
          if (!response.headersSent) headers();
          upstream.pipe(response);
        }, 11000);
      },
    );
    response.on("close", () => {
      clearTimeout(timer);
      source?.destroy();
      forward.destroy();
      if (fault)
        console.log(
          JSON.stringify({
            mode,
            completed: response.writableFinished,
            elapsedMs: Math.round(performance.now() - started),
          }),
        );
    });
    forward.on("error", () => {
      if (response.headersSent) response.destroy();
      else response.writeHead(503).end();
    });
    request.pipe(forward);
  })
  .listen(9082);
