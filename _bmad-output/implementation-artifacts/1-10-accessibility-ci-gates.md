# Story 1.10: Accessibility CI gates

Status: ready-for-dev

## Story

As **Wahid**,
I want **axe-core + Lighthouse CI + ESLint template-a11y + stylelint logical-property rule running on every PR with zero-violation thresholds**,
so that **accessibility is mechanically enforced before any merge (NFR-A1, NFR-A3, UX-DR20)**.

**Covers requirements:** NFR-A1 (WCAG 2.1 AA), NFR-A3 (keyboard nav), NFR-A9 (screen-reader AR support), UX Responsive Design & Accessibility sections.

**Why this story now:** Per the reorder, 1.10 lands after 1.9 (components) and before 1.11 (perf budget). With the Phase-1 components freshly committed, this story activates automated a11y enforcement so every subsequent Epic 1 PR (and all future work) is blocked on a11y violations.

## Acceptance Criteria

### AC1 — axe-core on every route-level E2E

**Given** the Playwright E2E harness in `apps/owner-workspace/e2e/`
**When** any `*.spec.ts` executes
**Then** `@axe-core/playwright` runs `AxeBuilder` against the page post-navigation
**And** the spec fails if any WCAG 2.1 AA violation is reported (zero tolerance)
**And** WCAG 2.1 AAA **critical** violations also fail (non-critical AAA advisories are warnings, not failures)
**And** the axe rules tags include `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`, `best-practice`
**And** axe runs in both LTR and RTL for every route (identical rule set both directions)

### AC2 — Lighthouse CI thresholds enforced

**Given** Lighthouse CI configured at `.lighthouserc.cjs` or `.lighthouserc.json`
**When** the CI job runs
**Then** the accessibility score is **≥ 95** on every key route: `/`, `/login` (Auth0 callback landing), `/onboarding`, `/dashboard`, `/profile`, `/profile/delete`
**And** the performance score is **≥ 85 desktop** / **≥ 75 mobile throttled** (Slow 4G, 4× CPU slowdown)
**And** best-practices score is **≥ 95**
**And** SEO score is **≥ 90** (the `/` landing surface)
**And** below-threshold scores fail the PR with a diff report commented onto the PR via `lhci autorun --upload.target=github-status`

### AC3 — ESLint `@angular-eslint/template-accessibility-*` rules

**Given** `.eslintrc.js` at repo root
**When** any Angular template file (`.html`) changes
**Then** `@angular-eslint/template/accessibility-alt-text`, `/accessibility-elements-content`, `/accessibility-label-has-associated-control`, `/accessibility-table-scope`, `/accessibility-valid-aria`, `/click-events-have-key-events`, `/no-autofocus`, `/no-distracting-elements`, `/no-positive-tabindex`, `/accessibility-interactive-supports-focus` rules all run with `error` severity
**And** violations block the PR merge
**And** a report is uploaded as a CI artifact for review

### AC4 — stylelint logical-property rule active

**Given** stylelint config inherited from Story 1.1
**When** any feature-level SCSS file changes
**Then** physical properties (`margin-left`, `margin-right`, `padding-left`, `padding-right`, `left`, `right`, `border-left`, `border-right`, `text-align: left|right`) fail with a clear error
**And** `libs/design-system/src/theme/primeng-quiet.preset.ts` is the only allowlisted file
**And** the CI job `pnpm run lint:style` runs on every PR and gates merge

### AC5 — Manual screen-reader test matrix documented

**Given** `docs/accessibility/manual-qa.md` (create if not present)
**When** a release candidate is prepared
**Then** the file documents the manual screen-reader test matrix:
- **VoiceOver AR on iOS** — Safari, iOS 17+ — test routes: `/`, `/login`, `/onboarding`, `/dashboard`, `/profile`
- **TalkBack AR on Android** — Chrome, Android 13+ — same routes
- **NVDA AR on Windows** — Firefox + Chrome, Windows 11 — same routes
**And** the file includes a release-candidate checklist template with pass/fail per screen × per AT × per locale (AR + EN)
**And** each entry links to the specific ACs the screen must satisfy
**And** the manual QA is run before each major Epic merge (documented in the Epic retrospective)

### AC6 — Keyboard navigation matrix

**Given** `docs/accessibility/keyboard-matrix.md` (create if not present)
**When** inspected
**Then** the file documents keyboard operability for every interactive component: Tab order, focus visibility, Enter/Space activation, Escape dismissal, Arrow-key navigation where appropriate
**And** maps to specific Phase-1 components from Story 1.9: DebtRow, DenominationPicker, HeroTile, ContextualNudge
**And** Playwright `.spec.ts` covers the matrix with `page.keyboard.press(...)` assertions

### AC7 — Touch target size enforced

**Given** any interactive element (button, link, form control) in feature code
**When** Lighthouse runs
**Then** the "tap targets are sized appropriately" audit passes — minimum 48 × 48 CSS px per UX Accessibility §Touch Targets
**And** a stylelint custom rule warns (not fails) when a `<button>` or `<a>` has explicit `height` or `min-height` below `48px`

### AC8 — Focus ring invariant

**Given** any focusable element in feature code
**When** focused (via `:focus-visible`)
**Then** a 2px solid teal ring with 2px offset is visible (`outline: 2px solid var(--primary); outline-offset: 2px`)
**And** global CSS in `libs/design-system/src/styles/focus-ring.scss` sets this as the default for `*:focus-visible`
**And** any feature CSS that sets `outline: none` without providing an alternate visible focus indicator fails a custom stylelint rule `no-focus-outline-removal-without-replacement`

### AC9 — RTL parity assertion

**Given** every route that has an E2E spec
**When** the spec runs
**Then** the spec executes twice — once with `dir="ltr"` + `lang="en"`, once with `dir="rtl"` + `lang="ar"`
**And** axe-core reports zero violations in both
**And** a Playwright helper `testInBothDirections(name, testFn)` wraps the `test()` call for every spec

### AC10 — CI reporting + PR comment

**Given** the a11y CI job completes
**When** the job posts results
**Then** a PR comment (via `actions/github-script` or equivalent) summarizes: axe-core violations count, Lighthouse scores, stylelint violations, ESLint a11y violations
**And** the comment links to the full CI artifact on failure
**And** the comment is updated on subsequent pushes (not duplicated)

## Tasks / Subtasks

### axe-core integration

- [ ] **T1 (AC: 1, 9)** — Install + configure `@axe-core/playwright`
  - [ ] Add `@axe-core/playwright` to `apps/owner-workspace/package.json`
  - [ ] Create `apps/owner-workspace/e2e/a11y/axe-harness.ts` with a `runAxe(page)` helper
  - [ ] Helper asserts zero violations at tags `wcag2a, wcag2aa, wcag21a, wcag21aa, best-practice`
- [ ] **T2 (AC: 9)** — `testInBothDirections` helper
  - [ ] Create `apps/owner-workspace/e2e/utils/rtl-ltr-runner.ts`
  - [ ] Wraps Playwright `test()` to run the body twice — once LTR + English, once RTL + Arabic
  - [ ] Sets `localStorage.faktuboh.lang` before navigation

### Lighthouse CI

- [ ] **T3 (AC: 2)** — Configure Lighthouse CI
  - [ ] Create `.lighthouserc.cjs` with assertions: `categories.accessibility: ['error', { minScore: 0.95 }]`, `categories.performance: ['error', { minScore: 0.85 }]` (desktop), `categories.best-practices: ['error', { minScore: 0.95 }]`, `categories.seo: ['error', { minScore: 0.9 }]`
  - [ ] Configure mobile profile with `throttlingMethod: 'simulate'`, `throttling: { rttMs: 150, throughputKbps: 1638, cpuSlowdownMultiplier: 4 }` (Slow 4G)
  - [ ] Mobile performance minScore: 0.75
  - [ ] Scan routes: `/`, `/login`, `/onboarding`, `/dashboard`, `/profile`, `/profile/delete`
- [ ] **T4 (AC: 2, 10)** — CI workflow
  - [ ] Create `.github/workflows/a11y-ci.yml` (or merge into existing `frontend-ci.yml`)
  - [ ] Job runs `lhci autorun` against a preview deploy
  - [ ] Upload results as artifact + comment on PR

### ESLint + stylelint rules

- [ ] **T5 (AC: 3)** — Enable Angular ESLint template-a11y rules
  - [ ] Update `.eslintrc.js` with all rules from AC3 at `error` severity
  - [ ] Add `@angular-eslint/template/prefer-self-closing-tags` as `warn` (cleanup-friendly)
- [ ] **T6 (AC: 4)** — Verify stylelint logical-property config from Story 1.1 is active
  - [ ] Run `pnpm run lint:style` — expect clean output
  - [ ] Add fixture test ensuring rule still fires on physical properties
- [ ] **T7 (AC: 8)** — Create `no-focus-outline-removal-without-replacement` stylelint rule
  - [ ] Scans for `outline: none` without a paired `box-shadow`, `border`, or other focus-indicating rule in the same declaration block
  - [ ] Add unit tests (fixture-based)
- [ ] **T8 (AC: 8)** — Create `libs/design-system/src/styles/focus-ring.scss`
  - [ ] Global `*:focus-visible { outline: 2px solid var(--primary); outline-offset: 2px; }`
  - [ ] Imported from `global.scss` (Story 1.1)

### Docs

- [ ] **T9 (AC: 5)** — Create `docs/accessibility/manual-qa.md`
  - [ ] Structure: ATs × routes × locales × pass-fail table template
  - [ ] Checklist items linked to AC IDs from relevant stories
  - [ ] "When to run" section: pre-Epic-merge, release candidate
- [ ] **T10 (AC: 6)** — Create `docs/accessibility/keyboard-matrix.md`
  - [ ] Per-component table: Tab order, focus state, Enter/Space behavior, Escape, arrows
  - [ ] Covers all 7 Phase-1 components

### Reporting

- [ ] **T11 (AC: 10)** — PR comment bot
  - [ ] Use `actions/github-script` to aggregate axe-core + Lighthouse + ESLint + stylelint counts
  - [ ] Post/update single PR comment per PR (de-dupe via comment body marker)

### Tests

- [ ] **T12 (AC: 1, 9)** — E2E spec for the shell (carries over from Story 1.1)
  - [ ] `apps/owner-workspace/e2e/shell/a11y.spec.ts` runs axe-core on the shell in LTR + RTL
  - [ ] Zero violations expected
- [ ] **T13 (AC: 3, 4, 7, 8)** — Lint rule self-tests
  - [ ] Fixture files for each custom rule exercise positive + negative paths
  - [ ] All fixtures pass/fail as expected

## Dev Notes

### Critical guardrails (do not violate)

1. **Zero WCAG 2.1 AA violations.** No exceptions, no "accept this one for now" inline disables. If a violation appears, fix the code — do not add `// axe-disable` comments.
2. **Lighthouse scores are floors, not targets.** The thresholds in AC2 are minimums; aim higher. If a regression drops a score, fix the regression, do not raise the threshold.
3. **RTL parity is non-negotiable.** Every E2E runs in both directions. Do not skip RTL because "it's the same code." Layout bugs surface only in one direction (flex-reverse, logical property bugs).
4. **Manual screen-reader QA runs before every epic merge.** Automated tooling catches ~70% of a11y issues; the remaining 30% surface only under VoiceOver/TalkBack/NVDA. Do not skip the manual pass.
5. **No `aria-hidden="true"` on focusable elements.** Common footgun — catches fail on both axe + Lighthouse. Template-a11y lint rule blocks.
6. **Touch targets ≥ 48 × 48 CSS px.** Even if the visual is smaller, pad the hit area via `::before` / negative margin. PrimeNG defaults satisfy this; custom buttons must verify.

### Source tree — files to create

```
apps/owner-workspace/
├── e2e/
│   ├── a11y/
│   │   └── axe-harness.ts                          # NEW
│   ├── shell/
│   │   └── a11y.spec.ts                            # NEW
│   └── utils/
│       └── rtl-ltr-runner.ts                       # NEW
└── package.json                                    # MODIFY (add @axe-core/playwright)

libs/design-system/src/styles/
└── focus-ring.scss                                 # NEW

docs/accessibility/
├── manual-qa.md                                    # NEW
└── keyboard-matrix.md                              # NEW

tools/stylelint-rules/
└── no-focus-outline-removal-without-replacement.js # NEW

.github/workflows/
└── a11y-ci.yml                                     # NEW (or merge into frontend-ci.yml)

.lighthouserc.cjs                                   # NEW
.eslintrc.js                                        # MODIFY (add template-a11y rules)
.stylelintrc.js                                     # MODIFY (add focus-outline rule)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| E2E a11y | Every route × LTR/RTL | Playwright + @axe-core/playwright |
| Audit | Every key route | Lighthouse CI |
| Lint | Every PR | ESLint @angular-eslint/template + custom stylelint rules |
| Manual | Pre-epic-merge | VoiceOver AR / TalkBack AR / NVDA AR matrix |

**Forbidden:**
- Inline `// axe-disable` or `/* stylelint-disable */` in feature code (doc-level overrides require a comment explaining why)
- `aria-hidden="true"` on focusable elements
- `outline: none` without replacement
- `tabindex` values > 0
- Image elements without `alt` attribute
- Click handlers on non-button non-link elements without keyboard equivalents

### Project Structure Notes

This story wires a11y enforcement into the CI pipeline created by Epic 0. It does not add new app-level features; it activates gates on top of Story 1.9's component library and Story 1.1's shell.

**Detected variances to reconcile:**
1. `@axe-core/playwright` assumes Playwright 1.40+; verify the pinned version supports it.
2. Lighthouse CI in CI needs a deployed preview URL to audit — this depends on Azure Static Web Apps PR preview environments set up in Epic 0 Story 0.10. If those aren't live, Lighthouse runs against a local `npx http-server` serving `dist/` as a fallback.
3. Storybook stories (from Story 1.9) also deserve axe coverage — `@storybook/addon-a11y` adds a viewport-local axe panel; wire it up in this story for manual QA convenience.

### Architecture compliance

- **NFR-A1** (WCAG 2.1 AA) — directly enforced by axe + Lighthouse
- **NFR-A3** (keyboard nav) — enforced by Playwright keyboard matrix + template-a11y rules
- **NFR-A9** (screen-reader AR) — covered by manual QA matrix
- **UX Design Foundation → Accessibility Considerations** — 48px touch targets, 2px focus ring, zoom survival
- **UX Responsive Design & Accessibility → Accessibility Strategy, RTL-Specific Accessibility, Keyboard Navigation**

### Library / framework requirements

| Layer | Package | Version |
|---|---|---|
| E2E a11y | @axe-core/playwright | latest stable |
| Audit | @lhci/cli (Lighthouse CI) | latest stable |
| Angular lint | @angular-eslint/template | latest stable (Angular 21 compatible) |
| stylelint | stylelint + custom rules | latest stable |

### Testing requirements

- **E2E a11y**: every route spec runs axe-core in LTR + RTL
- **Audit**: Lighthouse CI audits 6 key routes on every PR against thresholds from AC2
- **Lint**: template-a11y + logical-property + focus-removal rules gate every PR
- **Manual**: documented matrix run pre-epic-merge

### Previous Story Intelligence

**Prerequisites in order:**
- **Epic 0 scaffold** — CI pipelines exist per Story 0.9; Azure preview environments per 0.10
- **Story 1.1** — stylelint logical-property rule, Transloco runtime, focus-ring global style baseline
- **Story 1.9** — Phase-1 components exist for a11y coverage; Storybook stories ready for axe addon

**Downstream consumers:**
- Story 1.11 layers perf budgets on top of these a11y gates
- Every subsequent Epic 1 PR must pass these gates
- Epic 2/3/4/5/6/7 PRs inherit these gates

### Latest Technical Information

**Lighthouse CI on zoneless Angular 21:**
- `lhci autorun --collect.numberOfRuns=3` — median of 3 runs (avoids flaky TTI readings)
- Use `preset: 'desktop'` for desktop audits; `formFactor: 'mobile'` + explicit throttling for mobile
- Set `settings.skipAudits: ['uses-http2']` (Azure Front Door handles this; local preview may not)

**@axe-core/playwright 4.10+:**
```ts
import AxeBuilder from '@axe-core/playwright';
const results = await new AxeBuilder({ page })
  .withTags(['wcag2aa', 'wcag21aa', 'best-practice'])
  .analyze();
expect(results.violations).toEqual([]);
```

**Angular template-a11y rules (Angular ESLint 21):**
- Stable names (no longer prefixed with `accessibility-` in some versions — verify rule names against installed plugin version)

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Story 1.10
- UX Accessibility Strategy — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) §Responsive Design & Accessibility → Accessibility Strategy, RTL-Specific Accessibility, Keyboard Navigation, Motion/Contrast, Touch/Pointer
- NFR-A1/A3/A9 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md) Accessibility section
- Story 1.1 (tokens, stylelint) — [_bmad-output/implementation-artifacts/1-1-bilingual-shell-design-tokens-rtl-pipeline.md](./1-1-bilingual-shell-design-tokens-rtl-pipeline.md)
- Story 1.9 (components) — [_bmad-output/implementation-artifacts/1-9-phase-1-component-api-design-system-library.md](./1-9-phase-1-component-api-design-system-library.md)
- Frontend test conventions — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.4.2

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Activates mechanical a11y enforcement across axe-core, Lighthouse CI, ESLint template-a11y, and stylelint
- Manual screen-reader QA matrix documented for pre-Epic-merge execution
- RTL parity invariant via `testInBothDirections` helper

### File List

_Populated during implementation — see Source tree section above._
