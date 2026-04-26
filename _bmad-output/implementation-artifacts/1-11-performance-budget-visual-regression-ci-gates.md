# Story 1.11: Performance budget + visual regression CI gates

Status: ready-for-dev

## Story

As **Wahid**,
I want **perf budgets (LCP, INP, CLS, initial JS, route-level JS, font total) + visual regression screenshots (LTR + RTL) running on every PR**,
so that **performance + RTL parity are enforced before merge (UX-DR37, UX-DR39)**.

**Covers requirements:** NFR-P1 (LCP < 2.5s Slow 4G), NFR-P2 (INP < 200ms), NFR-P3 (CLS < 0.1), NFR-P9 (RTL parity), UX Performance Budgets.

**Why this story now:** Per the reorder, 1.11 lands after 1.10 (a11y gates) and before 1.2 (registration). With the a11y gates active and the components/shell committed, this story freezes the performance contract + visual regression baseline. Every subsequent PR is blocked on perf regressions or unintended visual diffs.

## Acceptance Criteria

### AC1 — Per-target initial JS bundle budgets

**Given** production builds of both Angular targets
**When** the CI job evaluates bundle size
**Then** `apps/owner-workspace` initial JS (parsed + gzipped) is ≤ **250 KB gzipped** per budget.json
**And** `apps/public-statement` initial JS is ≤ **180 KB gzipped** per budget.json (stricter — NFR-P9 + architecture §3.4 D1)
**And** budgets are defined in each app's `angular.json` under `configurations.production.budgets`:
```json
{
  "type": "initial",
  "maximumWarning": "230kB",  // owner-workspace
  "maximumError": "250kB"
}
```
**And** exceeding the error threshold fails the build

### AC2 — Per-route lazy-chunk budget

**Given** a feature routed lazily (e.g., `/dashboard`, `/profile`, `/contacts`)
**When** the build evaluates the route's chunk size
**Then** the chunk is ≤ **60 KB gzipped**
**And** `angular.json` `budgets` include:
```json
{
  "type": "anyComponentStyle",
  "maximumError": "8kB"
},
{
  "type": "bundle",
  "name": "dashboard",
  "maximumError": "60kB"
}
```
**And** routes exceeding the budget fail the build with the offending bundle name called out

### AC3 — Font budget

**Given** the woff2 fonts shipped in `apps/owner-workspace/src/assets/fonts/`
**When** their combined size is measured
**Then** Tajawal + Inter total woff2 is ≤ **120 KB** (all weights: 400, 500, 700 × 2 typefaces = 6 files)
**And** a CI script `scripts/assert-font-budget.sh` computes the sum and fails if over
**And** a lint pass asserts all fonts use `font-display: swap` in `@font-face` declarations

### AC4 — Lighthouse Core Web Vitals thresholds

**Given** Lighthouse CI from Story 1.10
**When** the perf audit runs on key routes
**Then** **LCP < 2.5s** on Slow 4G (mobile throttled)
**And** **INP < 200ms** (interaction latency)
**And** **CLS < 0.1** (cumulative layout shift)
**And** **FCP < 1.8s** on Slow 4G
**And** **TTFB < 800ms**
**And** thresholds apply to `/`, `/onboarding`, `/dashboard`, `/profile`
**And** the mobile throttling profile is the Slow 4G preset: `rttMs: 150, throughputKbps: 1638, cpuSlowdownMultiplier: 4`

### AC5 — Visual regression snapshots (LTR + RTL)

**Given** every Storybook story from Story 1.9 + every shell screen
**When** the visual regression CI job runs
**Then** Chromatic (or Percy) captures a screenshot per story × per direction (LTR + RTL)
**And** shell screens in `apps/owner-workspace/e2e/visual/` are captured via Playwright at breakpoints: mobile (375 × 812), tablet (768 × 1024), desktop (1440 × 900)
**And** diffs above the configured threshold (default 0.1% pixel difference) fail the PR
**And** baseline updates require explicit human approval via the Chromatic/Percy UI (not merged automatically)

### AC6 — Zoneless invariant verified in bundle

**Given** the built production bundles
**When** the bundle-scan script runs
**Then** no `zone.js` content appears in any `.js` file in either app's `dist/`
**And** `scripts/assert-zoneless.sh` (from Story 1.1) runs as a gating CI step
**And** the script also checks for `NgZone` internals (grep `this\.zone\.run|this\.ngZone\.`)

### AC7 — Bundle-graph gate (public-statement compile boundary)

**Given** `apps/public-statement` stats.json from the production build
**When** the bundle-graph CI job parses the graph
**Then** no module from `apps/owner-workspace/**` appears in the graph
**And** no forbidden submodule of `libs/core/**` (e.g., `libs/core/auth/**`) appears in the graph
**And** the allowlist for public-statement is: `libs/design-system`, `libs/domain-ui`, `libs/core/http`, `libs/core/i18n`, `libs/core/cache`, `libs/core/errors`
**And** violations fail the build with the specific forbidden module path called out

### AC8 — Performance-tracking metrics dashboard

**Given** Lighthouse CI results
**When** a PR lands on `main`
**Then** the result is uploaded to a persistent Lighthouse CI server (or GitHub Pages static report) for trend tracking
**And** a weekly digest (via GitHub Action) posts a perf trend summary (LCP p75, INP p75, CLS p75, bundle size) to the project's Discussions or a dedicated tracking doc
**And** a regression threshold of 10% on any metric triggers an issue creation via `actions/github-script`

### AC9 — Shell visual regression E2E

**Given** `apps/owner-workspace/e2e/visual/shell.spec.ts`
**When** Playwright runs
**Then** it navigates to every shell screen (`/`, `/onboarding`, `/dashboard`, `/profile`), takes a screenshot at mobile/tablet/desktop breakpoints, in LTR + RTL
**And** compares against the committed baseline via `toMatchScreenshot()`
**And** failures produce a diff PNG as a CI artifact

### AC10 — CI job ordering + fast-feedback

**Given** the CI pipeline
**When** a PR is opened
**Then** the build job runs first; unit tests + lint run in parallel; perf budget + bundle scan run after build; Lighthouse CI + visual regression run after preview deploy
**And** fast-failing checks (lint, unit, bundle size) complete in < 3 minutes
**And** full pipeline completes in < 12 minutes for a typical PR

## Tasks / Subtasks

### Bundle budgets

- [ ] **T1 (AC: 1, 2)** — Configure `angular.json` budgets per app
  - [ ] `apps/owner-workspace/angular.json` production config: `initial maximumError: 250kB`, per-route lazy `60kB`, component style `8kB`
  - [ ] `apps/public-statement/angular.json` production config: `initial maximumError: 180kB`, per-route lazy `60kB`
  - [ ] CI job `pnpm run build:prod` fails on budget violations (default Angular behavior when `maximumError` set)
- [ ] **T2 (AC: 3)** — Create `scripts/assert-font-budget.sh`
  - [ ] Sums byte size of `apps/owner-workspace/src/assets/fonts/*.woff2`
  - [ ] Exits non-zero if sum > 120 × 1024 bytes
  - [ ] Included in CI job after build
- [ ] **T3 (AC: 6)** — Verify `scripts/assert-zoneless.sh` from Story 1.1 runs on both app bundles
  - [ ] Extend to also scan for `NgZone` internals
  - [ ] Fails build on any hit

### Lighthouse + Web Vitals

- [ ] **T4 (AC: 4)** — Enhance Lighthouse CI config from Story 1.10
  - [ ] Add Web Vitals assertions in `.lighthouserc.cjs`: `largest-contentful-paint`, `interaction-to-next-paint`, `cumulative-layout-shift`, `first-contentful-paint`, `server-response-time`
  - [ ] Apply thresholds from AC4
  - [ ] Run on 4 key routes: `/`, `/onboarding`, `/dashboard`, `/profile`
- [ ] **T5 (AC: 8)** — Perf trend tracking
  - [ ] Option A: Deploy Lighthouse CI server to a small Azure App Service instance
  - [ ] Option B: Upload LHCI JSON to a repo-tracked `perf-history/` folder + generate Markdown trend summary via GitHub Action
  - [ ] Weekly digest workflow `.github/workflows/perf-digest.yml` runs `on: schedule` (Mondays)

### Visual regression

- [ ] **T6 (AC: 5)** — Configure Chromatic (or Percy) for Storybook
  - [ ] Add `chromatic` (or `@percy/cli`) dev dep
  - [ ] Create `.github/workflows/chromatic.yml` with `chromatic --project-token=${{ secrets.CHROMATIC_PROJECT_TOKEN }}`
  - [ ] Baseline on `main` branch; PR diffs gate merge
- [ ] **T7 (AC: 5, 9)** — Shell screen visual regression via Playwright
  - [ ] Create `apps/owner-workspace/e2e/visual/shell.spec.ts`
  - [ ] Loops over routes × breakpoints × directions; uses `page.screenshot()` + `toHaveScreenshot()`
  - [ ] Baselines committed under `apps/owner-workspace/e2e/visual/__screenshots__/`
  - [ ] Missing baseline on a new route fails CI with "run `playwright --update-snapshots` locally + commit" message

### Bundle-graph gate

- [ ] **T8 (AC: 7)** — Bundle-graph script
  - [ ] Create `scripts/assert-public-statement-boundary.js`
  - [ ] Reads `apps/public-statement/dist/**/stats.json` (enable stats.json emission in angular.json)
  - [ ] Walks the module graph; fails if any module path matches `apps/owner-workspace/` or forbidden `libs/core/` submodules
  - [ ] Allowlisted submodule paths enumerated in the script
  - [ ] Runs as a CI gate

### CI workflow

- [ ] **T9 (AC: 10)** — Compose CI workflow
  - [ ] `.github/workflows/frontend-ci.yml` with sequential stages: `lint → unit → build → bundle-checks → deploy-preview → lighthouse + visual-regression`
  - [ ] Parallelize `lint` + `unit` (no dependency)
  - [ ] Cache `node_modules` + Angular build cache across runs
  - [ ] Total pipeline < 12 minutes on a typical PR
- [ ] **T10 (AC: 8, 10)** — PR comment summary
  - [ ] Extend the PR comment from Story 1.10 (AC10) to include: bundle size delta vs main, LCP/INP/CLS deltas, visual regression diff count
  - [ ] Comment updates in place on subsequent pushes

### Tests

- [ ] **T11 (AC: 1-3, 6-7)** — Budget gate self-tests
  - [ ] Intentional oversize fixture (temporary file in a test branch) triggers failure
  - [ ] Once reverted, CI passes
- [ ] **T12 (AC: 5, 9)** — Visual regression baseline commit
  - [ ] Run `playwright test --update-snapshots` locally against the shell
  - [ ] Review generated PNGs visually
  - [ ] Commit baselines as a distinct PR with human review

## Dev Notes

### Critical guardrails (do not violate)

1. **Bundle budgets are enforced, not advisory.** A PR that pushes owner-workspace initial JS over 250 KB gzipped FAILS. Options: (a) lazy-load more, (b) remove a dep, (c) negotiate a budget increase in an ADR. Never raise the budget silently.
2. **Public-statement budget is stricter.** 180 KB gzipped — because NFR-P9 + architecture §3.4 D1. No auth libraries, no rich forms, minimal state. If a dep leaks in, the bundle-graph gate catches it.
3. **Visual regression baselines require human approval.** Never auto-accept Chromatic/Percy diffs. A regression that's "obviously fine" is exactly how unnoticed layout bugs ship.
4. **Font budget is hard.** Adding a third weight of Tajawal means dropping something else. Do not add Tajawal-900 or Inter-900 without explicit budget renegotiation.
5. **Zoneless invariant is non-negotiable.** Any PR that introduces `zone.js` fails the build. There is no "temporary zone for this one feature."
6. **Lighthouse thresholds on CI match prod targets.** If mobile perf < 75 on CI, it's < 75 in production. Do not blame CI environment noise — median-of-3 runs + stable throttling profile eliminate noise.
7. **RTL visual regression covers every shell screen.** Layout bugs only surface in one direction — never ship without both snapshots.

### Source tree — files to create

```
apps/owner-workspace/
├── angular.json                                    # MODIFY (budgets)
└── e2e/visual/
    ├── shell.spec.ts                               # NEW
    └── __screenshots__/                            # NEW (committed baselines)

apps/public-statement/
└── angular.json                                    # MODIFY (budgets + stats.json emission)

.github/workflows/
├── frontend-ci.yml                                 # MODIFY or NEW (orchestration)
├── chromatic.yml                                   # NEW
└── perf-digest.yml                                 # NEW (weekly)

scripts/
├── assert-font-budget.sh                           # NEW
├── assert-public-statement-boundary.js             # NEW
└── assert-zoneless.sh                              # INHERITED from Story 1.1

.lighthouserc.cjs                                   # MODIFY (Web Vitals assertions)

perf-history/                                        # NEW (optional — if not using LHCI server)
└── README.md
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Budget | Every PR | Angular budgets in angular.json + custom scripts |
| Perf audit | Every PR on 4 routes | Lighthouse CI with Web Vitals assertions |
| Visual regression | Every story + every shell screen × LTR/RTL × 3 breakpoints | Chromatic/Percy + Playwright toHaveScreenshot |
| Bundle-graph | Every PR | Custom script parsing stats.json |
| Zoneless | Every PR | Shell script grep |

**Forbidden:**
- Disabling budgets per-PR
- Auto-accepting visual regression diffs
- Adding fonts without budget renegotiation
- `zone.js` anywhere
- Unreviewed baseline updates

### Project Structure Notes

This story closes the CI gate on performance + RTL parity. After this story, every subsequent PR is blocked on regressions. It depends on Story 1.9's Storybook setup (for Chromatic baselines), Story 1.10's Lighthouse CI setup (for Web Vitals), and Story 1.1's self-hosted fonts + zoneless bootstrap.

**Detected variances to reconcile:**
1. Chromatic vs Percy — decision deferred; pick one in this story (default: Chromatic for better Storybook integration). Document in ADR.
2. Lighthouse CI server vs file-based trend tracking — file-based (`perf-history/` folder) is simpler and good enough at MVP scale. Defer LHCI server deployment to post-MVP.
3. Playwright visual regression vs Chromatic for shell screens — Chromatic handles Storybook; Playwright handles live-app shell screens. Both coexist.

### Architecture compliance

- **§3.4 Frontend Scaffold** — day-one frontend gates (bundle-graph, initial-JS size, zoneless invariant) all activated here
- **NFR-P1, P2, P3** — Web Vitals thresholds enforced
- **NFR-P4** — route-level perf budget supports the < 500ms debt-recording target
- **NFR-P9** — RTL parity via visual regression + Lighthouse running on both directions
- **UX Performance Budgets (UX-Correlated)** — per-route + per-font + per-image budgets

### Library / framework requirements

| Layer | Package | Version |
|---|---|---|
| Build | Angular CLI | 21 (budget enforcement) |
| Audit | @lhci/cli | latest stable |
| Visual regression | Chromatic (primary) or @percy/cli | latest stable |
| E2E visual | Playwright | latest stable |

### Testing requirements

- **Budget**: Angular budgets + custom scripts gate on every PR
- **Perf**: Lighthouse CI with Web Vitals assertions on 4 routes
- **Visual regression**: Chromatic/Percy on every Storybook story × LTR/RTL; Playwright toHaveScreenshot on shell screens × 3 breakpoints × LTR/RTL
- **Bundle-graph**: custom script parses stats.json; fails on forbidden module paths
- **Zoneless**: shell script inherited from Story 1.1

### Previous Story Intelligence

**Prerequisites in order:**
- **Epic 0 scaffold** — CI workflows exist per 0.9; Azure preview environments per 0.10
- **Story 1.1** — self-hosted fonts, zoneless bootstrap script, stylelint config
- **Story 1.9** — Storybook stories ready for Chromatic baselines, Phase-1 components instrumented
- **Story 1.10** — Lighthouse CI scaffold, PR comment infrastructure, axe-core E2E harness

**Downstream consumers:**
- Every subsequent Epic 1 PR (1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8) must pass these gates
- Epic 2–7 inherit the gates

### Latest Technical Information

**Angular 21 bundle budgets:**
- `initial` — app + polyfills + main route JS
- `anyComponentStyle` — per-component styles file
- `bundle` (named) — per-chunk budget; use for lazy routes
- `all` — total output

**Chromatic + Storybook 8+ (Angular 21):**
- `chromatic --project-token=$CHROMATIC_PROJECT_TOKEN --exit-zero-on-changes` on PR
- `--auto-accept-changes` only on `main` branch (still requires a maintainer review in Chromatic UI before storybook freezes)

**Playwright toHaveScreenshot:**
```ts
test('shell LTR dashboard', async ({ page }) => {
  await page.goto('/dashboard');
  await expect(page).toHaveScreenshot('dashboard-ltr.png', { maxDiffPixelRatio: 0.001 });
});
```

**Lighthouse Web Vitals assertions:**
```js
'largest-contentful-paint': ['error', { maxNumericValue: 2500 }],
'interaction-to-next-paint': ['error', { maxNumericValue: 200 }],
'cumulative-layout-shift': ['error', { maxNumericValue: 0.1 }],
```

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Story 1.11
- Frontend scaffold gates — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.4
- NFR-P1–P11 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md) Performance section
- UX Performance Budgets — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) §Performance Budgets (UX-Correlated)
- Story 1.1 (fonts, zoneless) — [_bmad-output/implementation-artifacts/1-1-bilingual-shell-design-tokens-rtl-pipeline.md](./1-1-bilingual-shell-design-tokens-rtl-pipeline.md)
- Story 1.9 (components, Storybook) — [_bmad-output/implementation-artifacts/1-9-phase-1-component-api-design-system-library.md](./1-9-phase-1-component-api-design-system-library.md)
- Story 1.10 (Lighthouse, axe) — [_bmad-output/implementation-artifacts/1-10-accessibility-ci-gates.md](./1-10-accessibility-ci-gates.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Freezes performance contract + visual regression baseline for Epic 1 and beyond
- Public-statement 180 KB gz budget protects NFR-P9 + public-flow simplicity
- Chromatic chosen for Storybook visual regression (default); Percy available as fallback

### File List

_Populated during implementation — see Source tree section above._
