# Story 4.2: apps/public-statement as distinct Angular build target

Status: ready-for-dev

## Story

As a **developer**,
I want **`apps/public-statement` building as a separate Angular bundle with compile-enforced separation from owner-workspace**,
so that **the hostile public surface cannot accidentally import owner-workspace code** (ADR-001).

**Covers requirements:** Epic 4 — FR35, §3.4 D1 (two build targets), ADR-001 [Source: epics.md Epic 4 Story 4.2; architecture.md §3.4, ADR-001].

**Why this story now:** Story 0.2 scaffolded the two-target workspace, but until a real `public-statement` app exists with an entry point, the bundle-graph CI gate has nothing to measure. This story activates the boundary mechanism for all downstream Epic 4 stories. Without it, 4.3 cannot assert the 180KB gz budget, and 4.5 has no place to render `StatementView`.

## Acceptance Criteria

### AC1 — `apps/public-statement` is a first-class Angular project

**Given** Story 0.2 scaffolded `apps/public-statement/` with placeholder files
**When** this story lands the app
**Then** `angular.json` lists `public-statement` as a project with `projectType: "application"`, its own `root: "apps/public-statement"`, and build/serve/test configurations
**And** `apps/public-statement/project.json` (or the equivalent angular.json block) defines `architect.build`, `architect.serve`, `architect.test`, `architect.lint`
**And** `ng build public-statement` produces `dist/apps/public-statement/` with its own `index.html`, `main.js`, `polyfills.js`, `styles.css`
**And** `ng serve public-statement` runs on port 4201 (owner-workspace on 4200)
**And** the app bootstraps via `bootstrapApplication(AppComponent, { providers: [provideZonelessChangeDetection(), ...] })` per Angular 21 zoneless invariant [Source: architecture.md §2.2, §3.4, §5.4].

### AC2 — Zero modules from `apps/owner-workspace` appear in the bundle

**Given** `ng build public-statement --stats-json` writes `stats.json`
**When** the bundle-graph CI gate parses it
**Then** no module path matches `/apps/owner-workspace/` in `stats.modules[]`
**And** no module path matches forbidden `libs/core/*` subtrees: `libs/core/auth/*`, `libs/core/owner-*`, `libs/core/dashboard/*`
**And** the gate fails the CI with a message listing the offending module paths + their importers [Source: architecture.md §3.4 compile-boundary separation].

### AC3 — ESLint boundary rule blocks forbidden imports at build time

**Given** `.eslintrc.cjs` in `apps/public-statement/` extends from the workspace boundary preset
**When** a source file imports from `apps/owner-workspace/*`, `libs/core/auth/*`, `libs/core/owner-*`, or `libs/core/dashboard/*`
**Then** the ESLint rule `@angular-eslint/no-cross-target-import` (custom, repo-local) flags the import as an error with message format: `"public-statement cannot import from {target} — compile-boundary violation per ADR-001"`
**And** `ng lint public-statement` fails when any such violation exists
**And** this lint rule runs as a required CI check on every PR touching `apps/public-statement/*` or `libs/*` [Source: architecture.md §3.4].

### AC4 — Allowed imports are actually usable

**Given** the allowed import surface is `libs/design-system`, `libs/domain-ui`, and a tightly-scoped subset of `libs/core`
**When** `apps/public-statement` imports from the allowed surface
**Then** `libs/design-system` (Aura theme, design tokens, Tailwind plugin config) imports succeed
**And** `libs/domain-ui` (`AmountDisplay`, `ContactAvatar`, `RtlDirective`) imports succeed
**And** the allowed `libs/core` subset: `libs/core/http` (HTTP client + error interceptor), `libs/core/i18n` (Transloco config), `libs/core/cache` (cache-key registry) imports succeed
**And** `libs/core/auth/*` imports fail per AC3 [Source: delegated decisions — allowed import surface per Epic 4 scope].

### AC5 — TypeScript path constraints enforce the boundary at compile time

**Given** root `tsconfig.base.json` defines path aliases with boundary metadata
**When** a source file under `apps/public-statement/` attempts `import { X } from '@faktuboh/core/auth'`
**Then** the TypeScript compiler fails with `error TS2307: Cannot find module '@faktuboh/core/auth'` because the alias is excluded from `apps/public-statement/tsconfig.app.json`'s resolution set
**And** `apps/public-statement/tsconfig.app.json` has `paths` narrowed to: `@faktuboh/design-system`, `@faktuboh/domain-ui`, `@faktuboh/core/http`, `@faktuboh/core/i18n`, `@faktuboh/core/cache`
**And** forbidden paths are absent from this config — compile fails before lint runs [Source: architecture.md §3.4, §5.4].

### AC6 — Bundle-graph CI gate runs on every PR touching the relevant files

**Given** `.github/workflows/ci.yml` is extended
**When** a PR touches any file under `apps/public-statement/*` or `libs/*`
**Then** a required job `bundle-graph-public-statement` runs:
1. `pnpm install`
2. `pnpm ng build public-statement --stats-json --configuration production`
3. `node scripts/ci/check-bundle-boundary.mjs dist/apps/public-statement/stats.json` — exits non-zero on any forbidden module, with list
**And** the job is marked `required` on branch protection for `main`
**And** a test PR that adds a forbidden import (`import { authService } from 'libs/core/auth'`) fails this job [Source: architecture.md §3.4, §3.5].

### AC7 — Zoneless invariant holds on public target

**Given** zoneless change detection is required on both apps
**When** this app's `main.ts` bootstraps
**Then** `provideZonelessChangeDetection()` is configured in providers
**And** `zone.js` is NOT imported in `polyfills.ts` (empty or absent)
**And** `angular.json` `projects.public-statement.architect.build.options.polyfills` does NOT reference `zone.js`
**And** an architecture test scans the built `polyfills.js` for the string `zone.js` — fails the build if found [Source: architecture.md §2.2; Story 1.11 performance-budget gates].

### AC8 — `index.html` and root component are minimal

**Given** the 180KB gz budget in Story 4.3 requires aggressive minimalism
**When** `apps/public-statement/src/index.html` renders
**Then** the shell contains `<app-root>` + `<title>` + `<meta name="viewport">` + RTL-ready `<html lang="" dir="">` (values set at runtime by Story 4.15)
**And** `<meta name="robots" content="noindex, nofollow">` is present per §2.9 public-surface hardening #8
**And** `AppComponent` is standalone, OnPush, and contains only `<router-outlet></router-outlet>`
**And** only one route is defined: `{ path: 't/:token', canActivate: [statementRouteGuard], loadComponent: () => import('./features/statement-view/statement-view.component').then(m => m.StatementViewComponent) }` — lazy-loaded per §5.4 [Source: architecture.md §2.9 #8, §5.4].

### AC9 — Package.json + dependency hygiene

**Given** `apps/public-statement` has no owner-only deps
**When** the workspace `package.json` is updated
**Then** no new dependency shared only with owner-workspace leaks into the public bundle (e.g., `ngx-toastr` used in owner only — must not be in `apps/public-statement/package.json` OR imported from a shared lib that carries it)
**And** `pnpm-lock.yaml` is single-source; filtered deps live per-app via `peerDependencies` + workspace protocol
**And** a check script `scripts/ci/check-deps-public-statement.mjs` lists the transitive module graph and flags any module larger than 10 KB gzipped (advisory, non-blocking) [Source: Story 4.3 sets hard budget].

### AC10 — No Auth0 surface reaches the public target

**Given** Auth0 is owner-only per ADR-021 + delegated Epic 4 decisions
**When** the bundle-graph gate runs
**Then** no module from `@auth0/auth0-angular`, `@auth0/auth0-spa-js`, or `libs/core/auth/*` appears in `stats.json`
**And** NetArchTest-equivalent (repo-local script `check-no-auth-in-public.mjs`) runs as part of the bundle-graph gate [Source: delegated decisions; ADR-021].

## Tasks / Subtasks

### Frontend scaffold

- [ ] **T1 (AC: 1, 8)** — Create `apps/public-statement/` directory with Angular 21 application skeleton
  - [ ] `src/main.ts` — `bootstrapApplication(AppComponent, appConfig)`
  - [ ] `src/app/app.component.ts` — standalone, OnPush, template `<router-outlet></router-outlet>`
  - [ ] `src/app/app.config.ts` — providers: `provideZonelessChangeDetection()`, `provideRouter(appRoutes)`, `provideHttpClient(withInterceptors([errorInterceptor]))`, `provideTransloco(publicTranslocoConfig)`
  - [ ] `src/app/app.routes.ts` — single lazy route to `statement-view`
  - [ ] `src/index.html` — minimal shell per AC8
  - [ ] `src/styles.scss` — imports design-system tokens only
  - [ ] `src/polyfills.ts` — empty file (zoneless)
- [ ] **T2 (AC: 1)** — Register project in `angular.json`
  - [ ] `projects.public-statement.root: "apps/public-statement"`
  - [ ] `projects.public-statement.sourceRoot: "apps/public-statement/src"`
  - [ ] `architect.build`: `browser-esbuild` builder with `configurations.production`
  - [ ] `architect.serve`: port 4201
  - [ ] `architect.test`: Vitest
  - [ ] `architect.lint`: ESLint with `apps/public-statement/.eslintrc.cjs`

### TypeScript path constraints

- [ ] **T3 (AC: 5)** — Create `apps/public-statement/tsconfig.app.json`
  - [ ] Extends `tsconfig.base.json`
  - [ ] Narrows `paths` to: `@faktuboh/design-system/*`, `@faktuboh/domain-ui/*`, `@faktuboh/core/http/*`, `@faktuboh/core/i18n/*`, `@faktuboh/core/cache/*`
  - [ ] Explicitly omits: `@faktuboh/core/auth/*`, `@faktuboh/core/owner-*`, owner-workspace paths
- [ ] **T4 (AC: 5)** — Update root `tsconfig.base.json` with path map fragments per allowed alias

### ESLint boundary rule

- [ ] **T5 (AC: 3)** — Create `tools/eslint/no-cross-target-import.js` — custom ESLint rule
  - [ ] Parses `import` statements; rejects any specifier matching forbidden path patterns
  - [ ] Error message: `"public-statement cannot import from {target} — compile-boundary violation per ADR-001"`
  - [ ] Rule exports a `meta.type: 'problem'`
- [ ] **T6 (AC: 3)** — Create `apps/public-statement/.eslintrc.cjs`
  - [ ] Extends workspace preset
  - [ ] Activates `@local/no-cross-target-import` with forbidden patterns list
  - [ ] Standard Angular ESLint rules
  - [ ] `no-console: 'error'` (token redaction)

### Bundle-graph CI gate

- [ ] **T7 (AC: 2, 6)** — Create `scripts/ci/check-bundle-boundary.mjs`
  - [ ] Reads `dist/apps/public-statement/stats.json`
  - [ ] Iterates `modules[]`; collects paths matching forbidden patterns
  - [ ] Outputs a tree of `forbidden-module → its-importer-chain`
  - [ ] Exits 1 with formatted message when any match
- [ ] **T8 (AC: 2, 6, 10)** — Create `scripts/ci/check-no-auth-in-public.mjs`
  - [ ] Reads `stats.json`
  - [ ] Greps for `@auth0/`, `libs/core/auth`, `AuthService`, `@auth0-angular/*`
  - [ ] Exits 1 on match
- [ ] **T9 (AC: 6)** — Extend `.github/workflows/ci.yml`
  - [ ] New job `bundle-graph-public-statement`
  - [ ] Triggers on path-filter: `apps/public-statement/**`, `libs/**`
  - [ ] Steps: install → `ng build public-statement --stats-json --configuration production` → run both check scripts
  - [ ] Marked `required` on `main` branch protection (manual GitHub config; document in PR description)

### Zoneless invariant verification

- [ ] **T10 (AC: 7)** — Create `scripts/ci/check-zoneless.mjs`
  - [ ] Scans `dist/apps/public-statement/polyfills*.js` for string `zone.js`
  - [ ] Exits 1 on match
  - [ ] Wired into `bundle-graph-public-statement` CI job

### Dependency hygiene

- [ ] **T11 (AC: 9)** — Create `scripts/ci/check-deps-public-statement.mjs`
  - [ ] Reads `stats.json`, aggregates module sizes by top-level package
  - [ ] Outputs sorted list; warns on any module > 10 KB gz (advisory)
  - [ ] Appended to the PR comment via GitHub Actions `actions/github-script`

### i18n + core subset wiring

- [ ] **T12 (AC: 4)** — Create `apps/public-statement/src/app/core/i18n/public-transloco.config.ts`
  - [ ] Loads AR + EN translations scoped to public-statement namespace
  - [ ] Synchronous loader (no HTTP fetch — bundles translations as JSON chunks per locale)
- [ ] **T13 (AC: 4)** — Create minimal HTTP interceptor in app-level `core/http/error.interceptor.ts`
  - [ ] Imports from `libs/core/http` (error RFC 9457 handler)
  - [ ] No auth token attachment — this is an unauthenticated surface

### Documentation

- [ ] **T14 (AC: 1)** — Create `apps/public-statement/README.md`
  - [ ] States: "This is the hostile public surface. Imports from owner-workspace, `libs/core/auth`, or owner-only libs will fail the build."
  - [ ] Links to ADR-001 + architecture §3.4
  - [ ] Lists the allowed import surface

### Tests

- [ ] **T15 (AC: 2, 3, 5)** — Integration test for the bundle-graph gate
  - [ ] `scripts/ci/tests/check-bundle-boundary.test.mjs` — fixture `stats.json` with one forbidden path; asserts exit code 1 + correct message
  - [ ] Fixture with only allowed paths — asserts exit code 0
- [ ] **T16 (AC: 3)** — ESLint rule test
  - [ ] `tools/eslint/no-cross-target-import.test.js` — good + bad import cases via `RuleTester`
- [ ] **T17 (AC: 7)** — Build assertion test
  - [ ] `apps/public-statement/src/app/app.spec.ts` — Vitest test that imports `NgZone` and asserts it's a mock `NoopNgZone` (proves zoneless is active)

## Dev Notes

### Critical guardrails

1. **The compiler, not the linter, is the boundary.** AC5's `tsconfig.app.json` narrowing is load-bearing. ESLint is belt-and-suspenders; CI is a third layer. Any one of them alone is a decay risk.
2. **No Auth0 in public bundle.** `@auth0/auth0-angular` is a large package. If it leaks in, both the 180KB budget breaks AND the trust boundary confuses (there's nothing to authenticate on the public surface). Two gates cover it: tsconfig path exclusion + the explicit `check-no-auth-in-public.mjs` script.
3. **`libs/core` is not a bulk allowlist.** Only three subtrees are allowed: `http`, `i18n`, `cache`. Any owner-only code moved into `libs/core` must go into a distinct subfolder (e.g., `libs/core/owner-dashboard/*`) that the tsconfig path map excludes.
4. **Zoneless, always.** `provideZonelessChangeDetection()` in providers; no `zone.js` in polyfills. A build that accidentally includes zone.js bloats the bundle ~40 KB and breaks the invariant.
5. **Single route.** This is not a general-purpose app. Adding a second route (e.g., a "My account" page) = domain creep = trust-boundary erosion. New routes require a PR approved against ADR-001.

### Source tree

```
apps/public-statement/
├── src/
│   ├── index.html                                # NEW — minimal shell
│   ├── main.ts                                    # NEW — bootstrapApplication
│   ├── polyfills.ts                               # NEW — empty (zoneless)
│   ├── styles.scss                                # NEW — imports design-system only
│   ├── app/
│   │   ├── app.component.ts                       # NEW — <router-outlet> only
│   │   ├── app.config.ts                          # NEW — provideZonelessChangeDetection, routes, http, i18n
│   │   ├── app.routes.ts                          # NEW — single route /t/:token
│   │   ├── app.spec.ts                            # NEW — zoneless assertion
│   │   └── core/
│   │       ├── http/error.interceptor.ts          # NEW — RFC 9457 handler (imports libs/core/http)
│   │       ├── i18n/public-transloco.config.ts    # NEW
│   │       └── guards/statement-route.guard.ts    # EXISTS (Story 4.1)
│   └── features/statement-view/                   # EMPTY (Story 4.5 lands content)
├── tsconfig.app.json                              # NEW — narrow paths
├── tsconfig.spec.json                             # NEW
├── .eslintrc.cjs                                  # NEW — custom boundary rule activated
├── project.json                                   # NEW (or angular.json block)
└── README.md                                      # NEW

tools/eslint/
├── no-cross-target-import.js                      # NEW — custom rule
└── no-cross-target-import.test.js                 # NEW — RuleTester

scripts/ci/
├── check-bundle-boundary.mjs                      # NEW
├── check-no-auth-in-public.mjs                    # NEW
├── check-deps-public-statement.mjs                # NEW
├── check-zoneless.mjs                             # NEW
└── tests/check-bundle-boundary.test.mjs           # NEW

.github/workflows/
└── ci.yml                                         # MODIFY — new job bundle-graph-public-statement

angular.json                                       # MODIFY — register project
tsconfig.base.json                                 # MODIFY — path map aliases
```

### Architecture compliance

- **ADR-001** (two build targets) — this story activates the mechanism. Before this, `apps/public-statement` was a placeholder.
- **§3.4** (frontend scaffold) — compile-boundary separation + cache-key registry + allowed imports all land here.
- **§3.5** (cross-cutting scaffold) — bundle-graph CI gate is the mechanical gate.
- **§2.9 #8** (noindex, nofollow meta) — `index.html` carries the header + Story 4.5 adds the server `X-Robots-Tag` response header.
- **§5.4** (frontend conventions) — selector prefix `ps-` for public-statement app; zoneless; Signal Forms (when forms arrive in 4.5/4.6).

### Library/framework requirements

| Layer | Package | Version | Notes |
|---|---|---|---|
| Frontend | Angular | 21 | Zoneless default |
| UI kit | PrimeNG | 21 | `libs/design-system` re-exports Aura theme |
| Styling | Tailwind CSS | 4.x | Via `libs/design-system` |
| i18n | Transloco | latest | Bilingual AR/EN; synchronous bundled loader for public app |
| Test runner | Vitest | latest | |
| Build | esbuild via `@angular/build` | Angular 21 default | |

**Forbidden in this bundle:**
- `@auth0/auth0-angular`, `@auth0/auth0-spa-js`
- `zone.js`
- `ngx-toastr`, `ngx-lottie`, any heavyweight owner-only UX library
- `rxjs-compat`
- Polyfill libraries for ES5 targets (public app targets ES2022 modern browsers only)

### Testing requirements

- **Unit:** `AppComponent` renders `<router-outlet>`; `statement-route.guard` writes to SessionStorage.
- **Integration:** Bundle-graph check script tested against fixtures (good + bad `stats.json`).
- **Build:** `ng build public-statement --configuration production` completes; stats.json valid.
- **Lint:** `ng lint public-statement` with a forbidden import in a test fixture → reports exactly one error.

### Previous Story Intelligence

**Prerequisites:**
- Story 0.2 — Angular two-target workspace scaffolded (placeholder `apps/public-statement`)
- Story 0.9 — GitHub Actions CI workflow (this story adds a new required job)
- Story 1.1 — bilingual shell + design tokens + RTL pipeline (libs/design-system, libs/domain-ui available)
- Story 4.1 — `statement-route.guard` (consumed in `app.routes.ts`)

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.2
- Two-build-targets decision — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.4, D1
- ADR-001 — [_bmad-output/planning-artifacts/adrs/adr-001-angular-two-target-workspace.md](../planning-artifacts/adrs/adr-001-angular-two-target-workspace.md)
- Zoneless invariant — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.2
- Bundle-graph CI gate — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.5
- Story 0.2 Angular two-target workspace — [_bmad-output/implementation-artifacts/0-2-angular-two-target-workspace.md](./0-2-angular-two-target-workspace.md)
- Story 4.1 capability-token scheme — [_bmad-output/implementation-artifacts/4-1-capability-token-scheme-with-constant-time-response.md](./4-1-capability-token-scheme-with-constant-time-response.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- This story activates the compile-boundary mechanism. Without it, all downstream Epic 4 stories fail to enforce the trust boundary.
- Bundle-graph CI gate is required on `main`; adding it is a GitHub branch-protection configuration step that lives outside the repo.

### File List

_Populated during implementation._
