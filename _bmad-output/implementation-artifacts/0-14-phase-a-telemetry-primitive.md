# Story 0.14: Phase-A telemetry primitive (AR-047)

Status: ready-for-dev

## Story

As **Wahid**,
I want **an App Insights event-emission SDK + PII-safe event schema + integration-test helper asserting events fire**,
so that **Phase-A success metrics (40% activation, viral coefficient ≥0.3, 5 signups/week) are instrumentable from Day 1 rather than discovered missing two weeks post-launch**.

**Covers requirements:** Epic 0 — AR-047. Phase-A telemetry primitive. Each FR-delivering epic inherits "emit X telemetry events, verified by integration test" ACs.

**Why this story now:** After Story 0.11 (App Insights wired). Before Story 2.1 — Story 2.1 AC9 `contact_created` telemetry event depends on this primitive being available.

## Acceptance Criteria

### AC1 — `TelemetryEmitter` API lives in Faktuboh.Infrastructure/Telemetry

**Given** AR-047 specifies an emission primitive
**When** this story scaffolds it
**Then** `backend/src/Faktuboh.Infrastructure/Telemetry/TelemetryEmitter.cs`:

```csharp
public interface ITelemetryEmitter
{
    void Emit<TPayload>(string eventName, TPayload payload) where TPayload : class;
}

public sealed class TelemetryEmitter(TelemetryClient appInsightsClient, ILogger<TelemetryEmitter> logger) : ITelemetryEmitter
{
    public void Emit<TPayload>(string eventName, TPayload payload) where TPayload : class
    {
        PiiGuard.AssertNoPii(payload);  // throws on PII field detection
        var properties = PayloadFlattener.ToProperties(payload);  // flattens to Dictionary<string, string>
        appInsightsClient.TrackEvent(eventName, properties);
    }
}
```

**And** `ITelemetryEmitter` is registered in DI as Singleton pointing at the App Insights TelemetryClient from Story 0.11
**And** emission is synchronous (client-side buffering handled by App Insights SDK) — no async/await required at call site

### AC2 — PII-safe payload schema + compile-time guards

**Given** event payloads must contain zero PII per ADR-004 crypto posture
**When** the payload type is defined
**Then** event payload types live in `backend/src/Faktuboh.Application/Telemetry/Events/` as sealed records:

```csharp
// backend/src/Faktuboh.Application/Telemetry/Events/UserSignupEvent.cs
public sealed record UserSignupEvent(
    string OpaqueUserId,   // hashed subject_id (not the raw Auth0 sub)
    DateTimeOffset OccurredAt,
    string Locale);         // "ar" or "en" — not PII

// backend/src/Faktuboh.Application/Telemetry/Events/ContactCreatedEvent.cs
public sealed record ContactCreatedTelemetry(
    string OpaqueUserId,
    string ContactIdHash,   // SHA-256(contact_id) — not the raw id
    bool OpeningBalancePresent,
    string Currency,        // ISO code — not PII
    DateTimeOffset OccurredAt);
```

**And** every payload property must be one of the approved types: `string`, `int`, `long`, `decimal`, `bool`, `DateTimeOffset`, or nested record of approved types
**And** property names matching PII patterns are forbidden: `*Email`, `*Name`, `*Phone`, `*Address`, `*Pii`, `*Subject` (without `Opaque` prefix)

### AC3 — `PiiGuard` runtime + NetArchTest enforcement

**Given** AR-047 specifies compile-time checked PII absence
**When** the guard is wired
**Then** `backend/src/Faktuboh.Infrastructure/Telemetry/PiiGuard.cs`:

```csharp
public static class PiiGuard
{
    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Email", "Phone", "Name", "FirstName", "LastName", "Address",
        "Iban", "CreditCard", "Ssn", "SubjectId", "PII"  // Subject is OK only when prefixed "Opaque"
    };
    public static void AssertNoPii<T>(T payload) where T : class
    {
        var props = typeof(T).GetProperties();
        foreach (var p in props)
        {
            var name = p.Name;
            if (name.StartsWith("Opaque", StringComparison.Ordinal)) continue;
            if (ForbiddenNames.Any(f => name.Equals(f, StringComparison.OrdinalIgnoreCase)
                                     || name.EndsWith(f, StringComparison.OrdinalIgnoreCase)))
                throw new PiiLeakDetectedException(typeof(T).Name, name);
        }
    }
}
```

**And** a NetArchTest fitness test at `backend/tests/Faktuboh.ArchitectureTests/TelemetryPiiTests.cs` scans every type in `Faktuboh.Application.Telemetry.Events.*` namespace + asserts no property matches the forbidden-name list
**And** the test fails CI on any violation [Source: epics.md Story 0.14 AC "NetArchTest asserts no PII fields"]

### AC4 — Integration-test helper `AssertEventEmitted`

**Given** tests must assert an event fired during a slice's execution
**When** a test invokes the helper
**Then** `backend/tests/Faktuboh.Api.Tests/Telemetry/TestTelemetryFixture.cs` provides:

```csharp
public sealed class TestTelemetryFixture : ITelemetryEmitter
{
    private readonly List<(string Name, object Payload)> _events = new();
    public void Emit<TPayload>(string eventName, TPayload payload) where TPayload : class
        => _events.Add((eventName, payload));
    public void AssertEmitted(string eventName)
        => _events.Should().Contain(e => e.Name == eventName, $"expected event '{eventName}' to be emitted");
    public void AssertEmitted<TPayload>(string eventName, Action<TPayload>? payloadAssertion = null) where TPayload : class
    {
        var match = _events.FirstOrDefault(e => e.Name == eventName && e.Payload is TPayload);
        match.Should().NotBe(default, $"expected event '{eventName}' with payload type {typeof(TPayload).Name}");
        if (payloadAssertion is not null) payloadAssertion((TPayload)match.Payload);
    }
}
```

**And** integration tests replace the `ITelemetryEmitter` DI registration with the fixture (via `WebApplicationFactory.ConfigureTestServices`)
**And** Story 2.1's `CreateContactEndpointTests` can assert `fixture.AssertEmitted<ContactCreatedTelemetry>("contact_created", p => p.OpeningBalancePresent.Should().BeTrue());`

### AC5 — Event-name convention

**Given** event names appear in App Insights as `customEvents.name`
**When** a slice emits an event
**Then** the name follows `<slice>_<past_tense_action>` snake_case pattern:
- `user_signup`
- `contact_created`
- `debt_recorded`
- `statement_viewed`
**And** event names are registered in `backend/src/Faktuboh.Application/Telemetry/Events/TelemetryEventNames.cs` as `public static class` const strings
**And** a fitness test asserts every `ITelemetryEmitter.Emit(...)` first argument references a registered const (prevents typos / drift)

### AC6 — Initial event types

**Given** Story 0.14 only ships the primitive — actual events are emitted in feature epics
**When** this story lands
**Then** the following payload record types are defined (ready for consumers in Epic 1-4):
- `UserSignupEvent` (Epic 1)
- `ContactCreatedTelemetry` (Epic 2 Story 2.1 consumer)
- `FirstDebtRecordedEvent` (Epic 3)
- `DebtRecordedEvent` (Epic 3)
- `StatementViewedEvent` (Epic 4 — viral funnel)
- `StatementSharedEvent` (Epic 4 — viral funnel)
**And** `TelemetryEventNames` contains matching const strings
**And** zero `Emit` calls happen in this story — consumers add them in their owning stories

### AC7 — Documentation

**Given** AR-047 requires developer guidance
**When** a slice author reads the guide
**Then** `docs/development/telemetry-guide.md` documents:
- How to add a new event type (file location + naming convention)
- How to emit (`ITelemetryEmitter.Emit`)
- How to test emission (`TestTelemetryFixture.AssertEmitted`)
- PII guardrails (PiiGuard + fitness test)
- Phase-A metric definitions (activation, viral coefficient, weekly signups)
- KQL examples for querying Phase-A metrics in App Insights

## Tasks / Subtasks

### T1 — Scaffold emitter interface + implementation (AC: 1)

- [ ] Create `backend/src/Faktuboh.Infrastructure/Telemetry/TelemetryEmitter.cs` per AC1 snippet
- [ ] Register in DI: `builder.Services.AddSingleton<ITelemetryEmitter, TelemetryEmitter>();` (`Program.cs`)
- [ ] Create `backend/src/Faktuboh.Infrastructure/Telemetry/PayloadFlattener.cs` — converts a payload record into `Dictionary<string, string>` for App Insights

### T2 — Event payload records (AC: 2, 6)

- [ ] Create `backend/src/Faktuboh.Application/Telemetry/Events/UserSignupEvent.cs` + matching records for the six initial events per AC6
- [ ] Create `backend/src/Faktuboh.Application/Telemetry/Events/TelemetryEventNames.cs` with const string event names

### T3 — PII guards (AC: 3)

- [ ] Create `backend/src/Faktuboh.Infrastructure/Telemetry/PiiGuard.cs` per AC3 snippet
- [ ] Create `backend/src/Faktuboh.Infrastructure/Telemetry/PiiLeakDetectedException.cs`
- [ ] Create `backend/tests/Faktuboh.ArchitectureTests/TelemetryPiiTests.cs` — scans event records for forbidden names
- [ ] Create `backend/tests/Faktuboh.ArchitectureTests/TelemetryEventNameRegistrationTests.cs` — scans `Emit` call sites for registered name usage (Roslyn)

### T4 — Test fixture (AC: 4)

- [ ] Create `backend/tests/Faktuboh.Api.Tests/Telemetry/TestTelemetryFixture.cs` per AC4 snippet
- [ ] Document usage in `docs/development/telemetry-guide.md`

### T5 — Self-tests (AC: 1-5)

- [ ] Create `backend/tests/Faktuboh.Infrastructure.Tests/Telemetry/TelemetryEmitterTests.cs`:
  - `Emit_with_valid_payload_forwards_to_telemetry_client`
  - `Emit_with_pii_property_throws_PiiLeakDetectedException`
  - `PayloadFlattener_flattens_nested_records_with_dot_separator`

### T6 — Documentation (AC: 7)

- [ ] Create `docs/development/telemetry-guide.md` per AC7 sections

## Dev Notes

### Critical guardrails (do not violate)

1. **Zero PII in event payloads.** Even a seemingly-safe field ("user's chosen currency") can become quasi-PII at low k-anonymity — follow the `Opaque*` prefix convention religiously. [Source: architecture.md §3.5 crypto posture + adrs/adr-004 implicit]
2. **Event names are snake_case past-tense.** `contact_created` not `createContact`. Patterns carry meaning — App Insights KQL queries read English-like.
3. **No async `Emit`.** App Insights SDK handles async internally; forcing slice handlers to `await` adds a suspend point that breaks the "telemetry is fire-and-forget from a slice's POV" invariant.
4. **Fixture replaces prod impl via `ConfigureTestServices`.** Never mock `TelemetryClient` directly — too intimate a coupling.
5. **Opaque user ID is SHA-256(Auth0 `sub`).** Not the raw `sub`. Both are stable across events, but SHA-256 resists reverse-lookup if telemetry data leaks. [Source: architecture.md §3.5]

### Source tree — files to create

```
backend/src/
├── Faktuboh.Application/Telemetry/Events/
│   ├── TelemetryEventNames.cs                            # NEW
│   ├── UserSignupEvent.cs                                # NEW
│   ├── ContactCreatedTelemetry.cs                        # NEW
│   ├── FirstDebtRecordedEvent.cs                         # NEW
│   ├── DebtRecordedEvent.cs                              # NEW
│   ├── StatementViewedEvent.cs                           # NEW
│   └── StatementSharedEvent.cs                           # NEW
└── Faktuboh.Infrastructure/Telemetry/
    ├── TelemetryEmitter.cs                               # NEW
    ├── PayloadFlattener.cs                               # NEW
    ├── PiiGuard.cs                                       # NEW
    └── PiiLeakDetectedException.cs                       # NEW

backend/tests/
├── Faktuboh.Api.Tests/Telemetry/
│   └── TestTelemetryFixture.cs                           # NEW
├── Faktuboh.Infrastructure.Tests/Telemetry/
│   └── TelemetryEmitterTests.cs                          # NEW
└── Faktuboh.ArchitectureTests/
    ├── TelemetryPiiTests.cs                              # NEW
    └── TelemetryEventNameRegistrationTests.cs            # NEW

docs/development/
└── telemetry-guide.md                                    # NEW
```

### Testing standards

| Level | Tool | What this story covers |
|---|---|---|
| Unit | xUnit v3 + FluentAssertions | `TelemetryEmitter` happy path + PII guard |
| Architectural | NetArchTest + Roslyn | Event record PII field scan + Emit call-site name scan |

### Project Structure Notes

- Event payload types live in `Faktuboh.Application`, not `Faktuboh.Domain`, because they're wire-format-adjacent and reference external identifiers (opaque hashes) rather than domain aggregates.
- Emitter implementation lives in `Infrastructure` because it depends on `Microsoft.ApplicationInsights`; the interface is in `Application` for slice handlers to inject without Infrastructure dependency.

### Architecture compliance

- [architecture.md §4 D4.20](../planning-artifacts/architecture.md) — App Insights sink
- [architecture.md §3.5](../planning-artifacts/architecture.md) — PII-safe scaffold + opaque subject IDs
- [epics.md Story 0.14](../planning-artifacts/epics.md) — AR-047 definition
- ADR-004 (compliance posture) — implicit via PII guard

### Library/framework requirements

| Layer | Package | Purpose |
|---|---|---|
| App Insights | Microsoft.ApplicationInsights | `TelemetryClient.TrackEvent` |
| Fitness tests | NetArchTest + Roslyn | Property-name scan |

### Testing requirements

- **PII guard test is MANDATORY.** A PR that adds `UserEmail` to an event record must fail CI. Run a dry-run proving enforcement before merging this story.
- **Fixture self-test:** `TestTelemetryFixture.AssertEmitted` behavior is tested in `TestTelemetryFixtureTests.cs` — meta-tests prove the helper works before slice tests rely on it.

### Previous Story Intelligence

- **Story 0.1 (backend scaffold)** complete: `Faktuboh.Application.csproj` exists for event records.
- **Story 0.10 (Azure infra)** complete: App Insights resource provisioned.
- **Story 0.11 (App Insights + traceId)** complete: `TelemetryClient` is available via DI (Aspire auto-wires).
- **Story 2.1 (Contacts/Create)** is the first consumer — its AC9 emits `contact_created` with `ContactCreatedTelemetry` payload.

## References

- [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) — Story 0.14 definition + AR-047
- [_bmad-output/planning-artifacts/architecture.md §4 D4.20](../planning-artifacts/architecture.md) — observability
- [_bmad-output/planning-artifacts/architecture.md §3.5](../planning-artifacts/architecture.md) — compliance posture

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

_Populated during implementation._

### File List

_Populated during implementation — expected new/modified files per the Source Tree section above._
