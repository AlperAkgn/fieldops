# ADR 0002: Build the local MinIO service from a pinned security release

- Status: Accepted for local development only
- Date: 2026-11-05
- Scope: Phase 0 infrastructure; revisit before Phase 6 attachments

## Context

The product specification requires MinIO for local S3-compatible storage. The [official MinIO repository](https://github.com/minio/minio) is archived. Its [latest security release](https://github.com/minio/minio/releases/tag/RELEASE.2025-10-15T17-29-55Z) is source-only and fixes a service-account/STS privilege-escalation issue. The release instructions require building a container from source. Old public binary images are therefore unsuitable as a current, maintainable dependency choice.

## Decision

Build `infrastructure/docker/Minio.Dockerfile` from upstream commit `9e49d5e7a648f00e26f2246f4dc28e6b07f8c84a`, corresponding to that security release. The source commit and its `go.sum` pin the build input dependencies. Compile with a current Go toolchain and run the binary as a non-root user in a minimal Alpine container. Retain the upstream license in the container. Bind S3 and console ports to loopback and store development objects in a named volume.

The API does not expose upload/download endpoints in Phase 0. The local server only proves that the required infrastructure starts and answers readiness checks. No production storage provider is selected by this decision.

## Consequences

- A clean first build downloads Go modules and compiles MinIO, taking longer than pulling a binary image. Docker build caches reduce subsequent build times.
- An archived upstream and its pinned transitive dependencies require review before attachments are introduced. A newer Go compiler does not repair vulnerabilities in application dependencies.
- Phase 6 must reassess maintained S3-compatible providers and review licensing, security updates, bucket policies, authorization, retention, and backup requirements.
- No public bucket, credentials embedded in client bundles, or production-readiness claim is introduced.
- Source: [MinIO AGPL-3.0 license](https://github.com/minio/minio/blob/RELEASE.2025-10-15T17-29-55Z/LICENSE).

