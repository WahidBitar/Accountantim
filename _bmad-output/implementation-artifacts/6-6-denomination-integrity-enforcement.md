# Story 6.6: Denomination integrity enforcement

Status: ready-for-dev

## Story

As **a user with debts in multiple denominations**,
I want **Faktuboh to never implicitly convert or aggregate across denominations**,
so that **my gold debts stay in gold and my USD debts stay in USD — my view of position is never silently falsified** (FR21).

**Covers requirements:** Epic 6 Story 6.6 — FR21 (denomination integrity, no implicit conversion); NetArchTest enforcement of "no cross-denomination arithmetic in any code path"; per-denomination subtotals on Glance + Dashboard; CSV export denomination column [Source: epics.md Epic 6 Story 6.6; prd.md FR21].

**Why this story now:** Story 6.4 (Exchange) is the ONE place cross-denomination conversion happens — and only via explicit user-confirmed locked rate. EVERY OTHER code path must be denomination-pure. This story ships the NetArchTest assertion BEFORE Story 6.4 lands, so 6.4's compound entry is the ONLY exception. Without this gate, a future story could silently `sum(allDebts)` and produce a meaningless "total" — the canonical fintech silent-failure mode.

## Acceptance Criteria

### AC1 — NetArchTest assertion: no cross-denomination arithmetic anywhere

**Given** the rule "no implicit conversion / aggregation across denominations"
**When** the NetArchTest suite runs
**Then** `Faktuboh.ArchitectureTests/DenominationIntegrityTests.cs` asserts:
- No method (excluding Exchange slice) groups `Money` instances WITHOUT including `Currency` in the grouping key
- No method (excluding Exchange slice) calls `decimal +/-/*//` on amount fields from `Money` instances of different `Currency` values
- The Exchange slice (`Slices/JournalEntries/Exchange/`) is the ONLY allowlisted exception (cross-denomination via locked rate)
**And** the assertion uses Roslyn-based AST analysis (NetArchTest + custom AST visitor) to detect:
- `.Sum(m => m.Amount)` calls where the source isn't pre-grouped by `Currency`
- `decimal a = money1.Amount + money2.Amount;` where types are inferred to potentially differ

```csharp
[Fact]
public void NoCrossDenominationArithmetic()
{
    var result = Types.InAssemblies(new[] { typeof(Program).Assembly })
        .That()
        .DoNotResideInNamespace("Faktuboh.Api.Slices.JournalEntries.Exchange")
        .And().DoNotResideInNamespace("Faktuboh.Domain.Tests")
        .Should()
        .NotHaveDependencyOn("Faktuboh.Domain.Primitives.MoneyArithmeticExtensions.UnsafeAdd")  // hypothetical helper if any
        .GetResult();
    result.IsSuccessful.Should().BeTrue($"Found cross-denomination arithmetic in: {string.Join(", ", result.FailingTypeNames)}");
}

// + Custom AST visitor pass (Roslyn analyzer or test-time scan):
[Fact]
public void NoUnsafeDecimalArithmeticOnMoneyAmounts()
{
    var violations = ScanForUnsafeMoneyArithmetic(GetAllSourceFiles().Except(ExchangeSliceFiles));
    violations.Should().BeEmpty($"Cross-denomination arithmetic forbidden outside Exchange. Violations: {string.Join("\n", violations)}");
}
```

**And** the test fails the build on any violation [Source: epics.md Epic 6 Story 6.6 AC2 — "no cross-denomination arithmetic exists in any code path (NetArchTest asserts)"].

### AC2 — `Money` value object enforces same-currency invariant on arithmetic

**Given** Story 0.1 / 2.1's `Money` record
**When** the user attempts `money1 + money2`
**Then** the helper `Money.Add(Money other)` throws `DenominationMismatchException` if `Currency` differs
**And** the same applies to `Subtract`, `MultiplyBy(decimal)` is allowed (scalar multiplication preserves denomination)
**And** division returns a scalar ratio (not Money) when both operands are Money
**And** the exception code `faktuboh.money.denomination_mismatch` is in `ErrorCatalog` (HTTP 422)
**And** unit tests cover all arithmetic operators [Source: architecture.md §5.2.5; this story extends Money helpers].

### AC3 — Per-denomination grouping on every aggregation query

**Given** queries that compute totals (Stories 3.5, 3.6, 3.7, 5.4, etc.)
**When** the query reads `journal_entries`
**Then** every `GROUP BY` includes `denomination` (or the equivalent EF Core LINQ `.GroupBy(j => new { j.ContactId, j.Denomination })`)
**And** every projection that materializes a "total" returns a list `Map<Currency, Money>`, NEVER a single `Money` value
**And** an integration test seeds debts in 3 currencies (USD, EUR, XAU) for one contact and asserts the dashboard returns 3 distinct subtotals

### AC4 — Glance HeroTiles render per-denomination subtotals

**Given** Story 3.5 ships the Glance with HeroTiles
**When** the user has debts in multiple denominations
**Then** each HeroTile (Lent / Borrowed) renders ONE row per denomination:
- "$300 USD"
- "€150 EUR"
- "10g gold"
**And** the rendering helper is in `libs/core/money/format-money-list.ts` (single source)
**And** the rendering NEVER produces a "total of $X equivalent" line — denomination integrity holds
**And** Vitest test asserts the multi-denomination rendering [Source: epics.md Epic 6 Story 6.6 AC3].

### AC5 — Denomination pie chart shows slices per-denomination unit count

**Given** Story 3.7 ships the denomination pie chart
**When** the user has 10g gold + $300 USD + €150 EUR
**Then** the chart shows 3 slices (one per denomination) sized by *unit count* of that denomination, NOT by value-equivalent
**And** the chart's tooltip reads "10g gold (3 debts)" — count of debts, not value
**And** the chart explicitly avoids any implicit conversion by NEVER calling Story 6.3's rate endpoint
**And** an inline note: "Slices show distribution by denomination — values not converted." (Transloco-keyed) [Source: epics.md Epic 6 Story 6.6 AC3].

### AC6 — Contact bar chart per-denomination subtotals

**Given** Story 3.6 ships the contact bar chart
**When** a contact has multi-denomination debts
**Then** the bar chart renders the contact as a STACKED bar with one segment per denomination
**And** the y-axis is labeled "Per denomination — values not converted"
**And** alternative: chart shows ONE bar per denomination per contact (separate bars), preserving per-denom integrity
**And** decision flag: stacked vs separate is a UX-DR decision; default to separate stacked bars unless UX-DR specifies otherwise

### AC7 — CSV export column lists denomination per row

**Given** Story 3.9 ships CSV export
**When** the export runs
**Then** every row includes a `denomination` column with the ISO 4217 code (or `XAU`/`XAG` for metals)
**And** the column is ALWAYS present (not conditional)
**And** the column appears as the rightmost column for RTL parity (per Story 3.9 RTL convention)
**And** an integration test asserts the column presence + content [Source: epics.md Epic 6 Story 6.6 AC4].

### AC8 — Roslyn analyzer (optional fast feedback layer)

**Given** the NetArchTest catches violations at CI time, but earlier feedback is better
**When** a developer attempts `money1.Amount + money2.Amount` in their IDE
**Then** an optional Roslyn analyzer (`FaktubohAnalyzers.MoneyDenominationAnalyzer`) emits a warning
**And** the warning becomes a build error in CI (already covered by NetArchTest as fallback)
**And** if the analyzer is too noisy at MVP, it's deferred — NetArchTest is the contract

### AC9 — UX consistency: no place displays a "grand total" across denominations

**Given** the design rule
**When** ANY UI surface (Dashboard, Glance, contact detail, transfer history, exchange history) renders totals
**Then** the rendering is per-denomination
**And** no string like "Total: $X equivalent" appears anywhere
**And** an ESLint rule `no-equivalent-total-language` flags suspicious i18n keys (`total`, `grand-total`, `equivalent`) for review
**And** UX docs reference this rule [Source: epics.md Epic 6 Story 6.6 AC3].

### AC10 — Telemetry: track denomination-mismatch incidents

**Given** the `DenominationMismatchException` is the canonical signal of a regression
**When** the exception fires anywhere
**Then** `TelemetryEmitter.Emit("denomination_mismatch_attempted", { exception_call_site, attempted_currencies })` fires
**And** App Insights alert `Faktuboh.DenominationMismatchSpike` triggers if > 0 events/hour (immediate signal something's wrong)

## Tasks / Subtasks

### Backend — Money helpers

- [ ] **T1 (AC: 2)** — Extend `Money` value object with arithmetic helpers
  - [ ] `Add(Money other)` — throws `DenominationMismatchException` on currency mismatch
  - [ ] `Subtract(Money other)` — same
  - [ ] `MultiplyBy(decimal scalar)` — preserves denomination
  - [ ] `DivideBy(Money other)` — returns scalar ratio (decimal, not Money)
  - [ ] Operator overloads: `+`, `-` invoke the helpers
- [ ] **T2 (AC: 2)** — Add `DenominationMismatchException` to Domain layer
  - [ ] Code `faktuboh.money.denomination_mismatch` (422)
  - [ ] Register in `ErrorCatalog`

### Backend — Architectural enforcement

- [ ] **T3 (AC: 1)** — Create `Faktuboh.ArchitectureTests/DenominationIntegrityTests.cs`
  - [ ] NetArchTest dependency-graph rule
  - [ ] Custom AST scan for unsafe arithmetic patterns
  - [ ] Allowlist `Slices/JournalEntries/Exchange/` only
- [ ] **T4 (AC: 8) — OPTIONAL** — Create `FaktubohAnalyzers/MoneyDenominationAnalyzer.cs`
  - [ ] Roslyn analyzer warning on `money1.Amount + money2.Amount`
  - [ ] Defer if too noisy at MVP

### Backend — Aggregation queries

- [ ] **T5 (AC: 3)** — Audit existing aggregation queries (Stories 3.5, 3.6, 3.7, 5.4)
  - [ ] Verify every `GROUP BY` includes `denomination`
  - [ ] Refactor any single-`Money` returns to `Map<Currency, Money>`
  - [ ] Integration test seeds 3-currency contact + asserts 3 distinct subtotals

### Backend — Telemetry

- [ ] **T6 (AC: 10)** — Wire `denomination_mismatch_attempted` event
  - [ ] Emit on every `DenominationMismatchException` throw
  - [ ] App Insights alert configured

### Frontend — Per-denomination rendering

- [ ] **T7 (AC: 4)** — Verify Story 3.5 Glance rendering uses per-denom helper
  - [ ] If not, refactor to consume `libs/core/money/format-money-list.ts`
- [ ] **T8 (AC: 4)** — Create `libs/core/money/format-money-list.ts`
  - [ ] Function: `formatMoneyList(amounts: Money[], locale: string): string[]`
  - [ ] Returns one string per denomination
  - [ ] Vitest tests
- [ ] **T9 (AC: 5)** — Verify Story 3.7 pie chart shows count, not value-equivalent
  - [ ] Ensure no call to Story 6.3 rate endpoint
  - [ ] Add inline note Transloco-keyed
- [ ] **T10 (AC: 6)** — Verify Story 3.6 bar chart renders per-denomination
  - [ ] Stacked bar per contact OR separate bars (decision flag)

### Frontend — CSV export

- [ ] **T11 (AC: 7)** — Verify Story 3.9 CSV column
  - [ ] Add `denomination` column if missing
  - [ ] Update RTL column-order test

### Frontend — UX guards

- [ ] **T12 (AC: 9)** — ESLint rule `no-equivalent-total-language`
  - [ ] Scans i18n keys + values for "total", "grand-total", "equivalent"
  - [ ] Allowlist legitimate uses (per-denom totals are OK; "value equivalent" is not)

### Tests

- [ ] **T13 (AC: 1)** — NetArchTest suite passes against current codebase
  - [ ] Refactor any pre-existing violations
- [ ] **T14 (AC: 2)** — Money arithmetic unit tests
  - [ ] All operators + exception paths
- [ ] **T15 (AC: 3)** — Multi-denomination integration test fixture
  - [ ] Seed 3-currency debts; assert per-denom queries return correct subtotals
- [ ] **T16 (AC: 4-7)** — Frontend Vitest tests for per-denom rendering
- [ ] **T17 (AC: 7)** — CSV column integration test
- [ ] **T18 (AC: 10)** — Telemetry assertion test

## Dev Notes

### Critical guardrails (do not violate)

1. **Exchange is the ONLY exception.** The NetArchTest allowlist contains `Slices/JournalEntries/Exchange/` and nothing else. Adding any other exception requires a UX-DR + ADR.
2. **Per-denomination always.** Every total is a list, never a single value. Every chart axis is per-denomination. Every CSV row carries the denomination column.
3. **No "X equivalent" copy.** The product never displays "$X equivalent" because that requires implicit conversion. UX-DR forbids; ESLint rule helps catch.
4. **`Money.Add(Money other)` throws on mismatch.** This is the C# enforcement; the NetArchTest catches the patterns that bypass it.
5. **Pie chart = unit count, not value.** A 10g gold debt and a $300 USD debt are visually equivalent slices in the pie (both = 1 debt) — the pie shows distribution, not magnitude.
6. **CSV `denomination` column is ALWAYS present.** Even single-denomination users see it.
7. **Telemetry on denomination-mismatch is a regression alarm.** > 0 events/hour = something silently broke.

### Source tree — files to create or touch

```
backend/src/
├── Faktuboh.Domain/Primitives/
│   ├── Money.cs                                            # MODIFY (+ Add/Subtract/MultiplyBy/DivideBy)
│   └── DenominationMismatchException.cs                    # NEW
├── Faktuboh.Application/Errors/
│   └── ErrorCatalog.cs                                     # MODIFY (+ denomination_mismatch code)
└── (audit existing aggregation queries — Stories 3.5/3.6/3.7/5.4 handlers)

backend/tests/
└── Faktuboh.ArchitectureTests/
    └── DenominationIntegrityTests.cs                       # NEW

backend/src/FaktubohAnalyzers/  (OPTIONAL)
└── MoneyDenominationAnalyzer.cs                            # NEW (deferrable)

libs/core/money/
└── format-money-list.ts                                    # NEW (or MODIFY)

apps/owner-workspace/
├── src/app/features/dashboard/  (Stories 3.5/3.6/3.7)      # AUDIT/MODIFY
├── src/app/features/export/  (Story 3.9)                   # AUDIT/MODIFY
└── src/assets/i18n/{ar,en}.json                            # MODIFY (per-denom hints)

eslint-rules/
└── no-equivalent-total-language.js                         # NEW
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Architectural | NetArchTest + custom AST scan | NetArchTest + Roslyn |
| Unit | Money arithmetic + exceptions | xUnit + FluentAssertions |
| Integration | Multi-denom aggregation queries | Testcontainers Postgres |
| Component | Per-denom rendering | Vitest |
| ESLint | `no-equivalent-total-language` | Custom ESLint rule |

### Architecture compliance

- **FR21** — denomination integrity.
- **§5.2.5** — Money + currency invariant.
- **NetArchTest fitness** — extends the existing suite from Story 0.8.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Architectural | NetArchTest | + custom AST visitor |
| Optional | Roslyn analyzer SDK | Deferrable |

### Testing requirements

- **Architectural:** NetArchTest passes; refactor pre-existing violations.
- **Unit:** Money arithmetic.
- **Integration:** multi-denom subtotals.
- **Component:** per-denom UI rendering.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.1 — `Money` value object + `CurrencyRegistry`
- Story 0.4 — `ErrorCatalog`
- Story 0.8 — NetArchTest fitness suite
- Story 0.14 — telemetry primitive
- Story 3.5 — Glance HeroTiles
- Story 3.6 — contact bar chart
- Story 3.7 — denomination pie chart
- Story 3.9 — CSV export
- Story 6.1 — `CurrencyRegistry` extended for metals (XAU + XAG)

**Coordination:**
- This story MUST land BEFORE Story 6.4 (Exchange) merges. The NetArchTest is the gatekeeper that makes Exchange's allowlist meaningful.

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 6 Story 6.6
- FR21 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Money + Currency — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.2.5

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- NetArchTest is the gatekeeper for FR21; Exchange slice is the SOLE allowlisted exception.
- Money arithmetic operators throw on currency mismatch.
- Pie chart shows count distribution, not value-equivalent.
- CSV `denomination` column always present.
- Roslyn analyzer is optional fast-feedback; NetArchTest is the contract.

### File List

_Populated during implementation._
