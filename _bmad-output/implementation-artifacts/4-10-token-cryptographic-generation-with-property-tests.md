# Story 4.10: Token cryptographic generation with property tests

Status: ready-for-dev

## Story

As **Wahid**,
I want **capability tokens generated from a cryptographically secure RNG with ≥ 128-bit entropy, validated by property-based tests**,
so that **token guessability is infeasible** (FR41, NFR-S4).

**Covers requirements:** Epic 4 — FR41, NFR-S4 (minimum 128-bit entropy), §2.9 public-surface item #1 (opaque tokens), §4.3 D4.6 (token scheme) [Source: epics.md Epic 4 Story 4.10; prd.md FR41/NFR-S4].

**Why this story now:** Story 4.1 defined the scheme (`CapabilityToken` struct + `capability_tokens` table). Story 4.10 provides the generator so Story 4.4 can actually issue tokens. The generator ships with property-based entropy tests + adversarial shape tests — a silent-failure class where "uses RNG" is claimed but `Random` is used by mistake.

## Acceptance Criteria

### AC1 — `TokenGenerator.Create()` uses `RandomNumberGenerator`, never `Random`

**Given** `CapabilityToken` value object from Story 4.1 requires 16 random bytes
**When** `Faktuboh.Infrastructure/Security/TokenGenerator.cs` implements `Create()`
**Then** the implementation calls `RandomNumberGenerator.Fill(Span<byte>)` from `System.Security.Cryptography` (not `Random.Next`, not `System.Random`)
**And** the generator returns a fully-constructed `CapabilityToken` with the first byte set to `version_byte = 1` per Story 4.1
**And** a unit test uses reflection or source-scanning to assert the implementation's method body references `RandomNumberGenerator.` and does NOT reference `System.Random` or `new Random()` [Source: architecture.md §4.3 D4.6; prd.md NFR-S4].

### AC2 — 10,000 generated tokens have zero duplicates

**Given** FsCheck property-based tests in `tests/Faktuboh.Infrastructure.Tests/Security/`
**When** the property test runs `TokenGenerator.Create()` 10,000 times
**Then** the resulting `HashSet<byte[]>` has exactly 10,000 elements (`SequenceEqual` comparer)
**And** the test is marked `[Trait("Category", "PropertyBased")]` and runs in CI on every PR [Source: epics.md Story 4.10 AC1].

### AC3 — Sample-variance entropy test exceeds 128 bits

**Given** 128-bit entropy is the minimum per NFR-S4
**When** the entropy test runs
**Then** it generates 10,000 tokens
**And** computes the per-byte frequency distribution across all 16 byte positions (16 positions × 256 possible values = 4096 buckets)
**And** applies the chi-square goodness-of-fit test against a uniform distribution: expected frequency = `10000 / 256 = ~39` per bucket per byte position
**And** the chi-square statistic is below the critical value for p = 0.01 with 255 degrees of freedom (critical ≈ 310) for each of the 16 byte positions
**And** the Shannon entropy of the aggregated output exceeds 7.9 bits/byte × 16 bytes = 126.4 bits (close to the 128-bit max; a reading below this fails the test)
**And** reference: NIST SP 800-22 (Runs and Frequency tests as lightweight supplements) [Source: prd.md NFR-S4].

### AC4 — Adversarial token-shape tests all reject with constant-time response

**Given** Story 4.1's `CapabilityTokenValidator` handles rejection
**When** these adversarial cases hit `GET /v1/statements/t/{token}`:
1. **Truncated-by-1-char** — valid token with last char removed (21 chars)
2. **Tampered-middle-byte** — valid token with byte 8 flipped
3. **Replay-after-expiry** — token with `exp` in the past
4. **Wrong-audience** — token issued for scope X, presented for scope Y (MVP has single scope so this is scope-mismatch post-future-widening; test asserts lookup miss)
5. **Expired-by-1ms** — token with `exp = now() - 1 millisecond`
6. **Revoked** — token with `revoked_at = now() - 1 day`
7. **Unknown** — random 22-char base64url not matching any DB row
8. **Wrong version byte** — token with first byte = 2 (not 1)

**Then** each case returns the same-shape `faktuboh.statements.token_invalid` Problem Details body per Story 4.1 AC3
**And** the timing difference between any two adversarial cases is < 5ms p99 (asserted statistically in Story 4.12 — this story asserts shape-equality only) [Source: epics.md Story 4.10 AC3].

### AC5 — Generator is ambient-context aware

**Given** `ProcessingActivity` must be set for every generator call
**When** `TokenGenerator.Create(ownerSubjectId, scope, contactId, exp)` runs
**Then** it requires `ProcessingActivity.OwnerLedger` to be set (validates via `ProcessingActivity.Current is not null && Current != SystemBackground`)
**And** an attempt to call without a set context throws `InvalidOperationException` with message `"TokenGenerator requires ProcessingActivity.OwnerLedger context"`
**And** NetArchTest asserts no caller of `TokenGenerator.Create` exists outside `Slices/Contacts/*` or `Slices/PublicStatements/*` [Source: architecture.md §3.5; §4.3 D4.6].

### AC6 — Token issuance writes to DB atomically with owner action

**Given** a caller (Story 4.4's `GenerateStatementLinkHandler`) wants to issue a token
**When** the handler calls `TokenGenerator.Create(...)` + `CapabilityTokenRepository.InsertAsync(...)`
**Then** both the `capability_tokens` INSERT and any parent aggregate update commit in the same `DbContext.SaveChangesAsync()` transaction
**And** Wolverine publishes `CapabilityTokenIssuedEvent` via same-transaction outbox (atomic with the DB write)
**And** if the transaction aborts, no token enters the DB (no leak) [Source: architecture.md §4.4 D4.12].

### AC7 — Generator cost is predictable — stress test passes

**Given** token generation is on the hot path for link-generation
**When** a benchmark test generates 100,000 tokens sequentially
**Then** p99 generation latency is < 50 microseconds (generation itself, not including DB write)
**And** the benchmark is `[Trait("Category", "Benchmark")]` and runs nightly (not per-PR)
**And** regression > 2x triggers an alert [Source: architecture.md §2.2 performance budgets].

### AC8 — Key-shred propagation test

**Given** Story 4.1's `InvalidateCapabilityTokensOnSubjectKeyDestroyed` handler exists
**When** this story adds a test that exercises the flow end-to-end
**Then** a test in `tests/Faktuboh.Api.Tests/Slices/PublicStatements/`:
1. Generates a token via `TokenGenerator.Create` + inserts it
2. Publishes `SubjectKeyDestroyed` for the owner
3. Asserts `capability_tokens.revoked_at` is non-null for the row
4. Asserts subsequent token validation returns `faktuboh.statements.token_invalid` [Source: architecture.md §4.3 D4.6].

## Tasks / Subtasks

### Infrastructure

- [ ] **T1 (AC: 1, 5, 6)** — Create `Faktuboh.Infrastructure/Security/TokenGenerator.cs`
  - [ ] Interface `ITokenGenerator` in `Faktuboh.Application/Security/`
  - [ ] Implementation uses `RandomNumberGenerator.Fill(Span<byte>)`
  - [ ] `Create(Guid ownerSubjectId, string scope, Guid? contactId, DateTimeOffset exp)` returns `(CapabilityToken token, CapabilityTokenRecord record)` tuple
  - [ ] First byte of raw = `version_byte = 1`
  - [ ] Remaining 15 bytes filled from RNG
  - [ ] Asserts `ProcessingActivity.Current` is set
  - [ ] Registered as Singleton in DI
- [ ] **T2 (AC: 1)** — Static analyzer rule
  - [ ] Create `tools/Roslyn/NoSystemRandomInSecurityAnalyzer.cs` — fails compile if any file under `Faktuboh.Infrastructure/Security/` references `System.Random` or `new Random(`
  - [ ] Wire into `Directory.Build.props` for backend projects

### Tests — Property-based

- [ ] **T3 (AC: 2)** — `TokenGeneratorUniquenessTests.cs` in `tests/Faktuboh.Infrastructure.Tests/Security/`
  - [ ] FsCheck test: `Arb.Default.UInt32.Generator.Sample(size: 10000)` → generate 10k tokens → `HashSet.Count == 10000`
  - [ ] Tagged `[Trait("Category", "PropertyBased")]`
- [ ] **T4 (AC: 3)** — `TokenGeneratorEntropyTests.cs`
  - [ ] Chi-square goodness-of-fit: iterate 10k tokens, count byte frequencies per position, compute chi-square
  - [ ] Assert chi-square < 310 (critical value for p=0.01, df=255) per byte position
  - [ ] Compute Shannon entropy of the 160,000-byte aggregate output; assert > 7.9 bits/byte
  - [ ] Tagged `[Trait("Category", "PropertyBased")]`

### Tests — Adversarial

- [ ] **T5 (AC: 4)** — `AdversarialTokenShapeTests.cs` in `tests/Faktuboh.Api.Tests/Slices/PublicStatements/`
  - [ ] Test class with `[Theory]` + `[InlineData]` for 8 adversarial shapes per AC4
  - [ ] Each case asserts response body JSON equals the canonical `faktuboh.statements.token_invalid` shape (byte-equal except `traceId`)
  - [ ] Uses `WebApplicationFactory<Program>` + Testcontainers Postgres
  - [ ] Seeds DB with one valid token to provide a "known good" baseline for truncation/tamper cases

### Tests — Integration

- [ ] **T6 (AC: 6)** — `TokenIssuanceAtomicityTests.cs` in `tests/Faktuboh.Api.Tests/Slices/Contacts/`
  - [ ] Test that forces a transaction abort after `TokenGenerator.Create` but before `SaveChanges` — asserts no row in `capability_tokens`
  - [ ] Test that asserts `CapabilityTokenIssuedEvent` is published via Wolverine outbox only on commit
- [ ] **T7 (AC: 8)** — Key-shred end-to-end test
  - [ ] Generate token → publish `SubjectKeyDestroyed` → assert revoked_at populated → assert subsequent validation fails with `token_invalid`

### Tests — Benchmark

- [ ] **T8 (AC: 7)** — `TokenGeneratorBenchmarkTests.cs`
  - [ ] BenchmarkDotNet `[MemoryDiagnoser]` benchmark: generate 100k tokens sequentially
  - [ ] Assert p99 latency < 50μs
  - [ ] Tagged `[Trait("Category", "Benchmark")]`
  - [ ] Wired into nightly CI (separate workflow `benchmarks-nightly.yml`)

### Architecture tests

- [ ] **T9 (AC: 1, 5)** — Extend `tests/Faktuboh.ArchitectureTests/TokenGeneratorTests.cs`
  - [ ] Assert no file under `Faktuboh.Infrastructure/Security/` references `System.Random`
  - [ ] Assert `TokenGenerator.Create` is invoked only from `Slices/Contacts/` or `Slices/PublicStatements/` (no rogue callers)

## Dev Notes

### Critical guardrails

1. **`RandomNumberGenerator`, never `Random`.** The single most common token-security failure is using `System.Random` (predictable seed). Three gates: T2 Roslyn analyzer at compile time, T9 NetArchTest, and the explicit unit test in T3.
2. **Property-based is not optional.** Unit tests verify "the code runs"; property-based tests verify "the statistical claim holds." NFR-S4 is a statistical claim — uniqueness over 10k + entropy chi-square.
3. **Adversarial shapes are the timing baseline.** Shape-equality is asserted here; timing-equality is asserted in Story 4.12. Both must hold. Shape without timing = still leaks through latency.
4. **No `Random.Shared`.** `Random.Shared` (added in .NET 6) is NOT cryptographic. It uses the same PRNG, just with thread-safety. `RandomNumberGenerator.Fill` is the only acceptable API.
5. **Entropy claim references must be cited.** NIST SP 800-22 + chi-square critical tables. The test comments must reference the exact test being applied.

### Source tree

```
backend/
├── src/
│   ├── Faktuboh.Application/Security/
│   │   └── ITokenGenerator.cs                              # NEW
│   └── Faktuboh.Infrastructure/Security/
│       └── TokenGenerator.cs                               # NEW
├── tests/
│   ├── Faktuboh.Infrastructure.Tests/Security/
│   │   ├── TokenGeneratorUniquenessTests.cs                # NEW
│   │   ├── TokenGeneratorEntropyTests.cs                   # NEW
│   │   └── TokenGeneratorBenchmarkTests.cs                 # NEW (BenchmarkDotNet)
│   ├── Faktuboh.Api.Tests/Slices/
│   │   ├── PublicStatements/
│   │   │   ├── AdversarialTokenShapeTests.cs               # NEW
│   │   │   └── KeyShredEndToEndTests.cs                    # NEW
│   │   └── Contacts/
│   │       └── TokenIssuanceAtomicityTests.cs              # NEW
│   └── Faktuboh.ArchitectureTests/
│       └── TokenGeneratorTests.cs                          # NEW

tools/Roslyn/
└── NoSystemRandomInSecurityAnalyzer.cs                     # NEW
```

### Architecture compliance

- **§4.3 D4.6** (token scheme) — this story produces the generator.
- **§2.9 item #1** (opaque tokens with 128-bit entropy) — property-tested here.
- **§4.4 D4.12** (same-transaction outbox) — atomic issuance tested here.
- **§3.5** (ambient context) — `ProcessingActivity` required at generator entry.
- **NFR-S4** (minimum 128-bit entropy) — chi-square + Shannon tests.
- **FR41** — 128-bit opaque shareable tokens.

### Library/framework requirements

| Layer | Package | Notes |
|---|---|---|
| .NET | 10 LTS | |
| Security | `System.Security.Cryptography.RandomNumberGenerator` | First-party |
| Test | FsCheck | Property-based |
| Test | BenchmarkDotNet | Benchmark tagged `[Trait("Category", "Benchmark")]` |
| Test | `MathNet.Numerics` (optional) | For chi-square critical value lookup — alternative: hardcode the critical value with citation |
| Analyzer | Custom Roslyn analyzer | Blocks `System.Random` in security namespace |

**Forbidden:**
- `System.Random`, `Random.Shared`
- `Guid.NewGuid().ToString()` as a token source (Guid has only 122 bits of entropy and isn't cryptographically random by default)

### Testing requirements

- **Unit:** Implementation uses the right API (static analyzer + NetArchTest belt-and-suspenders).
- **Property-based:** 10k uniqueness + chi-square entropy.
- **Integration:** Adversarial shape equality + atomic issuance + key-shred propagation.
- **Benchmark:** Throughput regression trend (nightly).

### Previous Story Intelligence

**Prerequisites:**
- Story 0.6 — `IBitemporal` interceptor (extends `capability_tokens` history)
- Story 0.7 — `ProcessingActivity` ambient context (generator entry gate)
- Story 0.8 — NetArchTest base (extended here)
- Story 0.14 — telemetry primitive (for `capability_token_issued` emit, though issuance emission lives in Story 4.4's handler)
- Story 4.1 — `CapabilityToken` + `CapabilityTokenRecord` + `capability_tokens` table

## References

- Story definition — [_bmad-output/planning-artifacts/epics.md](../planning-artifacts/epics.md) Epic 4 Story 4.10
- Token scheme — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.3 D4.6
- Public-surface hardening — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §2.9
- Same-transaction outbox — [_bmad-output/planning-artifacts/architecture.md](../planning-artifacts/architecture.md) §4.4 D4.12
- FR41, NFR-S4 — [_bmad-output/planning-artifacts/prd.md](../planning-artifacts/prd.md)
- Story 4.1 — [_bmad-output/implementation-artifacts/4-1-capability-token-scheme-with-constant-time-response.md](./4-1-capability-token-scheme-with-constant-time-response.md)

## Dev Agent Record

### Agent Model Used

_To be filled by implementing dev agent._

### Debug Log References

_Populated during implementation._

### Completion Notes List

- Chi-square critical value used (310 for df=255 at p=0.01) is from standard statistical tables; cite NIST SP 800-22 in test comments.
- BenchmarkDotNet introduces a test dependency — pin to a specific version to avoid trend noise from library updates.

### File List

_Populated during implementation._
