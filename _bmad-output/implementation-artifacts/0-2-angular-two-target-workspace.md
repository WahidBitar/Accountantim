# Story 0.2: Angular two-target workspace with compile-boundary separation

Status: ready-for-dev

## Story

As a **developer**,
I want an **Angular 21 workspace with two build targets (`apps/owner-workspace` + `apps/public-statement`) sharing `libs/design-system` + `libs/core` + `libs/domain-ui`**,
so that **the public-statement bundle cannot import from the owner workspace as a property the compiler enforces (ADR-001)**.

**Covers requirements:** Epic 0 Platform Foundation — hard gate. Establishes the frontend surface that every Epic 1/2/3/4 UI story ships into. The public-statement compile boundary is ADR-001's load-bearing guarantee.

**Why this story now:** Story 0.1 scaffolded backend. This story is the symmetric FE scaffold. Story 0.3 (Auth0) needs callback URLs against `app.faktuboh.com` + `statement.faktuboh.com`, which are the two FE origins this story wires.

## Acceptance Criteria

### AC1 — Workspace scaffolded with two apps + four libs

**Given** the `frontend/` directory is empty
**When** the workspace scaffold runs
**Then** `frontend/angular.json` defines six projects: `owner-workspace` + `public-statement` (applications) + `design-system` + `core` + `domain-ui` + `api-contracts` (libraries)
**And** `ng build owner-workspace` and `ng build public-statement` both succeed on a fresh clone after `npm ci`
**And** `ng lint` succeeds on all six projects
**And** `npm test` (Vitest) discovers at least one passing smoke test per project [Source: architecture.md §3.4 + §6.2.3]

### AC2 — `sourceRoot` overrides relocate apps/libs per §6.2.3

**Given** `angular.json` defines all six projects
**When** the file is inspected
**Then** `projects.owner-workspace.sourceRoot` = `"apps/owner-workspace/src"`, `root` = `"apps/owner-workspace"`
**And** `projects.public-statement.sourceRoot` = `"apps/public-statement/src"`, `root` = `"apps/public-statement"`
**And** `projects.design-system.sourceRoot` = `"libs/design-system/src"`, `root` = `"libs/design-system"` (and similarly for `core`, `domain-ui`, `api-contracts`)
**And** the exact `angular.json` `projects` block below is present [Source: architecture.md §6.2.3]:

```json
{
  "projects": {
    "owner-workspace":  { "projectType": "application", "root": "apps/owner-workspace",  "sourceRoot": "apps/owner-workspace/src" },
    "public-statement": { "projectType": "application", "root": "apps/public-statement", "sourceRoot": "apps/public-statement/src" },
    "design-system":    { "projectType": "library",     "root": "libs/design-system",    "sourceRoot": "libs/design-system/src" },
    "core":             { "projectType": "library",     "root": "libs/core",             "sourceRoot": "libs/core/src" },
    "domain-ui":        { "projectType": "library",     "root": "libs/domain-ui",        "sourceRoot": "libs/domain-ui/src" },
    "api-contracts":    { "projectType": "library",     "root": "libs/api-contracts",    "sourceRoot": "libs/api-contracts/src" }
  }
}
```

### AC3 — Public-statement bundle-graph gate

**Given** the `public-statement` app is built
**When** a CI script parses `dist/public-statement/stats.json` (Webpack/Rsbuild stats output)
**Then** no module path starting with `apps/owner-workspace/` appears in the bundle graph
**And** no module path starting with `libs/core/src/state/current-user.service.ts` or `libs/core/src/http/capability-token.interceptor.ts` appears (these are owner-only; scoped allowlist per §3.4)
**And** a failing attempt (dev deliberately imports from `apps/owner-workspace/` into `apps/public-statement/`) produces a clear ESLint error before build time [Source: architecture.md §3.4 public-statement bundle-graph gate]
**And** the script lives at `frontend/scripts/verify-public-statement-bundle.ts` and is called by CI (wired into Story 0.9's `ci.yml`)

### AC4 — ESLint boundary rule enforces compile-boundary

**Given** `frontend/eslint.config.js` defines boundary rules via `@typescript-eslint/no-restricted-imports` + `@nx/enforce-module-boundaries` equivalent for vanilla CLI
**When** a file in `apps/public-statement/**` imports from `apps/owner-workspace/**` or from `libs/core/src/state/**` (auth/owner-only subtree)
**Then** ESLint fails with the message `Public-statement bundle cannot import from owner-workspace (ADR-001 compile-boundary violation)`
**And** a file in `libs/design-system/**` that imports from `libs/core/**` or `libs/domain-ui/**` fails the same way (design-system has zero deps on app-specific libs per §6.4.4)
**And** the ESLint boundary config below is present in `frontend/eslint.config.js`:

```javascript
// frontend/eslint.config.js (flat v9)
import tseslint from 'typescript-eslint';
import angular from '@angular-eslint/eslint-plugin';

export default tseslint.config(
  {
    files: ['apps/public-statement/**/*.ts'],
    rules: {
      '@typescript-eslint/no-restricted-imports': ['error', {
        patterns: [
          { group: ['**/apps/owner-workspace/**'], message: 'ADR-001: public-statement cannot import from owner-workspace.' },
          { group: ['@core/state/*', '@core/http/capability-token.interceptor*'], message: 'ADR-001: owner-only subtree forbidden in public-statement.' }
        ]
      }]
    }
  },
  {
    files: ['libs/design-system/**/*.ts'],
    rules: {
      '@typescript-eslint/no-restricted-imports': ['error', {
        patterns: [
          { group: ['@core/*', '@du/*'], message: 'design-system cannot depend on @core or @du.' }
        ]
      }]
    }
  },
  {
    files: ['apps/**/*.ts', 'libs/**/*.ts'],
    rules: {
      // no-handwritten-shared-contracts — activated once libs/api-contracts/src/generated/ has content
      'no-restricted-syntax': ['warn', {
        selector: "TSInterfaceDeclaration[id.name=/.*(Request|Response|Dto)$/] > :not([path=/generated/])",
        message: 'Handwritten shared contracts forbidden (ADR-005). Generate via NSwag.'
      }]
    }
  }
);
```

### AC5 — Zoneless invariant

**Given** both apps' `main.ts` bootstrap
**When** the bootstrap code is inspected
**Then** each calls `bootstrapApplication(AppComponent, { providers: [provideExperimentalZonelessChangeDetection(), ...] })` (Angular 21 zoneless token; renamed from `provideZonelessChangeDetection` depending on release)
**And** neither app's `angular.json` `polyfills` array contains `"zone.js"`
**And** a CI script at `frontend/scripts/verify-zoneless.ts` scans `dist/*/polyfills*.js` and fails if `Zone.__` or `zone.js` token appears in either bundle [Source: architecture.md §3.4 zoneless invariant]

### AC6 — PrimeNG 21 + "Faktuboh Quiet" preset + Tailwind 4 + Transloco + fonts self-hosted

**Given** both apps import from `libs/design-system`
**When** the design-system library is inspected
**Then** `libs/design-system/src/lib/tokens/primeng-theme.ts` defines a `FaktubohQuietPreset` extending PrimeNG's `Aura` preset (placeholder colors OK — tokens fleshed out in Epic 1 Story 1.1)
**And** `libs/design-system/src/styles/tailwind-base.css` imports Tailwind 4 via `@import "tailwindcss"` + `@plugin "tailwindcss-primeui"`
**And** both apps' `app.config.ts` register `providePrimeNG({ theme: { preset: FaktubohQuietPreset } })`
**And** `libs/core/src/lib/i18n/transloco.config.ts` registers Transloco with `availableLangs: ['ar', 'en']`, `defaultLang: 'en'`, `fallbackLang: 'en'`
**And** `apps/*/src/assets/fonts/` contains self-hosted `Tajawal-Regular.woff2` + `Tajawal-Bold.woff2` + `Inter-Regular.woff2` + `Inter-Bold.woff2` referenced via `@font-face` with `font-display: swap` (no Google Fonts CDN) [Source: architecture.md §3.4]

### AC7 — Path aliases in `tsconfig.json`

**Given** `frontend/tsconfig.json` is the workspace root config
**When** inspected
**Then** `compilerOptions.paths` contains:
- `"@ds/*": ["libs/design-system/src/*"]`
- `"@core/*": ["libs/core/src/*"]`
- `"@du/*": ["libs/domain-ui/src/*"]`
- `"@contracts/*": ["libs/api-contracts/src/*"]`
**And** both apps + the three libs resolve these aliases in build + lint + test [Source: architecture.md §6.2.2]

### AC8 — Initial-JS-size gate (180KB gz for public-statement)

**Given** `apps/public-statement/project.json` or `angular.json` build budgets
**When** the build-budget config is inspected
**Then** the `initial` budget for `public-statement` production build is `{ "maximumWarning": "160kb", "maximumError": "180kb" }` (gzipped-equivalent; CI converts raw to gzip and asserts)
**And** the current public-statement smoke build (empty route tree + single `<ps-statement-card>` placeholder) ships well under budget (expected <50KB gz)
**And** Story 4.1 (public-statement first render) has a hard baseline to regress against [Source: architecture.md §3.4 + D4.19]

### AC9 — Smoke routes wire up

**Given** both apps have `app.routes.ts`
**When** inspected
**Then** `apps/owner-workspace/src/app/app.routes.ts` has a single placeholder route `{ path: '', component: HomePlaceholderComponent }`
**And** `apps/public-statement/src/app/app.routes.ts` has `{ path: ':token', loadComponent: () => import('./statement/statement.page').then(m => m.StatementPage) }` + `{ path: '', redirectTo: 'expired', pathMatch: 'full' }`
**And** `ng serve owner-workspace --port 4200` renders a page at `http://localhost:4200/`
**And** `ng serve public-statement --port 4201` renders a page at `http://localhost:4201/expired`

### AC10 — Vitest + Playwright configs

**Given** `frontend/vitest.config.ts` + `frontend/playwright.config.ts` exist
**When** `npm test` runs
**Then** Vitest runs unit/component tests across all six projects via workspace setup
**And** `frontend/playwright.config.ts` declares two projects: `chromium` + `webkit` (per architecture §5.4.2)
**And** `npx playwright install chromium webkit` is idempotent (CI caches the browser binaries)
**And** a placeholder E2E at `frontend/e2e/owner-workspace/smoke.spec.ts` hits `http://localhost:4200/` and asserts the page loads

## Tasks / Subtasks

### T1 — Run Angular scaffold commands (AC: 1, 2, 7)

- [ ] `cd frontend && npm create @angular-cli@21 -- --name faktuboh-workspace --create-application=false --style=scss --ssr=false`
- [ ] `ng generate application owner-workspace --routing --style=scss --inline-style=false --inline-template=false --standalone`
- [ ] `ng generate application public-statement --routing --style=scss --inline-style=false --inline-template=false --standalone`
- [ ] `ng generate library design-system`, `ng generate library core`, `ng generate library domain-ui`, `ng generate library api-contracts`
- [ ] Edit `frontend/angular.json` to apply the exact `projects.*.sourceRoot` overrides from AC2
- [ ] Move source folders on disk to match (default Angular CLI places under `projects/`)
- [ ] Edit `frontend/tsconfig.json` to register the four path aliases

### T2 — Directory.Build analogue: root configs (AC: 1)

- [ ] Write `frontend/package.json` with workspace devDeps: `@angular/cli@21`, `@angular/core@21`, `@primeng/primeng@21`, `tailwindcss@4`, `@jsverse/transloco`, `vitest`, `@vitest/browser`, `@playwright/test`, `typescript-eslint`, `@angular-eslint/eslint-plugin`
- [ ] Write `frontend/tsconfig.json` with `strict: true`, `noUncheckedIndexedAccess: true`, path aliases per AC7
- [ ] Write `frontend/eslint.config.js` with the boundary rules from AC4
- [ ] Write `frontend/tailwind.config.ts` scanning `apps/**/*.{html,ts}` + `libs/**/*.{html,ts}`
- [ ] Write `frontend/vitest.config.ts` + `frontend/playwright.config.ts`

### T3 — Zoneless bootstrap (AC: 5)

- [ ] Edit `apps/owner-workspace/src/main.ts` to use `bootstrapApplication` + `provideExperimentalZonelessChangeDetection()`
- [ ] Same for `apps/public-statement/src/main.ts`
- [ ] Remove `"zone.js"` from each app's `angular.json` `polyfills` array (confirm empty array or `[]`)
- [ ] Create `frontend/scripts/verify-zoneless.ts` asserting no zone.js in built bundles

### T4 — PrimeNG 21 + Tailwind 4 + Transloco wiring (AC: 6)

- [ ] Create `libs/design-system/src/lib/tokens/primeng-theme.ts` exporting `FaktubohQuietPreset` (initially Aura with placeholder accent color; real tokens land in Epic 1 Story 1.1)
- [ ] Create `libs/design-system/src/styles/tailwind-base.css` with Tailwind 4 directives
- [ ] Edit `apps/owner-workspace/src/app/app.config.ts` to register `providePrimeNG({ theme: { preset: FaktubohQuietPreset, options: { darkModeSelector: '.dark' } } })` + `provideTransloco({ config: { availableLangs: ['ar', 'en'], defaultLang: 'en' }, loader: TranslocoHttpLoader })`
- [ ] Repeat for `apps/public-statement/src/app/app.config.ts` (same theme, but minimal provider set — no auth)
- [ ] Download Tajawal-Regular.woff2, Tajawal-Bold.woff2, Inter-Regular.woff2, Inter-Bold.woff2 into `apps/*/src/assets/fonts/`
- [ ] Add `@font-face` declarations in `apps/*/src/styles.scss`

### T5 — Build-budget config (AC: 8)

- [ ] Edit `frontend/angular.json` `projects.public-statement.architect.build.configurations.production.budgets`:
  ```json
  [
    { "type": "initial", "maximumWarning": "160kb", "maximumError": "180kb" },
    { "type": "anyComponentStyle", "maximumWarning": "4kb", "maximumError": "8kb" }
  ]
  ```
- [ ] Owner-workspace keeps default Angular CLI budget (project is auth'd, no edge-cache latency pressure)

### T6 — Smoke routes + placeholder components (AC: 9)

- [ ] Create `apps/owner-workspace/src/app/app.routes.ts` with single `''` route → `HomePlaceholderComponent`
- [ ] Create `apps/owner-workspace/src/app/home-placeholder.component.ts` — standalone, `selector: 'ow-home-placeholder'`, template `<h1>{{ title() }}</h1>`
- [ ] Create `apps/public-statement/src/app/app.routes.ts` with `:token` + `expired` routes
- [ ] Create `apps/public-statement/src/app/statement/statement.page.ts` (selector `ps-statement-page`) + `apps/public-statement/src/app/expired/expired.page.ts`
- [ ] Smoke-run `ng serve owner-workspace --port 4200` and `ng serve public-statement --port 4201` to confirm render

### T7 — Boundary-gate tooling (AC: 3, 4)

- [ ] Write `frontend/scripts/verify-public-statement-bundle.ts` — parses `dist/public-statement/stats.json`, fails if any module ID starts with `apps/owner-workspace/` or matches the owner-only allowlist
- [ ] Add the four ESLint boundary rules from AC4 to `frontend/eslint.config.js`
- [ ] Deliberately add a forbidden import in a scratch branch to prove the rule fires; then revert

### T8 — Vitest + Playwright smoke tests (AC: 10)

- [ ] Create `frontend/vitest.config.ts` with workspace-level config + `environment: 'jsdom'` + `setupFiles: ['libs/core/src/test-utils/setup.ts']`
- [ ] Per-project `vitest.config.ts` that extends workspace
- [ ] Create `frontend/playwright.config.ts` with two projects (`chromium` + `webkit`), baseURL per app
- [ ] Create `frontend/e2e/owner-workspace/smoke.spec.ts` — navigate to `/` + assert title visible
- [ ] Create `frontend/e2e/public-statement/smoke.spec.ts` — navigate to `/expired` + assert page loads

### T9 — Route-ESLint for selector prefixes (AC: 4)

- [ ] Configure `@angular-eslint/component-selector` per project:
  - `apps/owner-workspace` → `prefix: 'ow'`
  - `apps/public-statement` → `prefix: 'ps'`
  - `libs/design-system` → `prefix: 'ds'`
  - `libs/domain-ui` → `prefix: 'du'`
  - `libs/core` — no components (services only)

## Dev Notes

### Critical guardrails (do not violate)

1. **Public-statement bundle-graph gate is a REQUIRED CI check.** If the CI script isn't wired by end of Story 0.9, this story is incomplete regardless of other ACs passing. [Source: architecture.md §3.4]
2. **180KB gz hard ceiling.** A PR that breaches the budget fails CI — no warning-only. Owner-workspace has no comparable ceiling; public-statement is the only budget-gated surface. [Source: architecture.md §3.4 + D4.19]
3. **No zone.js polyfill anywhere.** Angular 21 is zoneless. Any zone.js import anywhere in the tree fails the `verify-zoneless.ts` CI check. [Source: architecture.md §3.4 + §5.4.1]
4. **Selector prefix discipline.** `ow-` / `ps-` / `ds-` / `du-` are enforced by `@angular-eslint/component-selector`. Renaming a prefix mid-project requires a codemod + ADR. [Source: architecture.md §5.4.1]
5. **No handwritten shared contracts under `libs/api-contracts/`.** The ESLint rule `no-handwritten-shared-contracts` is initially warning-only (no generated content yet) but flips to error on first NSwag generation (Story 0.1 + Epic 2 Story 2.1's contract emission). [Source: architecture.md §3.5 + ADR-005]

### Source tree — files to create

```
frontend/
├── angular.json                              # NEW — with sourceRoot overrides
├── package.json                              # NEW — workspace + devDeps
├── package-lock.json                         # NEW (generated)
├── tsconfig.json                             # NEW — path aliases
├── tsconfig.app.json                         # NEW
├── tsconfig.spec.json                        # NEW
├── tsconfig.lib.json                         # NEW
├── eslint.config.js                          # NEW — boundary rules
├── vitest.config.ts                          # NEW — workspace-wide
├── playwright.config.ts                      # NEW — Chromium + WebKit
├── tailwind.config.ts                        # NEW — scans apps/**+libs/**
├── apps/
│   ├── owner-workspace/
│   │   ├── src/
│   │   │   ├── main.ts                       # NEW — zoneless bootstrap
│   │   │   ├── index.html                    # NEW
│   │   │   ├── styles.scss                   # NEW — Tailwind + font-face
│   │   │   ├── app/
│   │   │   │   ├── app.config.ts             # NEW — PrimeNG + Transloco
│   │   │   │   ├── app.routes.ts             # NEW — placeholder route
│   │   │   │   ├── app.component.ts          # NEW — shell
│   │   │   │   ├── app.component.html        # NEW
│   │   │   │   └── home-placeholder.component.ts # NEW
│   │   │   └── assets/fonts/                 # NEW — self-hosted woff2
│   │   ├── public/                           # NEW
│   │   └── vitest.config.ts                  # NEW
│   └── public-statement/
│       ├── src/
│       │   ├── main.ts                       # NEW
│       │   ├── app/
│       │   │   ├── app.config.ts             # NEW — minimal (no auth providers)
│       │   │   ├── app.routes.ts             # NEW — :token + expired
│       │   │   ├── statement/statement.page.ts # NEW
│       │   │   └── expired/expired.page.ts   # NEW
│       │   └── assets/fonts/                 # NEW
│       └── vitest.config.ts                  # NEW
├── libs/
│   ├── design-system/
│   │   ├── src/
│   │   │   ├── public-api.ts                 # NEW — barrel
│   │   │   ├── lib/tokens/primeng-theme.ts   # NEW — FaktubohQuietPreset
│   │   │   └── styles/tailwind-base.css      # NEW
│   │   └── ng-package.json                   # NEW
│   ├── core/
│   │   ├── src/
│   │   │   ├── public-api.ts                 # NEW
│   │   │   ├── lib/i18n/transloco.config.ts  # NEW
│   │   │   └── test-utils/setup.ts           # NEW — Vitest setup
│   │   └── ng-package.json                   # NEW
│   ├── domain-ui/
│   │   └── ng-package.json                   # NEW
│   └── api-contracts/
│       ├── src/
│       │   ├── public-api.ts                 # NEW — re-exports generated/
│       │   └── generated/                    # EMPTY (populated by NSwag via Story 0.1 tooling)
│       └── ng-package.json                   # NEW
├── e2e/
│   ├── owner-workspace/smoke.spec.ts         # NEW
│   └── public-statement/smoke.spec.ts        # NEW
└── scripts/
    ├── verify-public-statement-bundle.ts     # NEW — parses stats.json
    └── verify-zoneless.ts                    # NEW — scans bundle polyfills
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Unit | Vitest + Angular Testing Library | Smoke tests per project |
| Architectural | ESLint boundary rules + bundle-graph script | AC3 + AC4 |
| E2E | Playwright (Chromium + WebKit) | Smoke render per app |
| Bundle-budget | Angular CLI build budgets | AC8 180KB gz |
| Zoneless | Custom script (`verify-zoneless.ts`) | AC5 |

**Forbidden:**
- zone.js anywhere in the dep tree
- Imports from `apps/owner-workspace/` into `apps/public-statement/` (ADR-001 hard boundary)
- `libs/design-system` importing from `libs/core/` or `libs/domain-ui/` (DS must be stateless, i18n-free)
- Karma or Jasmine (replaced by Vitest per §5.4.2)

### Project Structure Notes

- **Angular CLI default places projects under `projects/`.** We override via `sourceRoot` to match §6.2.3. This is a known-working pattern; Angular CLI v17+ supports it.
- **`libs/api-contracts/src/generated/` is empty at end of this story.** NSwag invocation lands after Story 2.1 (first real endpoint) or via CI job in Story 0.9. Before that, the `no-handwritten-shared-contracts` rule is warning-only.
- **`libs/design-system` owns PrimeNG theme.** Both apps consume via `providePrimeNG({ theme: { preset: FaktubohQuietPreset } })`. Theme content is a scaffold placeholder; Epic 1 Story 1.1 populates the real design tokens.
- **`libs/core` owns zero components.** Services only (per §6.4.5). Selector prefix rule N/A.

### Architecture compliance

- [architecture.md §3.4](../planning-artifacts/architecture.md) — frontend scaffold (two-target workspace + compile-boundary separation)
- [architecture.md §3.5](../planning-artifacts/architecture.md) — contract-drift gate + `no-handwritten-shared-contracts` ESLint rule
- [architecture.md §5.4.1](../planning-artifacts/architecture.md) — naming + selector prefix per project
- [architecture.md §5.4.2](../planning-artifacts/architecture.md) — Vitest + Playwright conventions
- [architecture.md §6.2.2 + §6.2.3](../planning-artifacts/architecture.md) — root configs + Angular workspace path customization
- [architecture.md §6.4.1 - §6.4.7](../planning-artifacts/architecture.md) — workspace tree detail
- [architecture.md D4.19](../planning-artifacts/architecture.md) — public-statement bundle budget (180KB gz)
- [architecture.md ADR-001](../planning-artifacts/architecture.md) — two build targets + compile-boundary
- [architecture.md ADR-005](../planning-artifacts/architecture.md) — contract seams reserved (NSwag lands via Story 0.1)

### Library/framework requirements

| Layer | Package | Version | Purpose |
|---|---|---|---|
| Runtime | Node.js | 22 LTS | Matches Angular 21 engines |
| Framework | Angular | 21 | Zoneless default; Signal Forms |
| UI kit | PrimeNG | 21 | Aura preset + `tailwindcss-primeui` |
| Styling | Tailwind CSS | 4.x | Layout; PrimeNG handles visuals |
| i18n | @jsverse/transloco | latest | AR + EN runtime bilingual |
| Test runner | Vitest | latest | All projects |
| Component tests | @testing-library/angular | latest | User-visible queries |
| E2E | Playwright | latest | Chromium + WebKit |
| Lint | typescript-eslint + @angular-eslint | latest | Flat v9 config |
| Fonts | Tajawal + Inter | (self-hosted .woff2) | Arabic + Latin |

### Testing requirements

- **ESLint boundary violation** must fail CI, not just local lint (wired via Story 0.9 `ci.yml`).
- **Bundle-graph + zoneless scripts** run in CI after `ng build` on every PR.
- **Playwright smoke tests** run on PR (subset) + nightly (full matrix).
- **Chromium + WebKit** are both required — WebKit catches Safari-specific issues for MENA iOS users per §5.4.2.

### Previous Story Intelligence

- **Story 0.1 (backend solution scaffold)** is assumed complete. NSwag nswag.json + `backend/tools/NSwag/` exist but are not yet invoked (no real endpoints to generate from). Story 0.1 added the NSwag decision; this story inherits the `libs/api-contracts/` placeholder structure.
- **No other Epic 0 stories are prerequisites** for this story — frontend scaffold is parallel to backend beyond sharing the repo.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.2 definition
- [_bmad-output/planning-artifacts/architecture.md §3.4](../planning-artifacts/architecture.md) — FE scaffold
- [_bmad-output/planning-artifacts/architecture.md §5.4.1 - §5.4.4](../planning-artifacts/architecture.md) — FE conventions
- [_bmad-output/planning-artifacts/architecture.md §6.2.3 + §6.4](../planning-artifacts/architecture.md) — workspace tree + sourceRoot overrides
- [_bmad-output/planning-artifacts/architecture.md ADR-001](../planning-artifacts/architecture.md) — two build targets
- [_bmad-output/planning-artifacts/architecture.md ADR-005](../planning-artifacts/architecture.md) — contract seams reserved

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
