# Story 3.9: CSV export with RTL column order and UTF-8 BOM

Status: ready-for-dev

## Story

As a **user wanting to back up my data or exercise my GDPR right to data export**,
I want to **export all my contacts, transactions, and audit entries as a CSV I can open in Excel or Google Sheets via `GET /v1/export/csv`**,
so that I can **keep external backups or do custom analysis** (FR30), with proper UTF-8 encoding and BOM (for Excel-Arabic compatibility), RTL column order reversal when my locale is Arabic (NFR-A9e), per-currency symbol positioning, streaming export completing in under 10 seconds for accounts with up to 10,000 transactions (NFR-P10), and a response `Content-Disposition` naming the file `faktuboh-export-YYYY-MM-DD.csv`.

**Covers requirements:** FR30 (CSV export), NFR-P10 (10s at 10k transactions), NFR-A9e (RTL column order reversal), NFR-I5 (CSV Excel-AR compat).

**Why streaming is mandatory:** Buffering the full export in memory at 10k transactions × N contacts × M audit entries blows NFR-P10 and risks OOM on the Azure container host. `System.IO.Pipelines.Pipe` + `ResponseWriter` stream row-by-row to the response body.

## Acceptance Criteria

### AC1 — CSV contains all contacts + transactions + audit entries

**Given** an authenticated user taps "Export all data"
**When** `GET /v1/export/csv` streams the response
**Then** the CSV has three logical sections concatenated with section-header comment rows (`# CONTACTS`, `# TRANSACTIONS`, `# AUDIT_ENTRIES`):
  - **Contacts section:** `contact_id, name, phone, email, notes, created_at`
  - **Transactions section:** `transaction_id, contact_id, contact_name, occurred_on, direction, denomination_code, amount, note, created_at`
  - **Audit section:** `audit_id, entity_type, entity_id, action, actor_label, recorded_at, before_json, after_json`
**And** all three sections are in one CSV file (not a ZIP) for Excel/Sheets direct-open compatibility
**And** soft-deleted rows from the user's history are included with a `valid_to` column populated in their respective sections

### AC2 — UTF-8 with BOM for Excel-Arabic

**Given** the response is written
**When** the first bytes hit the response body
**Then** the stream starts with the UTF-8 BOM (`0xEF 0xBB 0xBF`)
**And** `Content-Type: text/csv; charset=utf-8`
**And** manual QA validates: opening the file in Excel on macOS + Windows + Google Sheets renders Arabic content correctly without mojibake

### AC3 — RTL column order reversal when Arabic locale

**Given** the user's locale is Arabic (resolved via their profile language setting)
**When** the CSV is written
**Then** column order within each section is reversed so the rightmost column matches the first data column in RTL reading order
**Example:** Contacts section in English is `contact_id, name, phone, email, notes, created_at`; in Arabic becomes `created_at, notes, email, phone, name, contact_id`
**And** the header row reflects the same reversal (localized column names in Arabic: `معرف_جهة_الاتصال`, `الاسم`, etc.)
**And** an English-locale user's export uses LTR order as default

### AC4 — Currency-symbol positioning

**Given** a transaction's amount is exported
**When** the row is written
**Then** a dedicated `currency_symbol` column accompanies the `amount` column, with locale-correct positioning metadata (e.g., `$` is prefix, `ج.م` is suffix)
**And** the amount column itself is stored as raw decimal string (per §5.2.5) — the symbol column is metadata for the consumer
**And** metals (gold, silver) use "g" suffix convention in the symbol column

### AC5 — Streaming via System.IO.Pipelines — NFR-P10 performance

**Given** an account with 10,000 transactions
**When** the export runs
**Then** `System.IO.Pipelines.Pipe` streams rows directly to the HTTP response; memory footprint remains < 50MB during the export
**And** total export completes in < 10s p95 per NFR-P10
**And** integration test seeds 10k transactions via Bogus and asserts the timing

### AC6 — Content-Disposition + filename

**Given** the response is returned
**When** the browser receives headers
**Then** `Content-Disposition: attachment; filename="faktuboh-export-2026-04-22.csv"; filename*=UTF-8''faktuboh-export-2026-04-22.csv`
**And** the date is today's UTC date
**And** the filename is invariant across locales (always English — prevents filesystem encoding issues on older Windows)

### AC7 — Idempotency + rate-limiting

**Given** repeated export requests within a short window
**When** the endpoint is hit
**Then** rate limiting caps exports to 5 per hour per user (NFR-SC2-related; soft gate)
**And** no Idempotency-Key is required (GET operation); ordinary HTTP caching applies via ETag on the user's generation timestamp

### AC8 — Telemetry

**Given** the endpoint stamps `ProcessingActivity.OwnerLedger`
**When** the export completes
**Then** telemetry `data_exported` emits with `{ opaque_user_id, row_count_bucket: "<100|100-1k|1k-10k|10k+", locale, duration_ms }`

### AC9 — A11y + UX of the trigger

**Given** the "Export all data" action lives in the user's profile/settings page
**When** the user taps the button
**Then** the button shows `<p-progressSpinner>` while the request is in flight
**And** on 200 response, the browser download dialog appears automatically
**And** on error (5xx or network), `<p-toast severity="error" sticky="true">` with Retry action per UX-DR31
**And** axe a11y zero violations on the trigger surface

## Tasks / Subtasks

### Backend

- [ ] **T1 (AC: 1, 5)** — Create `Slices/Export/Csv/ExportCsvHandler.cs`
  - [ ] Endpoint `group.MapGet("/v1/export/csv", ...)` — returns `Results<FileStreamHttpResult, ProblemHttpResult>`
  - [ ] Uses `System.IO.Pipelines.Pipe` to write directly to the response body
  - [ ] Queries Contacts, JournalEntries, AuditLog with `AsAsyncEnumerable()` (EF Core 10 streaming query) — no full materialization
- [ ] **T2 (AC: 2)** — Prepend UTF-8 BOM `new byte[] { 0xEF, 0xBB, 0xBF }` as the first bytes written
- [ ] **T3 (AC: 3)** — Column-order logic
  - [ ] `CsvColumnSet` helper with LTR column order + reversed RTL variant
  - [ ] Resolve user locale via their profile; pick the correct variant
  - [ ] Transloco-equivalent server-side header translation (JSON localization file `backend/src/Faktuboh.Api/Localization/csv-headers.ar.json`)
- [ ] **T4 (AC: 4)** — `CurrencySymbolResolver` — maps ISO codes to `{ symbol: string, position: "prefix" | "suffix" }` via `CurrencyRegistry` metadata
- [ ] **T5 (AC: 6)** — Set `Content-Disposition` with `filename*=UTF-8''` extension + plain filename; date derived server-side as UTC today
- [ ] **T6 (AC: 7)** — Rate-limit middleware applied to this endpoint: `.RequireRateLimiting("csv-export")` with `5 per 1h` policy
- [ ] **T7 (AC: 8)** — Emit `data_exported` telemetry

### Frontend

- [ ] **T8 (AC: 9)** — Create `apps/owner-workspace/src/app/features/profile/export/export-button.component.ts`
  - [ ] `<p-button>` with spinner state signal
  - [ ] Click handler triggers browser navigation to `GET /v1/export/csv` (with Auth0 token via query param fallback OR fetch+blob-download)
  - [ ] Preferred approach: `fetch('/v1/export/csv', { headers: { Authorization: `Bearer ${token}` } }).then(res => res.blob()).then(blob => downloadBlob(blob, filename))` — avoids URL-embedded token
  - [ ] Error toast via UX-DR31 pattern
- [ ] **T9 (AC: 9)** — Wire button into profile/settings page (Story 1.5 scaffolded the page; this adds the export section)

### Tests

- [ ] **T10 (AC: 1, 2, 4)** — `ExportCsvHandlerTests` — table-driven across empty, single-contact, multi-section scenarios; BOM assertion; column content correctness
- [ ] **T11 (AC: 3)** — Table-driven test on column-order reversal across both locales
- [ ] **T12 (AC: 5)** — Integration test with Testcontainers + Bogus seed at 10k transactions
  - [ ] Assert streaming: memory peak < 50MB (measured via `GC.GetTotalMemory`)
  - [ ] Assert duration < 10s
  - [ ] Assert response body byte-for-byte matches expected shape (or at minimum: first 1KB + last 1KB + total size)
- [ ] **T13 (AC: 2)** — Manual QA checklist in test plan: open exported file in Excel macOS + Windows + Google Sheets; Arabic text renders correctly
- [ ] **T14 (AC: 9)** — Vitest: export button loading state; error toast branch
- [ ] **T15 (AC: 9)** — Playwright E2E: click export, file downloads, check filename + size > 0

## Dev Notes

### Critical guardrails

1. **Streaming is not optional.** Buffering the full export in memory violates NFR-P10 at 10k transactions. `System.IO.Pipelines.Pipe` + EF Core `AsAsyncEnumerable` are the canonical combo.
2. **UTF-8 BOM is the ONLY way to get Excel-Arabic right.** Without it, Excel on Windows guesses `windows-1252` and renders mojibake. The three BOM bytes are load-bearing.
3. **Column-order reversal, not cell-content mirroring.** The Arabic CSV has the same cell CONTENT as English; only the column ORDER flips. Cell-content direction is a rendering concern of the consumer (Excel handles it via `dir`).
4. **Filename is always English.** `faktuboh-export-2026-04-22.csv` — avoids filesystem encoding issues on older Windows + simplifies bug reports.
5. **Rate limit to prevent abuse.** 5 exports/hour is generous for legitimate use but blocks scraping-via-export. Monitored in App Insights.
6. **No Idempotency-Key** — GET is idempotent by protocol. ETag on the user's last-modification timestamp enables browser caching.
7. **Before/after JSON in audit section is stored as escaped JSON within the CSV cell.** The consumer parses it as JSON. This is a known quirk; alternative would be dedicated columns per field which doesn't scale.
8. **Auth token attachment via fetch+blob is the correct pattern** — URL-embedded tokens leak into browser history + server logs. The endpoint requires `Authorization: Bearer` header.

### Source tree

```
backend/
├── src/Faktuboh.Api/Slices/Export/Csv/
│   ├── ExportCsvHandler.cs                           # NEW
│   ├── CsvColumnSet.cs                               # NEW
│   ├── CsvRowWriter.cs                               # NEW (System.IO.Pipelines writer)
│   └── CurrencySymbolResolver.cs                     # NEW
├── src/Faktuboh.Api/Localization/
│   ├── csv-headers.en.json                           # NEW
│   └── csv-headers.ar.json                           # NEW
└── tests/Faktuboh.Api.Tests/Slices/Export/Csv/
    ├── ExportCsvHandlerTests.cs                      # NEW
    ├── ExportCsvEndpointTests.cs                     # NEW (Integration + perf)
    └── CsvColumnSetTests.cs                          # NEW (RTL reversal)

frontend/
├── apps/owner-workspace/src/app/features/profile/export/
│   ├── export-button.component.ts                    # NEW
│   ├── export-button.component.html                  # NEW
│   └── export-button.component.spec.ts               # NEW
└── apps/owner-workspace/src/app/shared/download/
    └── download-blob.util.ts                         # NEW (browser-side blob download helper)
```

### Streaming pipeline (reference)

```csharp
public async Task Handle(HttpContext ctx, ExportCsvRequest req, IContactRepository contacts, ...)
{
    ctx.Response.Headers.ContentType = "text/csv; charset=utf-8";
    ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"faktuboh-export-{DateTime.UtcNow:yyyy-MM-dd}.csv\"; filename*=UTF-8''faktuboh-export-{DateTime.UtcNow:yyyy-MM-dd}.csv";

    var pipe = ctx.Response.BodyWriter;

    // UTF-8 BOM
    pipe.Write(new byte[] { 0xEF, 0xBB, 0xBF });

    var writer = new CsvRowWriter(pipe, locale);
    await writer.WriteSectionHeader("CONTACTS");
    await foreach (var c in contacts.StreamForOwnerAsync(ownerId).WithCancellation(ct))
        await writer.WriteRow(c, CsvColumnSet.ContactsFor(locale));

    // ... transactions, audit
    await pipe.FlushAsync();
}
```

### Testing standards

Per Story 3.1. **Performance test is the load-bearing assertion** — 10k-transaction timing + memory peak.

### Project Structure Notes

Export is a first-class slice under `Slices/Export/Csv/`. Rationale: the endpoint is owner-scoped, cross-cutting all entity types (contacts, transactions, audit). It's neither a Contacts nor JournalEntries concern — its own slice keeps the dependency arrows clean.

### Architecture compliance

- **§4 D4.1** Postgres streaming query via `AsAsyncEnumerable`
- **§5.2.5** Money decimal-string wire in the amount column
- **§5.3.1** Minimal APIs
- **FR30** CSV export
- **NFR-P10** 10s at 10k transactions
- **NFR-A9e** RTL column order reversal (ship-blocker — the CI test catches regressions)
- **NFR-I5** CSV Excel-AR compatibility
- **UX-DR31** error toast with Retry

### Library/framework requirements

Per Story 3.1. Additional:
- `System.IO.Pipelines` (first-party .NET 10 — no NuGet)
- `Microsoft.AspNetCore.RateLimiting` (first-party)
- Bogus (seeding 10k rows)
- No CSV library dependency — hand-rolled row writer with proper quoting/escaping (safer against injection than 3rd-party writers that may miss edge cases)

### Testing requirements

| Category | Tools |
|---|---|
| Unit | xUnit v3 + FluentAssertions (CSV quoting, column reversal) |
| Integration | Testcontainers Postgres + Bogus 10k seed + memory peak assertion |
| Performance | Explicit `[Fact]` with timing + memory measurement |
| Component | Vitest + Angular Testing Library |
| E2E | Playwright — download flow + filename check |
| Manual QA | Excel macOS, Excel Windows, Google Sheets — Arabic content rendering |

### Previous Story Intelligence

- **Story 0.14** (AR-047 telemetry)
- **Story 1.5** (profile view/edit) — profile page is the host for the export button
- **Story 1.7** (GDPR erasure) — CSV export is a GDPR "right to data portability" realization; this story ships before erasure is fully Epic-4-wired, so exports of anonymized entries include `[redacted]` cells per Story 3.8's pattern
- **Story 2.1-2.5** (Contacts) — contacts section data source
- **Story 3.1-3.3** (JournalEntries) — transactions section data source
- **Story 3.2, 3.3, 3.8** (audit_log) — audit section data source

## References

- [epics.md Story 3.9](../planning-artifacts/epics.md)
- [architecture.md §4 D4.1](../planning-artifacts/architecture.md) Postgres streaming
- [architecture.md §5.2.5, §5.3.1](../planning-artifacts/architecture.md)
- [prd.md FR30, NFR-P10, NFR-A9e, NFR-I5](../planning-artifacts/prd.md)
- [ux-design-specification.md UX-DR31](../planning-artifacts/ux-design-specification.md)

## Dev Agent Record

### Agent Model Used
_To be filled by implementing dev agent._

### Debug Log References
_Populated during implementation._

### Completion Notes List
_Populated during implementation._

### File List
_Populated during implementation._
