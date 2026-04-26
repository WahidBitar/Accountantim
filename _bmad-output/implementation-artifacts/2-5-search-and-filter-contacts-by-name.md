# Story 2.5: Search and filter contacts by name

Status: ready-for-dev

## Story

As a **power user with 50+ contacts**,
I want a **persistent search bar that filters contacts by name in real time with 150ms debounce and grapheme-aware matching**,
so that I can **jump directly to a specific counterparty without scrolling**, and the same `GET /v1/contacts?q=...` endpoint also powers the `AutoComplete` in debt-entry forms so there is exactly one search truth.

**Covers requirements:** FR12 (search contacts), NFR-A9 (grapheme-aware Arabic matching), UX-DR30 (empty search state), UX Consistency Patterns → Search.

## Acceptance Criteria

### AC1 — Grapheme-aware substring search

**Given** the user has ≥ 1 contact named with Arabic combining marks (e.g., "أحمد" which contains `U+0623` + `U+062D` + `U+0645` + `U+062F`)
**When** the user types "احمد" (different underlying code points — `U+0627` instead of `U+0623`)
**Then** the search normalizes both the query and the stored name via Unicode NFKD + diacritic stripping before matching
**And** matches succeed regardless of alif-variant (`ا` / `أ` / `إ` / `آ` all fold to base) and regardless of tashkeel diacritics
**And** server returns matching contacts within 150ms p95 at 1000-contact baseline

### AC2 — Endpoint powers both surfaces

**Given** the contacts search endpoint `GET /v1/contacts?q=<query>&limit=10`
**When** the frontend issues the request
**Then** the response is a `ContactSearchResponse` with `contacts: ContactSummary[]` sorted by match relevance (exact prefix > substring > fuzzy — fuzzy deferred to post-MVP)
**And** the same endpoint serves both (a) the `/contacts` list's search bar and (b) the `AutoComplete` in `/journal-entries/new` debt-entry form
**And** `limit=10` by default; the list surface may request `limit=100`

### AC3 — Frontend 150ms debounce + scroll-snap filter chips

**Given** the Stage 3 list surface (≥ 51 contacts) or the debt-entry AutoComplete
**When** the user types
**Then** input debounces at 150ms via `rxjs.debounceTime(150)` on a subject-driven pipeline (or equivalent via signals + effect)
**And** active filter chips (e.g., "USD only", "Receivable only") attach with `scroll-snap-type: x mandatory` on the chip strip
**And** the search bar renders with `<p-iconField>` + `<p-inputIcon>` (search icon) + `<p-inputText>` per PrimeNG 21

### AC4 — Empty-result state

**Given** the user types a query with no matches
**When** the response is an empty array
**Then** the list renders UX-DR30 empty state: "No matches found" + ghost button `Clear filter` that resets the query signal
**And** the button click emits telemetry `search_cleared`

### AC5 — AutoComplete integration in debt-entry

**Given** the Story 3.1 debt-entry form
**When** the user focuses the contact field
**Then** `<p-autoComplete>` triggers `GET /v1/contacts?q={typed}&limit=10` via `rxResource()`
**And** render template shows `ContactAvatar` + name + optional phone
**And** keyboard navigation (Up/Down/Enter) works; axe a11y passes
**And** if the typed query matches no existing contact, a footer "Add \"{query}\" as new contact" appears — routes to `/contacts/new?name={query}` (deep-link continuity deferred to Story 3.1 task)

### AC6 — Telemetry and performance

**Given** the endpoint stamps `ProcessingActivity.OwnerLedger`
**When** search runs
**Then** telemetry `contact_searched` emits with PII-safe payload `{ opaque_user_id, query_length, result_count, debounce_hit: bool }` (never the query itself — PII)
**And** p95 response time < 150ms at 1000-contact seeded baseline

## Tasks / Subtasks

### Backend

- [ ] **T1 (AC: 1, 2)** — Add optional `q` query parameter to `GetContactsHandler` from Story 2.4
  - [ ] Bind `q` (nullable string) via `[AsParameters]` on `GetContactsRequest`
  - [ ] If `q` present, add `WHERE normalized_name LIKE '%' || @normalizedQuery || '%'`
  - [ ] `normalized_name` is a new generated column + expression index on `contacts`
- [ ] **T2 (AC: 1)** — Migration `YYYYMMDD_AddContactSearchIndex.cs`
  - [ ] Add generated column: `normalized_name text generated always as (lower(unaccent(name))) stored;`
  - [ ] Add trigram index: `create index ix_contacts_normalized_name_trgm on contacts using gin (normalized_name gin_trgm_ops);`
  - [ ] Requires extensions: `create extension if not exists unaccent;` + `create extension if not exists pg_trgm;` — add to initial migration if not already present
  - [ ] Custom dictionary entries for Arabic alif variants — via `CREATE TEXT SEARCH DICTIONARY` or application-side normalization if `unaccent` doesn't cover them. Verify with test corpus before committing migration shape.
- [ ] **T3 (AC: 1)** — C# helper `ArabicNormalizer.Normalize(string)` handles alif variants (`ا`/`أ`/`إ`/`آ` → `ا`), tamarbuta `ة` → `ه`, tashkeel stripping via `System.Text.RegularExpressions` on the combining-mark Unicode category. Applied to the incoming `q` before SQL binding.
- [ ] **T4 (AC: 2)** — `ContactSearchResponse` reuses `ContactSummary` from Story 2.4 — no new DTO; the endpoint already returns `GetContactsResponse` so just document that `q` parameter activates filtering
- [ ] **T5 (AC: 6)** — Emit `contact_searched` telemetry via AR-047 primitive

### Frontend

- [ ] **T6 (AC: 3, 4)** — Update `contact-list.component.ts` from Story 2.4
  - [ ] Add `searchQuery = signal('')` + `debouncedQuery = toSignal(toObservable(searchQuery).pipe(debounceTime(150)))`
  - [ ] `rxResource` depends on `debouncedQuery()`; triggers `GET /v1/contacts?q={query}&include=balances`
  - [ ] Empty-result state per UX-DR30 with Clear filter ghost button
- [ ] **T7 (AC: 5)** — Create `libs/domain-ui/contact-autocomplete/contact-autocomplete.component.ts`
  - [ ] Consumes `<p-autoComplete>` with custom `<ng-template pTemplate="item">` rendering `ContactAvatar` + name
  - [ ] `completeMethod` event triggers search; `suggestions()` signal backed by `rxResource`
  - [ ] "Add new contact" footer slot via `<ng-template pTemplate="footer">`; emits `createNew.emit(query)` output
  - [ ] This component will be consumed by Story 3.1's debt-entry form
- [ ] **T8 (AC: 3)** — Search bar consumes `<p-iconField>` wrapper + `<p-inputIcon>`; RTL verified

### Tests

- [ ] **T9 (AC: 1)** — `ArabicNormalizerTests` — table-driven over alif variants, tashkeel, tamarbuta, mixed LTR/RTL queries
  - [ ] FsCheck property: `Normalize(Normalize(s)) == Normalize(s)` (idempotence)
- [ ] **T10 (AC: 1, 6)** — Integration test at 1000-contact seed with mix of Arabic/English names
  - [ ] Assert query time p95 < 150ms
  - [ ] Assert `EXPLAIN` shows trigram index scan
- [ ] **T11 (AC: 3, 4, 5)** — Vitest + Angular Testing Library
  - [ ] Debounce verified via fake timers (typing "ahmed" over 100ms triggers 1 request, not 5)
  - [ ] Empty-result state renders correctly
  - [ ] AutoComplete item template renders ContactAvatar + name
- [ ] **T12 (AC: 1)** — Playwright E2E: Arabic search flow with alif variant + axe a11y + RTL visual regression

## Dev Notes

### Critical guardrails

1. **Query is PII — never log it.** Telemetry payload contains `query_length` and `result_count` only. The raw query goes to the handler, stays in memory, is not persisted or emitted. Serilog scrubbing rule must cover the `q` parameter.
2. **Normalization must be deterministic + idempotent.** `Normalize(Normalize(s)) == Normalize(s)` is a property test. If anyone adds a new rule later, they must add its property case.
3. **Single search endpoint, two consumers.** The AutoComplete and the list bar both hit the same endpoint. Do not create a separate `/v1/contacts/autocomplete` route — one query surface is the architecture intent.
4. **Trigram index is mandatory at 1000+ contacts.** Without it, substring match degrades to seq scan and NFR-P7 (200ms general) is blown. The migration is a ship-blocker.
5. **LIKE '%q%' requires pg_trgm** to use the index. Plain btree won't help for unanchored substring.

### Source tree — files to create or touch

```
backend/
├── src/Faktuboh.Api/Slices/Contacts/List/
│   ├── GetContactsRequest.cs                   # MODIFY — add Q property
│   └── GetContactsHandler.cs                   # MODIFY — normalize q + add LIKE clause
├── src/Faktuboh.Application/Text/
│   └── ArabicNormalizer.cs                     # NEW
├── src/Faktuboh.Infrastructure/Persistence/Migrations/
│   └── YYYYMMDD_AddContactSearchIndex.cs      # NEW

frontend/
├── apps/owner-workspace/src/app/features/contacts/list/
│   └── contact-list.component.ts               # MODIFY — add search signal + debounce
└── libs/domain-ui/contact-autocomplete/
    ├── contact-autocomplete.component.ts       # NEW
    ├── contact-autocomplete.component.html     # NEW
    └── contact-autocomplete.component.spec.ts  # NEW
```

### Postgres migration fragment

```sql
CREATE EXTENSION IF NOT EXISTS unaccent;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

ALTER TABLE contacts
  ADD COLUMN normalized_name text
  GENERATED ALWAYS AS (lower(unaccent(name))) STORED;

CREATE INDEX ix_contacts_normalized_name_trgm
  ON contacts USING gin (normalized_name gin_trgm_ops);
```

### Testing standards

Per Story 2.1. Emphasis: **grapheme-aware test corpus** — curate 50+ Arabic name pairs with variant spellings as a golden file. FsCheck generates random Arabic strings and asserts `Normalize` idempotence.

### Project Structure Notes

Search is a modification to the existing Story 2.4 List handler — not a new Slices/Contacts/Search/ folder. Per architecture §5.3.2 one-folder-per-action, "search" is a parameter of List, not a separate action. If fuzzy matching or semantic search lands post-MVP, that's a separate slice.

### Architecture compliance

- **§5.3.2** one-folder-per-action — search folded into List
- **NFR-A9** Arabic grapheme support + RTL parity
- **NFR-P7** 200ms general; this story targets 150ms for search
- **UX Consistency Patterns → Search** (debounce, scroll-snap chips, empty state)
- **UX-DR30** empty-state ghost button
- **AR-047** telemetry primitive

### Library/framework requirements

Per Story 2.1. Additional:
- Npgsql extensions `unaccent` + `pg_trgm` (Postgres-native, no NuGet)
- PrimeNG `<p-iconField>`, `<p-inputIcon>`, `<p-autoComplete>` — query exact APIs via `@primeng/mcp`
- `rxjs.debounceTime` + `toObservable`/`toSignal` Angular 21 interop

### Testing requirements

| Category | Tools |
|---|---|
| Unit | xUnit v3 + FsCheck (normalizer idempotence) |
| Integration | Testcontainers Postgres + 1000-contact Arabic seed |
| Component | Vitest + Angular Testing Library (fake timers for debounce) |
| E2E | Playwright — Arabic alif-variant flow + axe a11y |

### Previous Story Intelligence

- **Story 2.4** (List contacts) — `GetContactsHandler` + indexes already exist; this story extends them
- **Story 1.1** (bilingual shell + RTL pipeline) — `dir="rtl"` is the application-root concern; this story's search bar inherits direction
- **Story 3.1** will consume `contact-autocomplete` for the debt-entry form — the component is built here but unused until then (its `.spec.ts` must pass in isolation)

## References

- [epics.md Story 2.5](../planning-artifacts/epics.md)
- [architecture.md §5.3.2](../planning-artifacts/architecture.md) slice organization
- [prd.md FR12, NFR-A9](../planning-artifacts/prd.md)
- [ux-design-specification.md UX Consistency Patterns → Search](../planning-artifacts/ux-design-specification.md)
- [ux-design-specification.md UX-DR30](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
