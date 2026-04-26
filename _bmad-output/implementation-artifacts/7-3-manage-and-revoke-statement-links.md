# Story 7.3: Manage and revoke statement links

Status: ready-for-dev

## Story

As an **owner**,
I want to **view all my generated statement links and revoke any specific link**,
so that **I can kill a link I sent by mistake or no longer need** (FR40) [Source: epics.md Epic 7 Story 7.3; prd.md FR40, NFR-S11].

**Covers requirements:** Epic 7 — FR40 (link management UI), NFR-S11 (link expiration + revocation), §2.9 public-surface item #1 (revoked tokens look identical to expired/unknown — constant-time invariant from Story 4.1) [Source: epics.md Epic 7 Story 7.3].

**Why this story now:** Stories 7.1 + 7.2 close the real-time push loop. With share happening at higher cadence (Stories 7.4–7.6 nudges land next), owners need a way to inspect + revoke links. The capability-token table from Story 4.1 already supports `revoked_at`; this story adds the owner UI + the AFD/SWA cache purge sequence on revocation.

## Acceptance Criteria

### AC1 — `/statements/links` route renders DataView of all owner's links

**Given** an authenticated owner has generated ≥ 1 statement link
**When** the user navigates to `/statements/links`
**Then** the page renders a PrimeNG `<p-dataView>` listing every `capability_tokens` row owned by the user
**And** each row displays: linked contact (name + avatar), generation date, expiration date, view count, confirmation status, revoke action button
**And** the data source is `GET /v1/statements/links?status={status?}&page={n}&size=20` returning `{ data: LinkSummaryDto[], pagination: { total, page, size } }`
**And** the response envelope follows the standard pattern from architecture §5.3.1
**And** the empty state renders "No statement links yet" factual copy with no illustration (per UX-DR philosophy "factual not encouraging") [Source: epics.md Story 7.3 AC; ux-design-specification.md].

### AC2 — Filter by status (active / expired / revoked)

**Given** the user wants to narrow the view
**When** the user selects a status pill (`Active` | `Expired` | `Revoked` | `All`)
**Then** the query string updates: `?status=active|expired|revoked` (omit for All)
**And** the backend filter logic:
- `active`: `revoked_at IS NULL AND exp > now()`
- `expired`: `revoked_at IS NULL AND exp <= now()`
- `revoked`: `revoked_at IS NOT NULL`
**And** the URL reflects the current filter (deep-linkable)
**And** browser back-button restores the previous filter selection
**And** an integration test asserts each filter returns the correct row subset [Source: epics.md Story 7.3 AC filter].

### AC3 — Revoke action with `<p-confirmDialog>` per UX-DR29

**Given** the user wants to revoke a specific link
**When** the user taps "Revoke" on a row
**Then** a `<p-confirmDialog>` opens with:
- Header: "Revoke this statement link?"
- Body: "The recipient will see 'Invalid or expired statement link' next time they open it. This cannot be undone."
- Confirm button label: "Revoke"
- Cancel button label: "Keep active"
- Per UX-DR29 destructive-confirm pattern (the only destructive owner action that requires confirm at this scope)
**And** confirming triggers `DELETE /v1/statements/links/{tokenHash}` (token hash is the URL identifier — the raw token never crossed back to the owner UI)
**And** the row updates inline to show status `Revoked` with timestamp, no toast (silent success per UX-DR27)
**And** an integration test asserts the dialog appears + cancel does nothing + confirm calls the API [Source: ux-design-specification.md UX-DR27, UX-DR29].

### AC4 — Revocation marks `capability_tokens.revoked_at = now()` + purges AFD cache

**Given** `DELETE /v1/statements/links/{tokenHash}` is called by the owner
**When** the handler executes
**Then** the handler:
1. Validates the owner is the token's `owner_subject_id` — 403 RFC 9457 `faktuboh.statements.link_not_owned` if not
2. Updates `capability_tokens SET revoked_at = now() WHERE token_hash = ? AND owner_subject_id = ? AND revoked_at IS NULL`
3. Writes a row to `capability_tokens_history` via the `IBitemporal` interceptor
4. Publishes `CapabilityTokenRevokedEvent` integration event
5. Calls Azure Front Door cache-purge API for path `/t/{tokenUrlSegment}` (recovered server-side via short-lived cache mapping `token_hash → token_url_segment_hint`)
6. Returns `200 OK` with the updated `LinkSummaryDto`
**And** the AFD purge is best-effort (logged on failure but does not fail the revocation; the constant-time 404 from Story 4.1 is the correctness guarantee — AFD purge is just a latency improvement)
**And** an integration test asserts: post-revoke, `GET /v1/statements/t/{token}` returns the constant-time `faktuboh.statements.token_invalid` [Source: epics.md Story 7.3 AC; Story 4.1 AC3].

### AC5 — Revoked link returns neutral expired page per NFR-S11

**Given** Story 4.1 already enforces constant-time response on revoked tokens (returns same Problem Details body as expired/unknown)
**When** a recipient with a revoked link opens it
**Then** they see the same generic 404 + "Invalid or expired statement link — contact the sender" page as for any other invalid token
**And** prior statement data is NOT exposed
**And** the link's owner identity is NOT exposed
**And** the recipient cannot distinguish revoked from expired from unknown — preserves the constant-time invariant from Story 4.1 [Source: epics.md Story 7.3 AC; Story 4.1].

### AC6 — `LinkSummaryDto` schema for the list endpoint

**Given** the list endpoint shape is defined
**When** `GET /v1/statements/links` returns
**Then** each `LinkSummaryDto` is:
```csharp
public sealed record LinkSummaryDto(
    string TokenHash,           // URL-safe base64 of SHA-256(token); used as the row key + URL identifier in DELETE
    Guid ContactId,
    string ContactName,         // resolved from owner's contacts (NOT cross-slice domain leak — joined at the read model)
    DateTimeOffset GeneratedAt,
    DateTimeOffset Exp,
    DateTimeOffset? RevokedAt,
    int ViewCount,              // sum of `capability_token_views.viewed_at` rows for this token (best-effort; counter from Story 4.13 telemetry)
    bool IsConfirmed,           // joined from `statements.confirmed_at IS NOT NULL`
    DateTimeOffset? ConfirmedAt
);
```
**And** the raw token plaintext is NEVER returned (it has never been stored server-side per Story 4.1)
**And** an OpenAPI snapshot test pins this shape; PR that changes it without bumping `openapi.json` snapshot fails CI [Source: Story 4.1 AC2; architecture.md §4.3 D4.6].

### AC7 — View count derives from existing telemetry (no new tracking column)

**Given** Story 4.13 already tracks `capability_token_view` events
**When** the list endpoint computes `view_count`
**Then** the value is sourced from a daily-rollup read model `capability_token_view_counts(token_hash, count, last_viewed_at)` populated by a Wolverine projection consuming the telemetry events
**And** the rollup is "best-effort" — eventual consistency; not authoritative for billing
**And** if no rows exist for a token (no views yet), the value is `0`
**And** an integration test creates 3 view events; asserts list shows count 3 within 60s [Source: Story 4.13 telemetry; architecture.md §3.5 read model pattern].

### AC8 — Telemetry `statement_link_revoked` emits

**Given** AR-047 telemetry primitive
**When** a link is revoked
**Then** `TelemetryEmitter.Emit("statement_link_revoked", new StatementLinkRevokedTelemetry(opaque_owner_id, contact_id_hash, link_age_days_bucket, was_confirmed))` fires
**And** `link_age_days_bucket` is one of `"<1d" | "<7d" | "<30d" | ">30d"` [Source: Story 0.14].

### AC9 — Mobile responsive — DataView switches to list layout < 768px

**Given** PrimeNG DataView supports grid + list layouts
**When** viewport width < 768px
**Then** the layout snaps to single-column list mode (no grid)
**And** each row remains tappable with revoke action accessible (button visible, not buried in overflow menu at MVP)
**And** Playwright assertion at 375px viewport renders correctly + axe-core reports zero WCAG 2.1 AA violations [Source: ux-design-specification.md responsive patterns].

## Tasks / Subtasks

### Backend — List endpoint

- [ ] **T1 (AC: 1, 2, 6, 7)** — Create `Slices/Statements/ListLinks/` action folder
  - [ ] `ListLinksRequest.cs` — `(string? Status, int Page, int Size)`
  - [ ] `LinkSummaryDto.cs` — sealed record per AC6
  - [ ] `ListLinksHandler.cs` — Wolverine handler, executes the filtered query + joins to contacts + view-count rollup
  - [ ] `ListLinksRequestValidator.cs` — `Status` in `{null, "active", "expired", "revoked"}`; `Page` ≥ 1; `Size` in `{10, 20, 50}`
  - [ ] Endpoint registered in `Statements.Endpoints.cs` via `MapGet("/v1/statements/links", ...)`

### Backend — Revoke endpoint

- [ ] **T2 (AC: 3, 4)** — Create `Slices/Statements/RevokeLink/` action folder
  - [ ] `RevokeLinkHandler.cs` — Wolverine handler
  - [ ] Endpoint `MapDelete("/v1/statements/links/{tokenHash}", ...)`
  - [ ] Authorization: `[Authorize]` + ownership check inside handler
- [ ] **T3 (AC: 4)** — Create `AzureFrontDoorPurgeClient` in `backend/src/Faktuboh.Infrastructure/Cdn/`
  - [ ] Method `PurgePathAsync(string path)` — calls AFD management API with managed-identity auth
  - [ ] On failure, logs WARN + returns false (does not throw — best-effort)
- [ ] **T4 (AC: 4)** — Define `CapabilityTokenRevokedEvent` integration event in `Faktuboh.Domain.Events.IntegrationEvents/`
  - [ ] `(byte[] TokenHash, Guid OwnerSubjectId, Guid? ContactId, DateTimeOffset RevokedAt)`

### Backend — Read model: view-count rollup

- [ ] **T5 (AC: 7)** — Create migration `YYYYMMDDHHMMSS_AddCapabilityTokenViewCountsTable.cs`
  - [ ] Table `capability_token_view_counts(token_hash bytea PK, count integer NOT NULL DEFAULT 0, last_viewed_at timestamptz NULL)`
- [ ] **T6 (AC: 7)** — Wolverine handler `ProjectCapabilityTokenViewCount` consuming telemetry-bridged event from Story 4.13
  - [ ] On `CapabilityTokenViewedEvent`, upserts the row with `count = count + 1; last_viewed_at = now()`
  - [ ] Idempotency via natural key — at-least-once delivery is acceptable (over-count is a non-issue at MVP)

### Backend — Token-hash to URL-segment hint mapping (for AFD purge)

- [ ] **T7 (AC: 4)** — Add column `capability_tokens.token_url_segment_hint text NULL` populated at issuance time (Story 4.10)
  - [ ] Stores the public URL segment (the 22-char base64url) ONLY — not the raw bytes
  - [ ] Used solely to construct the AFD purge path `/t/{hint}` at revoke time
  - [ ] Hint is acceptable to store because the URL is already public (in the recipient's browser history, AFD logs); the token bytes / hash are still server-side-only secrets
  - [ ] Migration: backfill is N/A — pre-existing tokens (none at this story's ship time) can be left null and AFD purge is skipped for them

### Backend — Error catalog

- [ ] **T8 (AC: 4)** — Register `faktuboh.statements.link_not_owned` (403) in `ErrorCatalog`

### Backend — Telemetry

- [ ] **T9 (AC: 8)** — Define `StatementLinkRevokedTelemetry` in `Faktuboh.Application/Telemetry/Events/`
- [ ] **T10 (AC: 8)** — Wire emission in `RevokeLinkHandler` after successful revocation

### Frontend — Route + page

- [ ] **T11 (AC: 1, 2)** — Create `apps/owner-workspace/src/app/features/statements/links/` feature folder
  - [ ] `links-list.routes.ts` — lazy route `/statements/links`
  - [ ] `links-list.component.ts` — standalone, OnPush, signals
  - [ ] `links-list.component.html` — `<p-dataView>` + status filter pills + empty state
- [ ] **T12 (AC: 1, 6)** — Create `links.client.ts` typed wrapper in `apps/owner-workspace/src/app/core/api/`
  - [ ] `listLinks(status?: string, page?: number): rxResource<LinkSummaryDto[]>`
  - [ ] `revokeLink(tokenHash: string): Observable<LinkSummaryDto>`
  - [ ] Generated by NSwag (per delegated decisions); commit the generator config

### Frontend — Revoke flow

- [ ] **T13 (AC: 3)** — Wire `<p-confirmDialog>` for revoke action
  - [ ] Use `ConfirmationService.confirm({ ... })` with bilingual message via Transloco
  - [ ] On accept, call `linksClient.revokeLink(tokenHash)`
  - [ ] On success, mutate the local rxResource value to reflect the revoked row (no full reload)

### Frontend — Status filter

- [ ] **T14 (AC: 2)** — Status pills component using PrimeNG `<p-selectButton>` with options `[All, Active, Expired, Revoked]`
  - [ ] Bound to a router query-param signal
  - [ ] Changing pill triggers rxResource refresh

### Tests — Backend

- [ ] **T15 (AC: 1, 2, 6, 7)** — Integration tests in `tests/Faktuboh.Api.Tests/Slices/Statements/ListLinksTests.cs`
  - [ ] Setup: 6 tokens (2 active, 2 expired, 2 revoked) for owner subject + 1 token for a different owner
  - [ ] List with no filter returns 6 (only the owner's)
  - [ ] Filter `active` returns 2; `expired` returns 2; `revoked` returns 2
  - [ ] Cross-owner leakage test: requests as different owner cannot see other owner's links
  - [ ] View-count rollup: insert 3 view events; assert count 3 in response within 60s
- [ ] **T16 (AC: 3, 4, 5)** — Integration test `RevokeLinkTests.cs`
  - [ ] Happy path: revoke sets `revoked_at`; bitemporal history row created
  - [ ] Ownership: revoke other owner's token returns 403
  - [ ] Idempotent: revoke already-revoked token is no-op
  - [ ] Post-revoke, `GET /v1/statements/t/{token}` returns constant-time `faktuboh.statements.token_invalid` (asserts the constant-time invariant from Story 4.1 is preserved)
  - [ ] AFD purge call: assert `AzureFrontDoorPurgeClient.PurgePathAsync` invoked with `/t/{hint}` (mock the HTTP client; assert the call shape)
  - [ ] AFD purge failure: simulate 500 from AFD; assert revocation still succeeds + WARN logged

### Tests — Frontend

- [ ] **T17 (AC: 1, 2)** — Vitest component test `links-list.component.spec.ts`
  - [ ] Renders DataView with mocked rxResource data
  - [ ] Status pill click updates query param
  - [ ] Empty state renders factual copy
- [ ] **T18 (AC: 3)** — Vitest test for revoke confirm dialog flow
- [ ] **T19 (AC: 9)** — Playwright E2E `apps/owner-workspace/e2e/statements/links-list.spec.ts`
  - [ ] Happy path: list renders, filter changes work, revoke flow completes
  - [ ] Mobile viewport (375px): assert single-column list layout
  - [ ] Axe-core: zero WCAG 2.1 AA violations
  - [ ] RTL parity (Arabic): visual regression screenshot

## Dev Notes

### Critical guardrails (do not violate)

1. **Constant-time invariant preserved.** Revocation does NOT add a `faktuboh.statements.token_revoked` error code. Story 4.1 owns the single error code; this story's integration test asserts the invariant is unbroken post-revoke.
2. **Raw token never returned to the owner UI.** The list endpoint returns only `token_hash` (or a derivative URL-safe hash). The original token plaintext was never stored server-side; the hash + URL-hint are sufficient for revoke + AFD-purge.
3. **AFD cache purge is best-effort.** Failure logs WARN but does not block revocation. The constant-time 404 from Story 4.1 is the correctness guarantee; AFD purge is a latency / experience improvement only.
4. **`<p-confirmDialog>` is the ONLY destructive UX in this story.** Per UX-DR29 destructive-confirm pattern. All other destructive owner flows (delete contact, delete debt) already use this pattern; revoke matches.
5. **Cross-owner leakage test is mandatory.** Without it, an authorization bug (e.g., missing `owner_subject_id` predicate) ships invisibly. The test is non-negotiable.
6. **View count is best-effort.** Eventual consistency. Do not promise authoritative numbers in UI copy ("3 views" not "exactly 3 views"). At MVP, billing has no link-count component, so "best-effort" is acceptable.
7. **`token_url_segment_hint` is acceptable PII-adjacent storage.** The URL segment is already in the recipient's browser history + AFD logs; storing a copy server-side does NOT widen the attack surface (token plaintext + hash are still secrets). Document this in ADR-022 update or as a comment in the migration.
8. **Silent success on revoke (UX-DR27).** No toast on success. The row updating to "Revoked" status IS the feedback.

### Source tree — files to create or touch

```
backend/
├── src/
│   ├── Faktuboh.Api/
│   │   └── Slices/
│   │       └── Statements/
│   │           ├── ListLinks/
│   │           │   ├── ListLinksRequest.cs                     # NEW
│   │           │   ├── LinkSummaryDto.cs                       # NEW
│   │           │   ├── ListLinksHandler.cs                     # NEW
│   │           │   └── ListLinksRequestValidator.cs            # NEW
│   │           ├── RevokeLink/
│   │           │   └── RevokeLinkHandler.cs                    # NEW
│   │           └── Projections/
│   │               └── ProjectCapabilityTokenViewCount.cs      # NEW
│   ├── Faktuboh.Infrastructure/
│   │   ├── Cdn/
│   │   │   └── AzureFrontDoorPurgeClient.cs                    # NEW
│   │   └── Persistence/Migrations/
│   │       ├── YYYYMMDDHHMMSS_AddCapabilityTokenViewCountsTable.cs  # NEW
│   │       └── YYYYMMDDHHMMSS_AddTokenUrlSegmentHintColumn.cs       # NEW
│   ├── Faktuboh.Domain/
│   │   └── Events/IntegrationEvents/
│   │       └── CapabilityTokenRevokedEvent.cs                  # NEW
│   └── Faktuboh.Application/
│       ├── Telemetry/Events/
│       │   └── StatementLinkRevokedTelemetry.cs                # NEW
│       └── Errors/ErrorCatalog.cs                              # MODIFY (+1 code)
└── tests/
    └── Faktuboh.Api.Tests/Slices/Statements/
        ├── ListLinksTests.cs                                   # NEW (Integration)
        └── RevokeLinkTests.cs                                  # NEW (Integration)

frontend/
└── apps/owner-workspace/src/app/
    ├── features/statements/links/
    │   ├── links-list.routes.ts                                # NEW
    │   ├── links-list.component.ts                             # NEW
    │   ├── links-list.component.html                           # NEW
    │   ├── links-list.component.scss                           # NEW
    │   └── links-list.component.spec.ts                        # NEW
    └── core/api/
        └── links.client.ts                                     # NEW (NSwag-generated)
```

### Testing standards

| Level | Tools |
|---|---|
| Unit (BE) | xUnit v3 + FluentAssertions + NSubstitute |
| Integration (BE) | `WebApplicationFactory<Program>` + Testcontainers Postgres + real Wolverine + mocked AFD HTTP client |
| Unit (FE) | Vitest + Angular Testing Library |
| E2E | Playwright Chromium + WebKit; axe-core; visual regression LTR + RTL |

### Project Structure Notes

The Statements slice already exists from Stories 4.4–4.13. This story adds two action folders (`ListLinks/`, `RevokeLink/`) and one projection. The `LinkSummaryDto` joins `capability_tokens` with `contacts` — this is a read-model join inside the slice's read endpoint, NOT a domain-model dependency. NetArchTest #2 (slice isolation) remains satisfied because the join happens at the SQL level via raw column references, not via importing the `Contact` aggregate.

### Architecture compliance

- [epics.md Epic 7 Story 7.3](../planning-artifacts/epics.md) — story definition
- [Story 4.1](./4-1-capability-token-scheme-with-constant-time-response.md) — constant-time invariant + capability_tokens table
- [Story 4.4](./4-4-generate-statement-html-shareable-link.md) — link generation
- [architecture.md §4.3 D4.6](../planning-artifacts/architecture.md) — capability tokens
- [ux-design-specification.md UX-DR27, UX-DR29](../planning-artifacts/ux-design-specification.md) — silent success + destructive confirm
- [prd.md FR40, NFR-S11](../planning-artifacts/prd.md)

### Library / framework requirements

| Layer | Package | Notes |
|---|---|---|
| Backend | Azure.Identity + HttpClient | AFD purge via managed identity |
| Frontend | PrimeNG DataView, ConfirmDialog, SelectButton | Standard PrimeNG v21 |

### Testing requirements

- **Cross-owner leakage** — already noted; if missed in code review, an attacker with a valid Auth0 session could enumerate other owners' tokens via crafted token-hash guesses.
- **Constant-time invariant preserved post-revoke** — the integration test from Story 4.1's perspective. If this fails, this story has broken Story 4.1's security property.
- **AFD purge mock** — uses NSubstitute on `HttpMessageHandler`; assert the URL pattern `/cdn/management/...` and the body contains the path `/t/{hint}`.

### Previous Story Intelligence

**Prerequisites (must be green):**
- Story 4.1 — capability_tokens table + constant-time validator
- Story 4.4 — link generation (sources `token_url_segment_hint`)
- Story 4.13 — `CapabilityTokenViewedEvent` telemetry event
- Story 0.14 — telemetry primitive
- Story 1.9 — Phase-1 component API (ContactAvatar, etc.)

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Epic 7 Story 7.3
- [_bmad-output/planning-artifacts/architecture.md §4.3 D4.6](../planning-artifacts/architecture.md)
- [_bmad-output/planning-artifacts/ux-design-specification.md UX-DR27, UX-DR29](../planning-artifacts/ux-design-specification.md)
- [_bmad-output/planning-artifacts/prd.md FR40, NFR-S11](../planning-artifacts/prd.md)
- [Story 4.1 capability tokens](./4-1-capability-token-scheme-with-constant-time-response.md)
- [Story 4.4 link generation](./4-4-generate-statement-html-shareable-link.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation._
