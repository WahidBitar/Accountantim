# Story 4.5: Public StatementView renders without account

Status: ready-for-dev

## Story

As a **recipient of a shared statement link**,
I want to **open the link on my phone without signing up and see a clean professional bilingual statement**,
so that **I can review the record quickly in any messaging-app context** (FR35, FR39).

**Covers requirements:** Epic 4 — FR29, FR35, FR39, NFR-P5 (< 2s statement page), §2.9 full public-surface hardening, UX-DR13 StatementView component [Source: epics.md Epic 4 Story 4.5; ux-design-specification.md Custom Components Phase 2].

**Why this story now:** Stories 4.1 + 4.2 + 4.3 built the scheme, target, and budget. Story 4.4 issues tokens. This story makes the token *do something* — render the statement for the recipient. This is the first story where a debtor sees the product.

## Acceptance Criteria

### AC1 — `GET /v1/statements/t/{token}` returns JSON statement payload

**Given** Story 4.1's constant-time endpoint stub
**When** this story fleshes out the handler
**Then** on valid token, the response is `200 OK` with `Content-Type: application/json`, body shape:

```json
{
  "lender": { "name": "Ahmed Hassan", "avatarColor": "#3B82F6" },
  "recipient": { "name": "Sami Nasser" },
  "balances": [{ "currency": "USD", "amount": "300.00", "direction": "receivable" }],
  "transactions": [
    { "id": "uuid", "date": "2026-03-15", "amount": "100.00", "currency": "USD", "direction": "receivable", "note": "..." }
  ],
  "issuedAt": "2026-04-10T12:00:00Z",
  "reconciliationMeta": { "state": "pending" }
}
```

**And** the response headers include: `Cache-Control: private, max-age=86400`, `X-Robots-Tag: noindex, nofollow`, `Vary: Accept-Language` (Story 4.4's filter applied)
**And** no internal IDs appear (`contactId`, `ownerId` are never in the payload — NFR-S8)
**And** the endpoint is `[AllowAnonymous]` with `ProcessingActivity.DebtorConfirmation` context
**And** all of Story 4.1's constant-time invariants still hold for rejection paths [Source: epics.md Story 4.5; prd.md FR29; architecture.md §2.9].

### AC2 — StatementView renders per UX-DR13

**Given** `apps/public-statement/src/app/features/statement-view/` is empty from Story 4.2
**When** this story lands the component
**Then** `StatementViewComponent` is a standalone, OnPush, zoneless component with the composition per UX-DR13:
1. Minimal header: product wordmark (inline-start) + language toggle (inline-end) — no nav
2. Parties strip: `ContactAvatar` (lender, from `libs/domain-ui`) + lender name + "→" + recipient name (no avatar)
3. Balance summary: xl `AmountDisplay` per denomination (from `libs/domain-ui`)
4. Transactions list: chronological read-only rows rendered via `StatementTransactionRow` local component
5. `ReconciliationMeta` (from `libs/domain-ui`) showing `pending` state pre-confirmation
6. Confirmation button — single large CTA "تأكيد" (Story 4.6 wires behavior)
7. Post-confirmation: signup CTA (Story 4.8 wires)
8. Footer: legal micro-copy (Story 4.15 provides text)
**And** the layout uses CSS logical properties (`padding-inline-start`, etc.) for RTL support
**And** `@defer` blocks wrap below-the-fold content (transactions list + post-confirm content) for faster LCP [Source: ux-design-specification.md Component 9 StatementView].

### AC3 — Page loads in < 2s on Slow 4G; LCP < 2.5s

**Given** NFR-P5 specifies < 2s load time
**When** the nightly Lighthouse CI workflow runs (Story 4.3)
**Then** LCP < 2500ms, FCP < 1800ms, TTFB < 800ms, CLS < 0.05, TBT < 200ms
**And** the `StatementViewComponent` is lazy-loaded (route-level `loadComponent`) — not bundled into the initial shell
**And** the `@defer` blocks keep below-the-fold chunks out of the initial paint path
**And** `ContactAvatar` + `AmountDisplay` are tree-shaken to their minimal surface for the public target [Source: epics.md Story 4.5 AC; prd.md NFR-P5; architecture.md §2.2].

### AC4 — `<html lang dir>` per recipient's Accept-Language; toggle available

**Given** bilingual AR+EN day-one is a hard constraint
**When** the page first loads
**Then** the `statement-route.guard` (Story 4.1) inspects `navigator.language`:
- Starts with `ar` → `document.documentElement.setAttribute('lang', 'ar'); setAttribute('dir', 'rtl')`
- Anything else → `lang='en', dir='ltr'`
**And** a language toggle in the header (Story 4.15 implements) switches `lang`/`dir` + calls Transloco `setActiveLang`
**And** the payload renders with formatted numerals per locale (Arabic-Indic digits when locale is `ar-*`, Western digits when `en-*`) via `Intl.NumberFormat` [Source: architecture.md §5.4; ux-design-specification.md RTL notes].

### AC5 — `noindex, nofollow` + `X-Robots-Tag` + `Cache-Control: private`

**Given** §2.9 public-surface hardening item #8 and #10
**When** any response to `/v1/statements/t/*` is returned
**Then** headers include: `X-Robots-Tag: noindex, nofollow`, `Cache-Control: private, max-age=86400`, `Vary: Accept-Language`
**And** `apps/public-statement/src/index.html` includes `<meta name="robots" content="noindex, nofollow">` (Story 4.2 added this)
**And** `Referrer-Policy: no-referrer` header is added to prevent token leak via `Referer`
**And** `Content-Security-Policy: default-src 'self'; frame-ancestors 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' https://api.faktuboh.com; base-uri 'self'; form-action 'self';` header is set (CSP + clickjacking defence)
**And** `X-Content-Type-Options: nosniff` header is set
**And** `Strict-Transport-Security: max-age=31536000; includeSubDomains` header is set [Source: architecture.md §2.9 items 8, 9, 10].

### AC6 — Token stripped from browser history on first navigation

**Given** Story 4.1's `statement-route.guard` implements this
**When** the Angular router fires the initial navigation for `/t/{token}`
**Then** the guard:
1. Extracts `token` from URL param
2. Writes to `SessionStorage` key `faktuboh.session.token`
3. Calls `history.replaceState({}, '', '/viewing')` exactly once
4. Returns true to allow `StatementView` activation
**And** `window.location.pathname === '/viewing'` after navigation settles
**And** subsequent GET calls within the tab replay `SessionStorage` token via HTTP interceptor [Source: architecture.md §2.9 #6; Story 4.1 AC6].

### AC7 — First-render interstitial blocks preview-bot token consumption

**Given** §2.9 public-surface item #10: "First-render interstitial blocks preview bots from triggering body render and token consumption"
**When** the public-statement app loads
**Then** `StatementViewComponent` first renders ONLY a minimal `<button>Tap to view statement</button>` + brand wordmark — NO statement content, NO API call yet
**And** only after the user clicks/taps (a real user gesture, not a preview-bot auto-render event) does the GET `/v1/statements/t/{token}` fire
**And** the interstitial is bilingual: `"اضغط لعرض الكشف"` / `"Tap to view statement"`
**And** a CSS `@media (prefers-reduced-motion: no-preference)` subtle fade from interstitial to content masks the transition
**And** telemetry `statement_interstitial_shown` + `statement_interstitial_tapped` fire — allowing bot-vs-debtor divergence detection per §2.9 item #11 [Source: architecture.md §2.9 items 10, 11].

### AC8 — Telemetry `statement_viewed` emits post-tap with IP-hashed payload

**Given** AR-047 primitive exists
**When** the tap-through fires and the GET completes
**Then** `TelemetryEmitter.Emit("statement_viewed", new StatementViewedEvent { IpHash = SHA256(ip+dailySalt), UaHash = SHA256(ua), StatementId, TokenHashPrefix8 = hex(first8bytes), Locale })` fires
**And** `PiiGuard` validates no raw IP, no raw UA string, no raw token
**And** an integration test `AssertEventEmitted("statement_viewed")` passes
**And** the telemetry stream is tagged `channel=debtor` vs the owner-workspace stream `channel=owner` (per §2.9 item #11 — divergence alert) [Source: architecture.md §2.9 #11; Story 0.14].

### AC9 — Error states per UX-DR13 component spec

**Given** the component has defined states: `viewable`, `confirming`, `confirmed`, `revoked`, `error`
**When** each state is reached
**Then**:
- `viewable` — CTA visible, signup CTA hidden
- `confirming` — CTA disabled with skeleton (Story 4.6 owns)
- `confirmed` — CTA replaced with `ReconciliationMeta` reconciled state + signup CTA (Story 4.6 + 4.8)
- `revoked` (generic 404 from constant-time endpoint) — entire content replaced with: "This statement is no longer available." (no signup CTA — trust-first)
- `error` (network error) — inline retry button
**And** the revoked/expired/unknown paths are indistinguishable (constant-time invariant) [Source: ux-design-specification.md Component 9 states; architecture.md §2.9 #2].

### AC10 — Accessibility per WCAG 2.1 AA + NFR-A9 RTL gate

**Given** accessibility is non-negotiable (§2.2 WCAG AA + AAA on financial surfaces)
**When** axe-core runs against the rendered view
**Then** zero WCAG 2.1 AA violations in both AR and EN locales
**And** landmark `<main>` wraps content; balance region has `role="status"` so amounts are announced
**And** CTA button has `aria-label="Confirm statement from {lender} showing {balance}"` (dynamic per render)
**And** keyboard navigation: TAB order is wordmark → language-toggle → (interstitial tap) → transactions → CTA → signup CTA
**And** Story 0.12 RTL acceptance gate scenarios a/b/c pass against this view [Source: architecture.md §2.2; ux-design-specification.md Component 9 Accessibility; Story 0.12 NFR-A9].

## Tasks / Subtasks

### Backend — Endpoint handler

- [ ] **T1 (AC: 1, 5)** — Flesh out `GET /v1/statements/t/{token}` in `Slices/PublicStatements/PublicStatements.Endpoints.cs`
  - [ ] On valid token: loads `Statement` + related `JournalEntries` + `Contact` lookup via cross-slice query handler
  - [ ] Maps to `PublicStatementPayload` response (new DTO; NOT shared with owner-workspace response shapes)
  - [ ] Applies `PublicStatementHeadersFilter` (Story 4.4 created, Story 4.5 applies)
  - [ ] Preserves constant-time invariant on all rejection branches
- [ ] **T2 (AC: 1)** — Create `PublicStatementPayload` sealed record + validator in `Slices/PublicStatements/View/`
  - [ ] Fields per AC1 schema — no internal IDs
  - [ ] `PublicStatementPayload.From(Statement, Contact, IReadOnlyList<JournalEntry>)` static factory
- [ ] **T3 (AC: 1)** — Create `GetStatementByTokenHandler`
  - [ ] Input: validated `CapabilityTokenRecord`
  - [ ] Queries `Statement` + `Contact` + `JournalEntry[]` via read-side repository
  - [ ] Builds payload, returns 200

### Backend — Security headers

- [ ] **T4 (AC: 5)** — Extend `PublicStatementHeadersFilter` with CSP + Referrer-Policy + HSTS + X-Content-Type-Options
  - [ ] Frame-ancestors 'none' (clickjacking prevention)
  - [ ] Precise CSP string from AC5
- [ ] **T5 (AC: 5)** — Add CSP unit test — assert exact header value

### Backend — Telemetry

- [ ] **T6 (AC: 8)** — Define `StatementViewedEvent` in `Faktuboh.Application/Telemetry/Events/`
  - [ ] Fields: IpHash, UaHash, StatementId, TokenHashPrefix8, Locale, Channel = "debtor"
  - [ ] Emit from handler on successful read

### Frontend — StatementViewComponent

- [ ] **T7 (AC: 2, 7)** — Create `apps/public-statement/src/app/features/statement-view/statement-view.component.ts`
  - [ ] Standalone, OnPush, zoneless, `selector: 'ps-statement-view'`
  - [ ] Signal inputs for state + payload
  - [ ] Interstitial state is initial; tap transitions to loading → rendered
  - [ ] Lazy-loaded via route `loadComponent`
- [ ] **T8 (AC: 2)** — Create `statement-view.component.html`
  - [ ] Structure per UX-DR13 composition
  - [ ] `@defer (on viewport)` wraps transactions list
  - [ ] Uses `ContactAvatar`, `AmountDisplay`, `ReconciliationMeta` from `libs/domain-ui`
- [ ] **T9 (AC: 2)** — Create `statement-view.component.scss` — logical properties only
- [ ] **T10 (AC: 2)** — Create local `StatementTransactionRow` component
  - [ ] Read-only row: date (localized) + amount (AmountDisplay xs) + optional note

### Frontend — Interstitial gate

- [ ] **T11 (AC: 7)** — Create `InterstitialGateComponent` in `statement-view/`
  - [ ] Renders bilingual "Tap to view" button
  - [ ] On tap, sets internal signal `tapped = true`
  - [ ] Parent `statement-view.component` watches and triggers API call
  - [ ] Emits `statement_interstitial_shown` telemetry on mount, `statement_interstitial_tapped` on click

### Frontend — Data fetch

- [ ] **T12 (AC: 1, 6)** — Create `apps/public-statement/src/app/core/api/public-statement.client.ts` (handwritten per delegated decisions)
  - [ ] `getByToken(): Observable<PublicStatementPayload>` — reads token from SessionStorage via interceptor
  - [ ] Minimal types: `PublicStatementPayload`, `StatementState` — match backend JSON shape
  - [ ] File header comment: `// Handwritten client — exempt from ADR-005 no-handwritten-shared-contracts rule per Epic 4 delegated decisions (keep public bundle small)`
- [ ] **T13 (AC: 6)** — HTTP interceptor reads SessionStorage token
  - [ ] Attaches token to path: `GET /v1/statements/t/{token}`
  - [ ] ONLY for this single endpoint; all other requests passed through

### Frontend — State machine

- [ ] **T14 (AC: 9)** — State machine in `statement-view.component.ts`
  - [ ] Signal `state = signal<StatementState>('interstitial')`
  - [ ] Transitions: `interstitial → loading → (viewable | revoked | error)`
  - [ ] On `revoked` (any 404 from backend): render "This statement is no longer available" (no detail)
  - [ ] On `error` (network / timeout): render retry button

### Frontend — i18n

- [ ] **T15 (AC: 4)** — Synchronous Transloco config for public app
  - [ ] Bundles `ar.json` + `en.json` as chunks
  - [ ] Set `lang` + `dir` on `<html>` based on Accept-Language on first navigation
- [ ] **T16 (AC: 4)** — Locale-aware numeral formatting
  - [ ] `AmountDisplay` (from `libs/domain-ui`) already supports `Intl.NumberFormat`
  - [ ] Verify with test fixtures in both locales

### Tests — Backend

- [ ] **T17 (AC: 1, 5)** — Integration tests in `tests/Faktuboh.Api.Tests/Slices/PublicStatements/`
  - [ ] `GetStatementByTokenEndpointTests` — happy path returns 200 + expected payload
  - [ ] Response has expected headers (Cache-Control, X-Robots-Tag, Vary, CSP, HSTS, Referrer-Policy, X-Content-Type-Options)
  - [ ] Constant-time invariant still holds (valid/revoked/expired/unknown → same-shape rejection)
- [ ] **T18 (AC: 8)** — Telemetry emission test

### Tests — Frontend

- [ ] **T19 (AC: 2, 7)** — Vitest component tests
  - [ ] Interstitial renders first; no API call fires
  - [ ] Tap triggers API call + state transitions
  - [ ] Revoked payload → "no longer available" message
  - [ ] Error state shows retry button
- [ ] **T20 (AC: 10)** — Playwright + axe-core a11y tests
  - [ ] Zero WCAG AA violations in `ar` + `en`
  - [ ] Keyboard navigation: wordmark → toggle → tap → ...

### Tests — E2E

- [ ] **T21 (AC: 6)** — Playwright E2E: `/t/{token}` → after navigation, `window.location.pathname === '/viewing'`
- [ ] **T22 (AC: 2, 7)** — Full user flow: open link → tap interstitial → statement renders with correct data

## Dev Notes

### Critical guardrails

1. **Interstitial is non-negotiable.** Preview bots auto-render URLs (WhatsApp, Slack, iMessage, Facebook). Without the interstitial, they consume tokens + skew telemetry. The interstitial is UX, NOT a security primitive — the backend rate-limit + cost cap are the real defenses.
2. **No Auth0 SDK. No auth headers.** This is the public surface. `libs/core/auth` is compile-excluded per Story 4.2. The only client state is SessionStorage-scoped tab state.
3. **Handwritten TS client is an explicit ADR-005 exemption.** File header comment cites the decision. NSwag generates for owner-workspace only.
4. **Route is `/t/{token}` at the URL layer but `/viewing` post-guard.** All internal routing happens at `/viewing`. If the user refreshes the tab, they land on `/viewing` with SessionStorage token; if the tab is closed, token is gone and they need the original URL. This is intentional — no cross-tab leakage.
5. **CSP `frame-ancestors 'none'`** — clickjacking defence. A malicious page cannot iframe the statement.
6. **No internal IDs on the wire.** `contactId`, `ownerId`, `statementId` are internal. The payload uses `statementId` but only as an opaque correlation marker for telemetry. Don't expose `ownerSubjectId` or `contactId` per NFR-S8.
7. **`@defer` is zoneless-compatible.** Angular 21 `@defer (on viewport)` works without zone.js. Verified in tests.

### Source tree

```
backend/
├── src/Faktuboh.Api/Slices/PublicStatements/
│   ├── PublicStatements.Endpoints.cs                           # MODIFY (flesh out GET handler)
│   └── View/
│       ├── GetStatementByTokenHandler.cs                       # NEW
│       ├── PublicStatementPayload.cs                           # NEW
│       └── StatementViewedEvent.cs                             # NEW (or in Telemetry/Events/)
├── src/Faktuboh.Api/Slices/Statements/Infrastructure/
│   └── PublicStatementHeadersFilter.cs                         # MODIFY (add CSP + HSTS + etc.)
└── tests/Faktuboh.Api.Tests/Slices/PublicStatements/
    ├── GetStatementByTokenEndpointTests.cs                     # NEW
    ├── StatementHeadersTests.cs                                # NEW
    └── StatementViewedTelemetryTests.cs                        # NEW

frontend/apps/public-statement/src/app/features/statement-view/
├── statement-view.component.ts                                 # NEW
├── statement-view.component.html                               # NEW
├── statement-view.component.scss                               # NEW
├── statement-view.component.spec.ts                            # NEW
├── interstitial-gate.component.ts                              # NEW
├── statement-transaction-row.component.ts                      # NEW
└── state-machine.ts                                            # NEW

apps/public-statement/src/app/core/api/
└── public-statement.client.ts                                  # NEW (handwritten, exempted)

apps/public-statement/e2e/
├── statement-view.spec.ts                                      # NEW (Playwright)
└── a11y-rtl.spec.ts                                            # NEW

libs/core/i18n/transloco/
├── public/ar.json                                              # NEW (statement-view keys)
└── public/en.json                                              # NEW
```

### Architecture compliance

- **FR29, FR35, FR39** — all bound here.
- **§2.9 items 1-11** (full public-surface hardening) — every item is now active.
- **§4.3 D4.6** — token-based read flow lands.
- **§5.4** (frontend conventions) — zoneless, Signal Forms (Story 4.6), selector prefix `ps-`.
- **NFR-P5** (< 2s statement page) — Lighthouse nightly asserts.
- **NFR-S8** (no internal IDs in URLs or payloads).
- **UX-DR13 StatementView component API** — consumed faithfully.
- **ADR-001 two-target** — this code lives only in `apps/public-statement`.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Angular | 21 | Zoneless; `@defer` for below-fold |
| PrimeNG | 21 | `Button`, `Tooltip` |
| Transloco | latest | Bundled synchronously for public target |
| axe-core | latest | A11y test |
| Playwright | latest | E2E + a11y assertion |

### Testing requirements

- **Unit:** Interstitial state machine; token/interstitial tap sequence.
- **Integration:** Full payload shape + security headers.
- **Telemetry:** `AssertEventEmitted("statement_viewed")`.
- **E2E (Playwright):** Happy path + revoked + error; a11y in both locales.
- **Performance:** Lighthouse CI (nightly, Story 4.3).

### Previous Story Intelligence

**Prerequisites:**
- Story 0.12 — RTL acceptance gate harness (AC10 relies on scenarios a/b/c)
- Story 1.1 — bilingual shell + design tokens + RTL pipeline
- Story 1.9 — Phase 1 component API (`ContactAvatar`, `AmountDisplay`)
- Story 4.1 — capability-token scheme + constant-time endpoint stub + `statement-route.guard`
- Story 4.2 — `apps/public-statement` scaffold
- Story 4.3 — budget + LCP nightly
- Story 4.4 — statement issuance + `PublicStatementHeadersFilter`

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.5
- UX StatementView component — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) Component 9 + Flow 3 Recipient side
- Public-surface hardening — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.9 all items
- FR29, FR35, FR39, NFR-P5, NFR-S8 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Phase 1 component APIs — [_bmad-output/implementation-artifacts/1-9-phase-1-component-api-design-system-library.md](./1-9-phase-1-component-api-design-system-library.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- This is the first story where a debtor sees the product. Conversion funnel attribution (Story 4.8) starts measuring from here.
- `PublicStatementPayload` is explicitly not shared with owner-workspace DTOs — public surface has a distinct schema.

### File List

_Populated during implementation._
