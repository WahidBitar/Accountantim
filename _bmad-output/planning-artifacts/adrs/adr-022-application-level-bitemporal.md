---
id: ADR-022
title: Bitemporal Storage — Application-Level Pattern via EF Core Interceptor
status: ACCEPTED
date: 2026-04-21
supersedes_partial: [D4.1 bitemporal implementation strategy]
source: Adversarial Review R-02
decision_type: two-way-door
---

## Context

Adversarial review (AR-14) identified a blocking incompatibility: the original D4.1 decision specified PostgreSQL's `temporal_tables` extension for bitemporal storage, but Azure PostgreSQL Flexible Server does not permit that extension. The bitemporal guarantee underlies audit, GDPR erasure reconciliation, and the 7-year retention requirement — it cannot be descoped.

## Decision

Implement bitemporal storage at the **application layer** using an EF Core `SaveChanges` interceptor and per-entity `<entity>_history` companion tables. Entities opt in via a marker interface:

```csharp
public interface IBitemporal
{
    DateTimeOffset ValidFrom { get; set; }
    DateTimeOffset? ValidTo { get; set; }
    DateTimeOffset RecordedAt { get; set; }
}
```

The interceptor inspects `entry.Entity is IBitemporal` (no reflection on the hot path), writes the previous row to `<entity>_history` on UPDATE and DELETE, and stamps `RecordedAt = UtcNow`. Read-side: an `AsOf(DateTimeOffset)` query extension returns the entity view at a given wall-clock.

## Rationale

- **Keeps Azure PostgreSQL Flexible Server** — no DB migration forced by the extension restriction.
- **Marker interface over attribute**: `entry.Entity is IBitemporal` is an interface dispatch, not an attribute reflection lookup. Matters on the `SaveChanges` hot path. Also more testable (can be faked/mocked per entity in unit tests).
- **No extension lock-in**: if we later move to a DB that supports native bitemporal, the interface + interceptor can be swapped for the native feature without domain-model changes.
- Pattern is well-documented, deterministic, CI-testable.

## Consequences

- Modest perf cost: one extra insert per mutation on audited entities. Acceptable at MVP scale.
- Developer discipline required: the interceptor must be registered on every `DbContext`. A unit test asserts this on boot.
- **New fitness test (CI-wired):** "audit round-trip" — for every entity implementing `IBitemporal`, a mutation creates exactly one row in `<entity>_history` with the pre-mutation state.
- Schema migrations must now create history tables alongside source tables; EF Core migration convention updated.

## Revisit Triggers

- **RT-BITEMP-1:** Migration to a DB that supports `temporal_tables` natively (e.g., SQL Server, SQL Edge, or Azure PG if the extension becomes supported). Action: evaluate swapping the interceptor for native temporal tables.
- **RT-BITEMP-2:** Audit round-trip fitness test failure in CI. Action: block merge; investigate interceptor registration.

## Supersession Notes

- D4.1 in architecture.md §4 is rewritten to reference this ADR. The valid-time columns (`ValidFrom`, `ValidTo`) remain on the aggregate; the implementation mechanism shifts from `temporal_tables` to the interface + interceptor pattern.

## Links

- PRD FR/NFR referring to audit retention (7-year commitment) — this ADR preserves that guarantee under the new implementation.
