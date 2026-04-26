# Story 3.6: Dashboard bar chart — distribution across contacts

Status: ready-for-dev

## Story

As a **user tracking debts across many counterparties**,
I want a **bar chart on `/dashboard` showing my debt distribution across contacts per denomination**,
so that I can **spot which counterparties concentrate my exposure** (FR32), with RTL-correct rendering across legends, axes, tooltips, and labels (NFR-A9d), tabular numerals and no animated counters (Stability Signals Trust), role-colored bars (`--primary` receivable, `--danger` payable), and WCAG 2.1 AA screen-reader narration.

**Covers requirements:** FR32 (bar chart distribution across contacts), NFR-A9d (RTL chart compliance — **ship-blocker**), UX color-blindness rule (pattern backup for role colors).

## Acceptance Criteria

### AC1 — Denomination selector + server data

**Given** the user has debts across multiple denominations
**When** the bar chart renders on `/dashboard`
**Then** a denomination selector `<p-select>` appears above the chart with denominations the user holds (populated from the dashboard response payload extended from Story 3.5)
**And** switching denomination re-queries `GET /v1/dashboard/chart/contacts?denomination={code}` and re-renders the chart
**And** the default selection is the user's primary denomination (already computed server-side in Story 3.5)

### AC2 — One bar per contact, net balance per denomination

**Given** a denomination is selected
**When** the chart renders
**Then** the chart shows one horizontal bar per contact with a non-zero balance in that denomination
**And** each bar's length equals the **net balance** for that contact-denomination pair: `SUM(receivable) - SUM(payable)` (positive = they owe you; negative = you owe them)
**And** bars with positive values use `--primary` color; negative use `--danger`
**And** no cross-denomination aggregation ever (chart is single-denomination-scoped by design)

### AC3 — RTL chart compliance (NFR-A9d ship-blocker)

**Given** the user's locale is Arabic
**When** the chart renders in `dir="rtl"`
**Then** bars grow from right to left (value axis origin at right)
**And** legend, axis labels, tooltip text all render right-to-left
**And** PrimeNG `<p-chart>` is configured with Chart.js options `{ scales: { x: { position: 'right', reverse: true } }, plugins: { legend: { rtl: true, textDirection: 'rtl' }, tooltip: { rtl: true, textDirection: 'rtl' } } }`
**And** a Playwright visual-regression test against the RTL screenshot passes (ship-blocker gate)

### AC4 — Color-blindness pattern backup

**Given** WCAG 2.1 AA color-differentiation
**When** bars render
**Then** receivable bars additionally use solid fill; payable bars use diagonal-stripe pattern (SVG pattern on Chart.js `backgroundColor` via `patternomaly` or custom canvas pattern)
**And** a11y test harness verifies legend entries include both color + pattern descriptors

### AC5 — Stability Signals Trust rendering

**Given** the chart renders
**When** data loads or updates
**Then** NO animated bar-grow transitions (set Chart.js `animation: false`)
**And** tabular numerals applied to all numeric labels
**And** hover reveals tooltip with contact name + denomination + exact amount (no cross-denomination sum)

### AC6 — Screen-reader narration

**Given** a screen reader (VoiceOver, TalkBack, NVDA) encounters the chart
**When** it focuses the chart container
**Then** an `aria-label` summarizes: "Bar chart, {N} bars, denomination {code}. Contact {name} has balance {amount}, ..."
**And** a hidden `<dl>` companion table lists each contact + value for screen-reader tabulation
**And** axe-core zero WCAG 2.1 AA violations

### AC7 — Performance + empty state

**Given** a single-denomination query
**When** the chart data is requested
**Then** response p95 < 200ms per NFR-P7
**And** if the user has no debts in any denomination, the chart area renders an empty state with copy "Record your first debt to see the distribution" + CTA routing to the record-debt form
**And** single-contact case renders a single bar without error

## Tasks / Subtasks

### Backend

- [ ] **T1 (AC: 1, 2)** — Create `Slices/Debts/Charts/GetContactDistributionChartHandler.cs`
  - [ ] Endpoint `group.MapGet("/v1/dashboard/chart/contacts", ...)` with `denomination` query param (required)
  - [ ] Query: `SELECT contact_id, c.name, SUM(CASE WHEN direction='receivable' THEN amount ELSE -amount END) AS net FROM journal_entries je JOIN contacts c ON ... WHERE je.owner_subject_id = @id AND je.denomination_code = @denom AND je.valid_to IS NULL GROUP BY c.id, c.name HAVING SUM(...) <> 0 ORDER BY ABS(SUM(...)) DESC`
  - [ ] `ContactDistributionChartResponse` with `bars: { contactId, contactName, net: string }[]`
- [ ] **T2 (AC: 7)** — Reuse the Story 2.4 / 3.5 index pattern; no new migration required
- [ ] **T3 (AC: 7)** — Emit `chart_viewed` telemetry `{ opaque_user_id, chart_type: "contact-distribution", denomination_code, bar_count }`

### Frontend

- [ ] **T4 (AC: 1, 2, 3, 5)** — Create `apps/owner-workspace/src/app/features/dashboard/charts/contact-distribution-chart.component.ts`
  - [ ] Standalone + signals + OnPush
  - [ ] Imports PrimeNG `ChartModule`; uses `<p-chart type="bar">`
  - [ ] Denomination selector via `<p-select>` bound to `selectedDenomination` signal
  - [ ] `rxResource` keyed on `selectedDenomination()` triggers GET; empty/loading/error states handled
  - [ ] Chart config: horizontal bars (`indexAxis: 'y'`), `animation: false`, tabular numerals via font-variant-numeric CSS
- [ ] **T5 (AC: 3)** — RTL chart configuration
  - [ ] Detect locale via Transloco `activeLang()` signal
  - [ ] Conditionally apply RTL-aware Chart.js options
  - [ ] Verify via visual regression screenshot baseline
- [ ] **T6 (AC: 4)** — Color-blindness pattern backup
  - [ ] Generate diagonal-stripe canvas pattern in a helper utility `libs/core/charts/patterns.ts`
  - [ ] Apply to negative (payable) bars; legend entries include pattern swatch
- [ ] **T7 (AC: 6)** — `aria-label` summary + hidden `<dl>` companion table rendered via `@for` loop

### Tests

- [ ] **T8 (AC: 1, 2, 7)** — `GetContactDistributionChartHandlerTests` — table-driven across denominations + empty state
- [ ] **T9 (AC: 2, 7)** — Integration test with Testcontainers — perf p95 < 200ms
- [ ] **T10 (AC: 1, 2, 5)** — Vitest: component renders N bars; selector change triggers new query; empty-state CTA route
- [ ] **T11 (AC: 3)** — Playwright E2E:
  - [ ] English/LTR visual regression baseline
  - [ ] Arabic/RTL visual regression baseline (**ship-blocker per NFR-A9d**)
  - [ ] axe-core zero violations
  - [ ] Screen-reader NVDA smoke test: chart announces summary + tabulated values

## Dev Notes

### Critical guardrails

1. **NFR-A9d RTL chart is a SHIP-BLOCKER.** The visual regression test against the Arabic screenshot is a required CI check. If the Arabic render doesn't match baseline, merge is blocked.
2. **Single-denomination per chart render.** Never sum across denominations — the selector enforces this. A "total exposure" bar chart mixing denominations is forbidden.
3. **No animated bar transitions.** `animation: false` in Chart.js config. Stability Signals Trust principle — animated charts read as marketing, not accounting.
4. **Pattern backup for color blindness.** Role color alone is insufficient — WCAG 2.1 AA requires non-color differentiation. Diagonal stripes on payable bars.
5. **Chart.js is the backend library under PrimeNG's `<p-chart>`.** PrimeNG v21 default is Chart.js 4+; query `@primeng/mcp` for exact API.
6. **Screen-reader companion table is mandatory.** Chart.js canvas output is opaque to SRs; the hidden `<dl>` is the accessibility surface.

### Source tree

```
backend/
├── src/Faktuboh.Api/Slices/Debts/Charts/
│   ├── GetContactDistributionChartRequest.cs         # NEW
│   ├── GetContactDistributionChartResponse.cs        # NEW
│   └── GetContactDistributionChartHandler.cs         # NEW
└── tests/Faktuboh.Api.Tests/Slices/Debts/Charts/
    ├── GetContactDistributionChartHandlerTests.cs    # NEW
    └── GetContactDistributionChartEndpointTests.cs   # NEW

frontend/
├── apps/owner-workspace/src/app/features/dashboard/charts/
│   ├── contact-distribution-chart.component.ts       # NEW
│   ├── contact-distribution-chart.component.html     # NEW
│   ├── contact-distribution-chart.component.scss     # NEW
│   └── contact-distribution-chart.component.spec.ts  # NEW
└── libs/core/charts/
    └── patterns.ts                                    # NEW (diagonal stripes generator)
```

### Chart.js RTL config (reference)

```typescript
const rtlConfig = {
  indexAxis: 'y',
  animation: false,
  scales: {
    x: { position: isRtl ? 'right' : 'left', reverse: isRtl, ticks: { font: { family: 'inherit' } } },
    y: { position: isRtl ? 'right' : 'left' }
  },
  plugins: {
    legend: { rtl: isRtl, textDirection: isRtl ? 'rtl' : 'ltr' },
    tooltip: { rtl: isRtl, textDirection: isRtl ? 'rtl' : 'ltr' }
  }
};
```

### Testing standards

Per Story 3.5. **Visual regression is the keystone test** — without it, RTL chart regressions slip silently.

### Project Structure Notes

Charts live in `Slices/Debts/Charts/` (the Debts slice owns dashboard-adjacent surfaces). The `contact-distribution-chart.component.ts` is a child of `/dashboard`'s route.

### Architecture compliance

- **§4 D4.1** Postgres primary aggregation
- **ADR-024** Minimal APIs
- **PrimeNG v21** `<p-chart>` whitelisted for deferred introduction (architecture §5.4); Epic 3 is the trigger
- **NFR-A9d** RTL chart compliance (ship-blocker)
- **NFR-P7** 200ms
- **UX Stability Signals Trust** — tabular numerals, no animation
- **UX Color-blindness rule** — pattern backup

### Library/framework requirements

Per Story 3.5. Additional:
- `@primeng/mcp`-queried exact `<p-chart>` and `ChartModule` API for v21
- Chart.js 4+ (bundled via PrimeNG v21)
- `patternomaly` npm package OR custom canvas pattern helper — decision: custom helper to avoid dependency bloat (small helper < 30 LOC)

### Testing requirements

| Category | Tools |
|---|---|
| Unit | xUnit v3 + FluentAssertions |
| Integration | Testcontainers Postgres + perf |
| Component | Vitest + Angular Testing Library |
| E2E | Playwright + axe-core + visual regression (RTL ship-blocker) |

### Previous Story Intelligence

- **Story 0.12** (NFR-A9 RTL gate harness) — the RTL visual-regression pipeline this story's chart is tested against
- **Story 1.11** (performance budget + visual regression CI gates) — the baseline storage + diffing harness
- **Story 2.4** (list contacts with balances) — aggregation patterns
- **Story 3.5** (populated Glance) — dashboard resource + denomination selector origin; primary-denomination signal reused here

## References

- [epics.md Story 3.6](../planning-artifacts/epics.md)
- [architecture.md §5.4](../planning-artifacts/architecture.md) PrimeNG Chart whitelisted
- [prd.md FR32, NFR-A9d, NFR-P7](../planning-artifacts/prd.md)
- [ux-design-specification.md Stability Signals Trust](../planning-artifacts/ux-design-specification.md)
- [ux-design-specification.md UX Consistency → Data Density → color-blindness rule](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
