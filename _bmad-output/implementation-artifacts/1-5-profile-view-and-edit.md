# Story 1.5: Profile view and edit

Status: ready-for-dev

## Story

As an **authenticated user**,
I want **to view and edit my profile information (name, language preference, timezone)**,
so that **Faktuboh displays dates and content correctly for me (FR5)**.

**Covers requirements:** FR5 (profile management), NFR-P4 (500ms response), UX-DR28 (IftaLabel/FloatLabel), UX-DR27 (silent success).

**Why this story now:** Per the reorder, 1.5 follows the auth triangle (1.2/1.3/1.4). Authenticated users can now manage their profile; this story introduces the first Signal Forms pattern reused by Epic 2/3 forms.

## Acceptance Criteria

### AC1 — Profile view at `/profile`

**Given** an authenticated user
**When** they navigate to `/profile`
**Then** the page renders current: `name` (editable), `email` (read-only, with "Change email via Auth0 security settings" link), `locale` (AR | EN toggle), `timezone` (IANA zone dropdown)
**And** the page is behind `authGuard` — unauthenticated visitors redirect to login with return-to
**And** axe-core reports zero WCAG 2.1 AA violations

### AC2 — Signal Forms with FormMutationConfig

**Given** the profile edit form
**When** inspected
**Then** the form is built with Angular Signal Forms
**And** a `FormMutationConfig<UpdateProfileRequest>` drives the submit logic
**And** `Idempotency-Key` UUID v4 generated per render + refreshed on success/reset
**And** submit button `pending` signal shown during network activity

### AC3 — IftaLabel ≥ 1024px / FloatLabel < 1024px

**Given** the form's label strategy per UX-DR28
**When** the viewport is ≥ 1024px
**Then** each input uses PrimeNG `<p-iftaLabel>` with label-inside-border style
**And** when < 1024px, each input uses `<p-floatLabel variant="on">` with floating label
**And** no red asterisks; optional fields carry "(optional)" suffix in the label
**And** label text localized via Transloco — `profile.form.name-label`, etc.

### AC4 — Optimistic UI + server-authoritative confirmation

**Given** the user edits a field and clicks Save
**When** the form submits
**Then** the UI optimistically reflects the new value immediately
**And** `PATCH /v1/users/me` fires with the delta payload + `Idempotency-Key` header
**And** on 200 OK, the optimistic state is confirmed (no visual change needed — silent success per UX-DR27)
**And** on 4xx/5xx, the optimistic state rolls back + RFC 9457 error surfaces inline

### AC5 — RFC 9457 server errors walked onto Signal Forms

**Given** a 4xx RFC 9457 response with `errors[]` containing JSON Pointers
**When** `applyServerErrors(form, problemDetails)` runs
**Then** each JSON Pointer (`/name`, `/timezone`, etc.) walks onto the corresponding Signal Forms node
**And** the node displays the localized error message from the server `detail` field (or Transloco key from `code`)
**And** the first errored field receives focus (accessibility + UX)

### AC6 — Locale change triggers shell update

**Given** the user toggles the locale via the form (or via the top app bar from Story 1.1)
**When** `PATCH /v1/users/me { locale }` succeeds
**Then** `<html lang dir>` updates + Transloco reloads + backend syncs to Auth0 Management API (per Story 1.3)
**And** the form re-renders with new locale labels
**And** visual regression baselines both locales (from Story 1.11)

### AC7 — Timezone dropdown with search

**Given** the IANA timezone list
**When** the user clicks the timezone field
**Then** a `<p-select>` with search renders the full IANA zone list (~600 entries)
**And** common zones for the user's locale surface at top (Arabic context → `Asia/Dubai`, `Asia/Riyadh`, `Africa/Cairo`, `Asia/Amman`, `Asia/Kuwait`, `Asia/Bahrain`, `Asia/Qatar`, `Asia/Baghdad`; English → `America/New_York`, `Europe/London`, `Asia/Dubai`, etc.)
**And** the selected zone displays as `Asia/Dubai (GMT+4)` in the trigger
**And** all timestamps rendered elsewhere in the app use the user's stored zone

### AC8 — Email change routed to Auth0

**Given** the email field is read-only
**When** the user clicks the "Change email via Auth0 security settings" link
**Then** a new tab opens to Auth0's user-profile management page (`https://auth.faktuboh.com/u/profile`)
**And** the user is authenticated via SSO (existing session)
**And** after email change, the next login re-syncs the email via the ID token

### AC9 — 500ms response target per NFR-P4

**Given** the full PATCH → persist → response flow
**When** measured end-to-end
**Then** p95 < 500ms against Testcontainers Postgres baseline
**And** integration test asserts via timing measurement

### AC10 — Accessibility + RTL parity

**Given** the profile page
**When** E2E runs in both LTR + RTL
**Then** axe-core reports zero WCAG 2.1 AA violations
**And** Playwright visual regression baselines captured per breakpoint (mobile/tablet/desktop) × direction (LTR/RTL)
**And** keyboard-only navigation works (Tab through all fields + Save button)
**And** screen reader announces form errors when they appear (live region)

## Tasks / Subtasks

### Backend — Profile slice

- [ ] **T1 (AC: 1, 4, 8)** — Create `backend/src/Faktuboh.Api/Slices/Users/`
  - [ ] `Users.Endpoints.cs` — `MapGroup("/v1/users").RequireAuthorization()` + `ProcessingActivity.OwnerLedger`
  - [ ] `GET /me` endpoint — returns current user profile
  - [ ] `PATCH /me` endpoint — updates name, locale, timezone (email excluded)
- [ ] **T2 (AC: 2, 4)** — `PatchUserRequest` + `PatchUserResponse` + `PatchUserRequestValidator`
  - [ ] Request: nullable fields for partial update (`name?`, `locale?`, `timezone?`)
  - [ ] Validator: name 1..100 chars; locale in `['ar', 'en']`; timezone validated against `TimeZoneInfo.GetSystemTimeZones()`
- [ ] **T3 (AC: 4)** — `PatchUserHandler`
  - [ ] Wolverine handler with `Idempotency-Key` header support
  - [ ] Updates `User` aggregate; writes bitemporal history row
  - [ ] Syncs to Auth0 Management API if locale changed (per Story 1.3)
- [ ] **T4 (AC: 5)** — RFC 9457 error codes
  - [ ] `faktuboh.users.name_invalid` (400)
  - [ ] `faktuboh.users.locale_unsupported` (400)
  - [ ] `faktuboh.users.timezone_unsupported` (400)

### Frontend — Profile feature

- [ ] **T5 (AC: 1, 3, 7)** — Create `apps/owner-workspace/src/app/features/profile/`
  - [ ] `profile.routes.ts` — lazy route `/profile`
  - [ ] `profile.component.ts` — standalone + OnPush + signal inputs
  - [ ] Template uses `<p-iftaLabel>` ≥ 1024px and `<p-floatLabel variant="on">` < 1024px (Angular CDK Breakpoint Observer signal)
  - [ ] Timezone select uses `<p-select>` with filter + grouped common zones
- [ ] **T6 (AC: 2, 4, 5)** — Signal Forms integration
  - [ ] `FormMutationConfig<PatchUserRequest>` drives submit
  - [ ] `generateIdempotencyKey` = UUID v4; refreshes on success/reset
  - [ ] HTTP interceptor attaches `Idempotency-Key` header
  - [ ] `applyServerErrors(form, problemDetails)` helper walks JSON Pointers onto form nodes
- [ ] **T7 (AC: 6)** — Locale change integration
  - [ ] On form locale change, call `langBootstrap.setLang(newLocale)` pre-submit (optimistic)
  - [ ] Success → no further action; failure → rollback
- [ ] **T8 (AC: 8)** — Email change link
  - [ ] `<a [href]="auth0ProfileUrl" target="_blank" rel="noopener">` with localized label
  - [ ] `auth0ProfileUrl = 'https://auth.faktuboh.com/u/profile'`

### Tests

- [ ] **T9 (AC: 1-10)** — Playwright E2E at `apps/owner-workspace/e2e/profile/edit-profile.spec.ts`
  - [ ] Load `/profile` → fields populated from `GET /me`
  - [ ] Edit name → submit → silent success; refresh → new name persists
  - [ ] Toggle locale → shell flips direction + form labels re-render
  - [ ] Timezone change → subsequent date rendering uses new zone
  - [ ] RFC 9457 error path: submit invalid name → inline error under field
  - [ ] axe-core zero violations; LTR + RTL
  - [ ] Visual regression baselines (3 breakpoints × 2 directions)
- [ ] **T10 (AC: 2, 5)** — Vitest component test for profile form
  - [ ] Signal Forms integration
  - [ ] `applyServerErrors` walks JSON Pointers correctly
  - [ ] Idempotency-Key header attached
- [ ] **T11 (AC: 1-4, 9)** — Backend integration test `PatchUserEndpointTests`
  - [ ] Happy path with Testcontainers Postgres
  - [ ] Idempotency double-submit returns cached response
  - [ ] Negative path: invalid timezone → 400 RFC 9457
  - [ ] p95 < 500ms timing assertion
  - [ ] Bitemporal history row verified
- [ ] **T12 (AC: 6)** — Auth0 Management API sync integration test
  - [ ] Mock Management API client → verify `UpdateAsync` called with new locale

## Dev Notes

### Critical guardrails (do not violate)

1. **Email is read-only in the app.** Auth0 owns email changes; link out to Auth0 profile UI. Do not try to proxy email changes through the backend.
2. **Silent success per UX-DR27.** Successful save closes the form or stays in-place without a toast. The field reverting to its normal state IS the feedback.
3. **Idempotency-Key on every PATCH.** Double-submit scenarios (mobile double-tap) must not create double-writes.
4. **RFC 9457 error JSON Pointers walked onto Signal Forms.** `applyServerErrors(form, problemDetails)` helper is the single place doing this — reused in Epic 2/3 forms.
5. **Locale sync to Auth0 is best-effort.** If Auth0 Management API fails, the backend update still persists — Auth0 re-sync happens on next login via the Post-Login Action (defensive redundancy).
6. **Timezone stored as IANA zone string**, not offset. Offsets change with DST; IANA zones are canonical.
7. **IftaLabel ≥ 1024px / FloatLabel < 1024px** — this is the UX-DR28 pattern; do not substitute a third label style.
8. **No red asterisks.** Optional fields labeled "(optional)" explicitly.

### Source tree — files to create or touch

```
apps/owner-workspace/
├── src/app/features/profile/
│   ├── profile.routes.ts                           # NEW
│   ├── profile.component.ts                        # NEW
│   ├── profile.component.html                      # NEW
│   ├── profile.component.scss                      # NEW (logical props only)
│   └── profile.component.spec.ts                   # NEW
└── e2e/profile/
    └── edit-profile.spec.ts                        # NEW

libs/core/src/forms/
├── apply-server-errors.ts                          # NEW (RFC 9457 → Signal Forms)
├── form-mutation-config.ts                         # NEW (reusable config type)
└── idempotency-key.interceptor.ts                  # NEW (HTTP interceptor)

backend/src/Faktuboh.Api/Slices/Users/
├── Users.Endpoints.cs                              # NEW
├── Get/
│   ├── GetUserResponse.cs                          # NEW
│   └── GetUserHandler.cs                           # NEW
└── Patch/
    ├── PatchUserRequest.cs                         # NEW
    ├── PatchUserResponse.cs                        # NEW
    ├── PatchUserRequestValidator.cs                # NEW
    └── PatchUserHandler.cs                         # NEW

backend/src/Faktuboh.Domain/Users/
├── User.cs                                         # VERIFY/EXTEND (IBitemporal, locale, timezone, name)
└── IUserRepository.cs                              # VERIFY/EXTEND

backend/src/Faktuboh.Application/Errors/
└── ErrorCatalog.cs                                 # MODIFY (add 3 user codes)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | 70% | Vitest + xUnit v3 |
| Integration | Happy + Idempotency + Negative | WebApplicationFactory + Testcontainers Postgres |
| E2E | Happy + RFC 9457 + locale toggle | Playwright in LTR + RTL |
| Visual regression | 3 breakpoints × 2 directions | Playwright toHaveScreenshot |
| Performance | p95 < 500ms | Integration test timing |

**Forbidden:**
- Bespoke email change UI in the app
- Success toasts on profile save (silent success per UX-DR27)
- Skipping Idempotency-Key on PATCH
- Storing timezone as offset minutes

### Project Structure Notes

This story creates the first **real domain slice with Signal Forms** — `libs/core/src/forms/` helpers are reused by every subsequent form in Epic 2/3. The `applyServerErrors` helper is a cornerstone — keep its API stable.

**Detected variances:**
1. Breakpoint detection for IftaLabel vs FloatLabel toggle — use Angular CDK `BreakpointObserver` wrapped in a signal via `toSignal()`; test behavior at exactly 1024px (inclusive boundary).
2. IANA timezone list source — use `Intl.supportedValuesOf('timeZone')` in the browser; backend uses `TimeZoneInfo` (which has Windows zone naming on Windows — use `TimeZoneConverter` NuGet if cross-OS consistency matters).

### Architecture compliance

- **FR5** (profile management) — directly satisfied
- **NFR-P4** (500ms response) — p95 < 500ms
- **§4 D4.11** (Idempotency-Key) — enforced on PATCH
- **§4 D4.9** (RFC 9457) — error responses conform
- **§5.2.4** (timezone storage) — IANA zone string
- **§5.3.1** (DTO naming) — `PatchUserRequest/Response/Validator/Handler`
- **UX-DR27** (silent success) — no success toast
- **UX-DR28** (IftaLabel/FloatLabel) — responsive label strategy

### Library / framework requirements

| Layer | Package | Version |
|---|---|---|
| Framework | Angular | 21 (Signal Forms) |
| UI kit | PrimeNG | 21 (`<p-iftaLabel>`, `<p-floatLabel>`, `<p-select>`) |
| Breakpoint observer | @angular/cdk | 21 |
| Backend timezone | TimeZoneConverter | latest stable (if cross-OS) |

### Testing requirements

- **Unit**: `applyServerErrors`, `IdempotencyKeyInterceptor`, Signal Forms integration
- **Integration**: PatchUserEndpointTests — happy + idempotent + negative + timing
- **E2E**: Profile edit flow in LTR + RTL
- **Visual regression**: 3 breakpoints × 2 directions baselined

### Previous Story Intelligence

**Prerequisites:**
- **Epic 0 Story 0.5** — Idempotency-Key middleware
- **Story 1.1** — Transloco + design tokens
- **Story 1.2** — User bootstrap via Auth0 Action
- **Story 1.3** — `PATCH /v1/users/me` scaffold already started (for locale); this story extends
- **Story 1.9** — Phase-1 component API (form components consumed from `libs/design-system`)
- **Story 1.10** — a11y gates
- **Story 1.11** — perf + visual regression gates

**Downstream:**
- Story 1.7 (deletion) navigates from `/profile` to `/profile/delete`
- Epic 2+ forms reuse `applyServerErrors` + `FormMutationConfig`

### Latest Technical Information

**Angular Signal Forms (Angular 21):**
- Built on signals, no FormControl/FormGroup legacy
- `form<T>(initialValue)` creates a reactive form state
- `form.value()` reads, `form.patch({...})` updates
- Integrates with `rxResource()` for async validation

**PrimeNG v21 label components:**
- `<p-iftaLabel><input/><label/></p-iftaLabel>` — inline-filled label
- `<p-floatLabel variant="on"><input/><label/></p-floatLabel>` — floating label

**Angular CDK BreakpointObserver:**
```ts
const isDesktop = toSignal(
  breakpointObserver.observe('(min-width: 1024px)').pipe(map(s => s.matches))
);
```

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Story 1.5
- FR5, NFR-P4 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- §4 D4.11 Idempotency — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4 D4.11
- §4 D4.9 RFC 9457 — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4 D4.9
- §5.3.1 DTO naming — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.3.1
- UX-DR27 silent success, UX-DR28 labels — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) UX Consistency Patterns
- Story 1.1 — [_bmad-output/implementation-artifacts/1-1-bilingual-shell-design-tokens-rtl-pipeline.md](./1-1-bilingual-shell-design-tokens-rtl-pipeline.md)
- Story 1.3 — [_bmad-output/implementation-artifacts/1-3-user-login-language-selection.md](./1-3-user-login-language-selection.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- First real Signal Forms + FormMutationConfig slice; reusable helpers land in `libs/core/src/forms/`
- Silent success per UX-DR27; responsive label strategy per UX-DR28
- Email change delegated to Auth0 profile UI; locale syncs to Auth0 Management API best-effort

### File List

_Populated during implementation — see Source tree section above._
