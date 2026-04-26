# Story 1.1: Bilingual shell with design tokens and RTL pipeline

Status: ready-for-dev

## Story

As a **new visitor**,
I want **Faktuboh to render in Arabic (RTL) or English (LTR) with a visual language consistent with the Faktuboh Quiet design system**,
so that **the product feels native to my reading direction from the first moment I load it**.

**Covers requirements:** FR4 (language preference), NFR-A1 (WCAG 2.1 AA), NFR-P1 (LCP), NFR-P9 (RTL parity), UX-DR1 (Faktuboh Quiet tokens).

**Why this story first:** This is the structural root of every subsequent Epic 1 UI. Every component built in Stories 1.9, 1.8, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7 consumes the design tokens, the Transloco runtime, and the `<html lang dir>` pipeline created here. Per the reorder, 1.1 precedes the component-API story (1.9) because the tokens + RTL plumbing must exist before components can reference them.

## Acceptance Criteria

### AC1 — `<html lang dir>` set before first paint

**Given** a fresh browser session on `https://app.faktuboh.com`
**When** the user selects Arabic as the language (or has Arabic stored in localStorage / profile)
**Then** `<html lang="ar" dir="rtl">` is written by the bootstrap script **before** Angular hydration — no FOUC, no direction flip after paint
**And** selecting English writes `<html lang="en" dir="ltr">` in the same pre-hydration window
**And** the direction-persistence contract holds across reload: value read from localStorage key `faktuboh.lang` → fallback to `navigator.language` matching `ar*` → fallback to `en`

### AC2 — Logical properties only

**Given** any feature-level SCSS file under `apps/owner-workspace/src/app/features/**`
**When** stylelint runs in CI
**Then** physical properties (`margin-left`, `margin-right`, `padding-left`, `padding-right`, `left`, `right`, `border-left-*`, `border-right-*`, `text-align: left|right`) fail the lint check with a clear error message pointing at the logical replacement
**And** the allowed equivalents are `margin-inline-start|end`, `padding-inline-start|end`, `inset-inline-start|end`, `border-inline-start|end-*`, `text-align: start|end`
**And** `libs/design-system/**` is allowed to use physical props **only** inside the PrimeNG theme shim file (`primeng-quiet.preset.ts`) because PrimeNG internals are LTR-authored; everywhere else is logical-only

### AC3 — Fonts preloaded with `font-display: swap`

**Given** `apps/owner-workspace/src/index.html`
**When** the page loads
**Then** `<link rel="preload" as="font" type="font/woff2" crossorigin href="/fonts/tajawal-regular.woff2">` appears in `<head>` (plus medium, bold variants)
**And** `<link rel="preload" as="font" type="font/woff2" crossorigin href="/fonts/inter-regular.woff2">` appears (plus medium, bold variants)
**And** `@font-face` declarations in `libs/design-system/src/styles/fonts.scss` use `font-display: swap` for both Tajawal and Inter
**And** total preloaded woff2 footprint is ≤ 120 KB (enforced in Story 1.11 perf budget)

### AC4 — Faktuboh Quiet PrimeNG preset (light-mode only)

**Given** `libs/design-system/src/theme/faktuboh-quiet.preset.ts`
**When** Angular bootstraps
**Then** the PrimeNG Aura preset is extended with the Faktuboh Quiet token mappings: `--primary: #0F766E`, `--primary-hover: #0D9488`, `--accent-gold: #B45309`, `--surface: #FFFFFF`, `--surface-raised: #FAFAFA`, `--text-primary: #18181B`, `--text-secondary: #52525B`, `--text-muted: #71717A`, `--success: #15803D`, `--warning: #D97706`, `--danger: #B91C1C`, `--divider: #E4E4E7`
**And** tokens are scoped to `:root` with `[data-theme="light"]` selector (dark mode tokens are **deferred to v2** per this story's delegated decision — no `[data-theme="dark"]` block lands at MVP)
**And** `providePrimeNG({ theme: { preset: FaktuboQuietPreset } })` is wired into `apps/owner-workspace/src/app/app.config.ts`

### AC5 — Brand orange forbidden in UI

**Given** any TS/SCSS/HTML file under `apps/**` or `libs/**`
**When** ESLint + stylelint run
**Then** the literal color `#ff8100` (case-insensitive) fails with rule `no-brand-orange-in-ui` — message: "Brand orange is reserved for marketing; never use it in product UI. Use `--accent-gold` for value emphasis."
**And** `rgb(255, 129, 0)` and `hsl(30, 100%, 50%)` equivalents also fail
**And** the rule is in `.eslintrc.js` + `.stylelintrc.js` at repo root so both apps inherit

### AC6 — Language toggle flips within 200ms without reload

**Given** an authenticated or anonymous user on any route
**When** the user activates the language toggle in the top app bar
**Then** `<html lang>` and `<html dir>` update synchronously
**And** Transloco `setActiveLang(newLang)` triggers the new bundle load
**And** measured end-to-end (click → layout flip complete) ≤ 200ms (verified in Playwright E2E timing)
**And** no full page reload occurs (Playwright asserts no `navigation` event)
**And** route state, form inputs, scroll position are preserved

### AC7 — Typography scale tokens applied

**Given** the typography token set from UX Design System §Typography
**When** any component references the scale
**Then** 9 type tokens are defined as CSS custom properties: `--text-xs 12/16 400`, `--text-sm 14/20 400`, `--text-base 16/24 400`, `--text-lg 18/28 500`, `--text-xl 20/28 500`, `--text-2xl 24/32 600`, `--text-3xl 30/36 600`, `--text-4xl 36/40 700`, `--text-5xl 48/48 700`
**And** financial contexts (any `.amount`, `.hero-amount`, `AmountDisplay` component) apply `font-feature-settings: "tnum" 1, "lnum" 1` — tabular + lining numerals
**And** Arabic numerals (0-9) are used, not Arabic-Indic (٠-٩), even when `lang="ar"` — prevents paste breakage against bank statements

### AC8 — Spacing scale + border-radius tokens

**Given** `libs/design-system/src/theme/tokens.scss`
**When** tokens are inspected
**Then** spacing scale is exposed as `--space-0: 0, --space-2: 0.125rem, --space-4: 0.25rem, --space-8: 0.5rem, --space-12: 0.75rem, --space-16: 1rem, --space-20: 1.25rem, --space-24: 1.5rem, --space-32: 2rem, --space-40: 2.5rem, --space-48: 3rem, --space-64: 4rem, --space-96: 6rem`
**And** radius tokens: `--radius-default: 6px, --radius-card: 8px, --radius-modal: 12px, --radius-pill: 9999px`
**And** shadows: `--shadow-card: 0 1px 2px rgba(0,0,0,0.06), --shadow-modal: 0 10px 38px rgba(0,0,0,0.18)`

### AC9 — Transloco bilingual runtime

**Given** `libs/core/src/i18n/`
**When** the app bootstraps
**Then** Transloco is configured with `availableLangs: ['ar', 'en']`, `defaultLang: 'ar'`, `fallbackLang: 'en'`, `reRenderOnLangChange: true`, `prodMode: environment.production`
**And** translation bundles load from `assets/i18n/{lang}.json` lazily per scope (scope per feature — `dashboard`, `profile`, `contacts`, etc.)
**And** a `LangBootstrapService` reads `localStorage.faktuboh.lang` → `navigator.language` → default `ar` and calls `translocoService.setActiveLang(...)` before Angular's first CD cycle
**And** translation keys follow `feature.section.element` pattern (e.g., `dashboard.nudge.add-first-debt`)

### AC10 — Zoneless + OnPush invariant

**Given** `apps/owner-workspace/src/main.ts` and `angular.json` polyfills
**When** CI runs bundle scan
**Then** `zone.js` appears **nowhere** in the bundle graph of either app (scan `dist/**/*.js` for `zone.js` or `NgZone` internals — zero matches)
**And** every component created in this story uses `ChangeDetectionStrategy.OnPush` and standalone components
**And** `bootstrapApplication(App, { providers: [provideZonelessChangeDetection(), ...] })` is the entry point
**And** a dev-mode test fails the build if any component in the workspace declares `NgZone` as a dependency

## Tasks / Subtasks

### Scaffold + bootstrap

- [ ] **T1 (AC: 1, 9, 10)** — Create Angular workspace bootstrap at `apps/owner-workspace/src/main.ts`
  - [ ] Use `bootstrapApplication(AppComponent, appConfig)` with `provideZonelessChangeDetection()`
  - [ ] Create `apps/owner-workspace/src/app/app.config.ts` with `provideRouter`, `provideHttpClient`, `provideAnimationsAsync`, `providePrimeNG`, `provideTransloco`
  - [ ] Add `LangBootstrapService` as `APP_INITIALIZER` — runs before root component construction
- [ ] **T2 (AC: 1)** — Create `apps/owner-workspace/src/index.html`
  - [ ] Inline `<script>` in `<head>` reads `localStorage.faktuboh.lang` or `navigator.language` and writes `document.documentElement.lang` + `document.documentElement.dir` **before** the Angular polyfill script tag
  - [ ] Keep script ≤ 1 KB; no external imports; CSP-safe inline (use hash in CSP policy)
- [ ] **T3 (AC: 3)** — Preload fonts
  - [ ] Add 6 `<link rel="preload" as="font" type="font/woff2" crossorigin>` entries in `<head>` for Tajawal + Inter × 3 weights (400, 500, 700)
  - [ ] Host the woff2 files under `apps/owner-workspace/src/assets/fonts/` — self-host, not Google CDN (CSP + NFR-P9 consistency)

### Design tokens + theme preset

- [ ] **T4 (AC: 4, 7, 8)** — Create `libs/design-system/src/theme/tokens.scss`
  - [ ] Define all color, typography, spacing, radius, shadow tokens listed in AC4/AC7/AC8 as CSS custom properties under `:root, [data-theme="light"]`
  - [ ] **Omit dark-mode tokens** — v2 scope per delegated decision; leave a `TODO: dark-mode deferred to v2` comment
- [ ] **T5 (AC: 4)** — Create `libs/design-system/src/theme/faktuboh-quiet.preset.ts`
  - [ ] Extend PrimeNG Aura preset via `definePreset(Aura, { semantic: {...} })`; map `primary.500`, `surface.0`, etc. to the CSS custom property names from T4
  - [ ] Export `FaktuboQuietPreset` for consumption in `app.config.ts`
- [ ] **T6 (AC: 4, 7)** — Create `libs/design-system/src/styles/global.scss`
  - [ ] Import `tokens.scss`, `fonts.scss`, Tailwind preflight
  - [ ] Set base `font-family: 'Tajawal', 'Inter', system-ui, sans-serif` on `:root`
  - [ ] Add `.amount, .hero-amount, [data-tnum]` selector with `font-feature-settings: "tnum" 1, "lnum" 1`

### Lint rules

- [ ] **T7 (AC: 2)** — Configure stylelint logical-property rule at repo root `.stylelintrc.js`
  - [ ] Rule: `declaration-property-value-disallowed-list` blocking `margin-left`, `margin-right`, `padding-left`, `padding-right`, `left`, `right`, `border-left`, `border-right`, `text-align: /left|right/`
  - [ ] Override file pattern: `libs/design-system/src/theme/primeng-quiet.preset.ts` — allowed
  - [ ] Add CI job `pnpm run lint:style` that fails on any violation
- [ ] **T8 (AC: 5)** — Create `no-brand-orange-in-ui` ESLint + stylelint rules
  - [ ] ESLint custom rule (drop in `tools/eslint-rules/no-brand-orange-in-ui.js`) — scans string literals for `#ff8100`, `rgb(255, 129, 0)`, `hsl(30, 100%, 50%)`
  - [ ] Stylelint equivalent using `declaration-property-value-disallowed-list` on `color`, `background`, `background-color`, `border-color`
  - [ ] Unit tests for the custom ESLint rule — positive + negative cases

### Transloco runtime

- [ ] **T9 (AC: 1, 9)** — Create `libs/core/src/i18n/lang-bootstrap.service.ts`
  - [ ] Reads `localStorage.getItem('faktuboh.lang')` → `navigator.language.startsWith('ar') ? 'ar' : 'en'` → default `'ar'`
  - [ ] Exposes `setLang(lang: 'ar' | 'en')` that writes to localStorage + calls `translocoService.setActiveLang(lang)` + updates `document.documentElement.lang/dir`
  - [ ] Persist to profile via `PATCH /v1/users/me` if authenticated (deferred wiring — profile endpoint lands in Story 1.5)
- [ ] **T10 (AC: 9)** — Create scaffolding translation bundles
  - [ ] `apps/owner-workspace/src/assets/i18n/ar.json` — initial keys: `shell.app-bar.toggle-lang`, `shell.nav.dashboard`, `shell.nav.contacts`, `shell.nav.profile`, `shell.footer.disclaimer`
  - [ ] `apps/owner-workspace/src/assets/i18n/en.json` — same keys mirrored
  - [ ] Transloco ESLint rule verifies every key used in templates has an entry in both locale bundles

### Shell chrome

- [ ] **T11 (AC: 6)** — Create `apps/owner-workspace/src/app/shell/app-shell.component.ts`
  - [ ] Standalone + OnPush + signal inputs
  - [ ] Renders `<header>` top app bar with logo, nav links, language toggle button
  - [ ] Renders `<main>` router outlet + `<footer>` with legal disclaimer slot (populated in Story 1.6)
  - [ ] Language toggle calls `langBootstrap.setLang(otherLang)`; measured toggle latency ≤ 200ms (Playwright timing)
- [ ] **T12 (AC: 6)** — Language toggle accessibility
  - [ ] `<button>` with `aria-label="Switch to English"` / `aria-label="التبديل إلى العربية"` (localized via Transloco)
  - [ ] Visible focus ring (2px teal, 2px offset) per UX Design Foundation §Accessibility
  - [ ] Keyboard-operable (Enter + Space)

### Tests

- [ ] **T13 (AC: 1, 6, 9, 10)** — Playwright E2E at `apps/owner-workspace/e2e/shell/lang-toggle.spec.ts`
  - [ ] Fresh session renders `<html lang="ar" dir="rtl">` when no storage + `navigator.language` starts with `ar`
  - [ ] Fresh session renders `<html lang="en" dir="ltr">` when `navigator.language` is `en-US`
  - [ ] Clicking toggle flips dir + lang within 200ms (measured via `performance.now()` bracket)
  - [ ] Axe-core assertion: zero WCAG 2.1 AA violations on the shell
  - [ ] Run in both Chromium + WebKit (NFR-P9 cross-browser)
- [ ] **T14 (AC: 2, 5)** — Lint-rule unit tests
  - [ ] Stylelint fixture file with physical-prop usage → assert failure count
  - [ ] ESLint fixture file with `#ff8100` literal → assert failure
  - [ ] Both green on the cleared fixtures
- [ ] **T15 (AC: 10)** — Bundle-scan CI step
  - [ ] Script `scripts/assert-zoneless.sh` greps `dist/owner-workspace/**/*.js` for `zone.js` or `/Zone\.\$current/` — fails with non-zero exit on any hit
  - [ ] Run as a required CI check on every PR
- [ ] **T16 (AC: 6)** — Vitest unit tests for `LangBootstrapService`
  - [ ] Happy path with localStorage populated
  - [ ] Fallback to `navigator.language`
  - [ ] Final fallback to `ar`
  - [ ] `setLang` persists + updates DOM attributes

## Dev Notes

### Critical guardrails (do not violate)

1. **Zoneless invariant — absolute.** Never import `zone.js` anywhere. Never inject `NgZone`. Signals + `OnPush` are the only change-detection primitives permitted. CI bundle scan enforces.
2. **Dark mode deferred to v2.** Do not ship `[data-theme="dark"]` tokens or a theme toggle — leaves ambiguity for post-MVP. One less surface to test in Story 1.11 visual regression.
3. **`<html lang dir>` pre-hydration.** The inline script in `index.html` MUST run before Angular hydrates. If FOUC appears in a Playwright recording, the script order is wrong — fix the order, do not work around with a CSS `.loading` overlay.
4. **Self-host fonts.** Google Fonts CDN breaks our CSP and adds a DNS hop harming LCP. Ship the woff2 files in `assets/fonts/` and add the preload links.
5. **No physical CSS properties in feature code.** Stylelint fails the build. Violations in generated code (Tailwind output, PrimeNG shim) are allowlisted file-by-file only.
6. **Brand orange is off-limits in UI.** Used in landing page marketing only — which lives in a separate repo. If a stakeholder asks for orange in-app, respond with the `--accent-gold` escape hatch and move on.
7. **Standalone components only.** No NgModules. All feature components use `standalone: true` (implicit in Angular 21 but keep the explicit flag for grep-ability).

### Source tree — files to create

```
apps/owner-workspace/
├── src/
│   ├── index.html                                   # NEW (pre-hydration script + font preloads)
│   ├── main.ts                                      # NEW (bootstrapApplication + zoneless)
│   ├── app/
│   │   ├── app.config.ts                            # NEW (provideTransloco, providePrimeNG, etc.)
│   │   ├── app.component.ts                         # NEW (hosts <ow-app-shell>)
│   │   └── shell/
│   │       ├── app-shell.component.ts               # NEW
│   │       ├── app-shell.component.html             # NEW
│   │       └── app-shell.component.scss             # NEW (logical props only)
│   └── assets/
│       ├── fonts/
│       │   ├── tajawal-regular.woff2                # NEW (self-hosted)
│       │   ├── tajawal-medium.woff2
│       │   ├── tajawal-bold.woff2
│       │   ├── inter-regular.woff2
│       │   ├── inter-medium.woff2
│       │   └── inter-bold.woff2
│       └── i18n/
│           ├── ar.json                              # NEW
│           └── en.json                              # NEW
│   └── e2e/shell/
│       └── lang-toggle.spec.ts                      # NEW
libs/
├── design-system/src/
│   ├── theme/
│   │   ├── tokens.scss                              # NEW (CSS custom properties)
│   │   ├── faktuboh-quiet.preset.ts                 # NEW (PrimeNG preset)
│   │   └── primeng-quiet.preset.ts                  # NEW (PrimeNG shim — physical-props allowlisted here only)
│   ├── styles/
│   │   ├── fonts.scss                               # NEW (@font-face, font-display: swap)
│   │   └── global.scss                              # NEW (base font-family, Tailwind preflight)
│   └── index.ts                                     # NEW (public barrel)
└── core/src/
    ├── i18n/
    │   ├── lang-bootstrap.service.ts                # NEW
    │   └── lang-bootstrap.service.spec.ts           # NEW
    └── index.ts                                     # NEW
tools/
└── eslint-rules/
    └── no-brand-orange-in-ui.js                     # NEW (custom rule)
scripts/
└── assert-zoneless.sh                               # NEW (CI bundle scanner)
.eslintrc.js                                         # MODIFY (add rule + Transloco unused-keys rule)
.stylelintrc.js                                      # MODIFY (add logical-property disallowed list + brand orange block)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | 70% of tests | Vitest + Angular Testing Library — `LangBootstrapService`, custom ESLint rule, token resolution |
| E2E | 1 flow (lang toggle + a11y) | Playwright Chromium + WebKit + axe-core |
| Lint | Gate | stylelint + ESLint — physical props, brand orange, Transloco unused keys |
| Bundle scan | Gate | `assert-zoneless.sh` — no `zone.js` in dist |

**Forbidden:**
- `zone.js` import anywhere
- `@Input()` / `@Output()` legacy decorators — use `input()`, `output()`, `model()`
- `NgModule` usage
- Physical CSS properties in feature code
- Google Fonts CDN
- Dark mode tokens / toggle (deferred to v2)

### Project Structure Notes

This story plants the root of `libs/design-system` and `libs/core/i18n` for the whole frontend. Every downstream Epic 1 story (1.9 components, 1.2/1.3/1.4 auth flows, 1.5 profile, 1.6 disclaimer, 1.7 deletion, 1.8 empty Glance) imports from these libraries unchanged.

**Detected variances to reconcile before first commit:**
1. Transloco ESLint plugin for unused-keys may not yet exist as a package — if not available, the rule is deferred with a TODO and Story 1.10 activates it when we add the a11y CI gates.
2. The PrimeNG v21 `definePreset` API signature may differ from v20 — consult `@primeng/mcp` for exact syntax during implementation, do not pattern-match from training data.

### Architecture compliance

- **ADR-001** (two build targets) — `apps/public-statement` consumes `libs/design-system` too; its `index.html` gets the same pre-hydration script but no Transloco toggle (public flow is token-scoped, read-only; lang comes from URL capability token claim, not user interaction)
- **ADR-024** (Minimal APIs) — no backend surface touched in this story
- **§3.4 Frontend Scaffold** — this story creates the skeleton of `libs/design-system` + `libs/core`
- **§3.5 Cross-Cutting** — Transloco runtime is the i18n primitive referenced there
- **UX Design Foundation §Color System, §Typography, §Spacing** — token values match exactly
- **UX Design Foundation §Accessibility** — focus rings, touch targets, zoom survival baseline

### Library / framework requirements

| Layer | Package | Version |
|---|---|---|
| Framework | Angular | 21 (zoneless default, Signal Forms, `input()`/`output()`/`model()`) |
| UI kit | PrimeNG | 21 (Aura preset, `definePreset`) |
| Styling | Tailwind CSS | 4.x (layout only) |
| i18n | Transloco | latest stable |
| Lint | stylelint | latest stable |
| Lint | ESLint | latest stable |
| Test | Vitest | latest stable |
| E2E | Playwright | latest stable (Chromium + WebKit) |

### Testing requirements

- **Unit**: `LangBootstrapService` — localStorage branch, navigator branch, default branch, `setLang` side effects
- **E2E**: Playwright scenario covers first-paint direction, toggle latency, axe-core zero violations
- **Lint**: stylelint + ESLint custom-rule fixtures exercised in Vitest (rule testability)
- **Bundle scan**: shell script runs in CI post-build; verifies no `zone.js` in any `dist/**` chunk

### Previous Story Intelligence

**None applicable** — this is the first Epic 1 story per the reorder. Epic 0 scaffold stories (0.1–0.17) must have landed: Angular workspace scaffolded (0.2), CI workflows active (0.9). If those are missing, pause and run Epic 0 first.

### Latest Technical Information

**Angular 21 zoneless:**
- `provideZonelessChangeDetection()` replaces `provideExperimentalZonelessChangeDetection()` — stable in v21
- All change detection flows through signals; manual `ChangeDetectorRef.detectChanges()` rarely needed
- `afterNextRender` / `afterRender` hooks replace `ngAfterViewInit` for DOM-dependent init

**PrimeNG 21 Aura preset extension:**
- `definePreset(Aura, { semantic: {...} })` returns a new preset object
- Pass it to `providePrimeNG({ theme: { preset: MyPreset } })` in `appConfig.providers`
- Use `@primeng/mcp` MCP server during implementation for exact API signatures (repo is configured for this)

**Transloco on zoneless Angular 21:**
- `provideTransloco({ config: { availableLangs: ['ar', 'en'], defaultLang: 'ar', fallbackLang: 'en', reRenderOnLangChange: true, prodMode: environment.production }, loader: TranslocoHttpLoader })`
- Use `translate()` signal-based helper instead of `TranslocoPipe` where possible for OnPush clarity

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Story 1.1
- Design tokens — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) §Visual Design Foundation → Color System, Typography, Spacing
- Frontend scaffold — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.4
- Cross-cutting scaffold — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.5
- Frontend conventions — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.4.1, §5.4.2
- FR4 language preference — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md) FR4
- NFR-A1/NFR-P1/NFR-P9 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md) Performance + Accessibility
- ADR-001 two build targets — [_bmad-output/planning-artifacts/adrs/adr-001-two-build-targets.md](../planning-artifacts/adrs/adr-001-two-build-targets.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Establishes bilingual + design-token + zoneless foundation for all Epic 1 stories
- Dark mode deferred to v2 per delegated decision — light-mode tokens only at MVP
- Font preload + self-host sets up 120 KB woff2 budget enforced by Story 1.11

### File List

_Populated during implementation — see Source tree section above._
