# Story 4.15: Public-SPA locale toggle + disclaimer (FR2 + FR7 spans)

Status: ready-for-dev

## Story

As **a recipient viewing a shared statement**,
I want to **toggle between Arabic and English via a language selector + see the one-line legal disclaimer in my selected language**,
so that **the statement page is fully bilingual despite being a separate Angular build target** (FR2 span, FR7 span).

**Covers requirements:** Epic 4 Story 4.15 — FR2 span (auth/locale UX consistency on the public surface; the public surface has no auth, but the locale-selection UX must match), FR7 span (legal disclaimer parity with the owner workspace per NFR-A1), zoneless invariant on `apps/public-statement` per architecture §5.4 [Source: epics.md Epic 4 Story 4.15; prd.md FR2, FR7; architecture.md §5.4].

**Why this story now:** Epic 4 is otherwise complete. Stories 4.1–4.14 ship the public-statement scaffold, the constant-time discipline, the cache invariant, and the load gates. But the public SPA is currently mono-locale. Without this story, an Arabic recipient receives a WhatsApp link from an Arabic-using owner and lands on an English-only page — a credibility break inconsistent with the bilingual product promise (NFR-A1). The disclaimer parity also closes the FR7 compliance loop: the owner sees the disclaimer (Story 1.6); the recipient now does too.

## Acceptance Criteria

### AC1 — Language toggle in minimal header

**Given** `apps/public-statement` is loaded with the statement view rendered
**When** the user taps the language toggle in the minimal header
**Then** `<html lang>` flips between `ar` and `en`
**And** `<html dir>` flips between `rtl` and `ltr`
**And** Transloco swaps all visible strings (statement labels, button labels, disclaimer)
**And** the toggle works without any API call or account context — entirely client-side per epics.md AC3 [Source: epics.md Epic 4 Story 4.15 AC1, AC3].

### AC2 — Disclaimer text identical to owner-app per NFR-A1

**Given** the owner-workspace ships the legal disclaimer in Story 1.6 with Transloco keys `legal.disclaimer.title` + `legal.disclaimer.body`
**When** the public-statement renders its footer disclaimer
**Then** the disclaimer text is character-identical to the owner-app version in both locales (AR + EN)
**And** the Transloco keys are duplicated (NOT shared via a `libs/` dependency, because `apps/public-statement` cannot import from `libs/core/i18n` if that lib is owner-only — see architecture §3.4 compile-boundary)
**And** a CI gate (text-diff script) compares the AR + EN disclaimer strings between the two apps' i18n bundles and fails the build if they drift [Source: epics.md Epic 4 Story 4.15 AC2; prd.md NFR-A1; architecture.md §3.4 compile-boundary].

### AC3 — Toggle works without account context

**Given** the public surface has no authenticated user
**When** the toggle fires
**Then** no HTTP call is issued (no `/v1/users/preferences` POST, no auth header attached)
**And** the language preference is persisted to `localStorage` under key `faktuboh.public.lang` per epics.md AC4
**And** subsequent page loads within the session (same browser, same domain) read this preference and bootstrap to the saved language [Source: epics.md Epic 4 Story 4.15 AC3, AC4].

### AC4 — Bootstrap order: localStorage → Accept-Language → default Arabic

**Given** a recipient opens a statement link for the first time on this browser
**When** the SPA bootstraps
**Then** the language is determined by:
1. `localStorage.getItem('faktuboh.public.lang')` if set and valid (`ar` | `en`)
2. Otherwise, parse `navigator.language` — if it starts with `ar`, use Arabic; if `en`, use English; otherwise default
3. Default: Arabic (consistent with owner-workspace default per Story 1.1)
**And** the bootstrap completes BEFORE the first paint (no flash-of-wrong-language)
**And** Transloco's `defaultLang: 'ar'` + `fallbackLang: 'en'` matches the owner-workspace config

### AC5 — Zoneless invariant preserved

**Given** `apps/public-statement` runs zoneless per architecture §5.4 + §3.4
**When** the toggle fires
**Then** the language change propagates via signals + `signal(currentLang)` re-renders without `Zone.js`
**And** Transloco's `reRenderOnLangChange: true` works under zoneless (verified at first integration test pass)
**And** no `setTimeout`/`setInterval` is added by this story to trigger change detection [Source: architecture.md §5.4; ux-design-specification.md component implementation strategy].

### AC6 — RTL pipeline: `<html dir>` flips + logical CSS properties hold

**Given** Story 4.2 ships the public-statement workspace with the same RTL pipeline as Story 1.1's owner-workspace
**When** the user toggles to Arabic
**Then** `<html dir="rtl">` is set
**And** PrimeNG components (any used in the public-statement minimal chrome) flip via `dir` inheritance
**And** any custom CSS uses logical properties only (`margin-inline-start`, not `margin-left`) — ESLint/Stylelint enforces
**And** the language toggle button itself uses logical positioning (no `left`/`right`)

### AC7 — Toggle UI per ux-design-specification minimal-chrome rules

**Given** the public-statement chrome is intentionally minimal per architecture §6.4.3 (no nav, no chrome beyond the toggle)
**When** the toggle button is rendered
**Then** it sits in the top-inline-end corner of the viewport (right in LTR, left in RTL)
**And** uses a PrimeNG `<p-button text severity="secondary">` with `aria-label="Switch language"` (Transloco-keyed)
**And** displays the *target* language label, not the current one (e.g., when in EN, button shows "العربية" — tapping it switches to Arabic)
**And** the button does not consume real estate from the statement payload area (no layout shift)

### AC8 — Telemetry event `public_locale_toggled`

**Given** AR-047 telemetry primitive is wired (Story 0.14)
**When** the user toggles the language
**Then** `TelemetryEmitter.Emit("public_locale_toggled", { from_lang, to_lang, statement_view_session_hash })` fires
**And** no PII (token, owner ID, IP) appears in the payload
**And** the `statement_view_session_hash` is a per-tab opaque hash (not a tracking cookie) used to correlate toggle behavior with the statement view itself for funnel analysis [Source: Story 0.14 telemetry primitive; Story 4.8 funnel instrumentation].

### AC9 — i18n bundle structure per public-statement layout

**Given** architecture §6.4.3 specifies `apps/public-statement/src/assets/i18n/{ar,en}.json`
**When** translations are added
**Then** the bundles include keys:
- `statement.toggle.aria-label`
- `statement.toggle.target-label-ar` / `statement.toggle.target-label-en`
- `legal.disclaimer.title`
- `legal.disclaimer.body`
- `statement.header.tagline` (the "write it down" Arabic-source brand line per project_faktuboh_rebrand)
**And** the bundles are loaded at startup (not lazy per-scope, because the public SPA is single-purpose and the full bundle is small) [Source: architecture.md §6.4.3].

### AC10 — Bundle budget unaffected (180 KB gz still holds)

**Given** Story 4.3 enforces a 180 KB gz bundle budget on the public-statement build
**When** this story adds Transloco + 2 JSON i18n bundles to the public app
**Then** the resulting bundle is still < 180 KB gz
**And** Transloco is added with `mode: 'lazy'` for any non-critical scopes (not relevant for MVP — initial bundle is the only scope)
**And** a CI gate (already exists per Story 4.3) verifies the budget; this story should NOT regress it [Source: epics.md Epic 4 Story 4.3; architecture.md §6.4.3].

## Tasks / Subtasks

### Frontend — Locale infrastructure

- [ ] **T1 (AC: 1, 4, 5)** — Wire Transloco into `apps/public-statement/src/app/app.config.ts`
  - [ ] `provideTransloco({ config: { availableLangs: ['ar', 'en'], defaultLang: 'ar', fallbackLang: 'en', reRenderOnLangChange: true, prodMode: environment.production }})`
  - [ ] `TranslocoHttpLoader` reads from `assets/i18n/{lang}.json`
  - [ ] No NgModule — standalone-only per architecture §6.4.3
- [ ] **T2 (AC: 4)** — Create `apps/public-statement/src/app/core/lang-bootstrap.ts`
  - [ ] Function `bootstrapPublicLang(): 'ar' | 'en'` — runs in `main.ts` BEFORE bootstrap
  - [ ] Order: localStorage → navigator.language → 'ar' default
  - [ ] Sets `<html lang>` + `<html dir>` synchronously before first paint
- [ ] **T3 (AC: 1, 3)** — Create `apps/public-statement/src/app/core/lang-toggle.service.ts`
  - [ ] `currentLang = signal<'ar'|'en'>('ar')`
  - [ ] `toggle()` method — flips lang, calls `transloco.setActiveLang(newLang)`, updates `<html lang dir>`, persists to `localStorage`, emits telemetry
  - [ ] No HTTP calls

### Frontend — Toggle UI

- [ ] **T4 (AC: 1, 6, 7)** — Create `apps/public-statement/src/app/shell/lang-toggle.component.ts`
  - [ ] Standalone, OnPush, signals
  - [ ] Selector `ps-lang-toggle`
  - [ ] PrimeNG `p-button` with `text` + `severity="secondary"`
  - [ ] Displays target language label (computed signal)
  - [ ] `aria-label` from Transloco key
  - [ ] Logical positioning only
- [ ] **T5 (AC: 7)** — Mount toggle in `app.component.ts`
  - [ ] Top-inline-end positioning via Tailwind logical utilities (`top-2 end-2`)
  - [ ] No layout shift on statement payload area

### Frontend — Disclaimer

- [ ] **T6 (AC: 2)** — Create `apps/public-statement/src/app/shell/legal-disclaimer.component.ts`
  - [ ] Standalone, OnPush
  - [ ] Renders `legal.disclaimer.title` + `legal.disclaimer.body` via Transloco
  - [ ] Mounted in app shell footer
- [ ] **T7 (AC: 2)** — Create `scripts/i18n/diff-public-vs-owner-disclaimer.mjs`
  - [ ] Reads `apps/owner-workspace/src/assets/i18n/{ar,en}.json` (`legal.disclaimer.*`)
  - [ ] Reads `apps/public-statement/src/assets/i18n/{ar,en}.json` (`legal.disclaimer.*`)
  - [ ] Asserts character-equality on title + body for each lang
  - [ ] Exits non-zero on drift with diff output

### Frontend — i18n bundles

- [ ] **T8 (AC: 9)** — Create `apps/public-statement/src/assets/i18n/ar.json`
  - [ ] All keys from AC9 with Arabic translations
  - [ ] `legal.disclaimer.*` copied verbatim from owner-workspace ar.json
- [ ] **T9 (AC: 9)** — Create `apps/public-statement/src/assets/i18n/en.json`
  - [ ] Mirror of ar.json with English translations
  - [ ] `legal.disclaimer.*` copied verbatim from owner-workspace en.json

### Telemetry

- [ ] **T10 (AC: 8)** — Wire `public_locale_toggled` event
  - [ ] Use AR-047 client-side telemetry helper (Story 4.8 has the public-surface variant)
  - [ ] PII guard: assert no token / owner ID / IP in payload
  - [ ] `statement_view_session_hash` from Story 4.8 funnel instrumentation

### CI gates

- [ ] **T11 (AC: 2)** — Add disclaimer-parity gate to `.github/workflows/ci.yml`
  - [ ] Runs `scripts/i18n/diff-public-vs-owner-disclaimer.mjs`
  - [ ] Fails build on drift
- [ ] **T12 (AC: 10)** — Verify Story 4.3 bundle-budget gate still passes
  - [ ] Run `ng build apps/public-statement --configuration=production` locally
  - [ ] Check `dist/apps/public-statement/main-*.js.gz` < 180 KB

### Tests

- [ ] **T13 (AC: 1, 4, 5)** — Vitest tests in `apps/public-statement/src/app/core/lang-toggle.service.spec.ts`
  - [ ] Toggle flips currentLang signal
  - [ ] Toggle persists to localStorage
  - [ ] Toggle does NOT issue HTTP calls (assert via `HttpTestingController`)
  - [ ] Bootstrap order: localStorage → navigator.language → default
- [ ] **T14 (AC: 6)** — Vitest tests for RTL: assert `<html dir="rtl">` after AR toggle
- [ ] **T15 (AC: 1, 7)** — Playwright E2E in `apps/public-statement/e2e/locale-toggle.spec.ts`
  - [ ] Load page in EN, tap toggle, assert AR text + RTL direction
  - [ ] Reload page, assert preference persisted
  - [ ] Axe-core a11y assertion on both locales
- [ ] **T16 (AC: 2)** — Snapshot test asserting disclaimer text matches owner-workspace text (runs as part of T7 script + Vitest assertion)

## Dev Notes

### Critical guardrails (do not violate)

1. **No `libs/` import from `apps/public-statement` for owner-workspace code.** Architecture §3.4 enforces compile-boundary via two distinct Angular projects. Disclaimer text is duplicated, NOT shared via `libs/core/i18n`. The CI parity gate (T7) is the substitute for code-sharing.
2. **Zoneless invariant.** No `setTimeout`/`setInterval` for change detection. Transloco's `reRenderOnLangChange: true` must work under zoneless — verify in T13.
3. **No HTTP on toggle.** The public surface has no auth. Adding a `POST /v1/users/preferences` here would either fail (no token) or require relaxing the auth boundary — both are wrong.
4. **`localStorage` not `sessionStorage` for lang preference.** Returning visitors expect their language to stick (per epics.md AC4). This is *different* from the capability-token storage rule (Story 4.1 AC6 uses sessionStorage for tokens because cross-tab token reuse is a capability leak; lang preference is not sensitive).
5. **No layout shift on toggle.** The toggle button is fixed-position; the statement payload reflows for RTL but the button does not move the statement content vertically.
6. **180 KB gz bundle budget holds.** Adding Transloco can push toward the limit; if it exceeds, tree-shake aggressively or load bundles lazily. The Story 4.3 CI gate will catch a regression.
7. **Default lang is Arabic.** Match owner-workspace default per Story 1.1.

### Source tree — files to create or touch

```
apps/public-statement/
├── src/
│   ├── main.ts                                         # MODIFY (call bootstrapPublicLang first)
│   ├── app/
│   │   ├── app.config.ts                               # MODIFY (provideTransloco)
│   │   ├── app.component.ts                            # MODIFY (mount toggle + disclaimer)
│   │   ├── core/
│   │   │   ├── lang-bootstrap.ts                       # NEW
│   │   │   └── lang-toggle.service.ts                  # NEW
│   │   └── shell/
│   │       ├── lang-toggle.component.ts                # NEW
│   │       ├── lang-toggle.component.html              # NEW
│   │       ├── lang-toggle.component.spec.ts           # NEW
│   │       ├── legal-disclaimer.component.ts           # NEW
│   │       └── legal-disclaimer.component.html         # NEW
│   └── assets/i18n/
│       ├── ar.json                                     # NEW
│       └── en.json                                     # NEW
└── e2e/
    └── locale-toggle.spec.ts                           # NEW

scripts/i18n/
└── diff-public-vs-owner-disclaimer.mjs                 # NEW

.github/workflows/
└── ci.yml                                              # MODIFY (add disclaimer parity gate)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | Toggle service + bootstrap logic | Vitest + Angular Testing Library |
| Component | Toggle button + disclaimer | Vitest + Angular Testing Library |
| E2E | Toggle flow + persistence + a11y | Playwright (Chromium + WebKit) |
| Parity | Disclaimer text drift | Node script (CI gate) |
| Bundle | 180 KB gz still holds | Existing Story 4.3 gate |

### Project Structure Notes

This story extends `apps/public-statement` only. No backend changes. The disclaimer parity gate is the substitute for code-sharing across the compile boundary — the only acceptable mechanism per architecture §3.4. If a future story introduces a third app needing the same disclaimer, the parity gate generalizes (one source file, N comparison targets) rather than relaxing the boundary.

### Architecture compliance

- **§3.4** — public-statement is a separate build target; cannot import from owner-workspace `libs/`.
- **§5.4** — zoneless invariant on both SPAs.
- **§6.4.3** — public-statement layout (`assets/i18n/`, minimal chrome).
- **NFR-A1** — bilingual product promise; disclaimer parity enforces this.
- **FR2 + FR7 spans** — locale UX and disclaimer extend to the public surface.
- **Story 1.1 patterns** — Transloco config + RTL pipeline mirror the owner-workspace setup.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Framework | Angular | 21 (zoneless) |
| i18n | Transloco | Same as owner-workspace per Story 1.1 |
| UI | PrimeNG | 21 — minimal use (just `<p-button>` for toggle) |
| Telemetry | AR-047 client primitive | Per Story 4.8 |
| Test (FE) | Vitest + Angular Testing Library | |
| E2E | Playwright | Chromium + WebKit |

### Testing requirements

- **Unit:** lang-toggle service, bootstrap order, no-HTTP-on-toggle assertion.
- **Component:** toggle button rendering + a11y, disclaimer rendering.
- **E2E:** full toggle flow with persistence + a11y.
- **Parity:** disclaimer text equality across two app i18n bundles.
- **Bundle budget:** verify Story 4.3 gate still passes.

### Previous Story Intelligence

**Prerequisites:**
- Story 1.1 — bilingual shell + Transloco patterns + RTL pipeline (the template this story follows for the public app)
- Story 1.6 — owner-workspace legal disclaimer (the source of truth for disclaimer text)
- Story 4.2 — `apps/public-statement` Angular project + RTL pipeline already wired
- Story 4.3 — 180 KB gz bundle-budget CI gate
- Story 4.5 — public statement view (the page the toggle sits above)
- Story 4.8 — funnel telemetry primitive on the public surface
- Story 0.14 — AR-047 telemetry primitive

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.15
- Compile-boundary — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.4
- Public-statement layout — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §6.4.3
- Zoneless invariant — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.4
- FR2, FR7, NFR-A1 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Story 1.1 bilingual shell — [_bmad-output/implementation-artifacts/1-1-bilingual-shell-design-tokens-rtl-pipeline.md](./1-1-bilingual-shell-design-tokens-rtl-pipeline.md)
- Story 1.6 legal disclaimer — [_bmad-output/implementation-artifacts/1-6-legal-disclaimer-display.md](./1-6-legal-disclaimer-display.md)
- Story 4.2 public-statement workspace — [_bmad-output/implementation-artifacts/4-2-apps-public-statement-as-distinct-angular-build-target.md](./4-2-apps-public-statement-as-distinct-angular-build-target.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Closes the FR2 + FR7 spans onto the public surface.
- Disclaimer parity gate is the architectural substitute for code-sharing across the compile boundary.
- Default Arabic + Arabic-source brand identity reinforced.

### File List

_Populated during implementation._
