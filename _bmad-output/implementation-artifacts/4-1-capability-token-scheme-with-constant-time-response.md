# Story 4.1: Capability-token scheme with constant-time response

Status: ready-for-dev

## Story

As **Wahid**,
I want **128-bit opaque capability tokens stored server-side with constant-time response on unknown/expired/revoked tokens**,
so that **the public statement surface is defensible against token-existence-oracle timing attacks** (AR-016, §2.9 adversarial baseline).

**Covers requirements:** Epic 4 — FR41 (≥128-bit entropy), NFR-S4 (capability-token minimum entropy), NFR-S8 (no internal IDs exposed), §2.9 public-surface hardening items 1–3 and 11 (constant-time, no existence oracle, opaque tokens, redacted in logs) [Source: epics.md Epic 4 Story 4.1; architecture.md §2.9, §4.3 D4.6].

**Why this story first:** Every other Epic 4 story consumes `capability_tokens`. The table shape, version byte, the constant-time invariant, and the Serilog/OTel redaction rules must be settled before any endpoint touches a token. This is the public-surface analogue of Story 2.1 — an intentionally compound slice that makes the Day-One adversarial hardening baseline executable [Source: architecture.md §2.9; §4.3 D4.6].

## Acceptance Criteria

### AC1 — `capability_tokens` Postgres table with required shape

**Given** no `capability_tokens` table exists yet in any prior migration
**When** this story lands the EF Core migration `YYYYMMDDHHMMSS_AddCapabilityTokensTable.cs`
**Then** the table has columns exactly:
- `token_hash bytea NOT NULL` — SHA-256 of the token bytes, unique
- `owner_subject_id uuid NOT NULL` — FK to `users.subject_id`
- `scope text NOT NULL` — currently `'statement.read'` only, varchar-enum column check constraint
- `contact_id uuid NULL` — scope-target; nullable because future scopes may widen
- `exp timestamptz NOT NULL` — 30-day default from issuance per NFR-S11
- `revoked_at timestamptz NULL` — null while active
- `issued_by uuid NOT NULL` — `ProcessingActivity` issuer subject
- `version_byte smallint NOT NULL DEFAULT 1` — in-band scheme migration marker
- `created_at timestamptz NOT NULL DEFAULT now()`
**And** an index exists on `token_hash` (primary lookup key)
**And** a partial index exists on `(owner_subject_id) WHERE revoked_at IS NULL AND exp > now()` for rate-limit query in Story 4.9
**And** the table implements `IBitemporal` per ADR-022 — `capability_tokens_history` is created by the same migration [Source: architecture.md §3.5; ADR-022].

### AC2 — Token format + URL encoding

**Given** a token is generated (Story 4.10 owns the crypto; this story owns the scheme)
**When** the token is encoded for the URL
**Then** the token is **16 raw random bytes (128 bits)** encoded as **base64url without padding** — length is exactly **22 characters**
**And** the stored `token_hash` is `SHA-256(raw_bytes)` (never store the plaintext token server-side)
**And** the `version_byte` is carried as the first byte of the raw random material; encoding + hashing include it
**And** the URL shape is `https://statement.faktuboh.com/t/{22char-token}` — no PII in path or query per NFR-S8 [Source: architecture.md §4.3 D4.6; prd.md NFR-S4, NFR-S8].

### AC3 — Constant-time response on unknown/expired/revoked tokens

**Given** a `GET /v1/statements/t/{token}` endpoint stub exists
**When** any token arrives (valid, malformed, unknown, expired, revoked)
**Then** the endpoint **always** performs the full validation work sequence in the same order:
1. Parse token → 22 chars / base64url valid
2. SHA-256 hash the decoded bytes
3. DB lookup by `token_hash`
4. Check `exp > now()` and `revoked_at IS NULL`
5. Touch the owner subject row to constant-cost a key-shred check
**And** every branch returns the same shape/size RFC 9457 Problem Details body for rejection (`{ "type": "https://faktuboh.com/errors/faktuboh.statements.token_invalid", "title": "Invalid or expired statement link", "status": 404, "traceId": "...", "code": "faktuboh.statements.token_invalid" }`) — **never distinguishes unknown from expired from revoked on the wire** [Source: architecture.md §2.9 public-surface #2, §4.3 D4.6].
**And** Story 4.12 owns the statistical timing test that validates this invariant mechanically.

### AC4 — Token never appears in logs, telemetry, or error messages

**Given** `ProcessingActivity.DebtorConfirmation` context is active on this endpoint
**When** any request is processed
**Then** Serilog `Destructure.ByTransforming<CapabilityToken>(t => "[REDACTED]")` is registered globally
**And** OTel span attributes have a denylist applied: any attribute key matching `/token|authorization|cookie/i` is replaced with `"[REDACTED]"` at the span processor layer
**And** `TokenRedactionEnricher` scrubs any URL-shape string matching `/t/[A-Za-z0-9_-]{22}` from log messages
**And** NetArchTest rule `[ConstantTimeResponse]` attribute on the endpoint — any method decorated with this attribute must not call `.ToString()` or `.Dump()` on a variable typed `CapabilityToken`; NetArchTest fails the build on violation
**And** an integration test triggers an exception inside the handler and asserts the token is absent from the captured `problem+json` body, captured Serilog sink, and captured OTel span [Source: architecture.md §2.9 public-surface #1].

### AC5 — `version_byte` enables in-band scheme migration

**Given** the first byte of the raw 16 bytes is reserved as `version_byte = 1` for this scheme
**When** a future scheme v2 is introduced
**Then** the same URL shape accommodates both — the middleware dispatches by `version_byte` to the correct validator
**And** a unit test asserts that a v2-shaped token (version byte = 2) against the v1 validator returns the same constant-time 404 (not a dedicated "unsupported version" error, which would leak a timing branch) [Source: architecture.md §4.3 D4.6].

### AC6 — `history.replaceState` strips token from browser history after first navigation

**Given** `apps/public-statement` loads `/t/{token}`
**When** the Angular router finishes the initial navigation
**Then** the `StatementRouteGuard` (Story 4.5 consumes this) calls `history.replaceState({}, '', '/viewing')` exactly once
**And** the token is removed from `window.location.pathname` before any user-facing DOM settles
**And** the token is captured server-side in a `SessionStorage` key `faktuboh.session.token` scoped to the tab (NOT localStorage — not cross-tab) so subsequent API calls within the same tab can replay it [Source: architecture.md §2.9 public-surface #6; §4.3 D4.6].

### AC7 — Key-shred propagation invalidates tokens atomically

**Given** an owner triggers GDPR erasure (future flow; stubbed here)
**When** the `SubjectKeyDestroyed` domain event fires with `owner_subject_id = X`
**Then** a Wolverine handler `InvalidateCapabilityTokensOnSubjectKeyDestroyed` updates `capability_tokens SET revoked_at = now() WHERE owner_subject_id = X AND revoked_at IS NULL`
**And** the operation is idempotent — running twice produces the same state (already-revoked rows not bumped)
**And** an integration test asserts that post-event, the constant-time endpoint returns the generic 404 for those tokens [Source: architecture.md §4.3 D4.6; ADR-004].

### AC8 — `CapabilityToken` value object

**Given** token construction and validation flow through one type
**When** the `CapabilityToken` struct is defined in `Faktuboh.Domain.CapabilityTokens/`
**Then** it is a `readonly struct` (stack-allocated, cannot be boxed into a logged object)
**And** it has a `private byte[] _raw` backing field (never exposed)
**And** `ToString()` returns `"[REDACTED]"` unconditionally
**And** `ToUrlSegment()` returns the base64url-encoded form (only caller allowed to unwrap)
**And** `ComputeHash()` returns the SHA-256 bytes for DB lookup
**And** a property-based test asserts `CapabilityToken.Parse(token.ToUrlSegment()).ComputeHash().SequenceEqual(token.ComputeHash())` holds across 10k samples [Source: architecture.md §4.3 D4.6; Story 4.10 for generation].

### AC9 — No PII in URLs per NFR-S8

**Given** URL paths and query strings pass through multiple layers (CDN cache, logs, browser history)
**When** the endpoint is called
**Then** the only dynamic URL segment is the token (22 chars)
**And** NetArchTest asserts no route template under `Slices/PublicStatements/` contains `{contactId}`, `{ownerId}`, `{name}`, or `{email}` path parameters
**And** a manual schema-audit CI script scans OpenAPI output for `/t/*` paths and fails the build if any secondary path parameter appears [Source: prd.md NFR-S8; architecture.md §2.9 public-surface #4].

### AC10 — Telemetry `capability_token_issued` emits via AR-047 primitive (no PII)

**Given** AR-047 `ITelemetryEmitter` is wired (Story 0.14)
**When** a token is issued
**Then** `TelemetryEmitter.Emit("capability_token_issued", new CapabilityTokenIssuedTelemetry(opaque_owner_id, contact_id_hash, scope, version_byte, exp_bucket))` fires
**And** `exp_bucket` rounds to the day (`"30d"`, `"7d"`) — exact expiration timestamp is PII-adjacent and omitted
**And** PII-guard (`PiiGuard.AssertNoPii`) verifies no raw token, no raw subject id, no IP address in the payload [Source: Story 0.14; ADR-004].

## Tasks / Subtasks

### Backend — Domain layer

- [ ] **T1 (AC: 2, 8)** — Create `Faktuboh.Domain/CapabilityTokens/CapabilityToken.cs` as a `readonly struct`
  - [ ] Private `byte[] _raw` field (16 bytes)
  - [ ] `ToString()` returns `"[REDACTED]"` unconditionally
  - [ ] `ToUrlSegment()` returns base64url without padding
  - [ ] `static CapabilityToken Parse(string urlSegment)` — rejects non-22-char / non-base64url input with `FormatException`
  - [ ] `ComputeHash()` returns SHA-256 bytes
  - [ ] `VersionByte` property reads `_raw[0]`
- [ ] **T2 (AC: 1, 5, 7)** — Create `CapabilityTokenRecord` aggregate in `Faktuboh.Domain/CapabilityTokens/`
  - [ ] Implements `IBitemporal`
  - [ ] `static CapabilityTokenRecord Issue(Guid ownerSubjectId, string scope, Guid? contactId, DateTimeOffset exp, byte versionByte, byte[] tokenHash)`
  - [ ] `Revoke()` instance method sets `RevokedAt = DateTimeOffset.UtcNow`
  - [ ] Domain event `CapabilityTokenIssuedEvent` in `Faktuboh.Domain.Events.IntegrationEvents/`
  - [ ] Repository interface `ICapabilityTokenRepository` with `FindByHashAsync`, `InsertAsync`, `RevokeForOwnerAsync`

### Backend — Infrastructure layer

- [ ] **T3 (AC: 1)** — Create migration `YYYYMMDDHHMMSS_AddCapabilityTokensTable.cs` via `dotnet ef migrations add`
  - [ ] `capability_tokens` table with exact columns per AC1
  - [ ] `capability_tokens_history` table created (ADR-022 bitemporal)
  - [ ] Unique index on `token_hash`
  - [ ] Partial index `ix_capability_tokens_owner_active` on `(owner_subject_id) WHERE revoked_at IS NULL AND exp > now()`
  - [ ] FK to `users.subject_id` (ON DELETE restrict — erasure flows through key-shred, not row delete)
  - [ ] Check constraint on `scope`: `scope IN ('statement.read')`
- [ ] **T4 (AC: 1)** — Create `CapabilityTokenRepository` in `Faktuboh.Infrastructure/Repositories/`
  - [ ] `FindByHashAsync(byte[] hash)` returns `CapabilityTokenRecord?`
  - [ ] Query is parameterized (no string interpolation)
  - [ ] Tracing: `Activity.Current?.AddTag("db.operation", "capability_token.lookup")`

### Backend — Middleware + redaction

- [ ] **T5 (AC: 4)** — Register Serilog destructure policy in `Faktuboh.Api/Program.cs`
  - [ ] `.Destructure.ByTransforming<CapabilityToken>(_ => "[REDACTED]")`
  - [ ] `.Destructure.ByTransforming<CapabilityTokenRecord>(r => new { r.OwnerSubjectId, r.Scope, r.Exp, TokenHash = "[REDACTED]" })`
- [ ] **T6 (AC: 4)** — Create `OtelTokenRedactionProcessor : BaseProcessor<Activity>` in `Faktuboh.Infrastructure/Observability/`
  - [ ] On `OnStart` + `OnEnd`, iterate span tags; replace values matching `^[A-Za-z0-9_-]{22}$` OR keys matching `/token|authorization|cookie/i` with `"[REDACTED]"`
  - [ ] Register in Aspire ServiceDefaults OTel pipeline
- [ ] **T7 (AC: 4)** — Create `[ConstantTimeResponse]` attribute + NetArchTest rule
  - [ ] Attribute in `Faktuboh.Application/Abstractions/ConstantTimeResponseAttribute.cs`
  - [ ] NetArchTest in `Faktuboh.ArchitectureTests/ConstantTimeResponseTests.cs` asserts any method with this attribute does not call `ILogger.LogXxx` with a `CapabilityToken`-typed argument, does not invoke `.ToString()` on `CapabilityToken`, and does not `throw` an exception whose message embeds the token

### Backend — Constant-time endpoint stub (full endpoint in Story 4.5)

- [ ] **T8 (AC: 3, 5)** — Create `Faktuboh.Api/Slices/PublicStatements/PublicStatements.Endpoints.cs` with stub `GET /v1/statements/t/{token}`
  - [ ] `MapGroup("/v1/statements/t")` with `[AllowAnonymous]` and `ProcessingActivity.DebtorConfirmation`
  - [ ] Endpoint handler always executes the 5-step validation sequence even on early-parse failure (use `Task.WhenAll(dummyWork, realLookup)` to pad equal-time branches)
  - [ ] All rejection branches return identical Problem Details body with code `faktuboh.statements.token_invalid`
  - [ ] Decorated `[ConstantTimeResponse]`
- [ ] **T9 (AC: 3, 5)** — Create `CapabilityTokenValidator` in `Faktuboh.Application/CapabilityTokens/`
  - [ ] `ValidateAsync(CapabilityToken token, CancellationToken ct)` returns `ValidationOutcome.Valid(record)` or `ValidationOutcome.Invalid` (single shape)
  - [ ] Always hashes + always DB-queries + always touches owner row even when lookup misses (constant-work invariant)
  - [ ] Dispatches by `token.VersionByte` — version 1 validator only (throws unreachable for other versions intentionally returning Invalid)

### Backend — Key-shred integration

- [ ] **T10 (AC: 7)** — Create Wolverine handler `InvalidateCapabilityTokensOnSubjectKeyDestroyed` in `Slices/PublicStatements/Projections/`
  - [ ] Consumes `SubjectKeyDestroyed` integration event
  - [ ] Calls `ICapabilityTokenRepository.RevokeForOwnerAsync(ownerSubjectId)`
  - [ ] Idempotent — no-op when all already revoked

### Backend — Error catalog

- [ ] **T11 (AC: 3)** — Register `faktuboh.statements.token_invalid` in `ErrorCatalog`
  - [ ] Status 404, Transloco key `errors.statements.token_invalid`, externally visible, title "Invalid or expired statement link"
  - [ ] **Only one code** for unknown/expired/revoked/malformed — the constant-time invariant forbids distinguishing
  - [ ] NetArchTest ensures no code like `faktuboh.statements.token_expired` or `faktuboh.statements.token_revoked` exists

### Backend — Telemetry

- [ ] **T12 (AC: 10)** — Define `CapabilityTokenIssuedTelemetry` record in `Faktuboh.Application/Telemetry/Events/`
  - [ ] Fields: `OpaqueOwnerId (SHA256 hash)`, `ContactIdHash (SHA256)`, `Scope`, `VersionByte`, `ExpBucket ("30d"|"7d")`
  - [ ] `PiiGuard.AssertNoPii` attribute on the type (Story 0.14 primitive)

### Tests — Backend

- [ ] **T13 (AC: 1, 2, 8)** — Unit tests in `tests/Faktuboh.Domain.Tests/CapabilityTokens/`
  - [ ] `CapabilityTokenTests` — round-trip `Parse(token.ToUrlSegment()).ComputeHash() == token.ComputeHash()` for 10k FsCheck-generated samples
  - [ ] `CapabilityTokenTests.ToString_ReturnsRedacted` — asserts `$"{token}"` never leaks bytes
  - [ ] `CapabilityTokenTests.ParseRejectsMalformed` — 21/23-char, non-base64url, empty → throws `FormatException`
- [ ] **T14 (AC: 3, 4, 5)** — Integration tests in `tests/Faktuboh.Api.Tests/Slices/PublicStatements/`
  - [ ] `ConstantTimeResponseTests` — valid / expired / revoked / unknown / malformed-22-char tokens all return identical `problem+json` body (byte-for-byte equal except `traceId`)
  - [ ] `TokenRedactionTests` — trigger exception in handler, assert captured Serilog sink + OTel span + Problem Details body contain no 22-char base64url substring
  - [ ] `VersionByteDispatchTests` — v2-shaped token against v1 validator returns Invalid (not "unsupported version")
- [ ] **T15 (AC: 4)** — Architecture tests in `tests/Faktuboh.ArchitectureTests/CapabilityTokenTests.cs`
  - [ ] `[ConstantTimeResponse]`-decorated methods cannot invoke `ILogger<>` with `CapabilityToken` argument
  - [ ] No string literal in `Slices/PublicStatements/` matches `faktuboh.statements.token_(expired|revoked|unknown)` (only `token_invalid` allowed)
- [ ] **T16 (AC: 7)** — Integration test `KeyShredRevokesCapabilityTokensTests`
  - [ ] Insert 3 token records for subject X
  - [ ] Publish `SubjectKeyDestroyed` via Wolverine in-process bus
  - [ ] Assert `revoked_at` is non-null on all 3 rows
  - [ ] Publish again — assert rows unchanged (idempotent)

### Frontend — token hand-off (minimal, consumed by 4.5)

- [ ] **T17 (AC: 6)** — Create `apps/public-statement/src/app/core/guards/statement-route.guard.ts`
  - [ ] Extracts `/t/{token}` param from URL
  - [ ] Writes token to `SessionStorage` under key `faktuboh.session.token`
  - [ ] Calls `history.replaceState({}, '', '/viewing')` after session write
  - [ ] Returns `true` to allow `StatementView` route to activate

## Dev Notes

### Critical guardrails (do not violate)

1. **Constant-time invariant.** Every rejection branch returns the same-shape Problem Details body. Never add a code like `faktuboh.statements.token_revoked` — the timing signal is more dangerous than the UX benefit [Source: architecture.md §2.9 public-surface #2].
2. **Token never crosses the logging boundary.** `CapabilityToken.ToString() → "[REDACTED]"` is the first line of defence; Serilog destructure + OTel span redaction + NetArchTest `[ConstantTimeResponse]` are the second/third/fourth. A failure of all four is the silent-decay scenario.
3. **No PII in URLs.** The only dynamic path segment is `{token}`. Adding `{contactId}` or any other param is rejected by the schema-audit CI script. If a caller needs the contact context, it is derivable from the token server-side.
4. **128 bits of entropy. No exceptions.** Story 4.10 owns generation and its property-based entropy test; Story 4.1 owns the scheme. If Story 4.10 uses `Random` instead of `RandomNumberGenerator.Create()`, both fail.
5. **`version_byte` is carried inside the 16 bytes.** It is NOT a separate URL segment. A future v2 must share the same URL shape for bookmarkability (one-way door per D4.6).
6. **Table is bitemporal.** Revocation writes to `capability_tokens_history` via the `IBitemporal` interceptor (ADR-022). Audit trail survives erasure.
7. **`SessionStorage` not `localStorage`.** Token replay scoped to the tab. Cross-tab reuse is a capability-leak risk.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/
│   │       └── PublicStatements/
│   │           ├── PublicStatements.Endpoints.cs            # NEW (stub handler; Story 4.5 fleshes out)
│   │           └── Projections/
│   │               └── InvalidateCapabilityTokensOnSubjectKeyDestroyed.cs  # NEW
│   ├── Faktuboh.Application/
│   │   ├── CapabilityTokens/
│   │   │   ├── CapabilityTokenValidator.cs                  # NEW
│   │   │   └── ValidationOutcome.cs                         # NEW (single-shape Valid/Invalid)
│   │   ├── Abstractions/
│   │   │   └── ConstantTimeResponseAttribute.cs             # NEW
│   │   └── Telemetry/Events/
│   │       └── CapabilityTokenIssuedTelemetry.cs            # NEW
│   ├── Faktuboh.Domain/
│   │   └── CapabilityTokens/
│   │       ├── CapabilityToken.cs                           # NEW (readonly struct)
│   │       ├── CapabilityTokenRecord.cs                     # NEW (aggregate, IBitemporal)
│   │       └── ICapabilityTokenRepository.cs                # NEW
│   ├── Faktuboh.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── Configurations/
│   │   │   │   └── CapabilityTokenConfiguration.cs          # NEW
│   │   │   └── Migrations/
│   │   │       └── YYYYMMDDHHMMSS_AddCapabilityTokensTable.cs  # NEW
│   │   ├── Repositories/
│   │   │   └── CapabilityTokenRepository.cs                 # NEW
│   │   └── Observability/
│   │       └── OtelTokenRedactionProcessor.cs               # NEW
│   └── Faktuboh.Application/Errors/
│       └── ErrorCatalog.cs                                  # MODIFY (+1 code)
└── tests/
    ├── Faktuboh.Domain.Tests/CapabilityTokens/
    │   └── CapabilityTokenTests.cs                          # NEW
    ├── Faktuboh.Api.Tests/Slices/PublicStatements/
    │   ├── ConstantTimeResponseTests.cs                     # NEW
    │   ├── TokenRedactionTests.cs                           # NEW
    │   ├── VersionByteDispatchTests.cs                      # NEW
    │   └── KeyShredRevokesCapabilityTokensTests.cs          # NEW
    └── Faktuboh.ArchitectureTests/
        └── CapabilityTokenTests.cs                          # NEW

frontend/
└── apps/public-statement/src/app/core/guards/
    └── statement-route.guard.ts                             # NEW (minimal; 4.5 consumes)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | Round-trip + redaction assertions | xUnit v3 + FluentAssertions + FsCheck for property tests |
| Integration | Real Postgres via Testcontainers + real Wolverine bus | `WebApplicationFactory<Program>` with `[Trait("Category", "Integration")]` |
| Architecture | `[ConstantTimeResponse]` enforcement + token-code taxonomy | NetArchTest |
| Timing | Deferred to Story 4.12 | Welch's t-test harness |

**Forbidden:** Moq, EF Core in-memory provider, logging a `CapabilityToken` by any path, distinguishing revoked from expired on the wire.

### Project Structure Notes

This story scaffolds the `PublicStatements` vertical slice. Only the minimum — the token validator, the endpoint stub, and the constant-time discipline. Story 4.5 fleshes out the actual statement payload; Story 4.4 wires link generation. The slice boundary holds: no reference to `Slices/Contacts` or `Slices/Debts` domain types in the handler; the `contact_id` column is a raw `uuid` resolved via an integration event pattern in 4.4.

### Architecture compliance — ADRs and sections this slice binds

- **ADR-001** (two build targets) — this slice touches `apps/public-statement`'s route guard only; no owner-workspace code.
- **ADR-022** (application-level bitemporal) — `capability_tokens` opts into `IBitemporal`.
- **ADR-024** (Minimal APIs) — endpoint uses `MapGroup` + `TypedResults`.
- **ADR-004** (compliance posture) — key-shred propagation via `SubjectKeyDestroyed`.
- **§2.9 public-surface items 1, 2, 3, 4, 6, 11** — opaque tokens, constant-time, two-layer rate limit (Story 4.9), no PII in URLs, history strip, token never logged.
- **§4.3 D4.6** — the entire decision this story implements.
- **§5.2.6** error code taxonomy — one code `faktuboh.statements.token_invalid` by design.

### Library/framework requirements

| Layer | Package | Version | Notes |
|---|---|---|---|
| .NET | 10 LTS | | |
| EF Core | 10 | | Per ADR-022 — `IBitemporal` interceptor writes to history |
| Npgsql | latest | | `bytea` column for `token_hash` |
| Serilog | latest | | `Destructure.ByTransforming<CapabilityToken>` |
| OpenTelemetry | latest | | Custom `BaseProcessor<Activity>` for span redaction |
| NetArchTest | latest | | Enforces `[ConstantTimeResponse]` |
| FsCheck | latest | | 10k property-based samples for round-trip |

### Testing requirements

**Test pyramid for this slice:**
- **Unit:** `CapabilityToken` round-trip + `ToString()` redaction — property-based via FsCheck.
- **Integration:** Constant-time response shape (byte-equality across branches); token redaction end-to-end; version-byte dispatch; key-shred revocation.
- **Architecture:** `[ConstantTimeResponse]` attribute enforcement; code taxonomy single-value enforcement.

**Silent-failure landmines to guard:**
1. Adding a second error code for `token_expired` "for UX" — forbidden; NetArchTest catches.
2. Logging `record.Token` (typed correctly) without the Serilog destructure policy registered — integration test catches.
3. `console.log(token)` in frontend during dev — ESLint `no-console` in `apps/public-statement` per Story 4.2 catches.
4. Tab-crossing token reuse via `localStorage` — code review + policy doc.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 0.1 — solution scaffold
- Story 0.4 — RFC 9457 middleware + `ErrorCatalog`
- Story 0.6 — `IBitemporal` interceptor
- Story 0.7 — `ProcessingActivity` ambient context
- Story 0.8 — NetArchTest fitness tests (extended here with `[ConstantTimeResponse]`)
- Story 0.10 — Azure infrastructure provisioned (Key Vault Standard, Postgres)
- Story 0.11 — App Insights + traceId correlation
- Story 0.14 — `ITelemetryEmitter` primitive (AR-047)
- Story 0.17 — Cost circuit-breaker baseline (`FAKTUBOH_EMERGENCY_SHUTDOWN` middleware already in pipeline)

**Not yet landed (Story 4.1 tolerates absence):**
- Story 4.10 crypto generator — this story validates the scheme, not generation. If 4.10 hasn't shipped, T13's test fixtures use a deterministic mock RNG; swap to the real generator when 4.10 lands.

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.1
- Capability-token scheme — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.3 D4.6
- Adversarial hardening baseline — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.9 public-surface items 1, 2, 3, 4, 6, 11
- NFR-S4 (token entropy), NFR-S8 (no PII in URLs), NFR-S11 (30-day expiration) — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- ADR-022 bitemporal — [_bmad-output/planning-artifacts/adrs/adr-022-application-level-bitemporal.md](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md)
- ADR-024 Minimal APIs — [_bmad-output/planning-artifacts/adrs/adr-024-minimal-apis-framework.md](../planning-artifacts/adrs/adr-024-minimal-apis-framework.md)
- Story 0.17 emergency-shutdown middleware — [_bmad-output/implementation-artifacts/0-17-cost-circuit-breaker-baseline.md](./0-17-cost-circuit-breaker-baseline.md)
- Story 0.14 telemetry primitive — [_bmad-output/implementation-artifacts/0-14-phase-a-telemetry-primitive.md](./0-14-phase-a-telemetry-primitive.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- This story owns the capability-token scheme shape, not its cryptographic generation (Story 4.10) nor the full endpoint (Story 4.5).
- Expected outcome: `capability_tokens` table + bitemporal history + constant-time endpoint stub + redaction discipline all wired; Stories 4.4 and 4.5 build on the scheme unchanged.

### File List

_Populated during implementation._
