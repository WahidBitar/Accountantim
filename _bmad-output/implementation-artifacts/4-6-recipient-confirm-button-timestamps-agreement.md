# Story 4.6: Recipient "Confirm" button timestamps agreement

Status: ready-for-dev

## Story

As a **recipient who verified the statement matches my memory**,
I want to **tap "Confirm" to timestamp agreement for both parties**,
so that **we have a mutually-acknowledged record without an awkward conversation** (FR36).

**Covers requirements:** Epic 4 — FR36, §2.9 public-surface hardening item #3 (CSRF double-submit, frame-ancestors 'none'), UX-DR12 ReconciliationMeta state, delegated decision "passive next-session confirmation display" (SignalR deferred to Epic 7) [Source: epics.md Epic 4 Story 4.6].

**Why this story now:** StatementView renders (Story 4.5). This story adds the single state-mutating interaction the public surface supports — the Confirm button. It's append-only, idempotent, and writes the moment both parties see the statement as agreed.

## Acceptance Criteria

### AC1 — `POST /v1/statements/t/{token}/confirm` records confirmation

**Given** a valid statement view is loaded and the recipient wants to confirm
**When** the POST arrives with body `{}` + headers `Idempotency-Key: <uuid>` + `X-CSRF-Token: <token>`
**Then** the backend:
1. Validates the token via `CapabilityTokenValidator` (constant-time)
2. Validates CSRF double-submit: header `X-CSRF-Token` must equal a cookie `faktuboh_csrf` value (set by the GET response in Story 4.5)
3. Idempotency-Key middleware short-circuits if replay
4. Updates `statements.confirmed_at = now()` if null; NO-OP if already confirmed (idempotent)
5. Appends an audit row to `statement_confirmations` (append-only table)
6. Returns `200 OK` with body `{ confirmedAt: ISO8601, reconciliationMeta: { state: "reconciled", date: ISO8601 } }`
**And** `ProcessingActivity.DebtorConfirmation` is set on the endpoint
**And** the confirmation is append-only — cannot be modified or deleted via API [Source: epics.md Story 4.6; architecture.md §2.9 #3].

### AC2 — `statement_confirmations` table is append-only

**Given** audit integrity is non-negotiable (§2.7 R#5)
**When** migration `YYYYMMDDHHMMSS_AddStatementConfirmationsTable.cs` lands
**Then** table `statement_confirmations` has columns:
- `id uuid PRIMARY KEY DEFAULT gen_random_uuid()`
- `statement_id uuid NOT NULL` (FK to `statements.id`)
- `confirmed_at timestamptz NOT NULL`
- `recipient_ip_hash bytea NOT NULL` — SHA256(ip + daily_salt) per Story 4.9's salt
- `recipient_ua_hash bytea NOT NULL`
- `processing_activity text NOT NULL CHECK (processing_activity = 'DebtorConfirmation')`
- `created_at timestamptz NOT NULL DEFAULT now()`
**And** a Postgres trigger prevents UPDATE + DELETE on this table:

```sql
CREATE OR REPLACE FUNCTION block_statement_confirmation_mutation()
RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'statement_confirmations is append-only';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER prevent_mutation_statement_confirmations
BEFORE UPDATE OR DELETE ON statement_confirmations
FOR EACH ROW EXECUTE FUNCTION block_statement_confirmation_mutation();
```

**And** an integration test attempts UPDATE + DELETE, asserting both fail with the exception message [Source: architecture.md §4.6 D4.20 audit-log seam].

### AC3 — CSRF double-submit via cookie + header

**Given** CSRF defense per §2.9 #3
**When** `GET /v1/statements/t/{token}` responds (Story 4.5 endpoint)
**Then** a `Set-Cookie: faktuboh_csrf=<random-32byte-base64>; Path=/; Secure; HttpOnly; SameSite=Strict` is attached
**And** the GET response body includes `csrfToken` field matching the cookie value
**And** the frontend reads `csrfToken` from the JSON body (cookie is HttpOnly, so the body echo is how the SPA knows the value to set on POST headers)
**And** on POST, the endpoint compares `Request.Cookies["faktuboh_csrf"]` === `Request.Headers["X-CSRF-Token"]`
**And** mismatch returns HTTP 403 with code `faktuboh.statements.csrf_invalid` — NOT the constant-time token_invalid (CSRF is a semantically distinct attack; not a timing oracle on token validity)
**And** a test attempts POST without cookie → 403; without header → 403; with mismatched values → 403 [Source: architecture.md §2.9 #3].

### AC4 — `frame-ancestors 'none'` prevents clickjacking

**Given** the CSP header from Story 4.5 includes `frame-ancestors 'none'`
**When** a malicious page attempts to iframe `statement.faktuboh.com/t/{token}`
**Then** browsers refuse to render the content inside the iframe
**And** an integration test constructs an iframe via HTML test harness and asserts browser rejection [Source: architecture.md §2.9 #3].

### AC5 — Idempotent — double-tap produces same timestamp

**Given** the Confirm button may be tapped twice rapidly (mobile network)
**When** both requests arrive with the same `Idempotency-Key`
**Then** the first completes, writes `confirmed_at` + audit row
**And** the second returns the cached response (D4.11 middleware)
**And** if the second request has a DIFFERENT `Idempotency-Key` but `statements.confirmed_at` is already set, the handler returns 200 with the existing timestamp + NO new audit row (natural-key idempotency: `statement_id` is the natural key)
**And** a concurrent-double-submit integration test asserts both paths [Source: architecture.md §4.4 D4.11].

### AC6 — UI replaces CTA with `ReconciliationMeta` per UX-DR12

**Given** the confirmation succeeds
**When** the state machine transitions to `confirmed`
**Then** the Confirm button is unmounted from the DOM
**And** `ReconciliationMeta` component (from `libs/domain-ui`) renders in its place with state `reconciled` + `timestamp` = returned `confirmedAt`
**And** per UX-DR12 spec: icon (checkmark-shield) + "Confirmed: [relative date]" with Tooltip showing absolute date
**And** an `@defer` block reveals the signup CTA below (Story 4.8 owns) [Source: ux-design-specification.md Component 8 ReconciliationMeta; Component 9 StatementView states].

### AC7 — Telemetry `statement_confirmed` emits with PII-safe schema

**Given** AR-047 primitive exists
**When** the confirmation succeeds
**Then** `TelemetryEmitter.Emit("statement_confirmed", new StatementConfirmedEvent { StatementId, ConfirmedAt, IpHash, UaHash, Channel = "debtor" })` fires
**And** `PiiGuard` validates no PII
**And** integration test `AssertEventEmitted("statement_confirmed")` passes [Source: Story 0.14].

### AC8 — Owner gets NO real-time notification at MVP

**Given** SignalR is deferred to Epic 7 per delegated decisions
**When** the recipient confirms
**Then** no SignalR event, no push notification, no email, no webhook fires to the owner
**And** owner sees the confirmation state on next session load per Story 4.7 (passive display)
**And** no code in this story references SignalR, hub, push, or real-time
**And** NetArchTest asserts no `Microsoft.AspNetCore.SignalR` reference exists under `Slices/PublicStatements/` [Source: delegated decisions — SignalR deferred to Epic 7; epics.md Story 4.6 AC7].

### AC9 — RFC 9457 errors for confirm endpoint

**Given** several failures are possible
**When** errors arise
**Then** registered codes in `ErrorCatalog`:
- `faktuboh.statements.csrf_invalid` (403)
- `faktuboh.statements.token_invalid` (404) — reused from Story 4.1 (constant-time)
- `faktuboh.statements.confirmation_rate_limit_exceeded` (429) — per-token confirm limit: 5 attempts per token per minute (prevents script-driven spam)

### AC10 — Post-confirm GET returns `reconciliationMeta.state = reconciled`

**Given** after confirmation, the recipient refreshes the page
**When** `GET /v1/statements/t/{token}` re-fires
**Then** the response payload's `reconciliationMeta` field is `{ state: "reconciled", date: confirmedAt }` (not `pending`)
**And** the Confirm button renders in `confirmed` state immediately (no re-confirmation prompt)
**And** the cache-control header means this response may be served from CDN cache up to 24h old; refresh after edit invalidates per Story 4.11 AC2 [Source: ux-design-specification.md Component 9 States].

## Tasks / Subtasks

### Backend — Endpoint

- [ ] **T1 (AC: 1, 5)** — Create `POST /v1/statements/t/{token}/confirm` in `Slices/PublicStatements/Confirm/`
  - [ ] Handler: `ConfirmStatementHandler`
  - [ ] `MapPost` inside existing `MapGroup("/v1/statements/t")` with `[AllowAnonymous]`
  - [ ] Endpoint filter sets `ProcessingActivity.DebtorConfirmation`
  - [ ] Validates token via shared `CapabilityTokenValidator`
- [ ] **T2 (AC: 3)** — CSRF validation
  - [ ] Middleware or endpoint filter: compare `Request.Cookies["faktuboh_csrf"]` to `Request.Headers["X-CSRF-Token"]`
  - [ ] On mismatch: return problem+json 403 with code `faktuboh.statements.csrf_invalid`
  - [ ] Extend Story 4.5's GET handler: generate CSRF token, set cookie, echo in body

### Backend — Domain layer

- [ ] **T3 (AC: 1, 5)** — Extend `Statement` aggregate with `MarkConfirmed(DateTimeOffset at)`
  - [ ] Sets `ConfirmedAt` only if currently null
  - [ ] Emits `StatementConfirmedEvent` (new integration event) via domain event list
- [ ] **T4 (AC: 2)** — Create `StatementConfirmation` aggregate in `Faktuboh.Domain/StatementConfirmations/`
  - [ ] Fields matching table schema
  - [ ] Static factory `Record(Guid statementId, DateTimeOffset at, byte[] ipHash, byte[] uaHash)`
  - [ ] `IStatementConfirmationRepository` with `AddAsync` only (no Update/Delete)

### Backend — Infrastructure layer

- [ ] **T5 (AC: 2)** — Migration `YYYYMMDDHHMMSS_AddStatementConfirmationsTable.cs`
  - [ ] Table schema per AC2
  - [ ] Postgres function + trigger blocking UPDATE/DELETE
  - [ ] FK to `statements.id` (ON DELETE RESTRICT — can't delete confirmed statement)

### Backend — Telemetry

- [ ] **T6 (AC: 7)** — Define `StatementConfirmedEvent` (telemetry) in `Faktuboh.Application/Telemetry/Events/`

### Backend — Error catalog

- [ ] **T7 (AC: 9)** — Register codes in `ErrorCatalog`
  - [ ] `faktuboh.statements.csrf_invalid` (403)
  - [ ] `faktuboh.statements.confirmation_rate_limit_exceeded` (429)

### Backend — Rate limit

- [ ] **T8 (AC: 9)** — Add policy `statement-confirm-per-token` to Story 4.9's rate limiter
  - [ ] 5 attempts per token per minute
  - [ ] Partition: token_hash

### Backend — NetArchTest

- [ ] **T9 (AC: 8)** — Architecture test
  - [ ] No `Microsoft.AspNetCore.SignalR` reference in `Slices/PublicStatements/*` or `Slices/Statements/*`

### Frontend — Confirm UI

- [ ] **T10 (AC: 6)** — Extend `statement-view.component.ts`
  - [ ] Add `onConfirm` handler
  - [ ] State machine transition: `viewable → confirming → confirmed`
  - [ ] On confirmed: replace button with `ReconciliationMeta` state `reconciled`
- [ ] **T11 (AC: 1, 3)** — Extend `public-statement.client.ts`
  - [ ] `confirm(opts: { idempotencyKey: string, csrfToken: string }): Observable<ConfirmResponse>`
  - [ ] Attaches `Idempotency-Key` + `X-CSRF-Token` headers
- [ ] **T12 (AC: 3)** — Read `csrfToken` from the GET response body + store in component signal
  - [ ] Cookie is HttpOnly; CSRF token comes via body echo

### Tests — Backend

- [ ] **T13 (AC: 1, 5)** — `ConfirmStatementEndpointTests`
  - [ ] Happy path: POST → 200 + response body with `confirmedAt`
  - [ ] Double-tap idempotency: 2 POSTs same key → 1 execution, 2 identical responses
  - [ ] Natural-key idempotency: 2 POSTs different keys → 2nd returns existing timestamp + no new audit row
- [ ] **T14 (AC: 2)** — `StatementConfirmationsAppendOnlyTests`
  - [ ] Attempt UPDATE → trigger exception
  - [ ] Attempt DELETE → trigger exception
- [ ] **T15 (AC: 3)** — `CsrfValidationTests`
  - [ ] POST without cookie → 403
  - [ ] POST without X-CSRF-Token header → 403
  - [ ] Mismatched values → 403
  - [ ] Valid cookie + header → 200
- [ ] **T16 (AC: 7)** — `AssertEventEmitted("statement_confirmed")`
- [ ] **T17 (AC: 4)** — Integration test: response includes `Content-Security-Policy: frame-ancestors 'none'`
- [ ] **T18 (AC: 10)** — GET after confirm returns `reconciliationMeta.state === "reconciled"`

### Tests — Frontend

- [ ] **T19 (AC: 6)** — Vitest: state machine transitions on confirm; Button unmounts; `ReconciliationMeta` mounts
- [ ] **T20 (AC: 3)** — Vitest: `csrfToken` read from GET body, attached to POST header
- [ ] **T21 (AC: 6)** — Playwright E2E: full flow — view → tap Confirm → see `ReconciliationMeta` reconciled

## Dev Notes

### Critical guardrails

1. **Append-only audit.** `statement_confirmations` has a Postgres trigger, not just an ORM-level constraint. DB-layer enforcement survives application bugs.
2. **Natural-key idempotency + Idempotency-Key both.** Double-tap (same key) is a D4.11 case; different keys on already-confirmed is a natural-key case. Both paths return 200 with the existing timestamp — never a 409 "already confirmed" (confusing UX).
3. **No SignalR.** Story 4.7 is passive display. Epic 7 owns real-time. Any SignalR import in this slice fails NetArchTest.
4. **CSRF error distinguishable from token_invalid.** Unlike token validity, CSRF attack has a clear semantic — 403 is correct, doesn't leak timing info about tokens.
5. **HttpOnly cookie + body echo.** The cookie prevents XSS reading; the body echo is necessary for the SPA to know the CSRF value to send on POST headers. This is a documented double-submit pattern, not an invention.
6. **`frame-ancestors 'none'`** lives in the CSP header from Story 4.5. AC4 just verifies it covers this flow too.
7. **Rate limit on confirm** prevents a bot from hitting confirm 1000x with fake CSRF tokens to probe for a lucky match. 5/min/token is user-generous, bot-restrictive.

### Source tree

```
backend/
├── src/Faktuboh.Api/Slices/PublicStatements/
│   ├── PublicStatements.Endpoints.cs                       # MODIFY (add POST /confirm; GET sets CSRF cookie)
│   └── Confirm/
│       ├── ConfirmStatementHandler.cs                      # NEW
│       ├── ConfirmStatementRequest.cs                      # NEW (empty body + headers)
│       └── ConfirmStatementResponse.cs                     # NEW
├── src/Faktuboh.Application/
│   ├── Errors/ErrorCatalog.cs                              # MODIFY (+2 codes)
│   └── Telemetry/Events/
│       └── StatementConfirmedEvent.cs                      # NEW
├── src/Faktuboh.Domain/Statements/Statement.cs             # MODIFY (MarkConfirmed method)
├── src/Faktuboh.Domain/StatementConfirmations/
│   ├── StatementConfirmation.cs                            # NEW
│   └── IStatementConfirmationRepository.cs                 # NEW
├── src/Faktuboh.Domain/Events/IntegrationEvents/
│   └── StatementConfirmedEvent.cs                          # NEW (domain event, distinct from telemetry)
├── src/Faktuboh.Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/StatementConfirmationConfiguration.cs  # NEW
│   │   └── Migrations/
│   │       └── YYYYMMDDHHMMSS_AddStatementConfirmationsTable.cs  # NEW
│   └── Repositories/StatementConfirmationRepository.cs     # NEW
└── tests/Faktuboh.Api.Tests/Slices/PublicStatements/
    ├── ConfirmStatementEndpointTests.cs                    # NEW
    ├── StatementConfirmationsAppendOnlyTests.cs            # NEW
    ├── CsrfValidationTests.cs                              # NEW
    └── PostConfirmGetTests.cs                              # NEW

frontend/apps/public-statement/src/app/features/statement-view/
├── statement-view.component.ts                             # MODIFY (onConfirm)
├── statement-view.component.spec.ts                        # MODIFY
└── ...

apps/public-statement/src/app/core/api/
└── public-statement.client.ts                              # MODIFY (confirm method)
```

### Architecture compliance

- **FR36** — core delivery.
- **§2.9 #3** (CSRF double-submit + frame-ancestors 'none') — fully wired.
- **§4.4 D4.11** (Idempotency-Key + natural-key) — both layers active.
- **§4.4 D4.9** (RFC 9457) — CSRF code is distinct from token_invalid.
- **UX-DR12** (ReconciliationMeta) — consumed from `libs/domain-ui`.
- **UX-DR27** (silent success) — no toast on confirm; the `ReconciliationMeta` swap IS the feedback.
- **Delegated: SignalR deferred** — asserted via NetArchTest.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| HTTP | Minimal APIs | |
| CSRF | Manual (no package) | Standard double-submit pattern |
| Postgres trigger | Raw SQL in migration | Append-only enforcement |
| Tests | xUnit + Testcontainers | |

### Testing requirements

- **Unit:** `Statement.MarkConfirmed` idempotency; CSRF middleware logic.
- **Integration:** Full flow including trigger-level UPDATE/DELETE rejection.
- **E2E (Playwright):** View → Confirm → ReconciliationMeta visible.
- **Security:** CSRF mismatch cases all rejected.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.4 — RFC 9457
- Story 0.5 — Idempotency-Key middleware
- Story 0.14 — telemetry primitive
- Story 1.9 — Phase 1 component APIs (`ReconciliationMeta`)
- Story 4.1 — capability-token scheme + constant-time validator
- Story 4.4 — `Statement` aggregate + issuance
- Story 4.5 — StatementView component + GET handler (CSRF cookie set here)
- Story 4.9 — rate limit middleware (adds `statement-confirm-per-token` policy)

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.6
- CSRF double-submit — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.9 #3
- Idempotency + natural-key — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.4 D4.11
- UX-DR12 ReconciliationMeta — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) Component 8
- FR36 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Story 4.5 StatementView — [_bmad-output/implementation-artifacts/4-5-public-statementview-renders-without-account.md](./4-5-public-statementview-renders-without-account.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- SignalR is explicitly deferred; Story 4.7 provides passive display for owners.
- Postgres trigger for append-only enforcement is at DB layer, not just ORM — survives bugs above.

### File List

_Populated during implementation._
