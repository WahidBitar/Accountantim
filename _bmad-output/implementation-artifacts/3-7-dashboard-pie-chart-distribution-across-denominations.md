# Story 3.7: Dashboard pie chart — distribution across denominations

Status: ready-for-dev

## Story

As a **user who holds debts in multiple currencies and metals**,
I want a **pie chart on `/dashboard` showing the relative size of each denomination across my total book**,
so that I can **understand my currency/metal exposure at a glance** (FR33), with one slice per denomination whose size reflects the absolute-value sum in that denomination (no cross-currency conversion — slice sizes reflect unit counts, not value equivalence), WCAG 2.1 AA color-differentiation via hue + pattern backup, and clean single-slice rendering with an explanatory subtitle for single-denomination users.

**Covers requirements:** FR33 (pie chart distribution across denominations), UX color-blindness rule, NFR-A9d (RTL chart compliance).

## Acceptance Criteria

### AC1 — One slice per denomination, sized by absolute-value sum

**Given** the user has debts in ≥ 2 denominations
**When** the pie chart renders on `/dashboard`
**Then** one slice per denomination with slice size = `SUM(ABS(amount))` across all journal entries in that denomination (active only, `valid_to IS NULL`)
**And** absolute value is the size metric — payable and receivable both contribute to the slice's mass
**And** NO cross-currency FX conversion — slice sizes are per-denomination unit counts (e.g., "1000 USD" and "50 g gold" contribute raw)
**And** legend labels include denomination code + unit symbol + absolute total + percentage of total slice area

### AC2 — Color-blindness pattern backup

**Given** WCAG 2.1 AA color-differentiation
**When** slices render
**Then** each slice uses both a distinct hue (from a 10-color accessible palette validated against color-vision-deficiency simulators) AND a distinct SVG pattern (stripes, dots, cross-hatch, etc.)
**And** legend swatches show both color + pattern
**And** axe-core zero WCAG 2.1 AA violations

### AC3 — Single-denomination clean state

**Given** the user has debts in only one denomination
**When** the pie chart renders
**Then** a single full-circle slice renders (no error)
**And** an explanatory subtitle below the chart reads: "All your debts are in {denomination_code}. Add debts in other denominations to see distribution."
**And** legend still shows the single entry with 100%

### AC4 — RTL rendering (NFR-A9d)

**Given** the user's locale is Arabic
**When** the chart renders in `dir="rtl"`
**Then** legend flows right-to-left
**And** slice start angle adjusts so the first legend entry's slice begins at the top (12 o'clock) regardless of direction
**And** tooltip text renders RTL
**And** Playwright visual regression against RTL baseline passes

### AC5 — Stability Signals Trust rendering

**Given** the chart renders
**When** data loads or switches
**Then** `animation: false` in Chart.js config — no pie-grow or rotation animation
**And** tabular numerals in legend
**And** hovering a slice reveals tooltip with denomination + absolute sum + percentage

### AC6 — Screen-reader narration + SR companion table

**Given** a screen reader encounters the chart
**When** focus enters
**Then** `aria-label` summarizes "Pie chart, {N} denominations: {code1} {pct1}%, {code2} {pct2}%, ..."
**And** a hidden `<dl>` lists each denomination + absolute sum + percentage for SR tabulation

### AC7 — Performance

**Given** the chart data is requested
**When** the server query runs
**Then** `GET /v1/dashboard/chart/denominations` p95 < 200ms per NFR-P7
**And** the request is independent from Story 3.6's contact-distribution chart (parallel fetch from dashboard)

## Tasks / Subtasks

### Backend

- [ ] **T1 (AC: 1, 7)** — Create `Slices/Debts/Charts/GetDenominationDistributionChartHandler.cs`
  - [ ] Endpoint `group.MapGet("/v1/dashboard/chart/denominations", ...)`
  - [ ] Query: `SELECT denomination_code, SUM(ABS(amount)) AS total FROM journal_entries WHERE owner_subject_id = @id AND valid_to IS NULL GROUP BY denomination_code ORDER BY total DESC`
  - [ ] `DenominationDistributionChartResponse` with `slices: { denominationCode, absoluteTotal: string, percentage: number }[]`
  - [ ] Percentage computed server-side from total across all denominations
- [ ] **T2 (AC: 7)** — Reuse existing indexes from Story 3.5
- [ ] **T3 (AC: 7)** — Emit `chart_viewed` telemetry with `chart_type: "denomination-distribution"`

### Frontend

- [ ] **T4 (AC: 1, 2, 3, 4, 5)** — Create `apps/owner-workspace/src/app/features/dashboard/charts/denomination-distribution-chart.component.ts`
  - [ ] Standalone + signals + OnPush
  - [ ] `<p-chart type="pie">` with Chart.js 4+ via PrimeNG v21
  - [ ] `rxResource()` loads the endpoint
  - [ ] Single-denomination branch renders explanatory subtitle
- [ ] **T5 (AC: 2)** — Accessible palette utility `libs/core/charts/accessible-palette.ts`
  - [ ] 10-color palette validated via color-vision-deficiency simulator in dev tooling
  - [ ] Each color paired with a matching SVG pattern
  - [ ] Unit test: no two adjacent slices share a hue-or-pattern combination
- [ ] **T6 (AC: 4)** — RTL configuration mirrors Story 3.6's approach
- [ ] **T7 (AC: 6)** — Hidden `<dl>` companion table + `aria-label` summary

### Tests

- [ ] **T8 (AC: 1, 3, 7)** — `GetDenominationDistributionChartHandlerTests` — empty, single-denomination, multi-denomination scenarios
- [ ] **T9 (AC: 7)** — Integration test perf p95 < 200ms
- [ ] **T10 (AC: 1, 3)** — Vitest: single-denomination subtitle renders; multi-slice rendering
- [ ] **T11 (AC: 2)** — Vitest: accessible-palette utility test — pattern-color uniqueness
- [ ] **T12 (AC: 4, 6)** — Playwright E2E:
  - [ ] RTL + LTR visual regression
  - [ ] axe-core zero violations
  - [ ] SR companion table announced correctly (manual smoke + automated snapshot of aria-label text)

## Dev Notes

### Critical guardrails

1. **Absolute-value sum, NOT signed net.** Pie-slice size is `SUM(ABS(amount))`. The goal is "how much activity is in this denomination" — signed netting would obscure the answer.
2. **No cross-currency conversion.** The pie's sizes reflect unit counts, not value equivalence. A 1000-USD slice and a 1000-gram-gold slice look similarly sized by slice-area, but represent vastly different values. This is the intended semantic per FR33 and the NFR core (never conflate denominations).
3. **Single-slice is a valid render**, not an error state. Subtitle copy is the gentle signal to add more denominations.
4. **Color + pattern is non-negotiable** — WCAG 2.1 AA.
5. **No animation.** Same as Story 3.6.
6. **Charts are independent from the main dashboard payload.** They fetch separately so slow chart queries don't block the Glance's TTR budget.

### Source tree

```
backend/
├── src/Faktuboh.Api/Slices/Debts/Charts/
│   ├── GetDenominationDistributionChartResponse.cs    # NEW
│   └── GetDenominationDistributionChartHandler.cs     # NEW
└── tests/Faktuboh.Api.Tests/Slices/Debts/Charts/
    ├── GetDenominationDistributionChartHandlerTests.cs  # NEW
    └── GetDenominationDistributionChartEndpointTests.cs # NEW

frontend/
├── apps/owner-workspace/src/app/features/dashboard/charts/
│   ├── denomination-distribution-chart.component.ts  # NEW
│   ├── denomination-distribution-chart.component.html # NEW
│   └── denomination-distribution-chart.component.spec.ts # NEW
└── libs/core/charts/
    ├── accessible-palette.ts                         # NEW
    └── accessible-palette.spec.ts                    # NEW
```

### Chart.js pie config (reference)

```typescript
const pieConfig = {
  type: 'pie',
  data: {
    labels: slices.map(s => s.denominationCode),
    datasets: [{
      data: slices.map(s => parseFloat(s.absoluteTotal)),
      backgroundColor: slices.map((_, i) => palette[i].solid),
      // SVG patterns overlay via ctx.createPattern(...) in plugin hook
    }]
  },
  options: {
    animation: false,
    plugins: {
      legend: { rtl: isRtl, textDirection: isRtl ? 'rtl' : 'ltr' },
      tooltip: { rtl: isRtl }
    }
  }
};
```

### Testing standards

Per Story 3.6.

### Project Structure Notes

Second chart in `Slices/Debts/Charts/`. The two chart handlers share no code — each is a single-responsibility query. If a third chart lands post-MVP, refactor common aggregation helpers into a shared `Infrastructure/Queries/` module.

### Architecture compliance

- **§4 D4.1** Postgres primary
- **NFR-A9d** RTL chart compliance
- **NFR-P7** 200ms
- **FR33** denomination distribution
- **UX color-blindness rule** + Stability Signals Trust

### Library/framework requirements

Per Story 3.6.

### Testing requirements

| Category | Tools |
|---|---|
| Unit | xUnit v3 + FluentAssertions; Vitest for palette utility |
| Integration | Testcontainers Postgres + perf |
| Component | Vitest + Angular Testing Library |
| E2E | Playwright — RTL + LTR visual regression + axe |

### Previous Story Intelligence

- **Story 0.12** (RTL gate harness) — consumed for visual regression
- **Story 3.5** (populated Glance) — the dashboard shell this chart mounts into
- **Story 3.6** (bar chart) — pattern-backup helper utility created there; this story reuses it + extends the palette

## References

- [epics.md Story 3.7](../planning-artifacts/epics.md)
- [prd.md FR33](../planning-artifacts/prd.md)
- [ux-design-specification.md Stability Signals Trust + color-blindness rule](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
