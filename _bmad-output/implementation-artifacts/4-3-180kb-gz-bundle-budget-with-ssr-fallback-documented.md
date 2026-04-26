# Story 4.3: 180KB gz bundle budget with SSR fallback documented

Status: ready-for-dev

## Story

As **Wahid**,
I want **the `apps/public-statement` initial JS bundle enforced at ≤ 180 KB gzipped with LCP < 2.5s on Slow 4G, with a pre-committed SSR fallback documented if the budget cannot be hit**,
so that **the hostile first-contact surface renders fast OR falls back gracefully** (NFR-P1 public, Winston's entry criterion).

**Covers requirements:** Epic 4 — NFR-P1 (180 KB gz public), §2.2 critical NFRs (split budget), §4.5 D4.15 pure online SPA, §7.4.1 cost-floor constraint. Epic 4 entry criterion per the delegated decision set [Source: epics.md Epic 4 Story 4.3; architecture.md §2.2].

**Why this story now:** Story 4.2 made the bundle-graph boundary measurable. This story makes the bundle-size budget measurable. Without it, Story 4.5 can land an 800 KB bundle that passes all other gates. The SSR fallback is documented pre-emptively so that if the budget is unreachable, the pivot is deterministic rather than invented under pressure.

## Acceptance Criteria

### AC1 — `budget.json` or `angular.json` budgets block

**Given** Angular 21's `architect.build` supports `budgets[]`
**When** `apps/public-statement`'s production build configuration is set
**Then** `angular.json` contains:

```json
"projects": {
  "public-statement": {
    "architect": {
      "build": {
        "configurations": {
          "production": {
            "budgets": [
              {
                "type": "initial",
                "maximumError": "180kb",
                "maximumWarning": "160kb",
                "baseline": "gzip"
              },
              {
                "type": "anyComponentStyle",
                "maximumError": "10kb"
              },
              {
                "type": "bundle",
                "name": "main",
                "maximumError": "180kb",
                "baseline": "gzip"
              }
            ]
          }
        }
      }
    }
  }
}
```

**And** the `initial` budget is measured gzipped — Angular 21 defaults to raw bytes; this config explicitly uses `baseline: "gzip"` via the `@angular/build` browser-esbuild builder
**And** a build exceeding 180 KB gzipped exits non-zero [Source: architecture.md §2.2; §7.4.1 free-tier implications].

### AC2 — Route-lazy chunks respect their own 60 KB gz budget

**Given** §2.2 specifies "Route-lazy < 60 KB gz (both apps)"
**When** a lazy-loaded route (e.g., `statement-view`) is built
**Then** `angular.json` adds a per-lazy-bundle budget:

```json
{
  "type": "bundle",
  "name": "statement-view",
  "maximumError": "60kb",
  "baseline": "gzip"
}
```

**And** a PR that grows `statement-view.chunk.js` beyond 60 KB gz fails the build [Source: architecture.md §2.2].

### AC3 — CI reports bundle size delta per PR

**Given** every PR touches bundle size implicitly
**When** `ci.yml` runs for a PR
**Then** a new step in `bundle-graph-public-statement` job:
1. `git fetch origin main`
2. `git checkout origin/main -- .`
3. Build → measure → record `main` baseline
4. `git checkout -`
5. Build → measure → record PR size
6. Post a comment to the PR: `"public-statement initial JS: 165 KB gz (main) → 172 KB gz (+7 KB, 4.2% increase). Budget: 180 KB."`
**And** the comment thread updates on subsequent pushes (uses `actions/github-script` `github.rest.issues.listComments` + edit)
**And** the comment is informational (the actual budget failure fails the build separately) [Source: architecture.md §3.5 bundle-graph gate].

### AC4 — Lighthouse CI nightly asserts LCP < 2.5s on Slow 4G

**Given** LCP is a silent-decay metric (§2.2)
**When** `.github/workflows/lighthouse-nightly.yml` runs at 02:00 UTC
**Then** it targets the deployed staging URL `statement-staging.faktuboh.com/t/{fixture-token}`
**And** Lighthouse config emulates Moto G Power + Slow 4G throttling (`{ throttlingMethod: 'simulate', throttling: { cpuSlowdownMultiplier: 4, downloadThroughputKbps: 1638.4, uploadThroughputKbps: 675, rttMs: 150 } }`)
**And** asserts: `LCP < 2500ms`, `FCP < 1800ms`, `TTFB < 800ms`, `CLS < 0.05`, `TBT < 200ms`
**And** the fixture token is a pre-seeded valid `statement.read` scope token with a realistic 3-transaction test statement (seeded by an integration test fixture job)
**And** the workflow is nightly, not per-PR — Lighthouse runs are noisy and slow [Source: architecture.md §2.2; epics.md Story 4.3 AC].

### AC5 — LCP trend dashboard in App Insights

**Given** Lighthouse results must feed the golden-signals dashboard
**When** the nightly workflow completes
**Then** a step writes a custom metric to App Insights: `lighthouseLcpMs`, `lighthouseFcpMs`, `lighthouseClsScore`, with properties `{ route: '/t/{token}', userAgent: 'lighthouse-ci' }`
**And** the "Business liveness" + "Golden signals" dashboards (D4.20) include a 14-day trend panel on `lighthouseLcpMs`
**And** an alert fires when LCP exceeds 2500ms for 3 consecutive nights (Story 4.3's own drift trigger) [Source: architecture.md §4.9 silent-decay SLOs; §4.6 D4.20].

### AC6 — SSR fallback is documented before Epic 4 proceeds past this story

**Given** the Epic 4 entry criterion requires a pre-committed SSR fallback doc
**When** this story lands
**Then** `docs/architecture/ssr-fallback.md` exists in the repo with the following sections (headings mandatory):
- **Fallback trigger condition** — "2 consecutive PRs cannot hit the 180 KB gz budget OR LCP regression sustained > 14 days on the nightly Lighthouse trend"
- **Target topology** — server-rendered HTML for `/t/{token}` routes; Angular public-statement SPA retained as the owner-edit layer (post-confirmation) only; bot-safe rendering layer independent of the SPA
- **Implementation approach** — Razor Pages under a new `Faktuboh.PublicStatementHost` project OR a static-HTML path from the backend API; the Angular app becomes a progressive enhancement loaded only post-confirmation
- **Estimated effort** — 5 engineer-days (Winston's budget)
- **Dependencies** — no change to capability-token scheme (4.1); no change to confirm flow (4.6); minor change to telemetry emission point
- **Reversibility** — if SSR path proves sufficient long-term, the SPA path is deprecated; otherwise SSR remains as a bot-serve fallback layer
- **Test plan** — k6 against the SSR surface at 166 rps proves throughput parity; Lighthouse on SSR page asserts LCP < 1500ms

**And** the doc is reviewed before this story merges [Source: epics.md Story 4.3 AC; delegated decision — Epic 4 entry criterion].

### AC7 — Fallback trigger is operationalized, not just documented

**Given** trigger conditions must be executable
**When** the trigger "2 consecutive PRs cannot hit budget" fires
**Then** `.github/workflows/ci.yml`'s bundle-size step writes a failure marker to `/tmp/budget-breach.flag`
**And** a nightly GitHub Actions `ssr-fallback-check.yml` workflow:
1. Queries recent CI runs via GitHub API for the `bundle-graph-public-statement` job on `main`
2. If the last 2 consecutive runs failed the budget → creates a GitHub Issue titled `"SSR fallback trigger condition met — escalate"` with body linking to the failing runs + `docs/architecture/ssr-fallback.md`
**And** a separate check: if the nightly Lighthouse LCP trend shows > 2500ms sustained over 14 days → same escalation issue created [Source: epics.md Story 4.3 AC; architecture.md §7.9 revisit triggers].

### AC8 — Source-map explorer artifact on every PR

**Given** budget breaches need diagnosis-ready data
**When** the bundle-graph CI job runs
**Then** after the build, `pnpm dlx source-map-explorer dist/apps/public-statement/*.js --html > bundle-report.html`
**And** `bundle-report.html` is uploaded as a GitHub Actions artifact with 30-day retention
**And** the PR comment links to the artifact [Source: architecture.md §3.5].

### AC9 — Bundle-sensitive imports are flagged in review

**Given** some imports are known-heavy
**When** the `apps/public-statement` ESLint config is extended
**Then** a repo-local rule `@local/bundle-warn` warns on imports of: `rxjs` (prefer `rxjs/operators` scoped imports), `lodash` (reject — use focused utilities), `moment` (reject — use `date-fns` or native `Intl.DateTimeFormat`), `@angular/material` (reject — PrimeNG only)
**And** warnings appear in `ng lint public-statement` output for review [Source: architecture.md §2.2, §3.4].

## Tasks / Subtasks

### Budget configuration

- [ ] **T1 (AC: 1)** — Modify `angular.json` for `public-statement` project
  - [ ] Add `budgets[]` array per AC1 to `configurations.production`
  - [ ] Verify `baseline: "gzip"` is applied (Angular 21 browser-esbuild supports this)
  - [ ] Run `pnpm ng build public-statement --configuration production` locally to confirm gzipped output < 180 KB on scaffold (empty app ≈ 90-110 KB)
- [ ] **T2 (AC: 2)** — Add per-lazy-route budget
  - [ ] `{ "type": "bundle", "name": "statement-view", "maximumError": "60kb", "baseline": "gzip" }`
  - [ ] Verify against the placeholder statement-view chunk (currently empty)

### CI bundle-size reporting

- [ ] **T3 (AC: 3)** — Extend `bundle-graph-public-statement` CI job
  - [ ] New step: baseline build on `main` (clone, checkout, build, record size, restore)
  - [ ] New step: PR build, record size
  - [ ] New step: use `actions/github-script` to post/edit a PR comment with the delta
  - [ ] Comment body format: `"public-statement initial JS: {main-size} KB gz (main) → {pr-size} KB gz ({+/-delta} KB, {pct}% change). Budget: 180 KB."`
- [ ] **T4 (AC: 8)** — Source-map-explorer artifact step
  - [ ] Install `source-map-explorer` in CI
  - [ ] Run after build; output `bundle-report.html`
  - [ ] Upload via `actions/upload-artifact@v4` with 30-day retention

### Lighthouse CI

- [ ] **T5 (AC: 4)** — Create `.github/workflows/lighthouse-nightly.yml`
  - [ ] Cron: `0 2 * * *`
  - [ ] Steps: install Lighthouse CI, configure with `lighthouserc.json`, run against staging URL, assert thresholds
  - [ ] `lighthouserc.json`: Slow 4G throttling, mobile emulation, thresholds per AC4
- [ ] **T6 (AC: 4)** — Seed staging fixture token
  - [ ] Create integration-test-driven seed script `scripts/seed/lighthouse-fixture.mjs` that inserts a test owner + contact + 3-transaction statement + issues a `statement.read` token
  - [ ] Token stored in GitHub Actions secrets as `LIGHTHOUSE_FIXTURE_TOKEN` (re-seeded nightly before Lighthouse runs)
- [ ] **T7 (AC: 5)** — App Insights custom metric writer
  - [ ] Workflow step using `az monitor metrics` or Application Insights REST API
  - [ ] Writes `lighthouseLcpMs`, `lighthouseFcpMs`, `lighthouseClsScore`, `lighthouseTbtMs` with timestamp

### Dashboards + alerts

- [ ] **T8 (AC: 5)** — Extend App Insights workbook
  - [ ] Add 14-day LCP trend chart to golden-signals dashboard
  - [ ] Alert rule: `lighthouseLcpMs > 2500` for 3 consecutive days → email to `wahid.wb@gmail.com`

### SSR fallback doc

- [ ] **T9 (AC: 6)** — Create `docs/architecture/ssr-fallback.md`
  - [ ] Sections per AC6 headings
  - [ ] Draft content: trigger conditions, target topology, implementation approach, effort estimate, dependencies, reversibility, test plan
  - [ ] Link from main architecture.md §2.2 as a footnote
- [ ] **T10 (AC: 7)** — Create `.github/workflows/ssr-fallback-check.yml`
  - [ ] Nightly cron: `0 3 * * *`
  - [ ] Step 1: Query GitHub API for last 2 `bundle-graph-public-statement` runs on `main` → if both failed budget check → raise issue
  - [ ] Step 2: Query App Insights for 14-day LCP trend → if sustained > 2500ms → raise issue
  - [ ] Issue template at `.github/ISSUE_TEMPLATE/ssr-fallback-escalation.md` (auto-generated, not manually filed)

### ESLint bundle-warn rule

- [ ] **T11 (AC: 9)** — Create `tools/eslint/bundle-warn.js`
  - [ ] Warns on imports from denylist: `lodash`, `moment`, `@angular/material`, bare `rxjs` (without subpath)
  - [ ] Configurable denylist per `.eslintrc.cjs`
- [ ] **T12 (AC: 9)** — Register rule in `apps/public-statement/.eslintrc.cjs`

### Tests

- [ ] **T13 (AC: 1)** — Integration test: intentional budget breach
  - [ ] Fixture: add a large synthetic module (e.g., 200 KB of random bytes as an exported constant) to `apps/public-statement/src/app/features/statement-view/`
  - [ ] Run `pnpm ng build public-statement --configuration production`
  - [ ] Assert exit code non-zero + error message mentions `180kb`
  - [ ] Revert fixture after test
- [ ] **T14 (AC: 3)** — CI smoke test for the delta comment
  - [ ] Dry-run the GitHub-script step against a fixture PR; assert comment body format

## Dev Notes

### Critical guardrails

1. **180 KB gz is the hostile-contact budget.** Every KB past the budget loses a percentage of MENA Slow-4G users who bounce before confirm. This is not a "nice to have"; it is the defining NFR for the public surface per §2.2.
2. **`baseline: "gzip"` is load-bearing.** Angular's default budget check is raw bytes. Per-browser, per-CDN, the real wire cost is gzipped. Misreading raw as gz produces a false-negative budget — user-facing size is 3-4x larger than the dashboard implies.
3. **SSR fallback doc is not aspirational.** The trigger conditions are operationalized via the nightly workflow. When the trigger fires, the fallback is ready to invoke; nothing is "figured out under pressure."
4. **Lighthouse nightly, not per-PR.** Lighthouse runs are 30-60s each and flaky on shared CI runners. Per-PR would bleed the monthly Actions budget.
5. **Fixture token is re-seeded nightly.** Using a long-lived token against staging is a stale-fixture risk — the token may be expired or revoked when Lighthouse runs.
6. **No SSR wired in this story.** This story documents the fallback and operationalizes the trigger. Actually building SSR is a separate 5-day story triggered only when the condition fires. Don't pre-build infrastructure.

### Source tree

```
angular.json                                              # MODIFY — budgets[] per project
.github/workflows/
├── ci.yml                                                # MODIFY — extend bundle-graph job with delta reporting + source-map-explorer
├── lighthouse-nightly.yml                                # NEW
└── ssr-fallback-check.yml                                # NEW
.github/ISSUE_TEMPLATE/
└── ssr-fallback-escalation.md                            # NEW
docs/architecture/
└── ssr-fallback.md                                       # NEW

scripts/seed/
└── lighthouse-fixture.mjs                                # NEW

tools/eslint/
├── bundle-warn.js                                        # NEW
└── bundle-warn.test.js                                   # NEW

lighthouserc.json                                         # NEW — Slow 4G config

apps/public-statement/
└── .eslintrc.cjs                                         # MODIFY — activate @local/bundle-warn
```

### Architecture compliance

- **NFR-P1** (180 KB gz public) — this story is the mechanical enforcement.
- **§2.2** (critical NFRs — split budget, Slow 4G LCP target) — fully operationalized.
- **§3.4** (bundle-graph gate + initial-JS-size gate) — this story completes the gate by adding the SIZE check atop the BOUNDARY check from 4.2.
- **§3.5** (day-one CI gates) — bundle-size is one of the day-one gates.
- **§4.9** (silent-decay SLOs — LCP trend) — nightly Lighthouse feeds the dashboard.
- **§7.4.1** (cost-floor — SWA free tier) — bundle size directly affects SWA bandwidth consumption; budget keeps within 100GB/mo free tier.
- **§7.9** (revisit triggers — SSR fallback) — documented and operationalized here.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Build | `@angular/build` (esbuild) | Angular 21 default; supports `baseline: "gzip"` |
| CI | `@lhci/cli` (Lighthouse CI) | Nightly workflow |
| CI | `source-map-explorer` | PR artifact |
| CI | `actions/github-script` | PR comment + issue creation |
| CI | `@actions/github` | GitHub API for CI-run query |

### Testing requirements

- **Unit:** ESLint rule tests for `@local/bundle-warn` against good/bad imports.
- **Integration:** Intentional budget breach → build fails with clear message.
- **E2E/Synthetic:** Lighthouse nightly against staging.
- **Operational:** Manual verification — seed a staging token, trigger the nightly workflow once, confirm dashboard write.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.9 — GitHub Actions CI + OIDC
- Story 0.10 — Azure infrastructure (staging `statement-staging.faktuboh.com` DNS)
- Story 0.11 — App Insights (custom metric ingest)
- Story 1.11 — Performance budget + visual regression CI gates (this story extends the mechanism to the public app)
- Story 4.2 — `apps/public-statement` scaffold with `bundle-graph-public-statement` job existing

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.3
- Split budget — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.2
- Day-one gates — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.5
- Silent-decay SLOs — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.9
- Cost-floor SWA — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §7.4.1
- Revisit triggers — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §7.9
- NFR-P1 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Story 1.11 performance budget — [_bmad-output/implementation-artifacts/1-11-performance-budget-visual-regression-ci-gates.md](./1-11-performance-budget-visual-regression-ci-gates.md)
- Story 4.2 two-target build — [_bmad-output/implementation-artifacts/4-2-apps-public-statement-as-distinct-angular-build-target.md](./4-2-apps-public-statement-as-distinct-angular-build-target.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- SSR fallback doc is the Epic 4 entry criterion deliverable — this story's primary non-code artifact.
- Budget at scaffold time (empty app) is ~90-110 KB gz; StatementView (Story 4.5) will consume the remaining headroom. If 4.5 lands at >75 KB gz, 4.3's SSR fallback trigger will fire earlier than expected.

### File List

_Populated during implementation._
