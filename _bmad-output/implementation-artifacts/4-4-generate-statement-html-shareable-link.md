# Story 4.4: Generate statement HTML + shareable link

Status: ready-for-dev

## Story

As a **user**,
I want to **generate a shareable link for any contact's statement with one tap**,
so that **I can send the link via WhatsApp/email/SMS for reconciliation** (FR29, FR34).

**Covers requirements:** Epic 4 — FR29 (statement HTML render), FR34 (generate shareable link), NFR-S11 (30-day default expiration), FR50 link-issuance telemetry [Source: epics.md Epic 4 Story 4.4; prd.md FR29/FR34/NFR-S11].

**Why this story now:** Stories 4.1 + 4.10 delivered the scheme and the generator. This story wires the first caller: the authenticated owner action that issues a token and returns a shareable URL. Until this lands, the public-statement surface has no traffic.

## Acceptance Criteria

### AC1 — `POST /v1/contacts/{contactId}/statements` issues a token

**Given** an authenticated user on a contact's detail page
**When** the user taps "Share statement" and the frontend POSTs to `/v1/contacts/{contactId}/statements` with an `Idempotency-Key` header (UUID v4)
**Then** the backend:
1. Verifies the contact exists and belongs to the authenticated owner (otherwise 404 — never 403, avoids existence oracle on contact IDs)
2. Verifies the contact has ≥ 1 debt (otherwise 409 with code `faktuboh.statements.no_debts_to_share`)
3. Calls `ITokenGenerator.Create(ownerSubjectId, "statement.read", contactId, exp: now() + 30 days)` per NFR-S11
4. Inserts the `CapabilityTokenRecord` via `ICapabilityTokenRepository.InsertAsync`
5. Publishes `StatementLinkIssuedEvent` via Wolverine same-transaction outbox
6. Returns `201 Created` with `GenerateStatementLinkResponse { url: "https://statement.faktuboh.com/t/{token22char}", expiresAt: ISO8601, statementId: GUID }`

**And** the response body's `url` is built from `STATEMENT_BASE_URL` env var (staging vs prod) + token
**And** the token itself never appears in any Serilog log, OTel span, or error message (Story 4.1 redaction holds) [Source: epics.md Story 4.4; architecture.md §4.3 D4.6; prd.md NFR-S11].

### AC2 — `statements` aggregate row records the issuance

**Given** a statement link must be queryable by the owner (Story 4.7 displays reconciliation meta per contact)
**When** the handler runs
**Then** a row inserts into `statements` table with columns `{ id, contact_id, owner_subject_id, capability_token_id, issued_at, expires_at, revoked_at NULL, confirmed_at NULL, created_at }`
**And** `statements` implements `IBitemporal` per ADR-022 (`statements_history` table created by same migration if not yet)
**And** the relationship is `statements.capability_token_id → capability_tokens.id` (ON DELETE CASCADE)
**And** a unique constraint prevents two active (non-revoked, non-expired) statements per `(owner_subject_id, contact_id)` — reissuing returns `returnedExisting: true` with the existing URL per D4.9 [Source: architecture.md §4.3 D4.6; ADR-022].

### AC3 — Share-message template uses culturally-appropriate Arabic default

**Given** UX-DR33 specifies culturally appropriate share templates
**When** the frontend receives the URL
**Then** the frontend calls `navigator.share({ title, text, url })` (native share sheet on mobile) with:
- `text` (Arabic locale): `"شوف كشف حسابنا مع بعض، تأكد معي من الأرقام"`
- `text` (English locale): `"Take a look at our statement — please confirm the numbers with me"`
- `url` = response URL
**And** desktop fallback: copy-to-clipboard action using the native Clipboard API, with a PrimeNG Toast confirming "Link copied"
**And** the share message is a Transloco key `share.statement.message.default` so it can be A/B tested later [Source: ux-design-specification.md UX-DR33; epics.md Story 4.4].

### AC4 — Statement HTML is server-generated (rich PDF deferred)

**Given** FR29 specifies statement HTML rendering at MVP
**When** a recipient later loads `GET /v1/statements/t/{token}` (Story 4.5 consumes)
**Then** the backend renders statement payload as JSON (Story 4.5 hydrates in Angular)
**And** the endpoint additionally supports `Accept: text/html` — returns a server-generated static HTML snapshot if the client is a bot/preview surface (User-Agent matches common preview-bot strings like `WhatsApp`, `facebookexternalhit`, `Twitterbot`) — but this bot path returns ONLY a generic "Statement — tap to view" landing page (no statement content) to prevent preview-bot token consumption per §2.9 public-surface item #10
**And** the JSON response schema is documented in OpenAPI under `StatementPayload` with fields `{ lender: { name, avatarColor }, recipient: { name }, transactions: [{ id, date, amount, currency, direction }], balances: [{ currency, amount }], issuedAt, reconciliationMeta?: { confirmedAt } }`
**And** no PII beyond the displayed names appears in the response [Source: epics.md Story 4.4; architecture.md §2.9 item #10; delegated decisions — MVP = HTML-rendered only].

### AC5 — Link reissuance returns same URL via idempotency

**Given** the user taps "Share" twice in quick succession
**When** both requests arrive with the same `Idempotency-Key`
**Then** D4.11 middleware row-locks on `idempotency_keys`, only one handler runs, the second returns the cached response
**And** if the second request arrives with a DIFFERENT `Idempotency-Key` but an active statement already exists for `(owner, contact)`, the handler returns the existing URL with `returnedExisting: true` + original `expiresAt`
**And** a concurrent-double-submit integration test asserts both scenarios [Source: architecture.md §4.4 D4.11].

### AC6 — Owner-side "revoke" is deferred but kill-switch exists

**Given** FR40 owner-side link management is deferred to Epic 7 per delegated decisions
**When** Phase A needs emergency revocation
**Then** NO owner UI ships in Epic 4
**And** the `capability_tokens.revoked_at` column is set manually via a documented DB operation runbook at `docs/ops/revoke-capability-token.md`
**And** the runbook shows: SQL query to find the token by `contact_id`, UPDATE SQL with `revoked_at = now()`, AFD cache purge command
**And** the revocation path is covered by a test in Story 4.1's key-shred test (row-revoke → endpoint returns constant-time 404) [Source: delegated decisions — FR40 deferred; architecture.md §4.3 D4.6].

### AC7 — RFC 9457 error paths for the link-generation endpoint

**Given** several validation failures are possible
**When** the request is invalid
**Then** registered error codes in `ErrorCatalog`:
- `faktuboh.statements.no_debts_to_share` (409) — contact has no debts
- `faktuboh.statements.contact_not_found` (404) — contact does not belong to owner (used also for owner-of-another-user attempts to avoid existence oracle)
- `faktuboh.statements.link_generation_rate_limit_exceeded` (429) — Story 4.9 owns this, defined here for schema registration
**And** each carries `type`, `title`, `status`, `detail`, `instance`, `traceId`, `code`, optional `errors[]` [Source: architecture.md §4.4 D4.9].

### AC8 — Telemetry `statement_shared` emits with PII-safe schema

**Given** AR-047 `ITelemetryEmitter` primitive exists (Story 0.14)
**When** the handler completes successfully
**Then** `TelemetryEmitter.Emit("statement_shared", new StatementSharedEvent { OpaqueOwnerId = SHA256(ownerSubjectId), ContactIdHash = SHA256(contactId), StatementId = statementId.ToString(), ExpBucket = "30d" })` fires
**And** `PiiGuard.AssertNoPii` validates no raw IDs, no contact name, no token leak
**And** integration test `AssertEventEmitted("statement_shared")` passes [Source: Story 0.14; architecture.md AR-047].

### AC9 — Frontend "Share statement" CTA

**Given** `apps/owner-workspace/src/app/features/contacts/detail/` exists from Epic 2
**When** Story 4.4 adds the share UI to the contact detail page
**Then** a `<p-button icon="pi-share" label="مشاركة كشف الحساب">` renders on the contact detail page above the transactions list
**And** the button is disabled if the contact has zero debts (with tooltip "Add a debt first to share a statement")
**And** clicking issues the POST, then calls `navigator.share(...)` with the response URL
**And** the post-share UX shows `ReconciliationMeta` with state `pending` + "Shared, awaiting confirmation" per UX-DR12 + Story 4.7 [Source: ux-design-specification.md UX-DR12, Flow 3 Lender side; epics.md Story 4.4].

### AC10 — `Cache-Control: private` + `X-Robots-Tag` on the public-statement route

**Given** the public-statement GET endpoint in Story 4.5 will serve per-token content
**When** this story wires the response-header filter for the route group
**Then** `GET /v1/statements/t/{token}` responses include headers:
- `Cache-Control: private, max-age=86400` (24h per AR-050 + D4.19)
- `X-Robots-Tag: noindex, nofollow`
- `Vary: Accept-Language` (for Story 4.15 locale toggle)
**And** the `noindex` meta tag lives in `apps/public-statement/src/index.html` (Story 4.2 already added it)
**And** a CI gate `scripts/ci/check-cache-headers.mjs` asserts these headers are present on the route's OpenAPI response definition [Source: architecture.md §2.9 item #8; §4.6 D4.19; Story 4.11 AR-050].

## Tasks / Subtasks

### Backend — Slice

- [ ] **T1 (AC: 1, 2, 5, 7)** — Create `Faktuboh.Api/Slices/Statements/Statements.Endpoints.cs` with `POST /v1/contacts/{contactId}/statements`
  - [ ] `MapGroup("/v1/contacts/{contactId:guid}/statements").RequireAuthorization()`
  - [ ] Endpoint filter sets `ProcessingActivity.OwnerLedger`
  - [ ] Delegates to `GenerateStatementLinkHandler`
- [ ] **T2 (AC: 1, 2, 5)** — Create `Slices/Statements/Generate/GenerateStatementLinkHandler.cs`
  - [ ] Wolverine handler `public async Task<Results<Created<GenerateStatementLinkResponse>, ProblemHttpResult>> Handle(GenerateStatementLinkRequest, IMessageBus, ICapabilityTokenRepository, IContactRepository, IDebtRepository, ITokenGenerator, ISystemClock)`
  - [ ] Fetches contact; asserts owner match
  - [ ] Fetches debt count for contact; asserts ≥ 1
  - [ ] Checks existing active statement; returns `returnedExisting: true` path if one exists
  - [ ] Generates token via `ITokenGenerator.Create`
  - [ ] Inserts `CapabilityTokenRecord` + `Statement` aggregate
  - [ ] Publishes `StatementLinkIssuedEvent` via message bus
  - [ ] Returns `TypedResults.Created(url, response)`
- [ ] **T3 (AC: 1)** — Create `GenerateStatementLinkRequest`, `GenerateStatementLinkResponse`, `GenerateStatementLinkRequestValidator` as sealed records
  - [ ] Request is empty (contact ID from route), but request object still exists to satisfy Wolverine discovery + future query-string expansion

### Backend — Domain layer

- [ ] **T4 (AC: 2)** — Create `Faktuboh.Domain/Statements/Statement.cs` aggregate root
  - [ ] Implements `IBitemporal`
  - [ ] `static Statement Issue(Guid ownerSubjectId, Guid contactId, Guid capabilityTokenId, DateTimeOffset issuedAt, DateTimeOffset expiresAt)` factory
  - [ ] `Revoke(DateTimeOffset revokedAt)` instance method
  - [ ] `MarkConfirmed(DateTimeOffset confirmedAt)` — used by Story 4.6
  - [ ] Domain event `StatementLinkIssuedEvent` in `Faktuboh.Domain.Events.IntegrationEvents/`
  - [ ] Repository interface `IStatementRepository`

### Backend — Infrastructure layer

- [ ] **T5 (AC: 2)** — Migration `YYYYMMDDHHMMSS_AddStatementsTable.cs`
  - [ ] `statements` table with exact columns per AC2
  - [ ] `statements_history` per ADR-022
  - [ ] Unique constraint `ix_statements_owner_contact_active` as PARTIAL index: `(owner_subject_id, contact_id) WHERE revoked_at IS NULL AND expires_at > now()`
  - [ ] FK to `capability_tokens.id`
- [ ] **T6 (AC: 2)** — `StatementConfiguration` + `StatementRepository`

### Backend — Error codes

- [ ] **T7 (AC: 7)** — Register in `ErrorCatalog`:
  - [ ] `faktuboh.statements.no_debts_to_share` (409)
  - [ ] `faktuboh.statements.contact_not_found` (404)
  - [ ] `faktuboh.statements.link_generation_rate_limit_exceeded` (429) — schema only; Story 4.9 implements behavior

### Backend — Response headers

- [ ] **T8 (AC: 10)** — Create endpoint filter `PublicStatementHeadersFilter`
  - [ ] Applies `Cache-Control: private, max-age=86400`, `X-Robots-Tag: noindex, nofollow`, `Vary: Accept-Language`
  - [ ] Registered on the `/v1/statements/t/*` route group (not the POST here; the GET in Story 4.5)
  - [ ] This story lands the filter; Story 4.5 wires it in
- [ ] **T9 (AC: 10)** — Create `scripts/ci/check-cache-headers.mjs`
  - [ ] Parses OpenAPI JSON for `/v1/statements/t/{token}` response → asserts `headers.Cache-Control`, `headers.X-Robots-Tag`, `headers.Vary`

### Backend — Telemetry

- [ ] **T10 (AC: 8)** — Define `StatementSharedEvent` record in `Faktuboh.Application/Telemetry/Events/`
  - [ ] Fields: `OpaqueOwnerId`, `ContactIdHash`, `StatementId`, `ExpBucket`
  - [ ] Emit from handler post-commit

### Backend — Operations runbook

- [ ] **T11 (AC: 6)** — Create `docs/ops/revoke-capability-token.md`
  - [ ] SQL query template for lookup by contact_id
  - [ ] UPDATE template with `revoked_at = now()`
  - [ ] AFD cache purge command (for when we move to AFD; for SWA, invalidation is automatic on cache-control expiry)

### Frontend — Owner workspace

- [ ] **T12 (AC: 3, 9)** — Add share button + flow to `apps/owner-workspace/src/app/features/contacts/detail/contact-detail.component.ts`
  - [ ] `<p-button icon="pi-share" [disabled]="noDebts()" (click)="onShareStatement()">`
  - [ ] `onShareStatement()` calls `statementsClient.generate(contactId, { headers: { 'Idempotency-Key': uuidv4() } })`
  - [ ] On success: calls `navigator.share({ text: translocoService.translate('share.statement.message.default'), url: response.url })` or falls back to clipboard
  - [ ] Updates `ReconciliationMeta` state to `pending` (will be verified by Story 4.7 load flow)
- [ ] **T13 (AC: 3)** — Add Transloco keys in `libs/core/i18n/transloco/en.json` + `ar.json`
  - [ ] `share.statement.message.default` in both locales
  - [ ] `share.statement.copied` + `share.statement.failed`
- [ ] **T14 (AC: 1, 9)** — Create `libs/core/api/statements.client.ts` (owner-workspace consumer)
  - [ ] NSwag-generated per delegated decisions
  - [ ] `generate(contactId: string, opts: RequestOpts): Observable<GenerateStatementLinkResponse>`

### Tests — Backend

- [ ] **T15 (AC: 1, 2, 5, 7)** — Integration tests in `tests/Faktuboh.Api.Tests/Slices/Statements/`
  - [ ] `GenerateStatementLinkEndpointTests` — happy path returns 201 + URL shape
  - [ ] Duplicate request with same `Idempotency-Key` → cached response
  - [ ] Duplicate request with different key + existing active statement → `returnedExisting: true`
  - [ ] Contact without debts → 409 `no_debts_to_share`
  - [ ] Different-owner contact → 404 `contact_not_found`
  - [ ] Concurrent-double-submit test → single handler execution
- [ ] **T16 (AC: 8)** — `AssertEventEmitted("statement_shared")` end-to-end test
- [ ] **T17 (AC: 6)** — `RevokeTokenRunbookIntegrationTest` — the runbook's SQL actually works against a seeded fixture

### Tests — Frontend

- [ ] **T18 (AC: 9)** — Vitest component test for the share button
  - [ ] Disabled when `noDebts()` signal is true
  - [ ] Click triggers `statementsClient.generate` with correct `Idempotency-Key` shape
  - [ ] `navigator.share` called with expected args (mocked Web Share API)
- [ ] **T19 (AC: 3, 9)** — Playwright E2E: owner on contact detail → share → verify URL copied to clipboard in desktop Chromium

## Dev Notes

### Critical guardrails

1. **`returnedExisting: true` on reissue.** If an active statement already exists for `(owner, contact)`, don't issue a new token — return the existing URL. Prevents token-sprawl and gives users a stable, bookmarkable URL.
2. **404, not 403, for wrong-owner access.** Leaking "this contact exists but isn't yours" is an existence oracle on contact IDs. Return the same 404 as "contact doesn't exist."
3. **30-day expiration is a hard default.** NFR-S11 says 30 days. Longer defaults = more exposure; shorter = user friction. Don't drift.
4. **Token never logged.** Story 4.1's redaction discipline applies to this handler too — `log.Info("Issued token {TokenId}")` is fine only because `TokenId` is the DB row ID, not the token itself.
5. **FR40 owner-side revocation is explicitly deferred.** Don't build UI for it in Epic 4. The runbook + kill-switch env flag are the Phase-A story.
6. **Cache-Control headers land here, consumed by Story 4.5.** The filter exists now; 4.5 hooks it into the GET route group.

### Source tree

```
backend/
├── src/
│   ├── Faktuboh.Api/Slices/Statements/
│   │   ├── Statements.Endpoints.cs                         # NEW
│   │   ├── Generate/
│   │   │   ├── GenerateStatementLinkRequest.cs             # NEW
│   │   │   ├── GenerateStatementLinkResponse.cs            # NEW
│   │   │   ├── GenerateStatementLinkRequestValidator.cs    # NEW
│   │   │   └── GenerateStatementLinkHandler.cs             # NEW
│   │   └── Infrastructure/
│   │       └── PublicStatementHeadersFilter.cs             # NEW
│   ├── Faktuboh.Application/
│   │   ├── Errors/ErrorCatalog.cs                          # MODIFY (+3 codes)
│   │   └── Telemetry/Events/
│   │       └── StatementSharedEvent.cs                     # NEW
│   ├── Faktuboh.Domain/Statements/
│   │   ├── Statement.cs                                    # NEW (aggregate, IBitemporal)
│   │   ├── IStatementRepository.cs                         # NEW
│   │   └── StatementException.cs                           # NEW
│   ├── Faktuboh.Domain/Events/IntegrationEvents/
│   │   └── StatementLinkIssuedEvent.cs                     # NEW
│   └── Faktuboh.Infrastructure/
│       ├── Persistence/
│       │   ├── Configurations/StatementConfiguration.cs    # NEW
│       │   └── Migrations/
│       │       └── YYYYMMDDHHMMSS_AddStatementsTable.cs    # NEW
│       └── Repositories/StatementRepository.cs             # NEW
└── tests/Faktuboh.Api.Tests/Slices/Statements/
    ├── GenerateStatementLinkEndpointTests.cs               # NEW
    ├── ConcurrentDoubleSubmitTests.cs                      # NEW
    └── StatementSharedTelemetryTests.cs                    # NEW

docs/ops/
└── revoke-capability-token.md                              # NEW

scripts/ci/
└── check-cache-headers.mjs                                 # NEW

frontend/
├── apps/owner-workspace/src/app/features/contacts/detail/
│   └── contact-detail.component.ts                         # MODIFY (add share button)
└── libs/core/
    ├── api/statements.client.ts                            # NEW (NSwag-generated)
    └── i18n/transloco/{en,ar}.json                         # MODIFY
```

### Architecture compliance

- **FR29, FR34** — this story makes them work end-to-end.
- **NFR-S11** (30-day default) — hardcoded in handler with env-var override for testing.
- **§4.3 D4.6** (token scheme) — this is the first caller.
- **§4.4 D4.9** (RFC 9457) + **D4.11** (Idempotency-Key + natural-key backstop) — the `(owner, contact)` natural-key backstop is the unique partial index.
- **§4.4 D4.12** (same-transaction outbox) — issuance + event emit atomic.
- **§2.9 item #8** (noindex, Cache-Control: private) — header filter lands here.
- **ADR-022** (bitemporal) — `statements` table is bitemporal.
- **Delegated: FR40 deferred** — owner-side revocation UI is NOT in this story; runbook + kill-switch are.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| HTTP | ASP.NET Core Minimal APIs | `MapGroup` + `TypedResults` |
| Validation | FluentValidation | |
| Messaging | Wolverine | Same-transaction outbox |
| ORM | EF Core 10 | `IBitemporal` interceptor |
| TS client | NSwag | Per delegated decisions — owner-workspace uses NSwag; public-statement handwrites |
| Tests | xUnit v3 + Testcontainers Postgres + real Wolverine bus | |

### Testing requirements

- **Unit:** Handler logic with mocked repositories (validation edges, reissue flow).
- **Integration:** Full POST → DB + outbox + response — via `WebApplicationFactory<Program>` + Testcontainers Postgres + real Wolverine bus.
- **Concurrent double-submit:** Two parallel POSTs with same key → single execution.
- **Telemetry:** `AssertEventEmitted("statement_shared")`.
- **E2E:** Playwright desktop + mobile viewport, share button → clipboard + native share sheet.
- **Runbook:** Revoke-via-SQL works against a seeded fixture.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.4 — RFC 9457
- Story 0.5 — Idempotency-Key middleware
- Story 0.6 — `IBitemporal`
- Story 0.14 — telemetry primitive
- Story 2.1 — Contacts/Create (contact aggregate exists)
- Story 4.1 — `capability_tokens` table + scheme
- Story 4.10 — `ITokenGenerator`

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.4
- Token scheme — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.3 D4.6
- RFC 9457 + idempotency — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.4 D4.9, D4.11
- Public-surface hardening — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.9 items 8, 10
- UX Flow 3 (lender side) — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) Flow 3
- UX-DR33 share message template — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) UX Consistency Patterns
- FR29, FR34, NFR-S11 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- First real Epic 4 traffic-producing story. Stories 4.5, 4.6, 4.7 become testable once this ships.
- The `Statement` aggregate is created here; Story 4.6 extends it with `MarkConfirmed`.

### File List

_Populated during implementation._
