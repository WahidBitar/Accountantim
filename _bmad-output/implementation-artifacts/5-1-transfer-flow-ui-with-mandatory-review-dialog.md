# Story 5.1: Transfer flow UI with mandatory review dialog

Status: ready-for-dev

## Story

As **a user who wants to reassign a debt from Contact A to Contact B**,
I want **a guided flow that shows me the source debt + target contact + amount + review summary before confirming**,
so that **this irreversible compound operation feels safe** (FR24 UI portion, UX-DR29).

**Covers requirements:** Epic 5 Story 5.1 — FR24 (UI portion of "user can transfer all or part of a debt"); UX-DR29 (review-dialog discipline — Transfer is the ONE routine action that shows a confirmation dialog); ux-design-specification Flow 4 [Source: epics.md Epic 5 Story 5.1; prd.md FR24; ux-design-specification.md §Flow 4 Debt Transfer].

**Why this story now:** Epic 5 opens with the UI because the form exercise pins down: (a) the request shape Story 5.2's backend will accept, (b) the validation rules that travel from Signal Forms client hints to FluentValidation server-authoritative checks, and (c) the review-dialog discipline (UX-DR29) that is unique to Transfer in the entire product. Story 5.2 is the AR-049 first real consumer of `ICompoundJournalEntry`; that work is materially clearer once the request DTO is shaped by the UI.

## Acceptance Criteria

### AC1 — Transfer entry point on debt detail page

**Given** an authenticated user on a debt detail page
**When** the user taps "نقل الدَّين" (Transfer Debt)
**Then** the Transfer form opens (drawer or full-page route — drawer per UX-DR29)
**And** the source debt is pre-populated (debt ID + contact + remaining balance + denomination, all read-only)
**And** the entry point is also reachable from the contact detail page when the contact has ≥1 active debt [Source: epics.md Epic 5 Story 5.1 AC; ux-design-specification.md Flow 4].

### AC2 — Form fields: full/partial → target contact → optional note

**Given** the Transfer drawer is open
**When** the user fills the form
**Then** the form asks (in order):
1. Full or partial transfer (radio; default = full; partial reveals amount input pre-filled with current balance)
2. Target contact (PrimeNG `<p-autoComplete>` searching existing contacts; allows inline new-contact creation per UX-DR autocomplete-before-creation pattern)
3. Optional explanation note (PrimeNG `<p-inputTextarea>`, max 280 chars)
**And** the form follows UX-DR28 form patterns (`<p-iftaLabel>` ≥1024px / stacked `<p-floatLabel variant="on">` <1024px)
**And** no red asterisks (required is default; optional fields explicitly labeled "(optional)") [Source: epics.md Epic 5 Story 5.1 AC; ux-design-specification.md Flow 4].

### AC3 — Inline validation: target = source blocks with error

**Given** the user selects the same contact as the source debt's contact
**When** the autocomplete resolves to the source contact ID
**Then** an inline error appears immediately under the target field: "Cannot transfer to same party" (Transloco-keyed)
**And** the form's submit button is disabled while this error is present
**And** the error clears when the user picks a different target [Source: epics.md Epic 5 Story 5.1 AC].

### AC4 — Inline validation: partial amount > remaining balance blocks with error

**Given** the user chose "partial" and entered an amount
**When** the amount > the source debt's remaining balance
**Then** an inline error appears under the amount field: "Amount exceeds balance" (Transloco-keyed)
**And** the submit button is disabled while this error is present
**And** the validation is denomination-aware — it compares `Money` values via `libs/core/money` helpers, NOT raw numeric subtraction [Source: epics.md Epic 5 Story 5.1 AC; architecture.md §5.2.5].

### AC5 — Review dialog: plain-language summary before commit

**Given** the user has filled valid form values
**When** the user taps "Continue"
**Then** a PrimeNG `<p-dialog>` opens with `modal=true`, `closable=false` (force review or cancel — no escape-by-X)
**And** the dialog body reads (Transloco-keyed; example AR + EN):
- AR: "[سعد] يقفل 200 جنيه — [علي] يفتح 200 جنيه — الطرفان يُخطَران إذا كانت العلاقة مشتركة"
- EN: "[Saad] closes 200 EGP — [Ali] opens 200 EGP — Both sides notified if shared"
**And** the dialog uses ONLY plain language; the strings "debit", "credit", "journal entry" are FORBIDDEN in user-facing copy (ESLint rule `no-accounting-jargon-in-i18n` checks `assets/i18n/`)
**And** the dialog shows two buttons: "Confirm" (primary) + "Cancel" (text)
**And** tapping "Cancel" closes the dialog and returns to the form preserving all field values (NO side effects)
**And** tapping "Confirm" submits via `POST /v1/journal-entries/transfer` (Story 5.2's endpoint) with `Idempotency-Key` header [Source: epics.md Epic 5 Story 5.1 AC; ux-design-specification.md Flow 4 design notes].

### AC6 — Review dialog is the ONE routine-action confirmation in Faktuboh

**Given** UX-DR29 dictates Transfer is the only routine action with a review dialog
**When** any other Epic 5/6 story considers adding a confirmation dialog
**Then** Story 5.2 (Transfer backend) does NOT show a dialog (silent execution post-confirm)
**And** Story 6.4 (Exchange) shows its rate-lock confirmation dialog ONLY because it carries the same compound + irreversible weight as Transfer
**And** Story 5.4 (Transfer history view) — read-only, no dialog
**And** Story 5.5 (Partial-transfer support) — same flow as 5.1, same single dialog
**And** an architectural ESLint rule `single-routine-confirmation-dialog` flags any new `<p-dialog>` use on a routine-write endpoint outside of Transfer + Exchange [Source: ux-design-specification.md Flow 4 + UX-DR27 silent-success rule].

### AC7 — On Confirm: silent success per UX-DR27 (post-dialog)

**Given** the dialog has shown its confirmation
**When** the backend returns 201 from `POST /v1/journal-entries/transfer`
**Then** the dialog closes silently
**And** the drawer closes silently
**And** the user lands on the source-debt detail page (now showing the AuditBadge "Transferred to [target]" — Story 5.3 ships the badge)
**And** NO success toast fires — the new badge appearing IS the feedback
**And** if the backend returns RFC 9457 error, the dialog stays open + the error renders inline below the dialog body via `<p-message severity="error">`; the form is still submittable after error correction [Source: epics.md Epic 5 Story 5.1; ux-design-specification.md UX-DR27].

### AC8 — Optimistic loading state on Confirm

**Given** the network round-trip to `POST /v1/journal-entries/transfer` may take 100-500ms
**When** the user taps "Confirm"
**Then** the Confirm button shows `loading=true` with a spinner
**And** the button width is frozen (no layout shift)
**And** the dialog body becomes non-interactive (focus-trapped on the spinner)
**And** the Cancel button is disabled during the in-flight period
**And** if the request takes >2s, an inline `<p-message severity="info">` appears: "Still working..." (Transloco-keyed) per UX-DR31 [Source: ux-design-specification.md UX-DR31 loading states].

### AC9 — A11y: dialog is screen-reader announced + focus trapped

**Given** PrimeNG `<p-dialog>` has built-in dialog semantics
**When** the dialog opens
**Then** `aria-modal="true"` is set
**And** focus moves to the Confirm button (or to the first interactive element if Confirm is disabled — never to body)
**And** Escape key triggers Cancel (NOT close-without-cancel — Cancel is the explicit user action)
**And** axe-core reports zero WCAG 2.1 AA violations on the dialog (CI gate per Story 1.10)
**And** screen-reader test pass on VoiceOver Arabic + TalkBack Arabic per NFR-A9

### AC10 — Telemetry: `transfer_dialog_opened`, `transfer_dialog_confirmed`, `transfer_dialog_cancelled`

**Given** AR-047 telemetry primitive is wired
**When** the dialog opens, confirms, or cancels
**Then** three events emit:
- `transfer_dialog_opened` — `{ source_contact_id_hash, has_partial_amount: bool }`
- `transfer_dialog_confirmed` — `{ source_contact_id_hash, target_contact_id_hash, amount_bucket, denomination }`
- `transfer_dialog_cancelled` — `{ source_contact_id_hash, time_in_dialog_ms }`
**And** no PII (raw contact IDs, names, exact amounts) appears
**And** `amount_bucket` rounds to OOM (`<10`, `10-100`, `100-1k`, `>1k`) [Source: Story 0.14 telemetry primitive].

## Tasks / Subtasks

### Frontend — Form scaffold

- [ ] **T1 (AC: 1)** — Add Transfer entry-point button on debt detail + contact detail pages
  - [ ] Edit `apps/owner-workspace/src/app/features/debts/debt-detail/debt-detail.component.html`
  - [ ] Edit `apps/owner-workspace/src/app/features/contacts/contact-detail/contact-detail.component.html`
  - [ ] Button uses Transloco key `transfer.action.start`
  - [ ] Visible only when source debt is active (not closed/transferred-out)
- [ ] **T2 (AC: 1, 2)** — Create `apps/owner-workspace/src/app/features/transfers/transfer-form/transfer-form.component.ts`
  - [ ] Standalone, OnPush, signals
  - [ ] Selector `ow-transfer-form`
  - [ ] Lazy route `/contacts/:id/debts/:debtId/transfer` (drawer-style)
  - [ ] Signal Form via `FormMutationConfig`
  - [ ] Pre-populates source debt context
- [ ] **T3 (AC: 2)** — Form fields per UX-DR28
  - [ ] Radio: full | partial (default full)
  - [ ] Conditional: amount input via `libs/core/money` `<denom-input>` helper (only when partial)
  - [ ] Autocomplete: `<p-autoComplete>` on contacts; inline new-contact creation chip
  - [ ] Textarea: optional note (max 280)
  - [ ] No red asterisks; "(optional)" labels on note

### Frontend — Validation

- [ ] **T4 (AC: 3)** — Client-side validation: target = source
  - [ ] Computed signal `targetEqualsSource` returning bool
  - [ ] Inline error renders when true; submit disabled
- [ ] **T5 (AC: 4)** — Client-side validation: partial amount > remaining
  - [ ] Use `libs/core/money` `compareMoney(amount, remaining)` helper (denomination-aware)
  - [ ] Inline error renders when amount > remaining; submit disabled
  - [ ] NO raw arithmetic; ESLint `no-raw-money-arithmetic` already enforces (per architecture §5.2.5)

### Frontend — Review dialog

- [ ] **T6 (AC: 5, 6, 9)** — Create `apps/owner-workspace/src/app/features/transfers/transfer-review-dialog/transfer-review-dialog.component.ts`
  - [ ] Standalone, OnPush, signals
  - [ ] PrimeNG `<p-dialog modal="true" closable="false">`
  - [ ] Body uses Transloco keys `transfer.review.body.full` + `transfer.review.body.partial` with `[source]`, `[target]`, `[amount]` interpolation
  - [ ] Two buttons: Confirm (primary) + Cancel (text)
  - [ ] Confirm button calls `transfersClient.execute(request)` (NSwag-generated client)
  - [ ] Cancel returns to form preserving values
- [ ] **T7 (AC: 5)** — Add ESLint rule `no-accounting-jargon-in-i18n`
  - [ ] Custom ESLint rule scanning `assets/i18n/{ar,en}.json`
  - [ ] Forbidden tokens: `debit`, `credit`, `journal entry`, `مدين`, `دائن`, `قيد محاسبي`
  - [ ] Allows them ONLY in dev-tooling files (test fixtures, ADR markdown)

### Frontend — Submission flow

- [ ] **T8 (AC: 7, 8)** — Wire Confirm → API call → silent success
  - [ ] On 201, close dialog + drawer; navigate to source debt detail
  - [ ] No toast emission (UX-DR27)
  - [ ] On RFC 9457 error, render inline `<p-message severity="error">` in dialog; do NOT close
  - [ ] Loading state with frozen-width Confirm button
  - [ ] "Still working..." after 2s
  - [ ] `Idempotency-Key` UUID v4 generated on form mount, refreshed on success/reset

### Frontend — A11y

- [ ] **T9 (AC: 9)** — A11y verifications
  - [ ] Focus trap on dialog (PrimeNG default)
  - [ ] Escape key triggers Cancel handler
  - [ ] aria-modal="true"
  - [ ] axe-core CI gate per Story 1.10

### Frontend — Architectural enforcement

- [ ] **T10 (AC: 6)** — Add ESLint rule `single-routine-confirmation-dialog`
  - [ ] Custom ESLint rule scanning for `<p-dialog>` usage
  - [ ] Allowlists: `transfer-review-dialog`, `exchange-rate-lock-dialog` (Story 6.4)
  - [ ] Fails build if any other component invokes `<p-dialog>` on a routine-write endpoint

### Frontend — Telemetry

- [ ] **T11 (AC: 10)** — Wire 3 telemetry events per AC10
  - [ ] PII guard
  - [ ] amount_bucket helper

### Tests — Frontend

- [ ] **T12 (AC: 1-9)** — Vitest tests in `transfer-form.component.spec.ts`
  - [ ] Form renders + fields visible
  - [ ] Target = source validation
  - [ ] Partial amount > remaining validation
  - [ ] Submit disabled when invalid
- [ ] **T13 (AC: 5, 7)** — Vitest tests in `transfer-review-dialog.component.spec.ts`
  - [ ] Dialog opens with summary
  - [ ] Confirm calls API; success closes dialog
  - [ ] Cancel returns to form preserving values
  - [ ] RFC 9457 error renders inline; dialog stays open
- [ ] **T14 (AC: 1-10)** — Playwright E2E in `apps/owner-workspace/e2e/transfers/transfer-flow.spec.ts`
  - [ ] Happy path: open form, fill, review, confirm, see badge on debt detail
  - [ ] Validation paths: target=source error, exceeds-balance error
  - [ ] Cancel preserves form state
  - [ ] axe-core a11y assertion (LTR + RTL)

## Dev Notes

### Critical guardrails (do not violate)

1. **Transfer is the ONLY routine action with a confirmation dialog** (UX-DR29). The ESLint rule `single-routine-confirmation-dialog` enforces this. Adding a dialog elsewhere (e.g., Delete Debt) requires either an architectural exception ADR or a UX-spec amendment.
2. **No accounting jargon in user-facing copy.** "debit/credit/journal entry" appears in code (Story 5.2 backend) but never in i18n bundles. The ESLint rule `no-accounting-jargon-in-i18n` enforces.
3. **Cancel preserves form state.** No side effects, no field clearing. The user might tap Cancel to amend the note then retry.
4. **Silent success per UX-DR27.** No success toast on Confirm. The badge appearing on the debt detail IS the feedback.
5. **Dialog is `closable=false`.** The X button would create a third state (closed-without-decision) that breaks the explicit Confirm/Cancel discipline.
6. **`Idempotency-Key` refresh on success/reset.** Per the global rule from Story 2.1.
7. **Money validation via `libs/core/money` helpers.** Raw arithmetic on amount fields is ESLint-forbidden per architecture §5.2.5.
8. **The dialog is the UI-only piece.** This story does not implement the backend (`POST /v1/journal-entries/transfer`) — that's Story 5.2. The UI calls a stub or a generated client that 404s until Story 5.2 ships.

### Source tree — files to create or touch

```
apps/owner-workspace/
├── src/app/features/transfers/
│   ├── transfer-form/
│   │   ├── transfer-form.component.ts                     # NEW
│   │   ├── transfer-form.component.html                   # NEW
│   │   ├── transfer-form.component.scss                   # NEW (logical props)
│   │   └── transfer-form.component.spec.ts                # NEW
│   ├── transfer-review-dialog/
│   │   ├── transfer-review-dialog.component.ts            # NEW
│   │   ├── transfer-review-dialog.component.html          # NEW
│   │   └── transfer-review-dialog.component.spec.ts       # NEW
│   └── transfers.routes.ts                                # NEW (lazy route)
├── src/app/features/debts/debt-detail/debt-detail.component.html  # MODIFY (transfer entry-point)
├── src/app/features/contacts/contact-detail/contact-detail.component.html  # MODIFY (transfer entry-point)
├── src/assets/i18n/ar.json                                # MODIFY (transfer.* keys)
├── src/assets/i18n/en.json                                # MODIFY (transfer.* keys)
└── e2e/transfers/transfer-flow.spec.ts                    # NEW

libs/core/api/
└── transfers.client.ts                                    # NEW (NSwag-generated wrapper for POST /v1/journal-entries/transfer)

eslint-rules/
├── no-accounting-jargon-in-i18n.js                        # NEW
└── single-routine-confirmation-dialog.js                  # NEW
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit/Component | Form fields + validation + dialog states | Vitest + Angular Testing Library |
| E2E | Full transfer flow (LTR + RTL + a11y) | Playwright Chromium + WebKit |
| Architectural | ESLint rules `no-accounting-jargon-in-i18n` + `single-routine-confirmation-dialog` | ESLint + CI gate |

### Project Structure Notes

This story scaffolds the `transfers` feature folder under `apps/owner-workspace/src/app/features/`. Stories 5.4 (history view) and 5.5 (partial transfer) add sibling components (`transfer-history-list/`, no new component for 5.5 — same form). The route is `/transfers/*` with a child route `/contacts/:id/debts/:debtId/transfer` for the form drawer.

### Architecture compliance

- **§5.4 (frontend)** — zoneless, signal-based, OnPush.
- **UX-DR27** — silent success.
- **UX-DR28** — form patterns (IftaLabel ≥1024px / FloatLabel <1024px; no red asterisks).
- **UX-DR29** — confirmation dialog discipline; Transfer is the one routine action.
- **UX-DR31** — loading states; frozen-width buttons; "Still working..." after 2s.
- **§5.2.5** — Money validation via helpers, no raw arithmetic.
- **NFR-A9** — RTL acceptance + screen-reader pass.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Framework | Angular 21 | Zoneless, signals, standalone |
| UI | PrimeNG 21 | `<p-dialog>`, `<p-autoComplete>`, `<p-button>`, `<p-inputTextarea>`, `<p-message>` |
| Forms | Signal Forms | Per Story 1.1 + architecture D4.14 |
| i18n | Transloco | Per Story 1.1 |
| HTTP client | NSwag-generated `transfers.client.ts` | Per delegated decision |
| Test (FE) | Vitest + Angular Testing Library | |
| E2E | Playwright | Chromium + WebKit + axe-core |

### Testing requirements

**Test pyramid:**
- **Unit/Component:** form rendering, validation logic, dialog open/close, error rendering — Vitest.
- **E2E:** full happy-path + validation paths + cancel flow + a11y — Playwright.
- **Architectural:** two ESLint rules + bundle import-graph check (no `apps/public-statement` imports here).

### Previous Story Intelligence

**Prerequisites:**
- Story 1.1 — Transloco + RTL pipeline + Signal Forms config
- Story 1.9 — Phase-1 component API (`<p-dialog>` patterns referenced)
- Story 1.10 — axe-core CI gate
- Story 2.1 — `Idempotency-Key` UUID v4 generation discipline
- Story 2.2-2.5 — Contacts CRUD + autocomplete primitives
- Story 3.1 — Record New Debt (the form pattern this story follows)
- Story 3.4 — debt detail page (where the entry-point button lives)
- Story 0.15 — `ICompoundJournalEntry` interface (consumed by Story 5.2's backend, not directly here)
- Story 0.14 — telemetry primitive

**Stub coordination with Story 5.2:**
- This story expects `POST /v1/journal-entries/transfer` to accept `{ sourceDebtId, targetContactId, amount?, note?, idempotencyKey }` and return 201 with `{ correlationId, sourceClosedAt, targetOpenedAt }`.
- If Story 5.2 ships AFTER this UI, the form posts to a Mock Service Worker handler in dev + integration tests; production wires through to the real endpoint when 5.2 lands.

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 5 Story 5.1
- Flow 4 (Transfer) — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) §Flow 4
- UX-DR27 silent success, UX-DR28 form patterns, UX-DR29 dialog discipline, UX-DR31 loading — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md)
- FR24 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Money helpers — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.2.5
- Story 5.2 backend — [_bmad-output/implementation-artifacts/5-2-atomic-compound-journal-entry-transfer.md](./5-2-atomic-compound-journal-entry-transfer.md)
- Story 5.3 AuditBadge — [_bmad-output/implementation-artifacts/5-3-auditbadge-component-with-bidirectional-navigation.md](./5-3-auditbadge-component-with-bidirectional-navigation.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Transfer is the ONE routine action that shows a review dialog (UX-DR29) — enforced architecturally via ESLint rule.
- "debit/credit/journal entry" forbidden in user-facing copy — enforced via ESLint rule on i18n bundles.
- Cancel preserves form state; Confirm submits via Idempotency-Key; silent success on 201.

### File List

_Populated during implementation._
