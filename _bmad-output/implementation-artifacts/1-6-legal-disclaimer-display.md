# Story 1.6: Legal disclaimer display

Status: ready-for-dev

## Story

As a **new or existing user**,
I want **to see a clear legal disclaimer explaining that tracked debts are personal records (not legally binding contracts)**,
so that **I understand the product's positioning and regulatory scope (FR7)**.

**Covers requirements:** FR7 (legal disclaimer), NFR-A1 (WCAG contrast).

**Why this story now:** Per the reorder, 1.6 follows 1.5 — profile is in place; the disclaimer needs to surface in registration (Story 1.2 hosted page) + account settings (`/profile`) + authenticated owner shell footer. This is the simplest content story but has a regulatory weight — text must be reviewed by Wahid/legal counsel before prod.

## Acceptance Criteria

### AC1 — Disclaimer in authenticated shell footer

**Given** an authenticated user on any route inside `apps/owner-workspace`
**When** the page renders
**Then** the footer (from Story 1.1's `app-shell.component`) shows the legal disclaimer text in the active locale
**And** the disclaimer text reads: "Faktuboh helps you track personal debt records. These records are not legally binding contracts." (EN) / "يساعدك Faktuboh على تسجيل ديون شخصية. هذه السجلات ليست عقوداً ملزمة قانونياً." (AR)
**And** the disclaimer is visible without horizontal scroll at all breakpoints (mobile/tablet/desktop)

### AC2 — Disclaimer on registration (Auth0 hosted page)

**Given** the Auth0 hosted sign-up page (Story 1.2)
**When** a new user views the form
**Then** the disclaimer text is shown above the submit button (as a non-dismissible notice)
**And** a checkbox "I have read and understood" is required to enable submit
**And** the same wording is used across both surfaces (footer + registration) — no drift

### AC3 — Disclaimer in account settings

**Given** an authenticated user navigates to `/profile`
**When** the page renders
**Then** the disclaimer text appears as a `<p-message severity="info">` block at the bottom of the page
**And** users can re-read the disclaimer anytime from their account

### AC4 — Localized via Transloco

**Given** the active locale is AR or EN
**When** the disclaimer renders
**Then** the text appears in the active locale via Transloco key `shell.footer.disclaimer` (or equivalent)
**And** both AR and EN translations are reviewed for accuracy + tone before merge
**And** the EN translation is approved by Wahid; AR translation reviewed by Wahid (native Arabic speaker)

### AC5 — WCAG 2.1 AA contrast

**Given** the disclaimer text in the footer
**When** Lighthouse + axe-core check it
**Then** text-to-background contrast meets ≥ 4.5:1 (body text WCAG AA)
**And** if the footer uses `--text-secondary` (#52525B) on `--surface` (#FFFFFF) → 7.5:1 (AAA)
**And** the disclaimer remains readable on all themes / surfaces

### AC6 — Disclaimer registered as a structured content block

**Given** `libs/core/src/legal/disclaimer.constant.ts`
**When** inspected
**Then** the disclaimer text is exported as a versioned constant: `DISCLAIMER = { version: '1.0.0', text: { ar: '...', en: '...' } }`
**And** version bumps require explicit ADR + tracking in `docs/legal/disclaimer-history.md`
**And** the constant is consumed by both the shell footer and the profile page (single source of truth)

### AC7 — Disclaimer persistence in audit log on registration acceptance

**Given** a user submits the registration form with the "I have read and understood" checkbox
**When** the user-bootstrap webhook fires (Story 1.2)
**Then** the disclaimer version accepted is recorded on the `User` aggregate as `disclaimer_version_accepted`
**And** if a future version differs from the accepted one, the user is shown the new disclaimer + asked to acknowledge on next login (deferred to v2 — for now, just record initial acceptance)
**And** the audit log records the acceptance event with `disclaimer_version` + `accepted_at` + `User-7a3f` opaque identifier

### AC8 — Linkable disclaimer page

**Given** the disclaimer must be referenceable from email templates, support docs, etc.
**When** a user navigates to `/legal/disclaimer`
**Then** a standalone, public-readable page renders the full disclaimer text in both locales side-by-side
**And** the page does not require authentication (lives in the `apps/owner-workspace` public routes group)
**And** the page is indexable by search engines (`<meta name="robots" content="index, follow">`)

### AC9 — Tests + manual review checklist

**Given** the disclaimer rendering
**When** tests run
**Then** Vitest + Playwright cover: footer renders in AR + EN; profile page shows disclaimer; `/legal/disclaimer` route renders standalone
**And** manual review checklist documented in `docs/legal/disclaimer-qa.md` — must be checked off before production deploy
**And** Wahid (or designated reviewer) signs off on text content + version

### AC10 — Backend exposes disclaimer version

**Given** `GET /v1/legal/disclaimer/current`
**When** any caller (authenticated or not) hits the endpoint
**Then** it returns `{ version: '1.0.0', text: { ar: '...', en: '...' } }`
**And** the endpoint is rate-limited (Auth0 not involved; backend rate limiter from Epic 0)
**And** the endpoint response is cached for 24h via Cache-Control

## Tasks / Subtasks

### Content + constants

- [ ] **T1 (AC: 4, 6)** — Create `libs/core/src/legal/disclaimer.constant.ts`
  - [ ] Versioned constant with AR + EN text; export as `DISCLAIMER`
  - [ ] AR text reviewed by Wahid (native speaker); EN text approved by Wahid
- [ ] **T2 (AC: 6, 9)** — Create `docs/legal/disclaimer-history.md` + `disclaimer-qa.md`
  - [ ] History log: version, date, text diff, ADR ref
  - [ ] QA checklist: text accuracy, tone, contrast, accessibility, regulatory framing

### Frontend — disclaimer surfaces

- [ ] **T3 (AC: 1, 4, 5)** — Update `apps/owner-workspace/src/app/shell/app-shell.component.html`
  - [ ] Footer renders Transloco-translated disclaimer key
  - [ ] CSS: `color: var(--text-secondary)`; `font-size: var(--text-sm)`; padding via logical props
  - [ ] Lighthouse contrast verified
- [ ] **T4 (AC: 3)** — Add disclaimer block to `profile.component.html` (Story 1.5)
  - [ ] `<p-message severity="info">` at the bottom of the page
  - [ ] Same Transloco key as footer (single source via `DISCLAIMER` constant)
- [ ] **T5 (AC: 8)** — Create `apps/owner-workspace/src/app/features/legal/disclaimer-page.component.ts`
  - [ ] Standalone + OnPush
  - [ ] Renders both AR + EN side-by-side (or stacked on mobile)
  - [ ] Route `/legal/disclaimer` registered in `apps/owner-workspace/src/app/app.routes.ts` — public (no authGuard)
  - [ ] `<meta name="robots" content="index, follow">` set via Angular Meta service
- [ ] **T6 (AC: 4)** — Translation bundles
  - [ ] Add `shell.footer.disclaimer` + `legal.disclaimer.title` + `legal.disclaimer.body` to `assets/i18n/ar.json` + `en.json`
  - [ ] Use the same source text from `DISCLAIMER` constant — keep in sync via codegen or manual review

### Auth0 hosted page

- [ ] **T7 (AC: 2)** — Customize Auth0 sign-up page
  - [ ] Auth0 Dashboard → Branding → Universal Login → Custom Text → add disclaimer above submit
  - [ ] Required checkbox enables submit
  - [ ] Localized via Auth0's `{{user_metadata.locale}}` or `{{ui_locales}}`

### Backend — disclaimer endpoint + audit

- [ ] **T8 (AC: 10)** — Create `backend/src/Faktuboh.Api/Slices/Legal/`
  - [ ] `Legal.Endpoints.cs` — `MapGroup("/v1/legal").AllowAnonymous()` (public)
  - [ ] `GET /disclaimer/current` returns disclaimer text + version
  - [ ] Cache-Control: `max-age=86400` (24h)
- [ ] **T9 (AC: 7)** — Audit log on acceptance
  - [ ] Modify `BootstrapHandler` (Story 1.2) to record `disclaimer_version_accepted` on the `User` aggregate
  - [ ] Audit log row `disclaimer_accepted` with version + opaque user ID
  - [ ] Migration to add `disclaimer_version_accepted` column to `users` table

### Tests

- [ ] **T10 (AC: 1, 3, 4, 5, 8)** — Playwright E2E `apps/owner-workspace/e2e/legal/disclaimer.spec.ts`
  - [ ] Footer renders disclaimer on every authenticated route — assert presence in AR + EN
  - [ ] `/profile` shows disclaimer block
  - [ ] `/legal/disclaimer` standalone page renders unauthenticated, indexable
  - [ ] axe-core zero violations
  - [ ] Lighthouse contrast check passes
- [ ] **T11 (AC: 6, 9)** — Vitest unit test for `DISCLAIMER` constant
  - [ ] Both `text.ar` and `text.en` present
  - [ ] Version field present + matches semver
- [ ] **T12 (AC: 7, 10)** — Backend integration tests
  - [ ] `GET /v1/legal/disclaimer/current` returns version + text
  - [ ] Bootstrap handler records `disclaimer_version_accepted` on User
  - [ ] Audit log row written

## Dev Notes

### Critical guardrails (do not violate)

1. **Disclaimer text is regulatory content — Wahid signs off before prod.** Do not modify the text via casual PR.
2. **Single source of truth: `DISCLAIMER` constant.** Footer, profile, registration, `/legal/disclaimer` page, backend endpoint — all reference the same versioned constant.
3. **Audit log on acceptance.** Records `disclaimer_version_accepted` per user — defensible legal evidence trail.
4. **Version bumps require ADR.** Any text change increments the version + logs an ADR.
5. **Localized in both AR + EN.** No machine translation; both reviewed by a native speaker.
6. **WCAG AA contrast.** Disclaimer in footer uses `--text-secondary` on `--surface` (7.5:1 — AAA). Never reduce contrast for "subtlety."
7. **Public-readable disclaimer page.** Email templates, support docs, marketing site link to it. Indexable by search engines.

### Source tree — files to create or touch

```
apps/owner-workspace/
├── src/app/
│   ├── shell/app-shell.component.html              # MODIFY (footer disclaimer)
│   ├── features/legal/
│   │   ├── disclaimer-page.component.ts            # NEW
│   │   ├── disclaimer-page.component.html          # NEW
│   │   ├── disclaimer-page.component.scss          # NEW
│   │   └── disclaimer-page.component.spec.ts       # NEW
│   ├── features/profile/profile.component.html     # MODIFY (add disclaimer block)
│   └── app.routes.ts                               # MODIFY (add /legal/disclaimer)
├── src/assets/i18n/
│   ├── ar.json                                     # MODIFY
│   └── en.json                                     # MODIFY
└── e2e/legal/
    └── disclaimer.spec.ts                          # NEW

libs/core/src/legal/
├── disclaimer.constant.ts                          # NEW
├── disclaimer.constant.spec.ts                     # NEW
└── index.ts                                        # MODIFY

backend/src/Faktuboh.Api/Slices/Legal/
├── Legal.Endpoints.cs                              # NEW
└── GetDisclaimer/
    ├── GetDisclaimerResponse.cs                    # NEW
    └── GetDisclaimerHandler.cs                     # NEW

backend/src/Faktuboh.Domain/Users/
└── User.cs                                         # MODIFY (add disclaimer_version_accepted)

backend/src/Faktuboh.Infrastructure/Persistence/Migrations/
└── YYYYMMDDHHMMSS_AddDisclaimerVersionAccepted.cs  # NEW

backend/src/Faktuboh.Api/Slices/Auth/Bootstrap/
└── BootstrapHandler.cs                             # MODIFY (record disclaimer acceptance)

docs/legal/
├── disclaimer-history.md                           # NEW
└── disclaimer-qa.md                                # NEW
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | Constant shape | Vitest |
| Integration | GET endpoint + bootstrap audit | WebApplicationFactory + Testcontainers Postgres |
| E2E | Footer + profile + standalone page | Playwright in LTR + RTL |
| Manual | Text accuracy + tone | `docs/legal/disclaimer-qa.md` checklist |

**Forbidden:**
- Disclaimer text modifications without ADR + version bump
- Footer text without contrast verification
- Re-translating disclaimer with machine translation
- Drift between footer / profile / registration / public page text

### Project Structure Notes

This story creates a tiny `legal` slice on backend + a `legal/disclaimer` route on frontend. Modifies the existing shell footer (from Story 1.1) and profile page (from Story 1.5) and Auth0 hosted UI (from Story 1.2).

**Detected variances:**
1. Disclaimer wording — the AC1 text is a placeholder that satisfies FR7. Wahid reviews + revises before prod. The story's job is to ship the plumbing; the exact text is a content decision.
2. Re-acceptance flow when version bumps — deferred to v2; this story records initial acceptance only.

### Architecture compliance

- **FR7** (legal disclaimer) — directly satisfied
- **NFR-A1** (WCAG AA) — contrast verified
- **§4 D4.9** (RFC 9457) — `GET /v1/legal/disclaimer/current` returns 200 with content; no errors expected
- **§3.5 Cross-Cutting** — audit log row on acceptance; bitemporal `users` row update

### Library / framework requirements

| Layer | Package | Version |
|---|---|---|
| Framework | Angular | 21 |
| UI kit | PrimeNG | 21 (`<p-message>`) |
| i18n | Transloco | latest |

### Testing requirements

- **Unit**: `DISCLAIMER` constant shape + version
- **Integration**: `GET /disclaimer/current` + `BootstrapHandler` audit entry
- **E2E**: footer + profile + standalone page render in AR + EN
- **Manual**: checklist signed off before prod

### Previous Story Intelligence

**Prerequisites:**
- **Story 1.1** — shell footer slot exists; Transloco runtime
- **Story 1.2** — Auth0 sign-up page customization access; user bootstrap webhook
- **Story 1.5** — profile page exists for disclaimer block insertion

**Downstream:**
- Story 1.7 (deletion) confirmation copy references the disclaimer

### Latest Technical Information

**Auth0 Universal Login Custom Text:**
```json
{
  "signup": {
    "footer-text": "{{disclaimer_localized_text}}",
    "footer-link-text": "Read more"
  }
}
```

**Angular standalone public route:**
```ts
{
  path: 'legal/disclaimer',
  loadComponent: () => import('./features/legal/disclaimer-page.component').then(m => m.DisclaimerPageComponent),
  // No authGuard — public
}
```

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Story 1.6
- FR7 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md) FR7
- NFR-A1 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md) Accessibility
- Story 1.1 — [_bmad-output/implementation-artifacts/1-1-bilingual-shell-design-tokens-rtl-pipeline.md](./1-1-bilingual-shell-design-tokens-rtl-pipeline.md)
- Story 1.2 — [_bmad-output/implementation-artifacts/1-2-user-registration-auth0-hosted-flow.md](./1-2-user-registration-auth0-hosted-flow.md)
- Story 1.5 — [_bmad-output/implementation-artifacts/1-5-profile-view-and-edit.md](./1-5-profile-view-and-edit.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Single source of truth via `DISCLAIMER` constant; consumed by footer + profile + standalone page + Auth0 hosted UI + backend endpoint
- Disclaimer version recorded on User aggregate during bootstrap; audit row written
- Re-acceptance on version bump deferred to v2

### File List

_Populated during implementation — see Source tree section above._
