# Story 2.2: Edit existing contact

Status: ready-for-dev

## Story

As a **user with an existing contact whose details have changed**,
I want to **edit the contact's name, phone, email, and notes via `PATCH /v1/contacts/{id}`**,
so that I can **keep the counterparty's information accurate** while the bitemporal interceptor preserves the pre-update state in `contacts_history` for audit, and a concurrent-edit conflict (409) returns RFC 9457 with a `Reload` action preserving my draft.

**Covers requirements:** FR9 (edit contact), FR49 (audit immutability — history side), UX-DR32 (error recovery actions), UX-DR6 (ContactAvatar tint stability).

## Acceptance Criteria

### AC1 — PATCH persists changes with audit row

**Given** an existing contact owned by the authenticated user
**When** the user submits `PATCH /v1/contacts/{id}` with updated fields + `Idempotency-Key` header (UUID v4)
**Then** the `contacts` row is updated in a single transaction
**And** the bitemporal interceptor writes a `contacts_history` row containing the pre-update state with `ValidTo` stamped to the mutation wall-clock per ADR-022
**And** `ContactUpdated` integration event emits via Wolverine same-transaction outbox
**And** the response returns `200 OK` with `UpdateContactResponse` containing the current state

### AC2 — Concurrent-edit conflict returns 409 with reload action

**Given** a contact that has been updated by a second tab/device since the user opened the form
**When** the user submits the PATCH with a stale `If-Match` etag (or stale `RowVersion` per optimistic concurrency)
**Then** the response is `409 Conflict` with `Content-Type: application/problem+json` per RFC 9457
**And** the body contains `code: faktuboh.contacts.concurrent_edit_conflict` + `actions: [{ id: "reload", label: "Reload", preserveDraft: true }]` per UX-DR32
**And** the frontend renders an inline `<p-message severity="warn">` with the Reload button that fetches the latest row + re-applies the user's unsaved changes as Signal Form draft state

### AC3 — ContactAvatar tint remains stable

**Given** the user changes the contact's name from "أحمد" to "Ahmed"
**When** the row re-renders post-save
**Then** `ContactAvatar` tint is derived from `SHA256(contactId)` not from name, so the tint is identical pre- and post-edit per UX-DR6
**And** a component test asserts `computeTint("ahmed-id") === computeTint("ahmed-id")` across name mutations

### AC4 — RFC 9457 on validation failure

**Given** the user submits invalid input (e.g., empty name, duplicate name against another contact for same owner, invalid email)
**When** FluentValidation runs
**Then** the response is `4xx` RFC 9457 with JSON Pointers (`/name`, `/email`) walked onto Signal Forms via `applyServerErrors(form, problemDetails)`
**And** error codes `faktuboh.contacts.duplicate_name_for_owner` / `faktuboh.contacts.email_invalid` exist in `ErrorCatalog`

### AC5 — ProcessingActivity + telemetry

**Given** the slice's endpoint sets `ProcessingActivity.OwnerLedger`
**When** the handler writes rows
**Then** Serilog + OTel + audit log all stamp `ProcessingActivity`
**And** telemetry event `contact_updated` emits with PII-safe payload `{ opaque_user_id, contact_id_hash, fields_changed: string[] }` (field names only, never values)

### AC6 — Silent-success UX

**Given** the edit succeeds
**When** the response arrives
**Then** the drawer/dialog closes; the contact row updates in place with 120ms fade
**And** **no success toast** per UX-DR27
**And** only errors surface as toasts

### AC7 — Frontend form integration

**Given** the Angular owner-workspace at `/contacts/{id}/edit`
**When** the Signal Form submits via `FormMutationConfig`
**Then** `Idempotency-Key` UUID v4 header is attached via HTTP interceptor; `If-Match` header carries the etag loaded with the form
**And** optional fields labeled "(optional)"; no red asterisks
**And** `IftaLabel` ≥1024px / stacked `FloatLabel variant="on"` <1024px per UX-DR28
**And** axe-core reports zero WCAG 2.1 AA violations

## Tasks / Subtasks

### Backend

- [ ] **T1 (AC: 1, 4, 5)** — Create `Slices/Contacts/Edit/` folder
  - [ ] `UpdateContactRequest.cs` — sealed record with `Name?`, `Phone?`, `Email?`, `Notes?` (all optional so PATCH semantics work)
  - [ ] `UpdateContactResponse.cs` — sealed record with current-state fields + static factory `From(Contact)`
  - [ ] `UpdateContactRequestValidator.cs` — FluentValidation rules (name max length, email regex, phone format lax)
  - [ ] `UpdateContactHandler.cs` — Wolverine handler `public async Task<UpdateContactResponse> Handle(UpdateContactCommand cmd, IContactRepository repo, IMessageBus bus)`
  - [ ] Endpoint in `Contacts.Endpoints.cs`: `group.MapPatch("/{id:guid}", ...)` with `If-Match` header binding for etag
- [ ] **T2 (AC: 1)** — Add `RowVersion` / `xmin` concurrency token column to `contacts` via migration; configure EF `IsConcurrencyToken()` on the property
- [ ] **T3 (AC: 2)** — `ContactsExceptionHandler` maps `DbUpdateConcurrencyException` → RFC 9457 409 with code `faktuboh.contacts.concurrent_edit_conflict` + `actions: [{id: "reload"}]`
- [ ] **T4 (AC: 1)** — Publish `ContactUpdated` integration event with payload `{ ContactId, OwnerSubjectId, FieldsChanged: string[] }` — Debts slice consumer is a no-op stub for now
- [ ] **T5 (AC: 4)** — Register error codes in `ErrorCatalog`: `concurrent_edit_conflict` (409), `email_invalid` (400); existing `duplicate_name_for_owner` is reused
- [ ] **T6 (AC: 5)** — Emit `contact_updated` telemetry — NetArchTest verifies no field values appear in payload

### Frontend

- [ ] **T7 (AC: 3, 6, 7)** — Create `apps/owner-workspace/src/app/features/contacts/edit/edit-contact.component.ts`
  - [ ] Standalone + signals + OnPush; zoneless (no `zone.js`)
  - [ ] `rxResource()` loads the contact; etag captured from response header
  - [ ] Signal Form initialized from resource; submit sends `If-Match: {etag}` + `Idempotency-Key: {uuid}`
  - [ ] On 409: show inline `<p-message severity="warn">` with Reload button that refetches + merges draft (draft wins on conflict fields, server wins on untouched fields)
  - [ ] Silent success — drawer closes; row updates in place; no toast
- [ ] **T8 (AC: 4, 7)** — `applyServerErrors(form, problemDetails)` reused from Story 2.1's helper; add route `/contacts/:id/edit` to `contacts.routes.ts`
- [ ] **T9 (AC: 3)** — `ContactAvatar` tint is derived from contact ID (not name) per UX-DR6 — verified in Epic 1 Story 1.9; add component test at edit surface asserting tint unchanged across name mutations

### Tests

- [ ] **T10 (AC: 1, 4)** — `UpdateContactHandlerTests` — happy path + negative paths (invalid email, duplicate name)
- [ ] **T11 (AC: 1, 2)** — `UpdateContactEndpointTests` (Integration, Testcontainers Postgres)
  - [ ] Happy path: PATCH returns 200; `contacts_history` row has correct `ValidTo` and pre-update values
  - [ ] Concurrent-edit: two PATCHes with same stale etag → one succeeds, one returns 409 RFC 9457 with `actions[].id = "reload"`
  - [ ] Idempotency: double-submit with same `Idempotency-Key` returns cached response
  - [ ] `AsOf(t)` time-travel returns pre-update state
- [ ] **T12 (AC: 6, 7)** — Vitest + Angular Testing Library: `edit-contact.component.spec.ts`
  - [ ] 409 branch renders Reload button; clicking it refetches + preserves draft
  - [ ] Tint stability across name mutation
  - [ ] No success toast on happy path (asserted via test-only `ToastService` spy)

## Dev Notes

### Critical guardrails

1. **Audit row ALWAYS written.** The bitemporal interceptor is NOT optional — EF integration test asserts one `contacts_history` row per PATCH even if only `notes` changed.
2. **`If-Match` etag via `xmin`** (Postgres system column exposed as `RowVersion` in EF). Do not use a `last_modified_at` column as the concurrency token — timestamps collide on fast writes.
3. **Draft preservation on 409.** Do not naively re-fetch and overwrite the user's in-flight changes. Per UX-DR32 the Reload action MUST merge (draft wins on touched fields).
4. **Field-name-only telemetry.** `fields_changed: ["name", "email"]` is allowed; actual before/after values are PII and go only to the audit log (encrypted per AR-018).
5. **Silent success.** No `MessageService.add({severity:'success', ...})` on happy path.

### Source tree — files to create or touch

```
backend/
├── src/Faktuboh.Api/Slices/Contacts/
│   └── Edit/
│       ├── UpdateContactRequest.cs           # NEW
│       ├── UpdateContactResponse.cs          # NEW
│       ├── UpdateContactRequestValidator.cs  # NEW
│       └── UpdateContactHandler.cs           # NEW
├── src/Faktuboh.Domain/Contacts/
│   └── Contact.cs                             # MODIFY — add Update(...) method + RowVersion
├── src/Faktuboh.Infrastructure/Persistence/
│   ├── Configurations/ContactConfiguration.cs # MODIFY — IsConcurrencyToken + xmin mapping
│   └── Migrations/YYYYMMDD_AddContactRowVersion.cs  # NEW
├── src/Faktuboh.Application/Errors/ErrorCatalog.cs   # MODIFY — 2 new codes
└── tests/Faktuboh.Api.Tests/Slices/Contacts/Edit/
    ├── UpdateContactHandlerTests.cs          # NEW
    └── UpdateContactEndpointTests.cs         # NEW (Integration)

frontend/apps/owner-workspace/src/app/features/contacts/edit/
├── edit-contact.component.ts                  # NEW
├── edit-contact.component.html                # NEW
├── edit-contact.component.scss                # NEW (logical props only)
└── edit-contact.component.spec.ts             # NEW
```

### Postgres migration fragment

```sql
ALTER TABLE contacts ADD COLUMN xmin_snapshot xid DEFAULT xmin;
-- EF Core maps this to a byte[] RowVersion via Npgsql xid8 support; configured in ContactConfiguration
```

### Testing standards (per architecture §5.3.3)

Inherit from Story 2.1. Emphasis here on the **concurrent-edit integration test** — two parallel `HttpClient.PatchAsync` calls with same stale etag, assert exactly one 200 + one 409.

### Project Structure Notes

Sibling folder `Slices/Contacts/Edit/` alongside `Slices/Contacts/Create/` (from Story 2.1). No cross-slice coupling introduced. `ContactUpdated` integration event adds a second message type; Debts slice's projection stub grows to handle both `ContactCreated` + `ContactUpdated` — the no-op consumer pattern continues validating NetArchTest #3.

### Architecture compliance

- **ADR-022** (application-level bitemporal) — interceptor writes `contacts_history` on PATCH
- **ADR-024** (Minimal APIs + RFC 9457) — PATCH endpoint via `group.MapPatch`; concurrency exceptions → RFC 9457
- **§4 D4.9** (RFC 9457 actions array per UX-DR32)
- **§4 D4.11** (Idempotency-Key also on PATCH)
- **§5.2.1** snake_case; **§5.2.3** camelCase JSON; **§5.2.4** timestamps as `timestamptz`
- **UX-DR6** (ContactAvatar deterministic tint from contact ID hash)
- **UX-DR27** (silent success)
- **UX-DR28** (IftaLabel / FloatLabel)
- **UX-DR32** (error recovery actions array: id, label, preserveDraft)

### Library/framework requirements

Per Story 2.1. Additional: `Microsoft.EntityFrameworkCore` optimistic-concurrency extensions; Npgsql `xmin` pseudo-column support (built-in since Npgsql 8).

### Testing requirements

| Category | Tools |
|---|---|
| Unit | xUnit v3 + FluentAssertions + NSubstitute |
| Integration | `WebApplicationFactory<Program>` + Testcontainers Postgres + real Wolverine |
| Component | Vitest + Angular Testing Library |
| E2E | Playwright — deferred; covered by Story 2.4 list surface's E2E |

### Previous Story Intelligence

- **Story 0.5** (Idempotency-Key middleware) — PATCH endpoints also guarded by `Idempotency-Key`; reuse existing middleware
- **Story 0.6** (`IBitemporal` interceptor) — writes `contacts_history` on UPDATE/DELETE; this story is the first PATCH consumer
- **Story 0.8** (NetArchTest fitness #3) — `ContactUpdated` integration event validated by same fitness test
- **Story 1.9** (Phase-1 component library) — `ContactAvatar` consumed unchanged; tint stability is Epic 1's contract
- **Story 2.1** (Contacts/Create) — `Contact` aggregate, `ContactException` base, `ErrorCatalog` entry-point, `ContactsExceptionHandler` all landed here; this story extends them

## References

- [epics.md Story 2.2](../planning-artifacts/epics.md)
- [architecture.md §4 D4.9](../planning-artifacts/architecture.md) RFC 9457 actions
- [architecture.md §4 D4.11](../planning-artifacts/architecture.md) Idempotency
- [architecture.md §5.3.1, §5.3.2](../planning-artifacts/architecture.md) Slice conventions
- [adrs/adr-022-application-level-bitemporal.md](../planning-artifacts/adrs/adr-022-application-level-bitemporal.md)
- [adrs/adr-024-minimal-apis-framework.md](../planning-artifacts/adrs/adr-024-minimal-apis-framework.md)
- [prd.md FR9](../planning-artifacts/prd.md) Edit contact
- [ux-design-specification.md UX-DR27, UX-DR28, UX-DR32](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
