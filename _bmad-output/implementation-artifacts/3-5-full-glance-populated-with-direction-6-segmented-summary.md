# Story 3.5: Full Glance populated with Direction 6 Segmented Summary

Status: ready-for-dev

## Story

As a **user with multiple contacts and denominations**,
I want the **Glance at `/dashboard` to show both HeroTiles populated + both DebtRow columns + the settled-debts accordion, at the correct Glance Maturity Stage for my data volume**,
so that I **see my complete financial position in under 2 seconds** (NFR-P6), with TTR < 2s perceived, TTI < 500ms broadband / 1.5s 3G, CLS < 0.05 per "The Glance" engineering contract. Stage 1 (0-10 debts) hides sort/filter entirely; Stage 2 (11-50) reveals a sort Popover; Stage 3 (51+) activates sort + filter chips + virtual scroller at `itemSize=64` desktop / `72` mobile. Balance correctness is 100% — property-based tests assert `sum(transactions) == stored_balance` for every contact-denomination pair.

**Covers requirements:** FR27 (Glance populated view), FR31 (Direction 6 layout), NFR-P6 (dashboard 2s), Glance Maturity Model §2.6, UX-DR5 (AmountDisplay xl variant), UX-DR7 (DebtRow), UX-DR10 (HeroTile), UX-DR13 (settled accordion).

**Why this slice is the Epic 3 keystone:** Every prior Epic 3 story writes to the Glance; this story is the first time the Glance is populated at all three Stages with real data. The NFR-P6 2s-TTR contract is enforced here. The property-based correctness test is the compliance gate that blocks merge on balance drift.

## Acceptance Criteria

### AC1 — GET /v1/dashboard returns the Glance shape

**Given** the user has journal entries across N contacts and M denominations
**When** the frontend issues `GET /v1/dashboard`
**Then** the response is `DashboardResponse` with:
  - `heroTiles: { receivable: HeroTileData, payable: HeroTileData }` — each with `primaryDenomination: DenominationSummary` + `secondaryDenominations: DenominationSummary[]` (max 3; overflow count as `additionalDenominationCount: number`) + `debtCount: number` (Stage 2+ display)
  - `receivableDebts: DebtRowData[]` — active receivables, sorted by default rule (`occurred_on` desc)
  - `payableDebts: DebtRowData[]` — active payables, same sort
  - `settledDebts: SettledDebtGroup[]` — settled-pair groupings (denomination pairs that net to zero), collapsed in the accordion
  - `stage: 1 | 2 | 3` — computed server-side from `active_debt_count`
**And** p95 response time < 400ms at 1000-debt baseline (leaves 1.6s for render per NFR-P6)

### AC2 — Two HeroTiles render Direction 6 Segmented Summary

**Given** the dashboard renders
**When** HeroTile components mount
**Then** two `<p-card>`-backed HeroTile instances render with Arabic role labels "لي" / "عليّ"
**And** each HeroTile shows `AmountDisplay variant="xl"` for the primary denomination (the user's most-frequent denomination for that direction, server-computed)
**And** Stage 2+ shows `debtCount` secondary label ("12 debts")
**And** secondary denominations render as smaller `AmountDisplay variant="compact"` pills (max 3); overflow "+N more" pill reveals a popover listing the rest
**And** HeroTiles use `--primary` for receivable, `--danger` for payable role tokens
**And** layout: side-by-side on desktop (flex 1 each); stacked on mobile with receivable first

### AC3 — Two DebtRow column lists render

**Given** the Glance has active debts
**When** the DebtRow columns render
**Then** each column uses `<p-dataView layout="list">` with `DebtRow` components from `libs/domain-ui` (Epic 1 Story 1.9)
**And** column headers are "لي" / "عليّ" with a "+" button at the right (opens Story 3.1's record form pre-selected with that column's direction)
**And** sort order default: `occurred_on DESC, recorded_at DESC`

### AC4 — Settled-debts accordion

**Given** the user has settled denomination pairs (one receivable matches one payable of the same denomination + direction opposite, fully netted by a later transaction — Epic 5 settlement flow; Phase-A approximation: all-zeroed denominations per contact grouped as "settled")
**When** the accordion renders at the bottom
**Then** `<p-accordion>` header reads "المُسدَّدة ({N})" where N is the settled-pair count
**And** the accordion is collapsed by default; expanding reveals list of settled pairs
**And** if N = 0, the accordion is hidden entirely (not shown as "0 settled")

### AC5 — Glance Maturity Stage progressive disclosure

**Given** server-reported `stage: 1 | 2 | 3`
**When** the dashboard renders
**Then** **Stage 1 (0-10 active debts):** no sort control, no filter chips, no virtual scroller; plain `@for` loops for rows
**And** **Stage 2 (11-50):** sort Popover surfaces above each column with options `date-desc | date-asc | amount-desc | amount-asc | contact-az`; debtCount shown in HeroTile
**And** **Stage 3 (51+):** sort Popover + filter chips (by denomination + by direction) + `<p-virtualScroller [itemSize]="64">` (desktop) / `72` (mobile) activates
**And** Stage transitions are purely cosmetic — the data payload is identical; only rendering differs

### AC6 — NFR-P6 performance contract

**Given** the user loads `/dashboard`
**When** Lighthouse runs in CI
**Then** TTR (Time-to-Render) perceived < 2s (skeleton visible < 500ms; full data rendered < 2s)
**And** TTI < 500ms on broadband / < 1.5s on throttled 3G
**And** CLS (Cumulative Layout Shift) < 0.05 — skeleton dimensions match final content footprint
**And** zero loading spinners; skeletons only per "The Glance" engineering contract
**And** no animated counters — tabular numerals transition via React-style diff, not via incrementing animation (Stability Signals Trust)

### AC7 — Balance-correctness property test

**Given** the Testcontainers Postgres integration test
**When** FsCheck generates 10k random transaction sequences (varied contacts, denominations, dates, directions, including edits and deletes)
**Then** for every `(contact_id, denomination_code, direction)` tuple: `SUM(amount) FROM active journal_entries == HeroTile/DebtRow displayed balance`
**And** the property is asserted as a CI-required check
**And** balance drift on ANY tuple fails the build

### AC8 — RTL + a11y

**Given** the user's locale is Arabic
**When** the dashboard renders in `dir="rtl"`
**Then** receivable (لي) column is on the right; payable (عليّ) on the left
**And** sort/filter controls flip to logical `inset-inline-end`
**And** axe-core zero WCAG 2.1 AA violations
**And** screen reader announces the Glance structurally ("Two summaries, two debt lists, one settled section")
**And** manual VoiceOver Arabic walkthrough passes (NFR-A9)

### AC9 — Telemetry

**Given** dashboard loads
**When** the response arrives
**Then** telemetry `glance_rendered` emits with `{ opaque_user_id, stage, receivable_denomination_count, payable_denomination_count, active_debt_count, settled_pair_count, ttr_ms }`

## Tasks / Subtasks

### Backend

- [ ] **T1 (AC: 1, 5)** — Create `Slices/Debts/Dashboard/GetDashboardHandler.cs`
  - [ ] Endpoint `group.MapGet("/v1/dashboard", ...)` under Debts slice
  - [ ] Single aggregation query with CTEs for `hero_receivable`, `hero_payable`, `active_debts`, `settled_pairs`, `stage_computation`
  - [ ] `DashboardResponse` sealed record matching AC1 shape
- [ ] **T2 (AC: 1, 6)** — Add composite indexes
  - [ ] `ix_journal_entries_owner_direction_denom` on `(owner_subject_id, direction, denomination_code) WHERE valid_to IS NULL`
  - [ ] `ix_journal_entries_owner_occurred_on` on `(owner_subject_id, occurred_on DESC) WHERE valid_to IS NULL`
- [ ] **T3 (AC: 1)** — Stage computation: server-side via `SELECT COUNT(*) FROM journal_entries WHERE owner_subject_id = @id AND valid_to IS NULL`; map `{0-10 → 1, 11-50 → 2, 51+ → 3}`
- [ ] **T4 (AC: 2)** — Primary denomination per direction: `SELECT denomination_code FROM journal_entries WHERE direction = @dir GROUP BY denomination_code ORDER BY COUNT(*) DESC LIMIT 1`
- [ ] **T5 (AC: 4)** — Settled-pairs approximation (Phase-A): per contact, per denomination — if `SUM(receivable) == SUM(payable)`, mark as settled; full settlement aggregate lives in Epic 5
- [ ] **T6 (AC: 7)** — FsCheck property test: seed random transactions via integration test; call GET /v1/dashboard; assert balance invariants per tuple
- [ ] **T7 (AC: 9)** — Emit `glance_rendered` telemetry — TTR measured on the server side as `recorded_at - request_received`

### Frontend

- [ ] **T8 (AC: 2, 3, 4, 5, 8)** — Create `apps/owner-workspace/src/app/features/dashboard/dashboard.component.ts`
  - [ ] Standalone + signals + OnPush; zoneless
  - [ ] `rxResource()` loads `GET /v1/dashboard`
  - [ ] Composes: `<p-card>` x2 HeroTiles + `<p-dataView layout="list">` x2 DebtRow columns + `<p-accordion>` settled footer + `<p-virtualScroller>` (Stage 3 only)
  - [ ] Stage-driven conditional rendering via computed signal
  - [ ] `+` button per column opens Story 3.1 form pre-selected with direction
- [ ] **T9 (AC: 2)** — HeroTile component (from `libs/domain-ui`, Epic 1 Story 1.9) consumed with populated data — NO modifications required; Epic 1's frozen API is the consumption contract
- [ ] **T10 (AC: 5)** — Virtual scroller wrap: Stage 3 wraps `<p-dataView>` inside `<p-virtualScroller [itemSize]="isMobile() ? 72 : 64">`
  - [ ] `isMobile` signal via Angular `BreakpointObserver` on `(max-width: 767px)`
  - [ ] Lock `DebtRow` rendered height via CSS to match `itemSize` exactly
- [ ] **T11 (AC: 6)** — Skeleton loading state
  - [ ] Reuse Story 1.8's skeleton primitives (empty Glance skeleton)
  - [ ] Skeleton matches final layout dimensions exactly — CLS < 0.05 invariant
- [ ] **T12 (AC: 6)** — Lighthouse CI config: asserting TTR + TTI + CLS budgets via `@lhci/cli`; added to CI workflow

### Tests

- [ ] **T13 (AC: 1, 7)** — `GetDashboardHandlerTests` — table-driven across Stages 1/2/3; settled-pairs recognition
- [ ] **T14 (AC: 1, 6, 7)** — `GetDashboardEndpointTests` (Integration)
  - [ ] FsCheck property test seeds 10k sequences; asserts balance invariants
  - [ ] Perf: p95 < 400ms at 1000-debt seed
  - [ ] `EXPLAIN` plan snapshot asserts index use
- [ ] **T15 (AC: 5)** — Vitest component: Stage derivation; virtual-scroller activation at Stage 3; +button deep link
- [ ] **T16 (AC: 6)** — Lighthouse CI as part of E2E pipeline
- [ ] **T17 (AC: 8)** — Playwright E2E: full Glance in Arabic + English; axe a11y; RTL visual regression; manual screen-reader notes in test plan

## Dev Notes

### Critical guardrails

1. **Balance correctness is a ship-blocker.** The FsCheck property test runs in CI — drift on any tuple fails the build. This is the highest-value test in the codebase.
2. **No cross-denomination aggregation.** HeroTile's "primary denomination" is chosen by frequency, NOT by summing. Secondary denominations render as separate pills.
3. **Stage is server-computed, not client-guessed.** The response payload includes `stage`; the frontend trusts it. Rationale: avoids client-computed race with in-flight writes.
4. **Zero spinners; skeletons only.** Any `<p-progressSpinner>` in the dashboard is a bug. Skeletons must match final dimensions (CLS invariant).
5. **Virtual scroller `itemSize` is fixed + matches CSS.** Desktop 64, mobile 72. If a row's actual height deviates, scrolling offset calculations fail. Lock via CSS `block-size: 64px`.
6. **No animated counters.** Tabular-numeral diffs (React-like keyed rerender) are fine; `requestAnimationFrame`-driven counting animation is not. Stability Signals Trust principle.
7. **Server-push deferred.** Dashboard re-renders via `rxResource.reload()` triggered by post-mutation navigation (Story 3.1/3.2/3.3's success handlers call `dashboardResource.reload()`). SSE/WebSocket push is post-MVP.
8. **Settled-pairs is an approximation at MVP.** Per-contact per-denomination net-zero check. Real settlement semantics land in Epic 5 when compound journal entries are implemented. The Phase-A approximation is intentional — documented in the slice's README.

### Source tree

```
backend/
├── src/Faktuboh.Api/Slices/Debts/Dashboard/
│   ├── GetDashboardRequest.cs                        # NEW (empty — no query params at MVP)
│   ├── GetDashboardResponse.cs                       # NEW
│   ├── HeroTileData.cs                               # NEW
│   ├── DenominationSummary.cs                        # NEW
│   ├── DebtRowData.cs                                # NEW
│   ├── SettledDebtGroup.cs                           # NEW
│   └── GetDashboardHandler.cs                        # NEW
├── src/Faktuboh.Infrastructure/Persistence/Migrations/
│   ├── YYYYMMDD_AddJournalEntryOwnerDirectionDenomIdx.cs  # NEW
│   └── YYYYMMDD_AddJournalEntryOwnerOccurredOnIdx.cs       # NEW
└── tests/Faktuboh.Api.Tests/Slices/Debts/Dashboard/
    ├── GetDashboardHandlerTests.cs                   # NEW
    ├── GetDashboardEndpointTests.cs                  # NEW (Integration)
    └── BalancePropertyTests.cs                       # NEW (FsCheck)

frontend/
├── apps/owner-workspace/src/app/features/dashboard/
│   ├── dashboard.component.ts                        # MODIFY (Story 1.8 empty shell exists)
│   ├── dashboard.component.html                      # MODIFY
│   ├── dashboard.component.scss                      # MODIFY (logical props)
│   ├── dashboard.component.spec.ts                   # MODIFY
│   └── dashboard-skeleton.component.ts               # NEW (skeleton matches final)
└── apps/owner-workspace/e2e/dashboard/
    └── glance-populated.spec.ts                       # NEW
```

### Glance composition (reference layout)

```html
<div class="glance-shell" [dir]="locale()">
  <!-- HeroTiles: side-by-side desktop, stacked mobile (receivable first) -->
  <section class="hero-tiles-grid">
    <ow-hero-tile role="receivable" [data]="data.heroTiles.receivable" />
    <ow-hero-tile role="payable"    [data]="data.heroTiles.payable" />
  </section>

  <!-- Two parallel DebtRow columns -->
  <section class="debt-columns-grid">
    <ow-debt-column direction="receivable" [rows]="data.receivableDebts" [stage]="data.stage" />
    <ow-debt-column direction="payable"    [rows]="data.payableDebts"    [stage]="data.stage" />
  </section>

  <!-- Settled accordion (hidden when settledDebts.length === 0) -->
  @if (data.settledDebts.length) {
    <p-accordion>
      <p-accordionTab header="المُسدَّدة ({{ data.settledDebts.length }})">
        @for (group of data.settledDebts; track group.id) { <!-- ... --> }
      </p-accordionTab>
    </p-accordion>
  }

  <!-- GlanceFab (mobile only) -->
  <ow-glance-fab class="only-mobile" />
</div>
```

### Dashboard aggregation query (sketch)

```sql
WITH hero_sums AS (
  SELECT direction, denomination_code, SUM(amount) AS amount, COUNT(*) AS txn_count
  FROM journal_entries
  WHERE owner_subject_id = @id AND valid_to IS NULL
  GROUP BY direction, denomination_code
),
primary_denom AS (
  SELECT DISTINCT ON (direction) direction, denomination_code
  FROM hero_sums ORDER BY direction, txn_count DESC
),
active_count AS (
  SELECT COUNT(*) AS n FROM journal_entries
  WHERE owner_subject_id = @id AND valid_to IS NULL
)
SELECT
  (SELECT json_build_object('receivable', ..., 'payable', ...) FROM hero_sums ...) AS heroTiles,
  (SELECT json_agg(...) FROM journal_entries WHERE direction = 'receivable' AND ...) AS receivableDebts,
  (SELECT json_agg(...) FROM journal_entries WHERE direction = 'payable'    AND ...) AS payableDebts,
  (SELECT json_agg(...) FROM settled_pairs_cte) AS settledDebts,
  CASE WHEN n <= 10 THEN 1 WHEN n <= 50 THEN 2 ELSE 3 END AS stage
FROM active_count;
```

### Testing standards

Per Story 3.1. **Two ship-blocking additions:**
1. FsCheck balance-correctness property test — CI-required
2. Lighthouse CI with TTR/TTI/CLS assertions — CI-required

### Project Structure Notes

Dashboard lives in `Slices/Debts/Dashboard/` (Debts slice owns the read-side surface). The `JournalEntryCreatedHandler` projection stub from Story 3.1 grows into a multi-event consumer here (Created/Updated/Deleted) but remains log-only at MVP — primary store IS the query.

### Architecture compliance

- **§4 D4.1** Postgres primary; **§4 D4.4** no CQRS
- **ADR-022** `valid_to IS NULL` filter
- **ADR-024** Minimal APIs
- **UX-DR5** AmountDisplay xl variant; **UX-DR7** DebtRow; **UX-DR10** HeroTile; **UX-DR13** settled accordion
- **Glance Maturity Model** Stages 1/2/3 server-computed
- **FR27, FR31** Direction 6 Segmented Summary
- **NFR-P6** 2s TTR, 500ms TTI broadband, CLS < 0.05
- **NFR-A9** RTL parity (scenarios a + b + c)

### Library/framework requirements

Per Story 3.1. Additional:
- PrimeNG `<p-card>`, `<p-dataView>`, `<p-accordion>`, `<p-virtualScroller>`, `<p-popover>`, `<p-chip>` — APIs via `@primeng/mcp`
- `@lhci/cli` (Lighthouse CI) for perf budgets
- FsCheck for property-based balance testing

### Testing requirements

| Category | Tools |
|---|---|
| Unit | xUnit v3 + FluentAssertions + FsCheck (property-based) |
| Integration | Testcontainers Postgres + 1000-debt seed + perf p95 |
| Property | FsCheck at 10k sequences — balance invariant |
| Component | Vitest + Angular Testing Library (stage logic, virtual-scroller activation) |
| E2E + Lighthouse | Playwright + `@lhci/cli` CI gates |

### Previous Story Intelligence

- **Story 1.8** (Empty Glance skeleton) — skeleton primitives reused; dashboard component shell already scaffolded with empty state; this story populates it
- **Story 1.9** (Phase-1 components) — `HeroTile`, `DebtRow`, `GlanceShell`, `AmountDisplay`, `ContactAvatar`, `GlanceFab` all consumed unchanged; single-owner rule holds
- **Story 1.11** (Performance budget + visual regression CI gates) — Lighthouse CI workflow from there is extended with Glance-specific budgets
- **Story 2.4** (List contacts with balances) — aggregation index pattern
- **Story 3.1-3.3** (debt CRUD) — populate the journal_entries table whose reads this story serves
- **Story 3.6, 3.7** (dashboard charts) — add ON TOP of this Glance; their slice handlers share the aggregation query backbone

## References

- [epics.md Story 3.5](../planning-artifacts/epics.md)
- [architecture.md §4 D4.1, D4.4](../planning-artifacts/architecture.md)
- [architecture.md §3.5 compliance; bitemporal filters](../planning-artifacts/architecture.md)
- [prd.md FR27, FR31, NFR-P6](../planning-artifacts/prd.md)
- [ux-design-specification.md "The Glance" engineering contract](../planning-artifacts/ux-design-specification.md)
- [ux-design-specification.md §2.6 Glance Maturity Model](../planning-artifacts/ux-design-specification.md)
- [ux-design-specification.md UX-DR5, DR7, DR10, DR13, DR14](../planning-artifacts/ux-design-specification.md)
- [ux-design-specification.md Direction 6 Segmented Summary](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
