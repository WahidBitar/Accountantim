# Story 1.9: Phase-1 component API defined in design-system library

Status: ready-for-dev

## Story

As a **developer**,
I want **the Phase-1 components (AmountDisplay, ContactAvatar, DebtRow, HeroTile, GlanceShell, DenominationPicker, ContextualNudge) defined as standalone signal-based components in `libs/design-system` + `libs/domain-ui` with their public API frozen**,
so that **Epic 3 (live data) and Epic 4 (StatementView) consume them without modifying the API (Mary's single-owner rule)**.

**Covers requirements:** UX-DR5–UX-DR11, NFR-A1, NFR-P9 (RTL parity in Storybook), FR8/FR13 preparation (components empty-state rendered in Story 1.8 empty Glance).

**Why this story now:** Per the reorder, 1.9 ships immediately after 1.1 and before 1.8 — the empty Glance (1.8) consumes the components this story defines. Freezing the APIs early prevents downstream epics from modifying component interfaces ("single-owner rule" — Mary's audit finding).

## Acceptance Criteria

### AC1 — AmountDisplay public API

**Given** `libs/domain-ui/src/amount-display/amount-display.component.ts`
**When** the component is inspected
**Then** its inputs are exactly:
```ts
amount = input.required<number>();
denomination = input.required<Denomination>();
role = input<'receivable' | 'payable' | 'neutral'>('neutral');
size = input<'sm' | 'md' | 'lg' | 'xl'>('md');
originalDenomination = input<Denomination | null>(null);
```
**And** the component is `standalone: true`, `changeDetection: ChangeDetectionStrategy.OnPush`
**And** amount is rendered with `font-feature-settings: "tnum" 1, "lnum" 1` tabular numerals
**And** role colors apply via CSS custom properties: receivable → `--success`, payable → `--danger`, neutral → `--text-primary`
**And** `originalDenomination` when non-null renders a small secondary line "originally 300 USD" for converted amounts
**And** selector is `<ow-amount-display>`; no additional inputs or outputs exist

### AC2 — ContactAvatar public API

**Given** `libs/domain-ui/src/contact-avatar/contact-avatar.component.ts`
**When** inspected
**Then** inputs are exactly:
```ts
contactId = input.required<string>();
displayName = input.required<string>();
size = input<'xs' | 'sm' | 'md' | 'lg'>('md');
```
**And** tint color derives deterministically from `hash(contactId) % 12` mapped to a fixed 12-palette (defined in `contact-tint.palette.ts`)
**And** initials = first char of first word + first char of last word (handles Arabic + Latin names + single-name edge case)
**And** `<ow-contact-avatar>` selector; renders as `border-radius: --radius-pill`
**And** `aria-label="{{ displayName }}"` for screen readers

### AC3 — DebtRow public API

**Given** `libs/domain-ui/src/debt-row/debt-row.component.ts`
**When** inspected
**Then** inputs + outputs are exactly:
```ts
debt = input.required<Debt>();
role = input.required<'receivable' | 'payable'>();
onOpen = output<Debt>();
onLongPress = output<Debt>();
```
**And** the row renders: `<ow-contact-avatar>` + contact display name + `<ow-amount-display>` + due date + age badge
**And** row height = 56–64px per UX Content Density spec
**And** `onOpen` fires on click/Enter/Space; `onLongPress` fires on ≥ 500ms press-hold (mobile context menu)
**And** `<ow-debt-row>` selector

### AC4 — HeroTile public API

**Given** `libs/domain-ui/src/hero-tile/hero-tile.component.ts`
**When** inspected
**Then** inputs + outputs are exactly:
```ts
role = input.required<'receivable' | 'payable'>();
sums = input.required<DenominationSum[]>();
totalDebts = input.required<number>();
onTap = output<void>();
```
**And** receivable hero renders Arabic label "لي" with `--success` accent; payable renders "عليّ" with `--danger` accent
**And** sum per denomination rendered via `<ow-amount-display size="xl">`
**And** multi-denomination case: primary sum in hero size, additional denominations as smaller rows below
**And** empty case (`totalDebts === 0`): renders "0" with muted color + "no debts" Transloco key
**And** `<ow-hero-tile>` selector; tap triggers `onTap` output

### AC5 — GlanceShell public API

**Given** `libs/domain-ui/src/glance-shell/glance-shell.component.ts`
**When** inspected
**Then** inputs are exactly:
```ts
receivables = input.required<Debt[]>();
payables = input.required<Debt[]>();
settled = input.required<Debt[]>();
receivableSums = input.required<DenominationSum[]>();
payableSums = input.required<DenominationSum[]>();
glanceStage = input.required<1 | 2 | 3>();
nudgeState = input<NudgeState | null>(null);
```
**And** component composes: two `<ow-hero-tile>` components on top; two parallel `<ow-debt-row>` columns below; collapsed settled footer
**And** layout is identical at stages 1/2/3 per Glance Maturity Model — only data density differs
**And** `nudgeState` non-null renders `<ow-contextual-nudge>` above the columns; null hides it
**And** `<ow-glance-shell>` selector

### AC6 — DenominationPicker public API

**Given** `libs/domain-ui/src/denomination-picker/denomination-picker.component.ts`
**When** inspected
**Then** inputs are exactly:
```ts
value = model.required<Denomination>();
recent = input<Denomination[]>([]);
disabled = input<boolean>(false);
```
**And** uses `<p-select>` (PrimeNG Select) with recent denominations pinned to top + full list below (EUR, USD, GBP, AED, SAR, EGP, JOD, KWD, BHD, TND + XAU/XAG metals)
**And** selector `<ow-denomination-picker>`; integrates with Signal Forms via `model()` two-way binding
**And** `aria-label` localized via Transloco; keyboard navigation works per PrimeNG Select defaults

### AC7 — ContextualNudge public API (owned by Epic 1)

**Given** `libs/domain-ui/src/contextual-nudge/contextual-nudge.component.ts`
**When** inspected
**Then** inputs + outputs are exactly:
```ts
state = input.required<NudgeState>();
onAction = output<string>();
```
**And** `NudgeState` type:
```ts
interface NudgeState {
  kind: 'add-first-debt' | 'share-first-statement' | 'pending-confirmation' | 'offline' | 'staleness-reminder' | 'threshold-crossing';
  actionRef?: string;
}
```
**And** **only `add-first-debt` kind is wired at MVP** — other kinds render a placeholder + log a warn in dev mode (`console.warn('ContextualNudge kind "{kind}" not yet implemented')`); additive PRs in Epic 4/7 wire the other kinds
**And** a single-owner ESLint rule blocks cross-epic PRs from forking this component — new kinds MUST be added to this file, not re-implemented elsewhere
**And** fixed `min-height: 56px` prevents CLS
**And** `role="status"` + `aria-live="polite"` announces to screen readers
**And** `<ow-contextual-nudge>` selector

### AC8 — Zero legacy decorators

**Given** any Phase-1 component file
**When** ESLint runs
**Then** zero `@Input()`, `@Output()`, `@ViewChild()` legacy decorators appear — all use `input()`, `output()`, `viewChild()` signal-based equivalents
**And** zero `@HostBinding()` / `@HostListener()` — use `host: { ... }` metadata instead
**And** ESLint rule `signal-api-only` enforces this across `libs/domain-ui/**` and `libs/design-system/**`

### AC9 — Storybook in LTR + RTL

**Given** a Storybook (or Ladle/Histoire) instance under `libs/domain-ui/.storybook/`
**When** the dev server runs
**Then** each Phase-1 component has stories covering every meaningful state:
- AmountDisplay: sm/md/lg/xl × receivable/payable/neutral × with/without originalDenomination
- ContactAvatar: xs/sm/md/lg × Arabic name / Latin name / single-name / emoji
- DebtRow: receivable + payable × short/long contact name × overdue/due-soon/ok
- HeroTile: empty / single-denomination / multi-denomination × receivable/payable
- GlanceShell: stage 1 (sparse) / stage 2 (moderate) / stage 3 (dense)
- DenominationPicker: closed / open / disabled
- ContextualNudge: kind='add-first-debt' (the only wired kind)

**And** each story renders in LTR + RTL via a Storybook global toolbar toggle
**And** Chromatic (or Percy) snapshot is captured per story per direction

### AC10 — Component unit tests at all Glance stages

**Given** each Phase-1 component
**When** Vitest runs
**Then** every component has colocated `*.component.spec.ts` + Angular Testing Library tests
**And** GlanceShell specifically has tests at stage 1/2/3 data densities asserting the layout is identical
**And** `axe-core` runs in `toHaveNoViolations()` assertion on every component test — zero WCAG 2.1 AA violations
**And** coverage ≥ 80% lines, ≥ 80% branches across `libs/domain-ui/**`

## Tasks / Subtasks

### Setup

- [ ] **T1 (AC: 1-7)** — Scaffold `libs/domain-ui/` library structure
  - [ ] Create subfolders for each component: `amount-display/`, `contact-avatar/`, `debt-row/`, `hero-tile/`, `glance-shell/`, `denomination-picker/`, `contextual-nudge/`
  - [ ] Create `libs/domain-ui/src/index.ts` public barrel exporting each component
  - [ ] Add TypeScript domain types to `libs/domain-ui/src/types.ts`: `Denomination`, `Money`, `Debt`, `DenominationSum`, `NudgeState`
- [ ] **T2 (AC: 8)** — ESLint `signal-api-only` rule
  - [ ] Create `tools/eslint-rules/signal-api-only.js` — forbids `@Input`, `@Output`, `@ViewChild`, `@ContentChild`, `@HostBinding`, `@HostListener` decorator imports/usages in `libs/domain-ui/**` and `libs/design-system/**`
  - [ ] Add to `.eslintrc.js` with `error` severity

### Component implementation

- [ ] **T3 (AC: 1)** — Implement `AmountDisplay`
  - [ ] Template renders amount via `Intl.NumberFormat` (locale-aware thousands separators); denomination symbol after amount in both directions (CSS flex handles RTL)
  - [ ] SCSS uses logical properties only; role → CSS custom property variable mapping
  - [ ] When `originalDenomination` non-null, render secondary line "originally {amount} {denomination}"
- [ ] **T4 (AC: 2)** — Implement `ContactAvatar`
  - [ ] Create `contact-tint.palette.ts` with 12 light/accessible tints (each pair meets 4.5:1 contrast vs white text)
  - [ ] `computed()` derives tint from `contactId` hash
  - [ ] Initials `computed()` handles Arabic RTL name splits (use `Intl.Segmenter` for grapheme boundaries)
- [ ] **T5 (AC: 3)** — Implement `DebtRow`
  - [ ] Template: avatar | name | amount | due date | age badge
  - [ ] `onLongPress` uses `(pointerdown)` + timeout + cancel on `(pointerup)` / `(pointercancel)` / `(pointerleave)`
  - [ ] Keyboard accessibility: Enter + Space trigger `onOpen`
- [ ] **T6 (AC: 4)** — Implement `HeroTile`
  - [ ] Template: label + primary sum + optional secondary denomination stack + debt count
  - [ ] Empty-state branch when `totalDebts === 0` — renders muted "0" + localized "no debts" key
- [ ] **T7 (AC: 5)** — Implement `GlanceShell`
  - [ ] Template: two `<ow-hero-tile>` grid-area items; two `<ow-debt-row>` column regions; collapsed settled footer
  - [ ] `nudgeState !== null` → inserts `<ow-contextual-nudge>` above the columns
  - [ ] Layout is identical at stage 1/2/3 — tested by snapshot comparison
- [ ] **T8 (AC: 6)** — Implement `DenominationPicker`
  - [ ] Wraps `<p-select>`; data source is `CurrencyRegistry` (frontend mirror of backend registry)
  - [ ] `recent` items pinned to top of the dropdown with divider below
  - [ ] Integrates with Signal Forms — `model()` works as two-way control
- [ ] **T9 (AC: 7)** — Implement `ContextualNudge` (single-owner API)
  - [ ] `switch` on `state().kind` — only `'add-first-debt'` branch renders real content; all other branches render `<!-- TODO: implement in Epic X -->` placeholder + `console.warn` in dev mode
  - [ ] `role="status"` + `aria-live="polite"` on host element
  - [ ] Fixed `min-height: 56px` applied via host style; no dynamic height changes
  - [ ] Comment at top of file: `// Single-owner component per Mary's audit. Additive PRs only — do not fork.`

### Storybook + visual regression

- [ ] **T10 (AC: 9)** — Scaffold Storybook at `libs/domain-ui/.storybook/`
  - [ ] `main.ts` configures Angular 21 framework preset + Storybook Addon Themes (for LTR/RTL toolbar toggle)
  - [ ] Global decorator applies `dir` attribute from toolbar state
  - [ ] Create `*.stories.ts` per component covering states listed in AC9
- [ ] **T11 (AC: 9)** — Visual regression baseline
  - [ ] Chromatic (or Percy) configured in CI — snapshot every story in LTR + RTL
  - [ ] Baseline committed in this story; diffs above threshold fail subsequent PRs
  - [ ] Integration with Story 1.11 perf-budget gate

### Tests

- [ ] **T12 (AC: 10)** — Vitest + Angular Testing Library component specs
  - [ ] `amount-display.component.spec.ts` — all size/role variants; tnum rendering
  - [ ] `contact-avatar.component.spec.ts` — tint determinism (same id → same tint), initial extraction for Arabic + Latin
  - [ ] `debt-row.component.spec.ts` — click + keyboard + long-press + output emission
  - [ ] `hero-tile.component.spec.ts` — empty vs populated; multi-denomination rendering
  - [ ] `glance-shell.component.spec.ts` — stage 1/2/3 layout parity (snapshot comparison)
  - [ ] `denomination-picker.component.spec.ts` — recent pinning; Signal Forms integration
  - [ ] `contextual-nudge.component.spec.ts` — only `add-first-debt` fully renders; other kinds warn in dev
- [ ] **T13 (AC: 10)** — axe-core assertion in every component spec
  - [ ] Use `jest-axe` (or Vitest equivalent) matcher `expect(result).toHaveNoViolations()`
  - [ ] Runs at WCAG 2.1 AA level
- [ ] **T14 (AC: 8)** — ESLint `signal-api-only` rule self-test
  - [ ] Fixture file with `@Input()` decorator → rule fails
  - [ ] Cleared fixture → rule passes

## Dev Notes

### Critical guardrails (do not violate)

1. **Single-owner rule for ContextualNudge.** All future kinds (`share-first-statement`, `pending-confirmation`, `offline`, `staleness-reminder`, `threshold-crossing`) are added to **this component's file**, not forked into epic-local copies. ESLint rule enforces this. Epic 4/7 PRs extending the component ship additive changes only.
2. **Public API frozen at end of this story.** Adding inputs, outputs, or models later = breaking change requiring coordinated Epic 3/4/7 migration. The API signatures in AC1-AC7 are the contract — match them exactly.
3. **Signal APIs only.** No `@Input()`, `@Output()`, `@ViewChild()`, `@HostBinding()`, `@HostListener()`. Zero exceptions in `libs/domain-ui/**`.
4. **Standalone + OnPush everywhere.** Every component declares `standalone: true`, `changeDetection: ChangeDetectionStrategy.OnPush`.
5. **Logical CSS properties only.** Inherited from Story 1.1 stylelint config.
6. **Domain types live in `libs/domain-ui/src/types.ts`.** Until NSwag generates these from C# DTOs (deferred per ADR-005 to the generator implementation step), hand-author them here; they will be replaced with generated types. Mark the file with `// TODO: replace with NSwag-generated types when available`.
7. **DenominationPicker consumes `CurrencyRegistry`.** A frontend mirror of the backend registry lives in `libs/domain-ui/src/currency-registry.ts` at MVP (same registry both sides). Replaced by generated contract when NSwag lands.

### Source tree — files to create

```
libs/domain-ui/
├── src/
│   ├── index.ts                                                  # NEW (public barrel)
│   ├── types.ts                                                  # NEW (Denomination, Money, Debt, DenominationSum, NudgeState)
│   ├── currency-registry.ts                                      # NEW (frontend mirror)
│   ├── amount-display/
│   │   ├── amount-display.component.ts                           # NEW
│   │   ├── amount-display.component.html                         # NEW
│   │   ├── amount-display.component.scss                         # NEW
│   │   └── amount-display.component.spec.ts                      # NEW
│   ├── contact-avatar/
│   │   ├── contact-avatar.component.ts                           # NEW
│   │   ├── contact-avatar.component.html                         # NEW
│   │   ├── contact-avatar.component.scss                         # NEW
│   │   ├── contact-avatar.component.spec.ts                      # NEW
│   │   └── contact-tint.palette.ts                               # NEW (12 tints)
│   ├── debt-row/
│   │   ├── debt-row.component.ts                                 # NEW
│   │   ├── debt-row.component.html                               # NEW
│   │   ├── debt-row.component.scss                               # NEW
│   │   └── debt-row.component.spec.ts                            # NEW
│   ├── hero-tile/
│   │   ├── hero-tile.component.ts                                # NEW
│   │   ├── hero-tile.component.html                              # NEW
│   │   ├── hero-tile.component.scss                              # NEW
│   │   └── hero-tile.component.spec.ts                           # NEW
│   ├── glance-shell/
│   │   ├── glance-shell.component.ts                             # NEW
│   │   ├── glance-shell.component.html                           # NEW
│   │   ├── glance-shell.component.scss                           # NEW
│   │   └── glance-shell.component.spec.ts                        # NEW
│   ├── denomination-picker/
│   │   ├── denomination-picker.component.ts                      # NEW
│   │   ├── denomination-picker.component.html                    # NEW
│   │   ├── denomination-picker.component.scss                    # NEW
│   │   └── denomination-picker.component.spec.ts                 # NEW
│   └── contextual-nudge/
│       ├── contextual-nudge.component.ts                         # NEW (single-owner)
│       ├── contextual-nudge.component.html                       # NEW
│       ├── contextual-nudge.component.scss                       # NEW
│       └── contextual-nudge.component.spec.ts                    # NEW
├── .storybook/
│   ├── main.ts                                                   # NEW
│   ├── preview.ts                                                # NEW (LTR/RTL global decorator)
│   └── themes.ts                                                 # NEW (toolbar toggle)
└── stories/
    └── *.stories.ts                                              # NEW (one per component)
tools/eslint-rules/
└── signal-api-only.js                                            # NEW
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit / Component | 80% lines + branches | Vitest + Angular Testing Library + jest-axe |
| Visual regression | Every story × LTR + RTL | Chromatic or Percy, baselined in this story |
| Lint | Gate | ESLint `signal-api-only` rule |
| Storybook | Dev UX | Storybook 8+ with Angular framework preset |

**Forbidden:**
- Legacy `@Input`/`@Output`/`@ViewChild`/`@HostBinding`/`@HostListener` decorators
- Epic-local forks of `ContextualNudge` — only additive PRs to the single owner
- Hand-extended component public APIs after this story closes
- Physical CSS properties
- `NgModule` declarations

### Project Structure Notes

This story creates `libs/domain-ui/` — the domain-shaped UI primitives library referenced in architecture §3.4. Both `apps/owner-workspace` and `apps/public-statement` consume this library; Epic 4's StatementView will consume AmountDisplay, ContactAvatar, DebtRow unchanged.

**Detected variances to reconcile:**
1. Chromatic vs Percy — choose one and document the decision in ADR. Default recommendation: Chromatic (better Storybook integration, generous free tier) — unless cost blocks at scale, then fall back to Percy.
2. Storybook vs Ladle/Histoire — Storybook 8+ with Angular has mature tooling; Ladle is faster but Angular support is immature. Use Storybook 8+ for Angular 21.
3. `jest-axe` has no Vitest-native equivalent — use `vitest-axe` or similar. If neither works cleanly, use `axe-playwright` inside component tests via a lightweight harness.

### Architecture compliance

- **ADR-001** (two build targets) — `libs/domain-ui` is safe to import from both `apps/owner-workspace` and `apps/public-statement`; ESLint path constraints enforce
- **ADR-005** (TS contracts deferred) — domain types are hand-authored in `types.ts` until NSwag generates them (per delegated decision: NSwag is the chosen generator); mark the file with a TODO to replace with generated contracts
- **§3.4 Frontend Scaffold** — `libs/domain-ui/` landed per scaffold plan
- **UX Design Foundation** — every component reflects the Faktuboh Quiet tokens from Story 1.1
- **UX Component Strategy Phase 1 (AmountDisplay, ContactAvatar, DebtRow, HeroTile, GlanceShell, DenominationPicker)** + ContextualNudge from Phase 2 (but owned here)

### Library / framework requirements

| Layer | Package | Version |
|---|---|---|
| Framework | Angular | 21 (signals, input(), output(), model()) |
| UI kit | PrimeNG | 21 (`<p-select>` for DenominationPicker) |
| Storybook | @storybook/angular | 8+ |
| Visual regression | Chromatic (or Percy) | latest stable |
| Test runner | Vitest | latest stable |
| a11y test | jest-axe (or vitest-axe) | latest stable |

### Testing requirements

- **Unit/component**: 80% lines + branches; every AC has at least one assertion; every component has axe-core
- **Visual regression**: Storybook stories baselined across Chromatic/Percy in LTR + RTL; subsequent PRs fail on unintended diffs
- **Lint**: `signal-api-only` rule tested via fixture files
- **Integration with Story 1.10/1.11**: the Chromatic/Percy job is wired into the CI pipeline defined in Story 1.11

### Previous Story Intelligence

**Prerequisites in order:**
- **Epic 0 scaffold** (0.1–0.17) — Angular workspace + CI pipeline must be green
- **Story 1.1** (bilingual shell + tokens) — design tokens + stylelint rules + zoneless bootstrap + Transloco runtime must exist; components reference `--primary`, `--accent-gold`, `--success`, `--danger`, etc. as CSS custom properties

**Downstream consumers (per reorder: 1.9 → 1.10 → 1.11 → 1.2 → ... → 1.8):**
- Story 1.10 activates axe-core + template-a11y on these components
- Story 1.11 establishes visual regression baselines captured in Storybook
- Story 1.8 (empty Glance) renders `GlanceShell` with empty data + `ContextualNudge kind="add-first-debt"`
- Epic 3 (live data) consumes these components unchanged
- Epic 4 (public-statement) consumes `AmountDisplay`, `ContactAvatar`, `DebtRow` unchanged

### Latest Technical Information

**Angular 21 signal-based component APIs:**
- `input()` — required signal input: `input.required<T>()`
- `input<T>(defaultValue)` — optional signal input with default
- `model()` — two-way binding signal: `model.required<T>()` / `model<T>(defaultValue)`
- `output()` — typed output signal: `output<T>()`
- `viewChild()` / `contentChild()` — signal-based queries
- `effect(() => {...})` — reactive side effects
- `afterNextRender(() => {...})` — zoneless-safe DOM-dependent work

**PrimeNG 21 `<p-select>`:**
- Supports grouped options (for pinning recent denominations at the top)
- Integrates with Angular Signal Forms via `[ngModel]` or `formControlName`
- Use `@primeng/mcp` MCP server for exact API during implementation

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Story 1.9
- Component Strategy Phase 1 (DebtRow, AmountDisplay, ContactAvatar, HeroTile, GlanceShell, DenominationPicker) — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) Component Strategy → Custom Components Phase 1
- ContextualNudge (Phase 2 owned by Epic 1) — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) Component Strategy → Custom Components Phase 2
- Frontend scaffold — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.4
- Frontend conventions — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §5.4.1
- ADR-005 TS contracts — [_bmad-output/planning-artifacts/adrs/adr-005-typescript-contract-generator.md](../planning-artifacts/adrs/adr-005-typescript-contract-generator.md)
- UX Glance Maturity Model — [_bmad-output/planning-artifacts/ux-design-specification.md](../planning-artifacts/ux-design-specification.md) §2.6
- Mary's single-owner audit finding — delegated decision in story prompt

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Phase-1 component public APIs frozen per UX-DR5–UX-DR10 + ContextualNudge per UX-DR11
- ContextualNudge is single-owner — only `add-first-debt` kind wired at MVP; Epic 4/7 add other kinds via additive PRs
- Storybook + Chromatic/Percy baselines established for downstream visual regression

### File List

_Populated during implementation — see Source tree section above._
