# Story 1.7: Account deletion with GDPR erasure

Status: ready-for-dev

## Story

As a **user who wants to leave the service**,
I want **to delete my account and have my personal data cryptographically erased while transaction-audit structure is preserved**,
so that **I exercise my GDPR right to erasure without compromising the ledger's audit integrity (FR6, NFR-S7, AR-042)**.

**Covers requirements:** FR6 (account deletion + GDPR erasure), NFR-S7 (cryptographic erasure), AR-042 (per-data-class erasure scaffolding).

**Why this story now:** Per the reorder, 1.7 follows 1.6 — the disclaimer is in place; deletion is the GDPR exit ramp. This story exercises the cryptographic posture per data class (architecture §3.5) for the first time end-to-end: key-shred + tombstoning + audit anonymization. Treat with extra care — irreversible operation.

## Acceptance Criteria

### AC1 — `/profile/delete` route + destructive-action confirmation

**Given** an authenticated user navigates to `/profile/delete`
**When** the page renders
**Then** a destructive-action confirmation dialog (per UX-DR29) explains: "This will permanently erase your personal data. Transaction history is anonymized but retained for audit. This cannot be undone."
**And** the user must type their email address (case-insensitive match) into a confirmation field to enable the "Delete my account" button
**And** the destructive button uses `severity="danger"` red color from `--danger` token
**And** a "Cancel" secondary button returns to `/profile`

### AC2 — LegalHold check before erasure

**Given** the user submits the deletion confirmation
**When** the deletion handler runs
**Then** the handler queries `LegalHold` aggregate for any active hold scoped to this user
**And** if an active hold exists, the deletion is **NOT** executed; instead a deferred-erasure record is created (per architecture §3.5 LegalHold)
**And** the user sees a localized message: "Your account is currently under legal hold. Your deletion request has been recorded and will execute when the hold is released."
**And** if no active hold, deletion proceeds atomically per AC3-AC7

### AC3 — Subject DEK destroyed (key-shred)

**Given** deletion is permitted (no active hold)
**When** the deletion executes
**Then** the user's row in `subject_keys` table is **destroyed** (hard DELETE — not soft-delete)
**And** the per-subject DEK held in Azure Key Vault is destroyed
**And** all transactional rows (debts, journal entries, etc.) that referenced this subject's DEK become irrecoverable ciphertext
**And** the row count + deletion event are recorded in the audit log via `SubjectKeyDestroyed` domain event

### AC4 — Contact PII tombstoned

**Given** the user has contact rows referencing other natural persons
**When** the deletion executes
**Then** each contact row is **hard-deleted** (no soft-delete)
**And** every FK referencing the contact (e.g., from `journal_entries.contact_id`) is replaced with a permanent **tombstone token** (random UUID v4 specific to the deleted contact, stored in `contact_tombstones`)
**And** re-hydration from the tombstone is impossible (no lookup table maps token → original ID)
**And** the audit log references the tombstone token, not the original contact ID

### AC5 — Audit log anonymized (opaque identifiers)

**Given** the audit log contains entries with the user's identifier
**When** deletion executes
**Then** all entries are anonymized: `user_id` → `User-7a3f` (last 4 hex of `sha256(user_id)` salted with deletion timestamp)
**And** related contact references → `Contact-2b1e` (same hashing pattern with tombstone token)
**And** no lookup table exists to reverse the mapping (irreversibility assertion in integration test)
**And** transaction amounts + dates + denominations + mathematical relationships remain intact so audit balance reports still calculate

### AC6 — Auth0 user delete via Management API

**Given** local data is erased
**When** the deletion handler proceeds
**Then** Auth0 Management API `Users.Delete(user_id)` is invoked
**And** all sessions for this user are immediately revoked (Auth0 default)
**And** the user is logged out + redirected to a confirmation page: "Your account has been erased. We're sorry to see you go." in active locale

### AC7 — `SubjectKeyDestroyed` domain event fires

**Given** the deletion completes
**When** the transaction commits
**Then** a `SubjectKeyDestroyed` integration event publishes via Wolverine (same-transaction outbox)
**And** downstream projection handlers (analytics, reporting) consume the event and remove any cached references
**And** the event payload contains `tenant_id` + `opaque_user_id` + `deleted_at` — no PII

### AC8 — Atomic transaction or full rollback

**Given** the deletion sequence
**When** any step fails (Auth0 Management API timeout, DB write failure, key vault destroy failure)
**Then** the entire deletion rolls back — no partial erasure
**And** the user sees a localized error: "Deletion failed. Please try again or contact support." with a retry option
**And** an alert fires to ops channel with the failure details (no PII)

### AC9 — Telemetry `account_deleted`

**Given** AR-047 telemetry primitive
**When** deletion completes successfully
**Then** `account_deleted` event emits with `{ opaque_user_id, deletion_reason: 'user_initiated' }`
**And** no PII fields appear
**And** NetArchTest asserts the schema

### AC10 — Irreversibility integration test

**Given** the integration test suite
**When** the deletion test runs
**Then** after deletion, querying for the user's PII returns zero rows
**And** querying for the deleted user's transactional rows shows ciphertext that cannot be decrypted (DEK gone)
**And** the test attempts to reverse-map `User-7a3f` → original ID and fails (no lookup table exists)
**And** audit balance calculations on the anonymized data still produce the same totals as before deletion

## Tasks / Subtasks

### Backend — Erasure orchestrator

- [ ] **T1 (AC: 2-7)** — Create `backend/src/Faktuboh.Api/Slices/Users/Delete/`
  - [ ] `DeleteAccountRequest.cs` — sealed record with `confirmEmail` (string, must match case-insensitive)
  - [ ] `DeleteAccountResponse.cs` — sealed record with `status: 'deleted' | 'deferred_under_legal_hold'`
  - [ ] `DeleteAccountHandler.cs` — orchestrates: LegalHold check → key-shred → tombstone → audit anonymize → Auth0 delete → emit events
  - [ ] `DeleteAccountRequestValidator.cs` — `confirmEmail` non-empty + matches authenticated user's email
- [ ] **T2 (AC: 7)** — Domain event `SubjectKeyDestroyed`
  - [ ] `Faktuboh.Domain.Events.IntegrationEvents.SubjectKeyDestroyed` sealed record
  - [ ] Payload: `tenant_id`, `opaque_user_id`, `deleted_at`
- [ ] **T3 (AC: 3)** — Implement key-shred
  - [ ] `IKeyVaultService.DestroyDek(userId)` — destroys Azure Key Vault DEK + deletes row from `subject_keys` table
  - [ ] Both operations atomic (saga pattern: AKV destroy is irreversible + last; DB delete first; if AKV fails, alert ops + manual remediation)
- [ ] **T4 (AC: 4)** — Implement contact tombstoning
  - [ ] `IContactRepository.HardDeleteWithTombstone(contactId)` — INSERT row in `contact_tombstones` (token + tombstoned_at) + HARD DELETE from `contacts` + UPDATE all FKs to tombstone token
  - [ ] Migration to add `contact_tombstones` table
- [ ] **T5 (AC: 5)** — Implement audit anonymization
  - [ ] `IAuditLogAnonymizer.Anonymize(userId, contactIds[])` — UPDATE all rows in `audit_log` where `user_id` matches → set `user_id` to `User-{hash[0..4]}`
  - [ ] Same for contact IDs → `Contact-{hash[0..4]}`
  - [ ] Salt with deletion timestamp to prevent rainbow-table attack
- [ ] **T6 (AC: 6)** — Auth0 Management API integration
  - [ ] `IAuth0ManagementService.DeleteUser(userId)` wraps the Management API call
  - [ ] Retries 3× with exponential backoff on transient errors
  - [ ] On final failure, throw `Auth0DeleteFailedException` (caught by orchestrator → rollback)
- [ ] **T7 (AC: 8)** — Atomic transaction wrapper
  - [ ] All DB operations inside one EF Core transaction
  - [ ] Auth0 delete + AKV destroy outside the transaction; if either fails, raise alert + execute compensating actions documented in `docs/ops/erasure-recovery.md`

### Backend — LegalHold check

- [ ] **T8 (AC: 2)** — `ILegalHoldRepository.HasActiveHoldForUser(userId)` query
  - [ ] Returns true if any `LegalHold` row in state `Active` has `Scope` matching this user
  - [ ] If true, create `DeferredErasure` record + return `deferred_under_legal_hold` response
- [ ] **T9 (AC: 2)** — `DeferredErasure` aggregate (minimal Epic 1 scope)
  - [ ] Records pending deletion request with `requested_at`, `user_id`, `legal_hold_id`
  - [ ] Surfaces when hold releases (deferred trigger logic — Epic 7 wires the release event handler)
  - [ ] Migration to add `deferred_erasure` table

### Telemetry + audit

- [ ] **T10 (AC: 9)** — Emit `account_deleted` telemetry
  - [ ] After successful completion, `TelemetryEmitter.Emit("account_deleted", { opaque_user_id, deletion_reason: "user_initiated" })`
  - [ ] NetArchTest schema assertion

### Frontend — deletion flow

- [ ] **T11 (AC: 1)** — Create `apps/owner-workspace/src/app/features/profile/delete/`
  - [ ] `delete-account.component.ts` — standalone + OnPush
  - [ ] Destructive-action confirmation dialog per UX-DR29
  - [ ] Email confirmation field with case-insensitive match validation
  - [ ] Submit button disabled until match
- [ ] **T12 (AC: 6)** — Post-deletion UX
  - [ ] On success, clear all client state + redirect to `/legal/erasure-confirmed` standalone page
  - [ ] Page text: "Your account has been erased. We're sorry to see you go." (AR + EN)
- [ ] **T13 (AC: 2)** — LegalHold deferred state UX
  - [ ] On `deferred_under_legal_hold` response, render localized message
  - [ ] Provide a "Got it" button that returns to `/profile`

### Tests

- [ ] **T14 (AC: 1-10)** — Backend integration tests `DeleteAccountEndpointTests`
  - [ ] Happy path: full erasure sequence; verify all 10 ACs
  - [ ] LegalHold-active path: deferred record created; no erasure executed
  - [ ] AKV destroy failure: rollback + alert (mock failure)
  - [ ] Auth0 delete failure: rollback + alert
  - [ ] Irreversibility assertion (AC10): query post-deletion → zero PII rows; ciphertext cannot decrypt; no reverse-mapping table exists
  - [ ] Audit balance still calculates the same totals after anonymization (mathematical relationships preserved)
- [ ] **T15 (AC: 1, 11)** — Playwright E2E `apps/owner-workspace/e2e/profile/delete-account.spec.ts`
  - [ ] Happy path: navigate to `/profile/delete` → type email → confirm → erasure-confirmed page
  - [ ] Cancel path: cancel returns to `/profile`
  - [ ] axe-core zero violations
  - [ ] LTR + RTL parity
- [ ] **T16 (AC: 3-5)** — Unit tests for `DeleteAccountHandler`
  - [ ] Sequence ordering: LegalHold check → key-shred → tombstone → audit anonymize → Auth0 delete → emit event
  - [ ] Each step's failure triggers rollback
- [ ] **T17 (AC: 7)** — Wolverine integration event consumer test
  - [ ] Test-only `SubjectKeyDestroyedHandler` asserts event shape + payload PII-safe

## Dev Notes

### Critical guardrails (do not violate)

1. **Irreversible operation — extra QA care.** Any bug here costs user data permanently. Code review by Wahid + Murat before merge.
2. **LegalHold check before any destruction.** Skipping this is a regulatory failure mode.
3. **Cryptographic erasure, not soft-delete.** Per AR-042: transactional rows preserve audit structure via key-shred (DEK destroyed → ciphertext irrecoverable); contact PII is hard-deleted + tombstoned; audit log anonymized but balance-preserving.
4. **No lookup table from anonymized identifiers back to originals.** This is the irreversibility invariant — integration test asserts.
5. **Atomic transaction or full rollback.** Partial erasure is worse than no erasure.
6. **Auth0 delete is the LAST step (after local erasure).** If Auth0 succeeds but local fails, the user is locked out but their data remains — recoverable only by re-syncing Auth0. If local succeeds but Auth0 fails, the user can re-login but has no data — recoverable by ops manually re-deleting.
7. **Email confirmation field — case-insensitive match.** Prevents accidental deletion via copy-paste with case differences.
8. **`account_deleted` telemetry PII-safe.** No email, no name, no IP. Only `opaque_user_id` (`sha256(user_id)`).
9. **Deferred erasure trigger logic deferred to Epic 7.** This story creates the `DeferredErasure` aggregate + records pending deletions; the trigger that fires when a hold releases is Epic 7 work.

### Source tree — files to create or touch

```
apps/owner-workspace/
├── src/app/features/profile/delete/
│   ├── delete-account.component.ts                 # NEW
│   ├── delete-account.component.html               # NEW
│   ├── delete-account.component.scss               # NEW
│   └── delete-account.component.spec.ts            # NEW
├── src/app/features/legal/
│   └── erasure-confirmed-page.component.ts         # NEW
└── e2e/profile/
    └── delete-account.spec.ts                      # NEW

backend/src/Faktuboh.Api/Slices/Users/Delete/
├── DeleteAccountRequest.cs                         # NEW
├── DeleteAccountResponse.cs                        # NEW
├── DeleteAccountRequestValidator.cs                # NEW
└── DeleteAccountHandler.cs                         # NEW

backend/src/Faktuboh.Domain/
├── Events/IntegrationEvents/
│   └── SubjectKeyDestroyed.cs                      # NEW
├── Users/
│   └── DeferredErasure.cs                          # NEW
├── Contacts/
│   └── ContactTombstone.cs                         # NEW
└── LegalHold/
    └── ILegalHoldRepository.cs                     # MODIFY (add HasActiveHoldForUser)

backend/src/Faktuboh.Infrastructure/
├── Services/
│   ├── KeyVaultService.cs                          # MODIFY (add DestroyDek)
│   ├── Auth0ManagementService.cs                   # MODIFY (add DeleteUser)
│   └── AuditLogAnonymizer.cs                       # NEW
├── Persistence/Migrations/
│   ├── YYYYMMDDHHMMSS_AddContactTombstones.cs      # NEW
│   └── YYYYMMDDHHMMSS_AddDeferredErasure.cs        # NEW
└── Repositories/
    └── ContactRepository.cs                        # MODIFY (HardDeleteWithTombstone)

docs/ops/
└── erasure-recovery.md                             # NEW (compensating actions playbook)
```

### Testing standards

| Level | Coverage | Tools |
|---|---|---|
| Unit | 70% | xUnit v3 + NSubstitute (handler orchestration) |
| Integration | All 10 ACs + LegalHold deferred + failure rollback | WebApplicationFactory + Testcontainers Postgres + mock Auth0 + mock Key Vault |
| E2E | Happy + cancel + LegalHold deferred | Playwright with mock backend (testing the UI flow, not real erasure) |
| Manual | Pre-prod erasure simulation | Documented in `docs/ops/erasure-recovery.md` |
| Property-based | Audit balance preservation post-anonymization | FsCheck — generate transaction sets, anonymize, assert balance equal |

**Forbidden:**
- Soft-delete instead of cryptographic erasure
- Skipping LegalHold check
- Storing reverse-mapping table from anonymized → original
- Partial erasure on failure
- PII in `account_deleted` telemetry
- Auth0 delete before local erasure

### Project Structure Notes

This story is the highest-risk in Epic 1 — irreversible data destruction. It depends on Epic 0's cryptographic foundation (Key Vault, per-subject DEK) and the LegalHold aggregate scaffold. The `DeferredErasure` aggregate is a placeholder for Epic 7's hold-release trigger.

**Detected variances to reconcile:**
1. AKV destroy semantics — Standard tier supports soft-delete (recoverable for 90 days); for true erasure, use `purge` after destroy. Document the 90-day recovery window in `docs/ops/erasure-recovery.md` — users informed in the disclaimer? Wahid decides.
2. Contact tombstoning — when a deleted user had contacts, those contacts may have been counterparties of OTHER users' debts. Decision: tombstone only when no other user references them; otherwise keep the contact alive but anonymize PII. Default at MVP: tombstone unconditionally; flag for v2 if cross-user-contact-sharing materializes.
3. The `User-7a3f` opaque identifier format — last 4 hex of salted SHA-256 may collide for ~65k users. Acceptable at MVP scale; v2 may extend to 8 hex.

### Architecture compliance

- **FR6** (account deletion + GDPR erasure) — directly satisfied
- **NFR-S7** (cryptographic erasure) — key-shred + tombstone + audit anonymize
- **AR-042** (per-data-class erasure scaffolding) — three classes (transactional / aggregates / contact PII) handled per architecture §3.5
- **§3.5 Cross-Cutting → Cryptographic posture per data class** — directly implemented
- **§3.5 LegalHold** — first real consumer of LegalHold check
- **ADR-021** (Auth0) — Management API deletes user
- **ADR-025** (Key Vault Standard) — DEK destruction

### Library / framework requirements

| Layer | Package | Version |
|---|---|---|
| Auth0 Management | auth0 (NuGet) | latest stable |
| Key Vault | Azure.Security.KeyVault.Keys | latest stable |
| Wolverine | latest stable | (outbox for SubjectKeyDestroyed) |
| Property tests | FsCheck | latest stable (audit balance preservation) |

### Testing requirements

- **Unit**: handler orchestration sequence, rollback on each failure point
- **Integration**: full erasure with mocks — verify each AC; assert irreversibility (AC10)
- **E2E**: UI flow happy + cancel + deferred
- **Property-based**: audit balance preservation across anonymization
- **Manual**: pre-prod simulation playbook in `docs/ops/erasure-recovery.md`

### Previous Story Intelligence

**Prerequisites:**
- **Epic 0 Story 0.6** — `IBitemporal` interceptor (audit log writes)
- **Epic 0 (cryptographic foundation)** — per-subject DEK in Key Vault, `subject_keys` table
- **Story 1.2** — Auth0 user creation (so there's a user to delete)
- **Story 1.5** — `/profile` page (deletion entry point)
- **Story 1.6** — disclaimer confirms erasure terms

**Downstream:**
- Epic 7 wires `DeferredErasure` hold-release trigger
- Epic 2 contact creation (Story 2.1) ensures contact rows exist for tombstoning
- Epic 3 debt creation ensures journal entries reference contacts

### Latest Technical Information

**Azure Key Vault Standard tier — DEK destruction:**
```csharp
// Soft-delete with 90-day recoverability (Standard tier default)
await _keyClient.StartDeleteKeyAsync(dekName);
// True erasure (cannot recover):
await _keyClient.PurgeDeletedKeyAsync(dekName);
```
Document the 90-day window in `docs/ops/erasure-recovery.md`.

**Auth0 Management API user delete:**
```csharp
var client = new ManagementApiClient(token, new Uri($"https://{domain}/api/v2"));
await client.Users.DeleteAsync(userId);
// Auth0 default: revokes all sessions immediately
```

**Audit anonymization SQL pattern:**
```sql
UPDATE audit_log
SET user_id_opaque = 'User-' || SUBSTRING(MD5(user_id || :deletion_salt) FOR 4)
WHERE user_id = :user_id;
```

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Story 1.7
- FR6, NFR-S7 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- AR-042 (per-data-class erasure) — referenced in architecture §3.5
- §3.5 Cross-Cutting (cryptographic posture, LegalHold) — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §3.5
- ADR-021 Auth0 — [_bmad-output/planning-artifacts/adrs/adr-021-auth0-identity-provider.md](../planning-artifacts/adrs/adr-021-auth0-identity-provider.md)
- ADR-025 Key Vault Standard — [_bmad-output/planning-artifacts/adrs/adr-025-key-vault-standard-for-mvp.md](../planning-artifacts/adrs/adr-025-key-vault-standard-for-mvp.md)
- Story 1.2 — [_bmad-output/implementation-artifacts/1-2-user-registration-auth0-hosted-flow.md](./1-2-user-registration-auth0-hosted-flow.md)
- Story 1.5 — [_bmad-output/implementation-artifacts/1-5-profile-view-and-edit.md](./1-5-profile-view-and-edit.md)
- Story 1.6 — [_bmad-output/implementation-artifacts/1-6-legal-disclaimer-display.md](./1-6-legal-disclaimer-display.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Highest-risk Epic 1 story — irreversible cryptographic erasure
- LegalHold check guards every deletion; deferred records persist for Epic 7 release trigger
- Audit log anonymized but balance-preserving via property-based tests

### File List

_Populated during implementation — see Source tree section above._
