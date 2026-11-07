# FieldOps engineering instructions

## Phase boundary

The user authorizes one phase at a time. Implement **Phase 0 only** in the initial run. Stop after verification and reporting. Do not implement Phase 1 until the user explicitly requests it. This overrides the automatic continuation sentence in the original master prompt and applies to every later phase.

## Resume workflow

Read this file, `docs/development-roadmap.md`, and `docs/implementation-status.md`, then inspect existing code before making changes. Preserve user work. Record verification evidence and limitations honestly.

## Architecture and scope

- Build a modular monolith: `FieldOps.Api`, `FieldOps.Application`, `FieldOps.Domain`, and `FieldOps.Infrastructure`.
- Dependencies point inward. Domain has no framework dependency; Application depends on Domain; Infrastructure implements application interfaces; API is the composition root.
- Phase 0 provides runnable API/database health, an initial infrastructure-only migration, minimal React/Expo foundation screens, Docker, CI, and the complete implementation plan. Identity, organizations, tenant entities, operational APIs, and business dashboards belong to later phases.
- Do not add speculative repositories, CQRS frameworks, Redis, microservices, or unimplemented UI actions.
- Use shared database/schema tenancy. Resolve the tenant from authenticated membership, enforce resource authorization and composite tenant foreign keys, and test isolation against real PostgreSQL.

## Engineering conventions

- English identifiers, UI copy, README, and technical documentation. Keep user-facing progress in the user's language.
- Strict TypeScript, nullable C#, asynchronous I/O, and cancellation propagation.
- Backend enforces business rules and authorization. Never trust a client-supplied tenant ID.
- Keep secrets outside Git. `.env.example` contains local-development examples only. Never log credentials or sensitive payloads.
- Pin dependencies through committed lockfiles; verify compatibility with official documentation. Use Expo-supported native versions.
- Run the supported build, lint/format, typecheck, and relevant tests at each phase boundary. Do not disable checks to hide failures.
- Database tests must use real PostgreSQL; do not report in-memory tests as PostgreSQL verification.
- Maintain `docs/implementation-status.md` with completed tasks, pending tasks, blockers, verification results, and next steps for all nine phases.
- Use Conventional Commits when commits are requested. Do not publish, deploy to external accounts, or create paid resources without authorization.

## Completion report

List created/changed files, architectural decisions, actual build/test results, known limitations, and next-phase work. Stop at the authorized phase boundary.

## Agent Execution Policy

- Use single-agent execution by default.
- Do not spawn or delegate tasks to subagents.
- Do not use multi-agent orchestration.
- Perform implementation and verification sequentially.
- Prioritize efficient context usage and focused testing.
- Only use subagents if the user explicitly authorizes them in a future request.