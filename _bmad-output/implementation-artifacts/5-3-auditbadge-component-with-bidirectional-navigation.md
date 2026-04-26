# Story 5.3: AuditBadge component with bidirectional navigation

Status: ready-for-dev

## Story

As **a user viewing a transferred debt**,
I want to **see a clickable badge "Transferred from [source contact]" or "Transferred to [target contact]" that navigates me to the linked record**,
so that **I can trace the transfer chain from either side** (FR25 UX, UX-DR15).

**Covers requirements:** Epic 5 Story 5.3 — FR25 (linked transactions with audit trail UX); UX-DR15 (AuditBadge component spec); ux-design-specification §11 [Source: epics.md Epic 5 Story 5.3; prd.md FR25; ux-design-specification.md §11 AuditBadge].

**Why this story now:** Stories 5.1 and 5.2 ship the Transfer flow + backend. This story adds the visible audit linkage that closes the loop on FR25. It is also the **single-owner component story** for `AuditBadge` — the component API is owned here and consumed unchanged by Stories 5.4 (Transfer history view), 6.4 (Exchange operation), and 6.5 (Exchange history). Per UX-DR15 + the `libs/domain-ui` single-owner rule, no other story may add inputs/outputs to this component without revisiting this story.

## Acceptance Criteria

### AC1 — Component lives in `libs/domain-ui` with frozen public API

**Given** UX-DR15 specifies the AuditBadge component API
**When** this story implements the component
**Then** `libs/domain-ui/src/lib/audit-badge/audit-badge.component.ts` exists as a standalone Angular component with selector `du-audit-badge`
**And** the public API matches ux-design-specification §11 exactly:

```ts
linkedRecordId = input.required<string>();
kind = input.required<'transfer-from' | 'transfer-to' | 'exchange-prior' | 'exchange-derived'>();
label = input.required<string>();  // pre-computed short label by caller
```

**And** the component is exported via `libs/domain-ui/src/public-api.ts`
**And** any addition of inputs/outputs requires a Story 5.3 amendment + UX-DR15 amendment + Wahid sign-off [Source: ux-design-specification.md §11; epics.md Epic 5 Story 5.3].

### AC2 — Renders as anchor wrapping PrimeNG Tag

**Given** ux-design-specification §11 anatomy
**When** the component renders
**Then** the DOM is `<a [routerLink]="..."><p-tag severity="info" [icon]="..." [value]="label()" /></a>`
**And** the outer `<a>` carries the click + focus semantics (PrimeNG `<p-tag>` is display-only)
**And** the icon is `pi-link` (direction-neutral) per UX-DR15 — NEVER `↔`, `→`, or `←` glyphs (RTL/LTR confusion) [Source: ux-design-specification.md §11; epics.md Epic 5 Story 5.3 AC].

### AC3 — Direction-neutral icon: `pi-link`

**Given** the four `kind` values map to two relationship types: transfer (from/to) + exchange (prior/derived)
**When** rendering
**Then** all four kinds use the SAME icon (`pi-link`)
**And** the directional meaning lives in the `label` text (provided by caller, Transloco-keyed)
**And** an ESLint rule `no-directional-glyphs-in-audit-badge` flags any literal `↔`/`→`/`←`/`⇄` character in `audit-badge.component.html` or in any caller's `label` input
**And** unit test asserts identical icon across the 4 kinds [Source: epics.md Epic 5 Story 5.3 AC; ux-design-specification.md §11 RTL notes].

### AC4 — Bidirectional navigation via `routerLink`

**Given** each AuditBadge points to a peer record
**When** the user taps the badge
**Then** Angular Router navigates to the linked debt's detail page
**And** the route resolves the `linkedRecordId` to the correct page:
- `transfer-from` / `transfer-to` → `/contacts/:contactId/debts/:debtId`
- `exchange-prior` / `exchange-derived` → `/contacts/:contactId/debts/:debtId` (same route shape)
**And** caller is responsible for passing the resolved router-link path; the component itself does not encode route shape (decouples component from routing changes)
**And** an integration test asserts: from source debt → tap badge → land on target debt → tap badge → return to source [Source: epics.md Epic 5 Story 5.3 AC].

**Component prop refinement:** instead of `linkedRecordId: string`, accept `linkedRoute: any[]` for `routerLink` directly:

```ts
linkedRoute = input.required<any[]>();  // e.g., ['/contacts', contactId, 'debts', debtId]
kind = input.required<'transfer-from' | 'transfer-to' | 'exchange-prior' | 'exchange-derived'>();
label = input.required<string>();
```

**Decision flag:** the ux-design-specification §11 prop is `linkedRecordId: string`; this story upgrades to `linkedRoute: any[]` to keep the component agnostic of route shape. UX-DR15 amendment proposed inline in this story; revert to `linkedRecordId` if Wahid prefers component-owned routing.

### AC5 — `aria-label` announces direction in natural language

**Given** screen-reader users need direction context
**When** the badge renders
**Then** `aria-label` is built from the `label` input + a kind-specific prefix:
- `transfer-from` → "View original debt with [Ali] that was transferred from"
- `transfer-to` → "View new debt with [Ali] that was transferred to"
- `exchange-prior` → "View prior denomination of this exchange with [Ali]"
- `exchange-derived` → "View derived denomination of this exchange with [Ali]"
**And** the prefix strings are Transloco-keyed (`audit-badge.aria.{kind}`) and interpolated with the `label` value
**And** the role is implicit `link` (default for `<a href>`)
**And** axe-core CI gate (Story 1.10) passes on every consuming page [Source: ux-design-specification.md §11 accessibility; epics.md Epic 5 Story 5.3 AC].

### AC6 — Focus ring on outer anchor per `--focus-ring` token

**Given** Story 1.1 ships the `--focus-ring` design token
**When** the badge receives keyboard focus (Tab navigation)
**Then** the outer `<a>` shows the focus ring per token
**And** the inner `<p-tag>` does NOT receive focus styling (display-only)
**And** the focus ring is visible in both LTR and RTL layouts
**And** Playwright keyboard-navigation test asserts focus appearance [Source: ux-design-specification.md §11; Story 1.1 design tokens].

### AC7 — Broken-link defensive state (display-only fallback)

**Given** the linked record may be unreachable (e.g., post-deletion regression — MVP prevents but defense-in-depth)
**When** the linked record returns 404 from a HEAD probe (or the parent component passes `disabled=true`)
**Then** the badge renders as plain text with strikethrough; no `<a>` wrapper, no navigation
**And** the strikethrough state has `aria-label="Linked record no longer available"`
**And** an additional optional input `disabled = input(false)` controls this state (caller-provided)
**And** the broken state is exercised by a unit test [Source: ux-design-specification.md §11 broken state].

### AC8 — Mounted on transferred-debt rows in both directions

**Given** Story 5.2 produced 2 linked `journal_entries` rows sharing a `correlation_id`
**When** either side's debt detail page renders
**Then** the source side shows `<du-audit-badge kind="transfer-to" label="{transloco('audit-badge.label.transfer-to', { contact: targetContactName })}}" />`
**And** the target side shows `<du-audit-badge kind="transfer-from" label="..." />`
**And** the badge sits in the debt-detail page header area (consistent placement)
**And** caller integration is wired in `apps/owner-workspace/src/app/features/debts/debt-detail/debt-detail.component.html`

### AC9 — Single-owner discipline enforced

**Given** UX-DR15 + `libs/domain-ui` single-owner rule
**When** any future story attempts to add inputs/outputs to this component
**Then** the build fails via NgArchTest equivalent (custom ESLint AST rule on the component's input declarations)
**And** the rule allowlists the 4 inputs declared in this story (`linkedRoute`, `kind`, `label`, `disabled`)
**And** any new input requires editing this story file + UX-DR15 file + the allowlist [Source: epics.md Epic 5 Story 5.3 + Phase-1 component API rule (Story 1.9)].

### AC10 — Used in 4 places by end of MVP

**Given** the component will be consumed by:
1. Source-debt detail page (Story 5.2 / 5.3)
2. Target-debt detail page (Story 5.2 / 5.3)
3. Transfer history list rows (Story 5.4)
4. Exchange operation result (Story 6.4)
5. Exchange history view (Story 6.5)

**When** this story ships, the first two consumers are wired
**And** Stories 5.4 + 6.4 + 6.5 consume unchanged
**And** the component's API surface holds across all five usages without modification [Source: epics.md Epic 5 + Epic 6].

## Tasks / Subtasks

### Component implementation

- [ ] **T1 (AC: 1, 2, 3)** — Create `libs/domain-ui/src/lib/audit-badge/audit-badge.component.ts`
  - [ ] Standalone, OnPush, signals
  - [ ] Selector `du-audit-badge`
  - [ ] Inputs: `linkedRoute`, `kind`, `label`, `disabled`
  - [ ] Template: `<a [routerLink]="linkedRoute()"><p-tag icon="pi pi-link" severity="info" [value]="label()" /></a>`
  - [ ] Conditional template branch for `disabled` state (plain text with strikethrough)
- [ ] **T2 (AC: 5)** — `aria-label` computed signal
  - [ ] `computed(() => transloco.translate('audit-badge.aria.' + kind(), { label: label() }))`
- [ ] **T3 (AC: 6)** — SCSS with `--focus-ring` token applied to outer anchor on `:focus-visible`

### Component public API

- [ ] **T4 (AC: 1)** — Export from `libs/domain-ui/src/public-api.ts`
  - [ ] `export { AuditBadgeComponent } from './lib/audit-badge/audit-badge.component';`
  - [ ] Public-API barrel rule: only intentional surface exposed

### i18n bundles

- [ ] **T5 (AC: 5)** — Add Transloco keys
  - [ ] `audit-badge.aria.transfer-from`, `audit-badge.aria.transfer-to`, `audit-badge.aria.exchange-prior`, `audit-badge.aria.exchange-derived`
  - [ ] `audit-badge.label.transfer-from`, `.transfer-to`, `.exchange-prior`, `.exchange-derived` — short labels with `{contact}` interpolation
  - [ ] Both `apps/owner-workspace/src/assets/i18n/{ar,en}.json`

### Architectural enforcement

- [ ] **T6 (AC: 3)** — ESLint rule `no-directional-glyphs-in-audit-badge`
  - [ ] Custom ESLint rule scanning `libs/domain-ui/src/lib/audit-badge/**/*` + any caller passing `label` input literal
  - [ ] Forbidden chars: `↔`, `→`, `←`, `⇄`, `↦`, `↤`
- [ ] **T7 (AC: 9)** — ESLint rule `audit-badge-single-owner`
  - [ ] Custom AST rule on `audit-badge.component.ts` input declarations
  - [ ] Allowlist: `linkedRoute`, `kind`, `label`, `disabled`
  - [ ] Fails build on any other input addition

### Caller wiring (consumption examples)

- [ ] **T8 (AC: 8, 10)** — Wire AuditBadge into source-debt detail
  - [ ] Modify `apps/owner-workspace/src/app/features/debts/debt-detail/debt-detail.component.html`
  - [ ] Read `correlationId` from journal entry; query peer leg via `transfersClient.getPeerLeg(correlationId)`; render badge with `kind="transfer-to"` when this side is source
- [ ] **T9 (AC: 8, 10)** — Wire AuditBadge into target-debt detail
  - [ ] Same component, render `kind="transfer-from"` when this side is target

### Backend support (peer-leg lookup)

- [ ] **T10 (AC: 4, 8)** — Add `GET /v1/journal-entries/{id}/peer-leg` endpoint
  - [ ] Resolves the journal entry's `correlation_id` → returns the OTHER leg's `{ contactId, debtId, contactName }`
  - [ ] Returns 404 if no peer (single-leg journal entry, e.g., a non-transfer)
  - [ ] Response shape used by frontend to construct `linkedRoute`

### Tests

- [ ] **T11 (AC: 1-7)** — Vitest tests in `libs/domain-ui/src/lib/audit-badge/audit-badge.component.spec.ts`
  - [ ] All 4 `kind` values render correctly
  - [ ] Icon is always `pi pi-link`
  - [ ] `aria-label` interpolates correctly per kind
  - [ ] Disabled state renders plain text with strikethrough (no `<a>`)
  - [ ] Focus ring applies on `:focus-visible`
- [ ] **T12 (AC: 4)** — Vitest test: routerLink resolution + click navigates
- [ ] **T13 (AC: 8, 10)** — Playwright E2E in `apps/owner-workspace/e2e/transfers/audit-badge-navigation.spec.ts`
  - [ ] Source debt → tap badge → lands on target debt
  - [ ] Target debt → tap badge → returns to source
  - [ ] Keyboard navigation (Tab to focus, Enter to activate)
  - [ ] axe-core: zero WCAG 2.1 AA violations
  - [ ] RTL parity test
- [ ] **T14 (AC: 9)** — Architectural test for single-owner rule
  - [ ] Custom ESLint rule test fixture (allowed: 4 declared inputs; rejected: a 5th input)

## Dev Notes

### Critical guardrails (do not violate)

1. **Single-owner discipline.** AuditBadge's API is OWNED here. No other story may add inputs/outputs without amending this story + UX-DR15. Per the Phase-1 component API rule (Story 1.9).
2. **Direction-neutral icon (`pi-link`).** Glyphs `↔`/`→`/`←` are FORBIDDEN — they confuse RTL/LTR. The directional meaning lives in the label text. ESLint rule enforces.
3. **Display-only `<p-tag>`; navigation on outer `<a>`.** PrimeNG `<p-tag>` does not have built-in click semantics; wrapping it in `<a routerLink>` gives correct keyboard activation + focus ring.
4. **Caller provides resolved `linkedRoute` array.** The component does not know about route shape — Stories 5.2/5.4/6.4/6.5 each pass their own router-link arrays. This decouples the component from routing changes.
5. **`pre-computed short label by caller`** per ux-design-specification §11. The component does not interpolate contact names into the label; the caller does (gives caller control over truncation + locale-specific name formatting).
6. **Broken state is defensive only.** MVP prevents deletion of linked records; the strikethrough state is for unforeseen regressions.
7. **Component lives in `libs/domain-ui`, not `libs/design-system` and not in any feature folder.** Per architecture §6.4.4 + §6.4.6: `libs/design-system` is generic primitives, `libs/domain-ui` is product-domain components.

### Source tree — files to create or touch

```
libs/domain-ui/
├── src/
│   ├── public-api.ts                                      # MODIFY (export AuditBadgeComponent)
│   └── lib/audit-badge/
│       ├── audit-badge.component.ts                       # NEW
│       ├── audit-badge.component.html                     # NEW
│       ├── audit-badge.component.scss                     # NEW (logical props + --focus-ring)
│       └── audit-badge.component.spec.ts                  # NEW

apps/owner-workspace/
├── src/app/features/debts/debt-detail/
│   ├── debt-detail.component.html                         # MODIFY (mount AuditBadge)
│   └── debt-detail.component.ts                           # MODIFY (wire peer-leg lookup)
├── src/assets/i18n/ar.json                                # MODIFY (audit-badge.* keys)
├── src/assets/i18n/en.json                                # MODIFY
└── e2e/transfers/audit-badge-navigation.spec.ts           # NEW

backend/src/Faktuboh.Api/Slices/JournalEntries/PeerLeg/
├── PeerLeg.Endpoints.cs                                   # NEW (GET /v1/journal-entries/{id}/peer-leg)
├── GetPeerLegResponse.cs                                  # NEW
└── GetPeerLegHandler.cs                                   # NEW

eslint-rules/
├── no-directional-glyphs-in-audit-badge.js                # NEW
└── audit-badge-single-owner.js                            # NEW
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit/Component | All 4 kinds, disabled state, aria-label, focus ring | Vitest + Angular Testing Library |
| E2E | Bidirectional navigation + a11y + RTL parity | Playwright |
| Architectural | Single-owner rule + no-directional-glyphs | Custom ESLint rules |

### Project Structure Notes

This story is the canonical example of the `libs/domain-ui` single-owner rule established by Story 1.9 (Phase-1 component API). The pattern: one story declares the component + its API + its enforcement; subsequent consumers reference unchanged. Stories 5.4, 6.4, 6.5 will mount this component without modifying it.

### Architecture compliance

- **§6.4.6** — `libs/domain-ui/` location (product-domain components).
- **§5.4 + UX-DR15** — component API.
- **Story 1.9** — Phase-1 component API single-owner rule.
- **§5.4 zoneless** — signals, OnPush, no Zone.js.
- **NFR-A9** — RTL + a11y CI gate.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| Framework | Angular 21 | Zoneless, signals, standalone |
| UI | PrimeNG 21 | `<p-tag>` |
| Routing | Angular Router | `routerLink` |
| i18n | Transloco | aria-label + label keys |
| Test | Vitest + Angular Testing Library + Playwright | |

### Testing requirements

- **Unit/Component:** all rendering states, including disabled.
- **E2E:** bidirectional navigation + keyboard + a11y + RTL.
- **Architectural:** ESLint rules for single-owner + no-directional-glyphs.
- **Coverage:** 80%+ on the component + caller wiring.

### Previous Story Intelligence

**Prerequisites:**
- Story 1.1 — `--focus-ring` design token + Transloco runtime
- Story 1.9 — Phase-1 component API single-owner rule
- Story 1.10 — axe-core CI gate
- Story 5.2 — Transfer backend produces `correlation_id` linkage between source + target legs

**Downstream consumers (consume unchanged):**
- Story 5.4 — Transfer history view (uses `kind="transfer-from"`/`-to"`)
- Story 6.4 — Exchange operation result (uses `kind="exchange-prior"`/`-derived"`)
- Story 6.5 — Exchange history (same kinds as 6.4)

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 5 Story 5.3
- AuditBadge component spec — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) §11 + UX-DR15
- FR25 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- libs/domain-ui location — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §6.4.6
- Phase-1 component API rule — [_bmad-output/implementation-artifacts/1-9-phase-1-component-api-design-system-library.md](./1-9-phase-1-component-api-design-system-library.md)
- Story 5.2 backend — [_bmad-output/implementation-artifacts/5-2-atomic-compound-journal-entry-transfer.md](./5-2-atomic-compound-journal-entry-transfer.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- AuditBadge API frozen here; consumed unchanged by 5.4, 6.4, 6.5.
- Direction-neutral icon (`pi-link`) only; ESLint rule prevents glyph drift.
- Single-owner rule enforced via custom ESLint AST check on input declarations.
- Decision flag (AC4): upgraded `linkedRecordId: string` → `linkedRoute: any[]` to decouple from route shape; revert if Wahid prefers component-owned routing.

### File List

_Populated during implementation._
