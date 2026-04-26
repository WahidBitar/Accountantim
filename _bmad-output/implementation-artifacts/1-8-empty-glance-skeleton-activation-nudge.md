# Story 1.8: Empty Glance skeleton with activation nudge

Status: ready-for-dev

## Story

As a **newly-registered user on my first session**,
I want **to land on an empty Glance showing "لي 0 / عليّ 0" hero tiles with a single contextual nudge saying "Lent or borrowed recently? Add it now — takes 10 seconds,"**
so that **my first impression of Faktuboh is calm, clear, and immediately actionable**.

**Covers requirements:** UX Glance Maturity Model Stage 0, UX Activation-to-Retention Loop, Phase-1 component consumption (from Story 1.9), AR-047 telemetry.

**Why this story is last in Epic 1:** Per the reorder, 1.8 ships after every other Epic 1 story because it consumes the Phase-1 components from Story 1.9, the auth flow from 1.2/1.3 (so users actually land here post-signup/login), the shell from 1.1, and the CI gates from 1.10/1.11. It is the visible payoff of the entire epic — the screen a new user sees first.

## Acceptance Criteria

### AC1 — Empty Glance renders at `/dashboard` (Stage 0)

**Given** a user has just completed registration (Story 1.2) — Stage 0 of the Glance Maturity Model
**When** they land on `/dashboard` (or `/onboarding` redirects here on first visit)
**Then** the page renders the `<ow-glance-shell>` component (from Story 1.9) with empty data: `receivables=[]`, `payables=[]`, `settled=[]`, `receivableSums=[]`, `payableSums=[]`, `glanceStage=1` (representing the empty starting state)
**And** two `<ow-hero-tile>` components render showing "0" with role labels "لي" (receivable) and "عليّ" (payable) per Direction 6 Segmented Summary
**And** two empty column placeholders render with clear visual affordances (not blank space — use a subtle dashed border + icon hint per UX Empty States)

### AC2 — Activation nudge `kind="add-first-debt"` rendered

**Given** the empty state
**When** the page renders
**Then** a `<ow-contextual-nudge>` component (from Story 1.9) is rendered with `state={ kind: 'add-first-debt' }`
**And** the nudge text reads (localized via Transloco): "Lent or borrowed recently? Add it now — takes 10 seconds." (EN) / "أقرضت أو اقترضت مؤخراً؟ سجّله الآن — 10 ثوانٍ فقط." (AR)
**And** the nudge is positioned **above** the hero tiles + columns
**And** the nudge has fixed `min-height: 56px` to prevent CLS on hide/show

### AC3 — Accessibility — `role="status"` + `aria-live="polite"`

**Given** the contextual nudge
**When** screen readers inspect the page
**Then** the nudge element has `role="status"` and `aria-live="polite"`
**And** screen readers announce the nudge text on page load (without interrupting other announcements)
**And** the announcement happens once per visit — re-rendering does not re-announce

### AC4 — No wizard, no overlay, no welcome modal

**Given** the user lands on the empty Glance
**When** the page renders
**Then** **NO wizard** appears (no multi-step setup walkthrough)
**And** **NO tutorial overlay** appears (no "click here to add a debt" tooltip pulsing)
**And** **NO welcome modal** appears
**And** the page is the empty Glance + nudge — nothing more
**And** this matches UX Anti-Patterns from §UX Pattern Analysis (avoid gamified onboarding)

### AC5 — Tapping the nudge opens debt-entry form (mocked at MVP)

**Given** the user clicks/taps the nudge
**When** the action fires
**Then** the `onAction` output emits with `actionRef = 'open-add-debt'`
**And** the parent `dashboard.component.ts` handles the emission by navigating to `/contacts/new` (Epic 2 form) OR a stub modal that says "Debt entry coming soon — Epic 3" if Epic 2 hasn't shipped at the time of Story 1.8 implementation
**And** the stub modal closes back to the empty Glance — keeps user oriented
**And** the chosen approach is determined at implementation time based on Epic 2 readiness

### AC6 — Layout identical at Stage 1/2/3

**Given** the Glance Maturity Model defines stages 1 (sparse), 2 (moderate), 3 (dense)
**When** comparing the layout
**Then** the empty Glance (Stage 0 / Stage 1) renders the **identical layout** as a populated Glance — only data density differs
**And** Storybook stories from Story 1.9 show this parity
**And** Playwright visual regression captures Stage 0/1 baseline; Epic 3 will add Stage 2/3 baselines

### AC7 — No bespoke empty-state UI

**Given** the components from Story 1.9
**When** rendering the empty Glance
**Then** all components are consumed unchanged from `libs/domain-ui` (single-owner rule from Story 1.9)
**And** the empty-state branches inside each Phase-1 component handle the empty data presentation
**And** no new component is created in this story — the dashboard route just composes existing pieces

### AC8 — Telemetry `glance_loaded` with stage label

**Given** AR-047 telemetry
**When** the dashboard renders
**Then** `glance_loaded` event emits with `{ opaque_user_id, stage: 0|1|2|3, has_nudge: true|false }`
**And** at MVP, only `stage: 0` (or `stage: 1` empty) variants emit; later Epic 3+ data populates the higher stages
**And** no PII fields appear in the event

### AC9 — Performance — first paint < 1s on Slow 4G

**Given** the dashboard route is the user's first authenticated load
**When** Lighthouse measures
**Then** LCP < 2.5s on Slow 4G (NFR-P1; inherited from Story 1.11)
**And** the initial JS for `/dashboard` route stays within the 60 KB lazy-chunk budget (Story 1.11)
**And** font swap (Tajawal/Inter) does not cause CLS (Story 1.1's preload + `font-display: swap`)

### AC10 — Manual screen-reader QA

**Given** the empty Glance is the new-user first impression
**When** manual QA runs (per Story 1.10's `docs/accessibility/manual-qa.md`)
**Then** VoiceOver AR (iOS Safari), TalkBack AR (Android Chrome), NVDA AR (Windows Firefox + Chrome) each verify:
  - `<html lang="ar" dir="rtl">` correctly switches reading direction
  - "لي 0" announced as "lee sifr" or equivalent natural reading
  - "عليّ 0" similarly
  - The nudge text announced once on load
  - Tapping the nudge with assistive tech triggers the action
**And** a green pass on the 3-AT × AR locale matrix gates the Epic 1 merge

## Tasks / Subtasks

### Frontend — dashboard route

- [ ] **T1 (AC: 1, 2, 4)** — Create `apps/owner-workspace/src/app/features/dashboard/`
  - [ ] `dashboard.routes.ts` — lazy route `/dashboard` (also `/onboarding` redirects here for newly-registered)
  - [ ] `dashboard.component.ts` — standalone + OnPush
  - [ ] Template composes `<ow-glance-shell>` from `libs/domain-ui` with empty data signals
  - [ ] Sets `nudgeState = signal({ kind: 'add-first-debt' })` for empty state
- [ ] **T2 (AC: 1)** — Empty data signals
  - [ ] `receivables = signal<Debt[]>([])`
  - [ ] `payables = signal<Debt[]>([])`
  - [ ] `settled = signal<Debt[]>([])`
  - [ ] `receivableSums = signal<DenominationSum[]>([])`
  - [ ] `payableSums = signal<DenominationSum[]>([])`
  - [ ] `glanceStage = signal<1>(1)`
- [ ] **T3 (AC: 5)** — Handle `onAction` emission
  - [ ] If Epic 2 (Contacts/Create) shipped: navigate to `/contacts/new`
  - [ ] Else: open a stub `<p-dialog>` "Debt entry coming soon — Epic 3" with a Close button
  - [ ] Track decision in story commit message
- [ ] **T4 (AC: 4)** — No additional UI
  - [ ] No wizard/tutorial/modal scaffolding — keep template minimal

### Translation bundles

- [ ] **T5 (AC: 2, 10)** — Add Transloco keys
  - [ ] `dashboard.empty.hero-receivable-label` = "لي" (AR) / "Owed to me" (EN — but visually we keep "لي" for the Arabic-first product)
  - [ ] Decision: at MVP, hero labels render in Arabic regardless of UI locale (per UX intent + Direction 6 design); document in story commit
  - [ ] `dashboard.nudge.add-first-debt` = "أقرضت أو اقترضت مؤخراً؟ سجّله الآن — 10 ثوانٍ فقط." (AR) / "Lent or borrowed recently? Add it now — takes 10 seconds." (EN)
  - [ ] `dashboard.empty.column-placeholder.receivables` = "Debts owed to you will appear here"
  - [ ] `dashboard.empty.column-placeholder.payables` = "Debts you owe will appear here"

### Telemetry

- [ ] **T6 (AC: 8)** — Emit `glance_loaded` event
  - [ ] On component init (`ngAfterViewInit` equivalent — `afterNextRender`), call `TelemetryEmitter.emit('glance_loaded', { opaque_user_id, stage: 0, has_nudge: true })`
  - [ ] NetArchTest schema assertion (no PII)

### Tests

- [ ] **T7 (AC: 1-9)** — Playwright E2E `apps/owner-workspace/e2e/dashboard/empty-glance.spec.ts`
  - [ ] New-user flow (mock signup → land on `/dashboard`)
  - [ ] Assert hero tiles show "0" with labels
  - [ ] Assert nudge present with correct text in active locale
  - [ ] Assert no wizard/overlay/modal exists
  - [ ] Assert clicking nudge triggers expected behavior (navigation or stub modal)
  - [ ] axe-core zero violations
  - [ ] Run in LTR + RTL via `testInBothDirections`
  - [ ] Visual regression baselines at 3 breakpoints × 2 directions
- [ ] **T8 (AC: 1, 4, 7)** — Vitest component test for `dashboard.component.ts`
  - [ ] Renders `<ow-glance-shell>` with empty data
  - [ ] Renders `<ow-contextual-nudge>` with `add-first-debt` kind
  - [ ] No additional UI elements
  - [ ] OnPush + signal-based
- [ ] **T9 (AC: 9)** — Lighthouse CI check on `/dashboard` route
  - [ ] Configured in `.lighthouserc.cjs` from Story 1.11
  - [ ] LCP < 2.5s, INP < 200ms, CLS < 0.1
  - [ ] Performance score ≥ 75 (mobile throttled)
- [ ] **T10 (AC: 10)** — Manual screen-reader QA
  - [ ] Update `docs/accessibility/manual-qa.md` with the dashboard-empty-Glance row
  - [ ] Test on VoiceOver AR (iOS), TalkBack AR (Android), NVDA AR (Windows)
  - [ ] Sign off before Epic 1 merge

## Dev Notes

### Critical guardrails (do not violate)

1. **No bespoke empty-state UI.** Consume Phase-1 components from `libs/domain-ui` unchanged. Single-owner rule from Story 1.9.
2. **No wizard/tutorial/modal/overlay.** UX intent is calm + immediately actionable. Onboarding is the empty Glance + one nudge — that's it.
3. **Hero labels in Arabic regardless of UI locale.** "لي" / "عليّ" are product names per Direction 6, not generic translations. Decision documented in story commit.
4. **`ContextualNudge kind="add-first-debt"`** is the only kind wired at MVP per Story 1.9. Other kinds added by Epic 4/7 via additive PRs.
5. **Stage 0/1 layout identical to Stage 2/3.** Glance Maturity Model — only data density differs. Visual regression enforces.
6. **`glance_loaded` telemetry PII-safe.** Only `opaque_user_id` + `stage` + `has_nudge`. NetArchTest enforces.
7. **Link the nudge to `/contacts/new` if Epic 2 shipped, else stub modal.** Decision deferred to implementation time based on Epic 2 readiness; document the choice.
8. **Performance budget inherited.** `/dashboard` route stays within 60 KB lazy-chunk budget (Story 1.11). LCP < 2.5s on Slow 4G.

### Source tree — files to create or touch

```
apps/owner-workspace/
├── src/app/features/dashboard/
│   ├── dashboard.routes.ts                         # NEW
│   ├── dashboard.component.ts                      # NEW
│   ├── dashboard.component.html                    # NEW
│   ├── dashboard.component.scss                    # NEW (logical props only)
│   └── dashboard.component.spec.ts                 # NEW
├── src/assets/i18n/
│   ├── ar.json                                     # MODIFY
│   └── en.json                                     # MODIFY
└── e2e/dashboard/
    └── empty-glance.spec.ts                        # NEW

docs/accessibility/
└── manual-qa.md                                    # MODIFY (add empty-Glance row)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | Composition + signal wiring | Vitest + Angular Testing Library |
| E2E | Empty-Glance happy path + nudge action | Playwright with axe-core in LTR + RTL |
| Visual regression | 3 breakpoints × 2 directions | Playwright toHaveScreenshot |
| Performance | LCP/INP/CLS on `/dashboard` | Lighthouse CI from Story 1.11 |
| Manual | 3-AT × AR locale matrix | `docs/accessibility/manual-qa.md` |

**Forbidden:**
- Bespoke empty-state UI (must consume Phase-1 components unchanged)
- Wizard / tutorial overlay / welcome modal
- Re-translating "لي" / "عليّ" to non-Arabic (these are product names)
- Adding new ContextualNudge kinds outside the single owner (Story 1.9)
- PII in `glance_loaded` telemetry

### Project Structure Notes

This story is the visible payoff of Epic 1. It composes the Phase-1 components (Story 1.9) into the user's first authenticated screen. No new components, no new domain logic — pure composition.

**Detected variances to reconcile at implementation time:**
1. Whether `/contacts/new` (Epic 2) is shipped when Story 1.8 implements — if yes, link directly; if no, use a stub modal. Document the decision.
2. Hero labels "لي" / "عليّ" — these are Arabic product terms used regardless of UI locale; document in commit + Storybook.

### Architecture compliance

- **§3.4 Frontend Scaffold** — `apps/owner-workspace/src/app/features/dashboard/` per the layout
- **UX Glance Maturity Model** — Stage 0/1 empty state
- **UX Activation-to-Retention Loop** — single contextual nudge, no friction
- **UX Anti-Patterns** — no gamified onboarding, no overlays
- **NFR-P1, P2, P3** — Web Vitals targets via Story 1.11 gates
- **AR-047** (telemetry) — `glance_loaded` event PII-safe

### Library / framework requirements

| Layer | Package | Version |
|---|---|---|
| Framework | Angular | 21 |
| UI kit | PrimeNG | 21 (`<p-dialog>` for stub modal if needed) |
| Components | libs/domain-ui (Story 1.9) | Phase-1 components consumed unchanged |

### Testing requirements

- **Unit**: composition test asserts GlanceShell + ContextualNudge instantiated with empty state + correct kind
- **E2E**: full flow from new-user signup → land on dashboard → see empty Glance + nudge → click nudge → expected behavior
- **Visual regression**: baselines for the empty Glance at 3 breakpoints × 2 directions
- **Performance**: LCP/INP/CLS within thresholds via Lighthouse CI
- **Manual**: VoiceOver/TalkBack/NVDA AR matrix passes before Epic 1 merge

### Previous Story Intelligence

**Prerequisites in order (per the reorder):**
- **Story 1.1** — shell + design tokens + Transloco runtime + zoneless bootstrap
- **Story 1.9** — Phase-1 components (`GlanceShell`, `HeroTile`, `DebtRow`, `ContactAvatar`, `AmountDisplay`, `DenominationPicker`, `ContextualNudge`) — single-owner API frozen
- **Story 1.10** — a11y CI gates active; `testInBothDirections` helper available
- **Story 1.11** — perf budget + visual regression baselines
- **Story 1.2** — registration → callback → `/onboarding` → `/dashboard`
- **Story 1.3** — login → callback → `/dashboard`
- **Story 1.4** — password reset (no direct dependency, but identity triangle complete)
- **Story 1.5** — `/profile` route (sibling)
- **Story 1.6** — disclaimer in shell footer
- **Story 1.7** — `/profile/delete` (sibling)

**Downstream:**
- Epic 3 (live data) populates Glance with real receivables/payables/settled
- Epic 4 (StatementView) inherits the same Phase-1 components

### Latest Technical Information

**Angular 21 standalone composition:**
```ts
@Component({
  selector: 'ow-dashboard',
  standalone: true,
  imports: [GlanceShellComponent, ContextualNudgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ow-glance-shell ... [nudgeState]="nudgeState()" />`
})
```

**`afterNextRender` for telemetry emission:**
```ts
afterNextRender(() => {
  this.telemetry.emit('glance_loaded', { opaque_user_id, stage: 0, has_nudge: true });
});
```

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Story 1.8
- UX Glance Maturity Model — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) §2.6
- UX Activation-to-Retention Loop — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) §2.7
- UX Anti-Patterns (avoid gamified onboarding) — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) §UX Pattern Analysis → Anti-Patterns
- UX Empty States — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) UX Consistency Patterns §6
- Story 1.1 — [_bmad-output/implementation-artifacts/1-1-bilingual-shell-design-tokens-rtl-pipeline.md](./1-1-bilingual-shell-design-tokens-rtl-pipeline.md)
- Story 1.9 — [_bmad-output/implementation-artifacts/1-9-phase-1-component-api-design-system-library.md](./1-9-phase-1-component-api-design-system-library.md)
- Story 1.10 — [_bmad-output/implementation-artifacts/1-10-accessibility-ci-gates.md](./1-10-accessibility-ci-gates.md)
- Story 1.11 — [_bmad-output/implementation-artifacts/1-11-performance-budget-visual-regression-ci-gates.md](./1-11-performance-budget-visual-regression-ci-gates.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- The visible payoff of Epic 1 — first authenticated screen for new users
- Pure composition of Phase-1 components from Story 1.9; no new components or domain logic
- Hero labels render in Arabic regardless of UI locale (product term, not translation)

### File List

_Populated during implementation — see Source tree section above._
