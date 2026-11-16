# ADR 0002: Build the local MinIO service from a pinned security release

- Status: Accepted for local development only
- Date: 2026-11-05
- Scope: local infrastructure and isolated development verification

## Context

The product specification requires MinIO for local S3-compatible storage. The [official MinIO repository](https://github.com/minio/minio) is archived. Its [latest security release](https://github.com/minio/minio/releases/tag/RELEASE.2025-10-15T17-29-55Z) is source-only and fixes a service-account/STS privilege-escalation issue. The release instructions require building a container from source. Old public binary images are therefore unsuitable as a current, maintainable dependency choice.

## Decision

Build `infrastructure/docker/Minio.Dockerfile` from upstream commit `9e49d5e7a648f00e26f2246f4dc28e6b07f8c84a`, corresponding to that security release. The source commit and its `go.sum` pin the build input dependencies. Compile with a current Go toolchain and run the binary as a non-root user in a minimal Alpine container. Retain the upstream license in the container. Bind S3 and console ports to loopback and store development objects in a named volume.

Phase 6 reviewed maintained S3-compatible options, including SeaweedFS, and uses the maintained AWS SDK S3 contract behind an application interface. The existing local MinIO stays available for repeatable development and preserves current volumes. Production must supply an existing private bucket on a maintained provider and a least-privilege identity. No hosted provider was provisioned. [AWS S3 SDK](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TS3Config.html) · [SeaweedFS S3](https://github.com/seaweedfs/seaweedfs)

Work-order attachments were introduced in Phase 6. The local bucket is private and the API streams content only after resource authorization. A production provider has yet to be selected and validated for the deployment.

## Consequences

- A clean first build downloads Go modules and compiles MinIO, taking longer than pulling a binary image. Docker build caches reduce subsequent build times.
- The archived upstream remains a development dependency. Production provider selection must consider licensing, security updates, IAM, retention and backups.
- No public bucket, credentials embedded in client bundles, or production-readiness claim is introduced.
- Source: [MinIO AGPL-3.0 license](https://github.com/minio/minio/blob/RELEASE.2025-10-15T17-29-55Z/LICENSE).

