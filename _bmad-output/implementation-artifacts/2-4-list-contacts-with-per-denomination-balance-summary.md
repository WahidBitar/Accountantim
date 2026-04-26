# Story 2.4: List contacts with per-denomination balance summary

Status: ready-for-dev

## Story

As a **user with multiple contacts and debts across currencies/metals**,
I want to **see all my contacts at `/contacts` with each one's balance broken down per denomination**,
so that I **understand my full position per counterparty at a glance** without ever seeing a cross-currency aggregation (NFR core: never conflate denominations), and the list surfaces progressively per Glance Maturity Stage — no sort/filter <11 contacts, sort Popover 11-50, sort+filter+search 51+.

**Covers requirements:** FR11 (list contacts with balances), NFR-P7 (balance calc 200ms), Glance Maturity Model Stages 1/2/3, UX-DR5 (AmountDisplay variants).

## Acceptance Criteria

### AC1 — GET returns contacts with server-aggregated per-denomination balances

**Given** the user has N contacts with varying journal entries
**When** the frontend issues `GET /v1/contacts?include=balances`
**Then** the response is `GetContactsResponse` with each contact containing `balances: [{ denominationCode, direction, amount: "300.00" }, ...]`
**And** balances are aggregated server-side in a single query (no N+1)
**And** response time p95 < 200ms at 1000 contacts × 1000 journal entries per contact per NFR-P7
**And** balances are NEVER summed across denominations — "1000 USD + 10g gold" is always two separate entries, never a single number

### AC2 — List renders with ContactAvatar + name + balance pills

**Given** the response
**When** the Angular `ContactListComponent` renders
**Then** each row shows `ContactAvatar` (tint from contact ID hash per UX-DR6) + contact name + one `AmountDisplay variant="compact"` pill per denomination with role color (receivable = `--primary`, payable = `--danger`)
**And** net-zero denominations are omitted from the pills
**And** tabular numerals applied

### AC3 — Glance Maturity Stage progressive disclosure

**Given** the contact count drives the Maturity Stage per UX spec §2.6
**When** the component determines stage via computed signal on `contacts().length`
**Then** **Stage 1 (0-10 contacts):** no sort control, no filter chips, no search bar — just the list
**And** **Stage 2 (11-50):** sort Popover surfaces with options `name-asc | name-desc | balance-desc | recent-activity`
**And** **Stage 3 (51+):** sort Popover + filter chips (by denomination) + persistent search bar (feeds Story 2.5) + `<p-virtualScroller [itemSize]="64">` on desktop / `72` on mobile
**And** pagination uses `<p-paginator>` only above 50 rows; otherwise plain `@for` loop

### AC4 — Empty state and error recovery

**Given** the user has no contacts
**When** the list renders
**Then** an empty-state card renders with "Add your first contact" CTA → routes to `/contacts/new`
**And** the empty state uses UX-DR11 `ContextualNudge kind="add-first-contact"` (additive PR to Epic 1's single-owner component — see Dev Notes)

### AC5 — ProcessingActivity + performance telemetry

**Given** the endpoint stamps `ProcessingActivity.OwnerLedger`
**When** the query runs
**Then** OTel spans include aggregation query latency
**And** telemetry event `contacts_list_viewed` emits with PII-safe payload `{ opaque_user_id, contact_count, denomination_count }`

### AC6 — RTL parity

**Given** the user's locale is Arabic
**When** the list renders in `dir="rtl"`
**Then** row layout flips (avatar right, actions left) via logical CSS properties
**And** AmountDisplay pills order reverses to match reading direction
**And** RTL gate test (NFR-A9) passes — visual regression screenshot matches baseline

## Tasks / Subtasks

### Backend

- [ ] **T1 (AC: 1, 5)** — Create `Slices/Contacts/List/GetContactsHandler.cs`
  - [ ] Endpoint `group.MapGet("/", ...)` with query parameters `include`, `sort`, `q` (search — wired but Story 2.5 activates), `limit`, `cursor`
  - [ ] Single aggregation query using Postgres `FILTER` clause: `SUM(amount) FILTER (WHERE direction = 'receivable') AS receivable` grouped by `contact_id, denomination_code`
  - [ ] `GetContactsResponse` sealed record with `contacts: ContactSummary[]` + cursor metadata
  - [ ] `ContactSummary` sealed record with `id, name, phone?, email?, balances: BalanceEntry[]`
- [ ] **T2 (AC: 1)** — Add composite index migration `YYYYMMDD_AddContactBalancesIndex.cs`
  - [ ] `create index ix_journal_entries_contact_denom_direction on journal_entries (contact_id, denomination_code, direction) where valid_to is null;`
  - [ ] Validate query plan: `EXPLAIN ANALYZE` must show index scan, not seq scan, at 1000×1000 baseline
- [ ] **T3 (AC: 1)** — Consider SQL view `v_contact_balance_summary` (decision: inline query preferred; view added only if reused by Story 3.4 — deferred)
- [ ] **T4 (AC: 5)** — Emit `contacts_list_viewed` telemetry

### Frontend

- [ ] **T5 (AC: 2, 3, 4, 6)** — Create `apps/owner-workspace/src/app/features/contacts/list/contact-list.component.ts`
  - [ ] Standalone + signals + OnPush; zoneless
  - [ ] `rxResource()` loads `GET /v1/contacts?include=balances`
  - [ ] `stage = computed(() => deriveStage(contacts().length))` — returns `1 | 2 | 3`
  - [ ] Conditional rendering: sort/filter/search/virtual-scroller gated on stage
  - [ ] Empty state → `ContextualNudge kind="add-first-contact"` (NEW kind, additive PR to `libs/domain-ui/context-nudge/`)
- [ ] **T6 (AC: 2)** — Create `ContactListItemComponent` (row) in `libs/domain-ui/contact-list-item/` (new domain-ui primitive)
  - [ ] Consumes `ContactAvatar`, `AmountDisplay`, plain name text
  - [ ] Logical CSS: `margin-inline-start` (not `margin-left`), `padding-block` (not `padding-top`)
- [ ] **T7 (AC: 3)** — `p-virtualScroller` activated only at Stage 3; import via `ScrollerModule` from PrimeNG 21
- [ ] **T8 (AC: 4)** — Additive PR to `libs/domain-ui/context-nudge/context-nudge.component.ts` — add `add-first-contact` to the `kind` union type + copy entries in `add-first-contact.i18n.ts` (AR + EN). Follows the single-owner rule from Epic 1 Story 1.9.

### Tests

- [ ] **T9 (AC: 1, 5)** — `GetContactsHandlerTests` — assert per-denomination aggregation correctness across 10 contacts with mixed denominations
- [ ] **T10 (AC: 1)** — Integration test with Testcontainers Postgres seeded at 1000 contacts × 1000 journal entries per contact — p95 response < 200ms (tagged `[Trait("Category", "Performance")]`)
  - [ ] Assert query plan via `EXPLAIN (FORMAT JSON)` captured in snapshot
- [ ] **T11 (AC: 2, 3)** — Vitest component test: stage derivation logic for counts 0, 5, 10, 11, 30, 50, 51, 200
- [ ] **T12 (AC: 6)** — Playwright E2E: Arabic + English list render; visual regression baseline locked; axe a11y

## Dev Notes

### Critical guardrails

1. **Never sum across denominations.** The API response MUST return an array of per-denomination entries, even when a contact has only one denomination. No `"total"` field anywhere.
2. **Server-side aggregation only.** Frontend never sums journal entries client-side — the correctness invariant lives on the server (`sum(transactions) == stored_balance` for every pair) per Story 3.5's property-based tests.
3. **Stage transition is cosmetic only.** Changing from Stage 2 → 3 does NOT invalidate the data; the stage is a view concern. Component tests verify identical data renders differently at each stage.
4. **ContextualNudge single-owner rule.** Adding `add-first-contact` kind means PR to `libs/domain-ui/context-nudge/` — never a local copy in the Contacts feature folder. ESLint rule `no-contextnudge-fork` enforces.
5. **Virtual-scroller itemSize is fixed.** Desktop 64px, mobile 72px — if a row's rendered height varies, the scroller mis-calculates. Lock row height via CSS `block-size: 64px`.

### Source tree — files to create or touch

```
backend/
├── src/Faktuboh.Api/Slices/Contacts/List/
│   ├── GetContactsRequest.cs                   # NEW (query binding model)
│   ├── GetContactsResponse.cs                  # NEW
│   ├── ContactSummary.cs                       # NEW
│   ├── BalanceEntry.cs                         # NEW
│   └── GetContactsHandler.cs                   # NEW
├── src/Faktuboh.Infrastructure/Persistence/Migrations/
│   └── YYYYMMDD_AddContactBalancesIndex.cs    # NEW

frontend/
├── apps/owner-workspace/src/app/features/contacts/list/
│   ├── contact-list.component.ts               # NEW
│   ├── contact-list.component.html             # NEW
│   ├── contact-list.component.scss             # NEW (logical props)
│   └── contact-list.component.spec.ts          # NEW
├── libs/domain-ui/contact-list-item/
│   ├── contact-list-item.component.ts          # NEW
│   ├── contact-list-item.component.html        # NEW
│   └── contact-list-item.component.spec.ts     # NEW
└── libs/domain-ui/context-nudge/
    ├── context-nudge.component.ts              # MODIFY — add 'add-first-contact' kind
    └── add-first-contact.i18n.ts               # NEW (AR + EN copy)
```

### Aggregation query (single-query, no N+1)

```sql
SELECT
  c.id, c.name, c.phone, c.email,
  COALESCE(
    json_agg(
      json_build_object(
        'denominationCode', je.denomination_code,
        'direction', je.direction,
        'amount', SUM(je.amount)::text
      )
    ) FILTER (WHERE je.id IS NOT NULL),
    '[]'::json
  ) AS balances
FROM contacts c
LEFT JOIN journal_entries je
  ON je.contact_id = c.id AND je.valid_to IS NULL
WHERE c.owner_subject_id = @ownerSubjectId AND c.valid_to IS NULL
GROUP BY c.id
ORDER BY c.name;
```

### Testing standards

Per Story 2.1. Emphasis: **perf test with 1M journal entries via Bogus seed** — capture the `EXPLAIN` plan in a Verify snapshot so PRs changing the query plan fail CI.

### Project Structure Notes

`libs/domain-ui/contact-list-item/` is a new shared primitive added under this story — Phase 2 of UX Component Strategy anticipated it. This keeps the row consistent between `/contacts` and future Epic 4 surfaces (statement recipient list reuses the avatar+name+balance pattern).

### Architecture compliance

- **§4 D4.1** Postgres-primary; no CQRS read model at MVP (NFR-SC1 gate at 100k users)
- **ADR-006** vertical-slice: List lives at `Slices/Contacts/List/`
- **UX-DR5** AmountDisplay variant="compact" for balance pills
- **UX-DR6** ContactAvatar tint from ID hash
- **Glance Maturity Model** §2.6 — Stages 1/2/3 progressive disclosure
- **§5.2.5** Money wire format as decimal string
- **NFR-A9** RTL gate
- **NFR-P7** 200ms balance calculation

### Library/framework requirements

Per Story 2.1. Additional: PrimeNG `ScrollerModule`, `PopoverModule`, `ChipModule` — pull exact APIs via `@primeng/mcp`.

### Testing requirements

| Category | Tools |
|---|---|
| Unit | xUnit v3 + FluentAssertions + FsCheck (balance aggregation properties) |
| Integration | Testcontainers Postgres + Bogus seed at 1M rows + query-plan snapshot |
| Component | Vitest + Angular Testing Library — stage derivation table-driven |
| E2E | Playwright Chromium + WebKit, RTL visual regression |

### Previous Story Intelligence

- **Story 0.6** (bitemporal) — `valid_to IS NULL` filter hides deleted rows
- **Story 1.9** (Phase-1 components) — `ContactAvatar`, `AmountDisplay`, `ContextualNudge` consumed unchanged; the new `add-first-contact` kind is an additive PR per single-owner rule
- **Story 2.1-2.3** (Contacts Create/Edit/Delete) — `Contact` aggregate + `journal_entries` schema assumed in place
- Epic 3 Story 3.5 consumes the same aggregation pattern at the dashboard level; the inline query here is the first test of the index + plan

## References

- [epics.md Story 2.4](../planning-artifacts/epics.md)
- [architecture.md §4 D4.1, §4 D4.4](../planning-artifacts/architecture.md) Postgres + no CQRS
- [architecture.md §5.2.5](../planning-artifacts/architecture.md) Money wire format
- [prd.md FR11, NFR-P7](../planning-artifacts/prd.md)
- [ux-design-specification.md §2.6 Glance Maturity Model](../planning-artifacts/ux-design-specification.md)
- [ux-design-specification.md UX-DR5, UX-DR6](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
