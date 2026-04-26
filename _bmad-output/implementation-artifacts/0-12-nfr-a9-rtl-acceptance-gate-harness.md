# Story 0.12: NFR-A9 RTL acceptance gate harness

Status: ready-for-dev

## Story

As **Wahid**,
I want **the six-scenario RTL acceptance gate defined as a CI fitness-test harness that surface-owning epics must satisfy**,
so that **the ship-blocker bilingual commitment has a single authoritative specification (NFR-A9)**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. NFR-A9 six-scenario acceptance gate is MVP ship-blocker.

**Why this story now:** After Stories 0.1 + 0.2 (workspace exists) + 0.9 (CI exists). The harness defines the test shape + CI integration NOW so Epic 1/3/4 surface stories can hook in without re-litigating gate definition.

## Acceptance Criteria

### AC1 — Harness scaffolded at `frontend/e2e/rtl/` + registered in Playwright config

**Given** Playwright is the E2E framework per Story 0.2
**When** the RTL harness is scaffolded
**Then** `frontend/e2e/rtl/` exists with a subfolder per scenario
**And** `frontend/playwright.config.ts` registers a dedicated `rtl-gate` Playwright project:

```typescript
{
  name: 'rtl-gate',
  testDir: './e2e/rtl',
  use: { locale: 'ar-SA', colorScheme: 'light' },
  dependencies: ['owner-workspace-setup']
}
```

**And** `npx playwright test --project=rtl-gate` runs the harness locally [Source: epics.md Story 0.12]

### AC2 — Six named scenarios enumerated verbatim from PRD NFR-A9

**Given** PRD NFR-A9 lists six named scenarios
**When** the harness is defined
**Then** each scenario has a test file at `frontend/e2e/rtl/<scenario-id>/<scenario-name>.spec.ts`:

- **(a) mixed AR + Latin-numeral bidi isolation** — `frontend/e2e/rtl/a-bidi-mixed-numerals/bidi-mixed-numerals.spec.ts` — "Mixed Arabic + Latin-numeral transaction stories with correct bidi isolation (embedded URLs, numbers, currency symbols render in the correct visual order)"
- **(b) Latin / Eastern-Arabic numeral toggle** — `frontend/e2e/rtl/b-numeral-toggle/numeral-toggle.spec.ts` — "Numeral display in balances and charts — user-configurable between Latin (0-9) and Eastern-Arabic (٠-٩) numerals; default per locale"
- **(c) transaction-note bidi isolation** — `frontend/e2e/rtl/c-transaction-note-bidi/transaction-note-bidi.spec.ts` — "Bidi isolation in transaction notes containing embedded Latin script or URLs — no visual order corruption"
- **(d) RTL dashboard charts** — `frontend/e2e/rtl/d-dashboard-charts/dashboard-charts.spec.ts` — "RTL layout in dashboard bar + pie charts — legends, axes, tooltips, and labels all flow right-to-left"
- **(e) RTL CSV export** — `frontend/e2e/rtl/e-csv-export/csv-export.spec.ts` — "RTL in CSV export — column order reversed for Arabic locale; UTF-8 with BOM for Excel-Arabic compatibility; currency symbols correctly positioned per-currency convention"
- **(f) right-aligned date-range picker with Hijri toggle** — `frontend/e2e/rtl/f-date-picker-hijri/date-picker-hijri.spec.ts` — "Right-aligned date-range picker with Hijri-calendar display as an optional toggle (Hijri calendar read-only at MVP; Gregorian remains primary)"

**And** each scenario file starts with a pending-state skeleton (test marked `.skip` until its owning epic fills in the implementation):

```typescript
// frontend/e2e/rtl/a-bidi-mixed-numerals/bidi-mixed-numerals.spec.ts
import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test.describe('NFR-A9 (a) — Mixed AR + Latin-numeral bidi isolation', () => {
  test.skip('renders correct bidi order for AR text containing Latin numerals', async ({ page }) => {
    // Surface-owning epic fills this in (expected: Epic 3 Story 3.X — debt stories)
    await page.goto('/contacts/<id>/debts');
    await expect(page.locator('[data-testid=debt-story]').first()).toHaveAttribute('dir', 'rtl');
    // Assert specific bidi embed markers (⁨ / ⁩ FSI / PDI isolation) or visual regression
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations).toHaveLength(0);
  });
});
```

**And** the exact verbiage from PRD NFR-A9 appears in the `test.describe` description for each scenario (verbatim matching so the gate definition IS the PRD clause) [Source: prd.md NFR-A9 lines 623-629]

### AC3 — Reviewer-device matrix documented

**Given** NFR-A9 requires "at least two RTL-native reviewer devices (native Arabic speakers, one mobile / one desktop)"
**When** the matrix is documented
**Then** `docs/runbooks/rtl-acceptance-gate.md` documents:
- Device #1: **Mobile** — e.g., iOS 17+ Safari on iPhone 14 Plus, screen reader VoiceOver Arabic
- Device #2: **Desktop** — e.g., Windows 11 Chrome, screen reader NVDA Arabic (or TalkBack Arabic on Android tablet)
- Reviewer contacts (Wahid's Arabic-speaking reviewer pool — TBD; placeholder "TBD: identify 2 reviewers by Day 30")
- Review cadence: every release candidate before production tag push
- Review artifact: filled-in checklist in `docs/runbooks/rtl-acceptance-review-<vX.Y.Z>.md`
**And** a sign-off template `docs/runbooks/rtl-acceptance-review-template.md` captures scenario results (pass/fail) per reviewer

### AC4 — Automated gate fails release build on violation

**Given** Story 0.9's `deploy-prod.yml` gates on tags
**When** `deploy-prod.yml` runs
**Then** a pre-deploy job `rtl-acceptance-gate` executes `npx playwright test --project=rtl-gate`
**And** any `.skip`ped test is reported separately — it is allowed (pending epic implementation) but counted
**And** any non-skipped failing test blocks the deploy
**And** at Epic 4 close (final surface shipped), ALL 6 scenarios must be non-skipped — the release pipeline fails if any remain `.skip`ped after Epic 4 complete (tracked via CHANGELOG.md ship-blocker checklist) [Source: epics.md Story 0.12 "violation of any scenario fails the release build"]

### AC5 — Axe-core runs per scenario

**Given** RTL + accessibility are coupled (NFR-A9 references WCAG 2.1 AA compliance implicitly via general NFR-A accessibility section)
**When** each scenario test runs
**Then** `AxeBuilder(page).analyze()` executes after the visual assertion
**And** zero WCAG 2.1 AA violations are expected
**And** the axe-core config uses `rules: { 'document-title': { enabled: true }, ... }` via shared `frontend/e2e/rtl/axe.config.ts`

### AC6 — Visual regression via Playwright snapshots

**Given** RTL layout issues often manifest visually, not programmatically
**When** a scenario test runs
**Then** a Playwright screenshot is captured via `await page.screenshot({ path: 'snapshots/<scenario-id>.png', fullPage: true })`
**And** baseline snapshots live at `frontend/e2e/rtl/__snapshots__/` (committed to repo)
**And** diff > 0.1% fails the test (`toHaveScreenshot({ maxDiffPixelRatio: 0.001 })`)
**And** baselines are regenerated via `--update-snapshots` after manual visual review [Source: architecture.md §5.4.2 Playwright visual-regression]

### AC7 — Epic mapping per scenario

**Given** surface-owning epics fill in the scenario implementations
**When** `docs/runbooks/rtl-acceptance-gate.md` documents the mapping
**Then** each of the six scenarios is mapped to its owning epic:
- (a) mixed bidi → Epic 3 (Debt Management / Transaction Stories)
- (b) numeral toggle → Epic 1 (First-Run Shell — locale + profile preferences)
- (c) note bidi → Epic 3 (Debt notes feature)
- (d) dashboard charts → Epic 7 (Dashboard — or earlier if dashboard lands in Epic 3 per slicing)
- (e) CSV export → Epic 7 or Epic 3 (wherever export lives)
- (f) date-range picker + Hijri → Epic 3 or Epic 7 (wherever date picker lands)
**And** each scenario file has a TODO comment with the expected epic + story ID [Source: epics.md "surface-owning epics (1, 3, 4) hook into"]

## Tasks / Subtasks

### T1 — Directory scaffold + Playwright project (AC: 1, 2)

- [ ] Create `frontend/e2e/rtl/` + one folder per scenario (a through f)
- [ ] Create `<scenario>.spec.ts` files with `.skip` skeleton + verbatim NFR-A9 descriptions
- [ ] Edit `frontend/playwright.config.ts` to register `rtl-gate` project per AC1

### T2 — Shared helpers (AC: 5, 6)

- [ ] Create `frontend/e2e/rtl/axe.config.ts` — shared axe-core config
- [ ] Create `frontend/e2e/rtl/snapshot-helpers.ts` — `expectSnapshot(page, name)` wrapper with diff-ratio config
- [ ] Create `frontend/e2e/rtl/__snapshots__/.gitkeep` — folder exists for baseline images

### T3 — CI integration (AC: 4)

- [ ] Edit Story 0.9's `.github/workflows/deploy-prod.yml` to add `rtl-acceptance-gate` job BEFORE the deploy job:
  ```yaml
  rtl-acceptance-gate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@<sha>
      - uses: actions/setup-node@<sha>
        with: { node-version: 22 }
      - run: npm ci --prefix frontend
      - run: npx playwright install --with-deps chromium webkit
        working-directory: frontend
      - run: npx playwright test --project=rtl-gate
        working-directory: frontend
  deploy-prod:
    needs: [rtl-acceptance-gate]
    # ... rest of deploy
  ```
- [ ] Also add to `ci.yml` (on every PR) — running skip-only tests is cheap; catches the day a scenario is accidentally un-skipped with a failing assertion

### T4 — Reviewer-device matrix (AC: 3)

- [ ] Create `docs/runbooks/rtl-acceptance-gate.md` with:
  - Device #1 (mobile) + #2 (desktop) specs
  - Reviewer identification (TBD placeholder)
  - Review cadence + artifact template
- [ ] Create `docs/runbooks/rtl-acceptance-review-template.md`

### T5 — Epic mapping documentation (AC: 7)

- [ ] Append epic-to-scenario mapping to `docs/runbooks/rtl-acceptance-gate.md`
- [ ] Add TODO comment in each scenario `.spec.ts` with expected epic + story
- [ ] Add cross-reference in `_bmad-output/planning-artifacts/epics.md` (already has this per §bilingual/NFR-A9)

## Dev Notes

### Critical guardrails (do not violate)

1. **NFR-A9 scenario verbiage is VERBATIM from PRD.** Do not paraphrase `test.describe` strings — future reviewers cross-reference them against the PRD. [Source: prd.md NFR-A9]
2. **`.skip` is allowed until the owning epic lands.** A skipped test is not a failing test — it's a pending scenario. [Source: epics.md Story 0.12]
3. **No scenario may remain skipped after Epic 4 closes.** A release-blocking CI check enforces this before MVP ship. [Source: epics.md Story 0.12]
4. **RTL-native reviewer is a human**, not an AI/automated check. Screen-reader passes are human-confirmed per release. Automation catches violations that humans miss; humans catch violations that automation misses (especially bidi visual order).
5. **Visual regression baselines must be reviewed by a human before commit.** Committing wrong baselines silently bakes bugs into the gate.

### Source tree — files to create

```
frontend/e2e/rtl/
├── a-bidi-mixed-numerals/bidi-mixed-numerals.spec.ts     # NEW (.skip)
├── b-numeral-toggle/numeral-toggle.spec.ts               # NEW (.skip)
├── c-transaction-note-bidi/transaction-note-bidi.spec.ts # NEW (.skip)
├── d-dashboard-charts/dashboard-charts.spec.ts           # NEW (.skip)
├── e-csv-export/csv-export.spec.ts                       # NEW (.skip)
├── f-date-picker-hijri/date-picker-hijri.spec.ts         # NEW (.skip)
├── axe.config.ts                                         # NEW
├── snapshot-helpers.ts                                   # NEW
└── __snapshots__/.gitkeep                                # NEW

frontend/playwright.config.ts                             # EDIT (register rtl-gate project)

.github/workflows/
├── ci.yml                                                # EDIT (add rtl-gate job)
└── deploy-prod.yml                                       # EDIT (add rtl-gate gate)

docs/runbooks/
├── rtl-acceptance-gate.md                                # NEW (device matrix + epic mapping)
└── rtl-acceptance-review-template.md                     # NEW
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| E2E | Playwright | Scenario skeletons |
| A11y | @axe-core/playwright | WCAG 2.1 AA assertion per scenario |
| Visual | Playwright screenshots | Regression baselines |
| Manual | Human reviewer (2 devices) | Screen-reader + bidi visual order |

### Project Structure Notes

- Scenarios live in `frontend/e2e/rtl/` not `frontend/e2e/owner-workspace/` because some scenarios span both apps (e.g., scenario (f) date picker may live in owner or a shared component).
- Screenshot baselines under `__snapshots__/` are committed but git-ignored from being re-committed accidentally; docs/runbook covers update procedure.

### Architecture compliance

- [prd.md NFR-A9](../planning-artifacts/prd.md) — six-scenario gate definition
- [architecture.md §5.4.2](../planning-artifacts/architecture.md) — Playwright Chromium + WebKit matrix
- [epics.md Story 0.12](../planning-artifacts/epics.md) — harness definition

### Library/framework requirements

| Layer | Package | Purpose |
|---|---|---|
| E2E | @playwright/test | Test runner |
| A11y | @axe-core/playwright | WCAG scan |
| Browser | chromium + webkit | Matrix per §5.4.2 |

### Testing requirements

- **Zero failing tests at Story 0.12 close** — only `.skip`ped tests. Non-skipped failing is a bug.
- **CI wires the harness on every PR**, not just on release — catches accidental un-skip-with-failing-assertion the moment it happens.

### Previous Story Intelligence

- **Story 0.2 (FE workspace)** complete: Playwright is installed + configured with Chromium + WebKit.
- **Story 0.9 (CI/CD)** complete: `ci.yml` + `deploy-prod.yml` exist; this story edits both to add `rtl-gate` jobs.
- **Epic 1/3/4 stories** fill in the scenario bodies later.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.12 definition
- [_bmad-output/planning-artifacts/prd.md NFR-A9](../planning-artifacts/prd.md) — six-scenario gate
- [_bmad-output/planning-artifacts/architecture.md §5.4.2](../planning-artifacts/architecture.md) — Playwright conventions

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
