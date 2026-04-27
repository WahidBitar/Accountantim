# Deferred Work

Tracks items deferred from code reviews and other quality gates. Each entry names the source story, the deferral target, and a one-line description of what needs doing.

## Deferred from: code review of story 0-1-backend-solution-scaffold (2026-04-27)

- **R2-D1 → Story 0.4** — Money JSON wire-format converter (`amount` as JSON string per architecture.md §5.2.5). Implement `MoneyJsonConverter` + factory alongside the RFC 9457 + Contracts DTO wiring.
- **R2-D2 → Story 0.4** — ADR-024 central exception-handler registration (`AddExceptionHandler<DomainExceptionHandler>()`, `AddExceptionHandler<ValidationExceptionHandler>()`, `app.UseExceptionHandler()`) when the handler types land.
- **R2-D3 → Story 0.4** — `IDomainException { string Code { get; } }` extraction interface so ProblemDetails middleware can read `Code` without an `as DomainException` cast.
- **R2-D4 → Story 0.6** — `WebApplicationFactory<Program>` DB-config override harness (custom factory subclass) before EF Core + bitemporal interceptor adds `ConnectionStrings:faktuboh`. Without this, every existing integration test will break the moment 0.6 boots.
- **R2-D5 → Story 0.4 + 0.9** — Repair NSwag pipeline: `nswag.json` paths (`../../artifacts/openapi.json`, `../../../frontend/libs/api-contracts/src/generated/`) currently point at non-existent locations; README documents a `--generate-openapi-doc` CLI switch that `Program.cs` does not handle. Wire the actual export step (Story 0.4) and the CI invocation (Story 0.9).
- **R2-D6 → Story 2.1** — `Direction` enum decision: add `Unspecified = 0` and pin explicit numeric values (`Receivable = 1`, `Payable = 2`) to prevent silent default-on-missing-field deserialization and to lock the wire-format integer mapping. Spec line 70 explicitly delegates this to Story 2.1; the Round-2 reviewers confirmed the concern is real.

## Architecture document amendments (non-blocking, scoped follow-up)

Surfaced by Acceptance Auditor §5 in the Round 2 review of Story 0.1; not blocking 0.1 — file as separate doc-corrections.

- architecture.md §6.3.1 + §6.5.3 — AppHost/ServiceDefaults paths should be `backend/aspire/Faktuboh.AppHost/` (matching §3.3 + the implementation), not `backend/src/Faktuboh.AppHost/`.
- architecture.md §6.3.1 — Domain folder layout aspirational view (`Abstractions/`, `Money/`, `Time/`, `Errors/`, `Identity/` subfolders) is the eventual shape; document that `Primitives/` is the Story 0.1 minimum and the granular folders fill in incrementally.
- architecture.md §6.3.1 — add `Faktuboh.Application` and `Faktuboh.Contracts` to the project tree (currently omitted).
- architecture.md §6.3.1 — `ErrorCatalog.cs` location: should be `Faktuboh.Application/Errors/ErrorCatalog.cs` (matches story spec + implementation), not `Faktuboh.Domain/Errors/`.
- architecture.md §6.3.4 — reference graph should include `Application` and `Contracts` projects and their edges (`Application → Domain + Contracts`, `Contracts → Domain`, `Api → Application + Contracts` instead of `Api → Domain` direct).
- architecture.md AC3 reference value (Story 0.1 spec) — update from `version: 10.0.100, rollForward: latestFeature` to the applied D4/4b decision (`version: 10.0.201, rollForward: latestPatch`).
