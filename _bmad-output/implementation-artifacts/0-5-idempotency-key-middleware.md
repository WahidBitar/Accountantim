# Story 0.5: Idempotency-Key middleware

Status: ready-for-dev

## Story

As a **developer**,
I want **every mutation endpoint to respect the `Idempotency-Key` HTTP header with 24h TTL and row-level locking**,
so that **form-resubmission and double-tap scenarios return the cached response instead of creating duplicate records (D4.11)**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. D4.11 — `Idempotency-Key` header middleware + natural-key DB constraint layering. The concurrent-double-submit test is one of Murat's false-confidence landmines (tests that assert publication but not single-execution).

**Why this story now:** Per architecture §4.7 step 6, `Idempotency-Key` lands together with error model + versioning. Within Epic 0 this follows Story 0.4 (error model) — needed because idempotency conflicts emit RFC 9457 error codes. Must ship before Story 2.1 (first mutation endpoint).

## Acceptance Criteria

### AC1 — `idempotency_keys` Postgres table exists

**Given** EF Core migrations are the schema path per D4.3
**When** `dotnet ef migrations add AddIdempotencyKeysTable` runs
**Then** the migration creates a `idempotency_keys` table with columns:
- `key` uuid PRIMARY KEY
- `endpoint` text NOT NULL
- `owner_subject_id` text NOT NULL — from `sub` JWT claim (Story 0.3)
- `request_hash` bytea NOT NULL — SHA-256 of request body (detects key-reuse-with-different-body)
- `response_status` integer NULL — populated after handler success
- `response_body` bytea NULL — populated after handler success (gzipped JSON)
- `response_headers` jsonb NULL — preserved across replay
- `last_seen_at` timestamptz NOT NULL DEFAULT now() — in-flight rowlock marker
- `completed_at` timestamptz NULL — NULL while in-flight, set on success/error completion
- `created_at` timestamptz NOT NULL DEFAULT now()
**And** a unique index `ux_idempotency_keys_owner_endpoint_key` on `(owner_subject_id, endpoint, key)` is created
**And** a btree index `ix_idempotency_keys_last_seen_at` supports TTL sweeping [Source: architecture.md §4 D4.11]

### AC2 — Middleware attaches to POST endpoints via endpoint filter

**Given** mutation endpoints declare idempotency via `.WithMetadata(new RequireIdempotencyKeyAttribute())` (or equivalent endpoint metadata)
**When** a POST request arrives without an `Idempotency-Key` header
**Then** the response is `400 Bad Request` with RFC 9457 body `{ code: "faktuboh.common.idempotency_key_required", ... }`
**And** when the header is present but not a UUID v4, the response is `400` with code `faktuboh.common.idempotency_key_invalid`
**And** the middleware lives at `backend/src/Faktuboh.Api/Infrastructure/Idempotency/IdempotencyEndpointFilter.cs` (as an endpoint filter, not global middleware)
**And** the filter registers via `app.MapPost("/...", ...).AddEndpointFilter<IdempotencyEndpointFilter>()` OR via slice-level `.MapGroup("/v1/contacts").AddEndpointFilter(new IdempotencyEndpointFilter())` [Source: architecture.md §5.3.1 + §6.3.1 Idempotency folder]

### AC3 — Concurrent double-submit → one execution, both return same response

**Given** the middleware is wired for POST endpoints
**When** two concurrent requests submit the same `(owner_subject_id, endpoint, key)` triple
**Then** only one handler execution completes
**And** the second request acquires a row-level lock on the existing `idempotency_keys` row via `SELECT ... FOR UPDATE` + waits
**And** when the handler completes, the lock releases and the second request returns the same cached response body with `X-Returned-Existing: true` header [Source: architecture.md §4 D4.11]
**And** an integration test `ConcurrentIdempotencyTests.cs` issues two parallel `HttpClient.PostAsync` calls + asserts:
- Both responses have identical body bytes
- The handler executes exactly once (verified via `Substitute.Received(1)` on a counting interceptor or a DB row count assertion)
- The second response carries `X-Returned-Existing: true` (and the first does not)

### AC4 — Key-replay-after-success returns cached response with flag

**Given** a POST with key `K` completed successfully 5 seconds ago
**When** a subsequent POST with the same key `K` + same body arrives
**Then** the response is identical (body + status + headers where applicable)
**And** `X-Returned-Existing: true` header is present
**And** the response body envelope includes `"returnedExisting": true` at the top level (per §5.3.1.1 ADR-005 + D4.9) — implemented via `IResultFilter` that injects the property into `application/json` success responses only (not on `application/problem+json` or non-JSON)

### AC5 — Key conflict mid-execution returns 409

**Given** a POST with key `K` is in-flight (handler executing)
**When** a concurrent POST with the same key `K` but a DIFFERENT body hash arrives
**Then** the middleware detects hash mismatch on the existing row
**And** returns `409 Conflict` with RFC 9457 body `{ code: "faktuboh.common.duplicate_idempotency_key", detail: "Idempotency-Key reused with a different request body" }`
**And** the in-flight handler is NOT interrupted (only the new request is rejected)

### AC6 — 24h TTL Wolverine-swept with in-flight-skip

**Given** Wolverine scheduled jobs are the sweep mechanism per D4.11
**When** the hourly sweep job runs
**Then** rows with `completed_at < now() - interval '24 hours'` are deleted
**And** the sweep query `WHERE last_seen_at < now() - interval '5 min'` skips in-flight rows (any row whose `last_seen_at` is within 5 minutes is considered possibly-active — prevents racing with active handlers) [Source: architecture.md §4 D4.11]
**And** the sweep job lives at `backend/src/Faktuboh.Infrastructure/Idempotency/IdempotencyKeyCleanupJob.cs` as a Wolverine scheduled handler
**And** a unit test asserts the sweep query shape via captured SQL

### AC7 — Error codes registered

**Given** Story 0.4 established `ErrorCatalog` + the per-slice `ErrorCatalogEntryAttribute` pattern
**When** this story contributes error codes
**Then** `backend/src/Faktuboh.Application/Errors/CommonErrors.cs` is extended (or created) with:
- `faktuboh.common.idempotency_key_required` (400)
- `faktuboh.common.idempotency_key_invalid` (400)
- `faktuboh.common.duplicate_idempotency_key` (409)
**And** each has a unique `codeId` GUID
**And** `ErrorCatalogTests` (from Story 0.4) passes with the new codes registered

### AC8 — Natural-key backstop documented

**Given** D4.11 specifies two layers (header + natural-key DB constraint)
**When** this story documents the layering
**Then** `docs/runbooks/idempotency.md` documents:
- **Layer 1 (header):** `Idempotency-Key` is the primary guard — always required on POST mutation endpoints
- **Layer 2 (natural key):** domain-specific unique constraints (e.g., `(owner_subject_id, contact_name_lower)` on `contacts`) are the DB-level belt-and-suspenders
- Classification of endpoints:
  - **domain-idempotent** — has a natural key (e.g., debt-confirmation: `(debt_id, debtor_id)`)
  - **domain-creative** — no natural key exists (e.g., new-debt — the request body IS the natural key; header is sole guard)
- Slice designers choose the classification at slice-design time; the header layer is unconditional [Source: architecture.md §4 D4.11]

## Tasks / Subtasks

### T1 — EF Core migration + configuration (AC: 1)

- [ ] Create `backend/src/Faktuboh.Infrastructure/Persistence/Configurations/IdempotencyKeyConfiguration.cs`:
  ```csharp
  public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
  {
      public void Configure(EntityTypeBuilder<IdempotencyKey> b)
      {
          b.ToTable("idempotency_keys");
          b.HasKey(x => x.Key);
          b.Property(x => x.Endpoint).HasColumnType("text").IsRequired();
          b.Property(x => x.OwnerSubjectId).HasColumnType("text").IsRequired();
          b.Property(x => x.RequestHash).HasColumnType("bytea").IsRequired();
          b.Property(x => x.ResponseStatus);
          b.Property(x => x.ResponseBody).HasColumnType("bytea");
          b.Property(x => x.ResponseHeaders).HasColumnType("jsonb");
          b.Property(x => x.LastSeenAt).HasColumnType("timestamptz").HasDefaultValueSql("now()").IsRequired();
          b.Property(x => x.CompletedAt).HasColumnType("timestamptz");
          b.Property(x => x.CreatedAt).HasColumnType("timestamptz").HasDefaultValueSql("now()").IsRequired();
          b.HasIndex(x => new { x.OwnerSubjectId, x.Endpoint, x.Key }).IsUnique();
          b.HasIndex(x => x.LastSeenAt);
      }
  }
  ```
- [ ] Create `backend/src/Faktuboh.Domain/IdempotencyKeys/IdempotencyKey.cs` (simple entity, NOT bitemporal — ephemeral cache)
- [ ] Run `dotnet ef migrations add AddIdempotencyKeysTable --project backend/src/Faktuboh.Infrastructure --startup-project backend/src/Faktuboh.Api`

### T2 — IdempotencyEndpointFilter (AC: 2, 3, 4, 5)

- [ ] Create `backend/src/Faktuboh.Api/Infrastructure/Idempotency/IdempotencyEndpointFilter.cs`:
  - Extracts `Idempotency-Key` header
  - Validates UUID v4 format
  - Computes SHA-256 of request body (reads + rewinds body stream)
  - Opens serializable transaction + `SELECT ... FOR UPDATE` on `idempotency_keys` row
  - If row exists with matching hash + completed → return cached response + `X-Returned-Existing: true` + `returnedExisting: true` envelope
  - If row exists with matching hash + in-flight → wait on lock (handled by SQL)
  - If row exists with different hash → throw `DuplicateIdempotencyKeyException` (mapped to 409)
  - If row doesn't exist → insert (marks in-flight), invoke next filter, capture response, update row with completion
- [ ] Create `backend/src/Faktuboh.Application/Errors/CommonErrors.cs` — three error codes per AC7
- [ ] Create custom exceptions `IdempotencyKeyRequiredException`, `IdempotencyKeyInvalidException`, `DuplicateIdempotencyKeyException` subclasses of `DomainException`

### T3 — `returnedExisting` envelope injection (AC: 4)

- [ ] Create `backend/src/Faktuboh.Api/Infrastructure/Idempotency/ReturnedExistingResultFilter.cs` — injects `"returnedExisting": true` into successful JSON responses when the filter context flag is set
- [ ] Hook the filter into the idempotency filter's replay path

### T4 — Wolverine TTL sweep job (AC: 6)

- [ ] Create `backend/src/Faktuboh.Infrastructure/Idempotency/IdempotencyKeyCleanupJob.cs` — Wolverine scheduled handler (runs every hour) that executes:
  ```sql
  DELETE FROM idempotency_keys
  WHERE completed_at IS NOT NULL
    AND completed_at < now() - interval '24 hours'
    AND last_seen_at < now() - interval '5 minutes';
  ```
- [ ] Register via Wolverine's scheduled-job registration in `Faktuboh.Infrastructure/Messaging/WolverineConfiguration.cs` (stub at this story; full Wolverine wire-up lands with Story 2.1 — document this dependency in Dev Notes)

### T5 — Integration tests (AC: 3, 4, 5)

- [ ] Create `backend/tests/Faktuboh.Api.Tests/Infrastructure/Idempotency/IdempotencyEndpointFilterTests.cs`:
  - Unit tests for header validation + hash computation
- [ ] Create `backend/tests/Faktuboh.Api.Tests/Infrastructure/Idempotency/ConcurrentIdempotencyIntegrationTests.cs`:
  - `[Trait("Category", "Integration")]`
  - Uses `WebApplicationFactory<Program>` + Testcontainers Postgres
  - Maps a test endpoint `/_test/idempotent-echo` that increments an in-memory counter + returns the current value
  - Issues two parallel `HttpClient.PostAsync` with same `Idempotency-Key`
  - Asserts counter = 1 (single execution) + both responses have identical body + second response has `X-Returned-Existing: true`
- [ ] Create `DuplicateKeyDifferentBodyTests.cs` — same-key different-body case returns 409

### T6 — Runbook (AC: 8)

- [ ] Create `docs/runbooks/idempotency.md` covering:
  - Two-layer defense rationale
  - Classification guide (domain-idempotent vs domain-creative)
  - Frontend contract: Idempotency-Key is UUID v4 per render, refreshed on success/reset (per Story 0.14 + D4.14)
  - DB maintenance: TTL sweep every hour; max row count expected <10K at MVP scale

## Dev Notes

### Critical guardrails (do not violate)

1. **Concurrent double-submit MUST be exercised in an integration test** — not mocked, not simulated. Murat's false-confidence landmine #2. [Source: Story 2.1 `dev notes / Murat's landmines`]
2. **Row-level lock via `SELECT ... FOR UPDATE`** — NOT application-level locking. Application locks don't survive process restart; DB locks do. [Source: architecture.md §4 D4.11]
3. **TTL sweep must skip in-flight rows** (`last_seen_at < now() - interval '5 min'`) — otherwise active handlers race against cleanup. [Source: architecture.md §4 D4.11]
4. **Idempotency-Key is UUID v4, not arbitrary string.** Non-UUID keys are rejected with `faktuboh.common.idempotency_key_invalid`. Slack/curl one-liners that use timestamp-strings fail — on purpose.
5. **Hash the full raw body bytes**, not the deserialized payload. JSON key re-ordering would produce a different hash otherwise.
6. **Row-level lock via serializable transaction** — use Postgres `SERIALIZABLE` isolation for the idempotency read+write; lower levels admit phantom reads that allow double-insert.
7. **`X-Returned-Existing` header + `returnedExisting: true` envelope are both emitted** on replay — FE checks the header for non-JSON responses; envelope is for JSON consumers.

### Source tree — files to create or edit

```
backend/src/
├── Faktuboh.Domain/IdempotencyKeys/
│   └── IdempotencyKey.cs                                # NEW
├── Faktuboh.Application/Errors/
│   └── CommonErrors.cs                                  # NEW (3 codes)
├── Faktuboh.Api/Infrastructure/Idempotency/
│   ├── IdempotencyEndpointFilter.cs                     # NEW
│   └── ReturnedExistingResultFilter.cs                  # NEW
├── Faktuboh.Api/Infrastructure/Idempotency/Exceptions/
│   ├── IdempotencyKeyRequiredException.cs               # NEW
│   ├── IdempotencyKeyInvalidException.cs                # NEW
│   └── DuplicateIdempotencyKeyException.cs              # NEW
├── Faktuboh.Infrastructure/Persistence/Configurations/
│   └── IdempotencyKeyConfiguration.cs                   # NEW
├── Faktuboh.Infrastructure/Persistence/Migrations/
│   └── YYYYMMDDHHMMSS_AddIdempotencyKeysTable.cs        # NEW (EF-generated)
└── Faktuboh.Infrastructure/Idempotency/
    └── IdempotencyKeyCleanupJob.cs                      # NEW (Wolverine scheduled)

backend/tests/Faktuboh.Api.Tests/Infrastructure/Idempotency/
├── IdempotencyEndpointFilterTests.cs                    # NEW (unit)
├── ConcurrentIdempotencyIntegrationTests.cs             # NEW (integration)
└── DuplicateKeyDifferentBodyTests.cs                    # NEW (integration)

docs/runbooks/
└── idempotency.md                                       # NEW
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Unit | xUnit v3 | Header validation + hash computation |
| Integration | `WebApplicationFactory<Program>` + Testcontainers Postgres | AC3 + AC4 + AC5 (the critical triple) |
| Contract | Verify | `returnedExisting` envelope shape |

**Forbidden:**
- Mocking the database for idempotency tests — row-level locking behavior IS the test
- `[JsonPropertyName("returnedExisting")]` — envelope naming follows `JsonNamingPolicy.CamelCase` automatically
- Testing only single-request-success — the concurrent case is the load-bearing assertion

### Project Structure Notes

- Wolverine's scheduled-job registration is deferred to Story 2.1 when Wolverine fully wires in; this story creates the `IdempotencyKeyCleanupJob.cs` handler class with the SQL + polling logic but registers it as a placeholder `IHostedService` for now (migrates to Wolverine scheduler in 2.1 without code change to the sweep SQL).
- `idempotency_keys` table is NOT an `IBitemporal` entity — it's ephemeral cache, not a domain aggregate. Story 0.6's interceptor skips it.

### Architecture compliance

- [architecture.md §4 D4.11](../planning-artifacts/architecture.md) — Idempotency-Key + natural-key backstop
- [architecture.md §4 D4.14](../planning-artifacts/architecture.md) — FE `FormMutationConfig` consumes this contract
- [architecture.md §5.3.1](../planning-artifacts/architecture.md) — endpoint filter pattern
- [architecture.md §6.3.1 Idempotency folder](../planning-artifacts/architecture.md) — middleware location

### Library/framework requirements

| Layer | Package | Purpose |
|---|---|---|
| Postgres | Npgsql.EntityFrameworkCore.PostgreSQL | `jsonb`, `bytea`, `timestamptz` |
| Migrations | Microsoft.EntityFrameworkCore.Design | `dotnet ef migrations add` |
| Scheduled jobs | Wolverine (from Story 2.1) | Job scheduler (placeholder hosted service until then) |

### Testing requirements

- **`ConcurrentIdempotencyIntegrationTests.cs` is mandatory.** No story completion without this integration test green.
- **Hash collision is NOT tested** — SHA-256 collisions are cryptographic, not idempotency concerns.
- **Verify-based snapshot test** captures the wire shape of a replay response to detect drift on the `returnedExisting` envelope.

### Previous Story Intelligence

- **Story 0.1 (backend scaffold)** complete: EF Core infrastructure project exists.
- **Story 0.3 (Auth0)** complete: `sub` JWT claim resolves to `owner_subject_id`.
- **Story 0.4 (RFC 9457)** complete: `DomainException` + `ErrorCatalog` register the three new error codes from AC7.
- **Story 0.10 (Azure infra)** complete: Postgres Flex Server exists + migration applies on boot via Aspire MigrationService (Story 0.6 wires MigrationService).
- **Story 0.6 (IBitemporal + MigrationService)** — must ship AFTER this story for migration to actually execute on cold start; this story creates the migration artifact, 0.6 wires MigrationService to run it.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.5 definition
- [_bmad-output/planning-artifacts/architecture.md §4 D4.11](../planning-artifacts/architecture.md) — idempotency decision
- [_bmad-output/planning-artifacts/architecture.md §4 D4.14](../planning-artifacts/architecture.md) — FE counterpart
- [_bmad-output/planning-artifacts/architecture.md §5.3.1](../planning-artifacts/architecture.md) — endpoint filter pattern

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
