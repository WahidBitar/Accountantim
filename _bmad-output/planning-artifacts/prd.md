---
stepsCompleted: ["step-01-init", "step-02-discovery", "step-02b-vision", "step-02c-executive-summary", "step-03-success", "step-04-journeys", "step-05-domain", "step-06-innovation", "step-07-project-type", "step-08-scoping", "step-09-functional", "step-10-nonfunctional", "step-11-polish", "step-12-complete"]
completedAt: "2026-04-15"
lastEdited: "2026-04-22"
editHistory:
  - date: "2026-04-16"
    changes: "Validation-guided polish: added Journey 5 (exchange operation), Journey 1 ongoing-use scenes (FR16/FR40), critical behavioral flows (onboarding, viral conversion, freemium UX), domain compliance details (GDPR erasure resolution, audit retention/access), NFR measurement criteria (SC4/SC5/R6), revenue readiness trigger, attachment support deferral. 14 quick fixes applied during validation (measurability, implementation leakage)."
  - date: "2026-04-22"
    changes: "Pre-mortem validation pass (bmad-validate-prd → bmad-advanced-elicitation method #34). Seven proactive edits to close failure-mode gaps discovered before formal 12-step validation: (F1) split activation into Stage-1 personal / Stage-2 viral moments; added MVP ship-milestone timeline to Business Success; P0/P1 triage tier added to Must-Have Capabilities table. (F2) added FR52 for ongoing share nudge across the viral loop. (F3) added FR53 (recipient fraud-report affordance); added NFR-S11 (30-day default link expiration); reconciled FR40 ↔ Phase-2 contradiction (basic revocation is MVP; advanced controls deferred). (F4) added NFR-A9 RTL quality acceptance gate with six named scenarios. (F5) clarified freemium thresholds ship configured-but-disabled, activated only by revenue-readiness trigger. (F6) amended NFR-I3 with 4h/24h rate-freshness SLA; added NFR-I6 gold-base provider redundancy. (F7) added operating-cost Technical Success constraint (≤€25/mo at 10K users, ≤€75/mo at 100K users) linked to architecture RT-47."
  - date: "2026-04-22"
    changes: "5 Whys Deep Dive (bmad-advanced-elicitation method #40). Split Business Success into Phase A (Bootstrap, Month 0–3 from launch) and Phase B (Viral-era steady state, post-transition). Phase A targets cold-start realities: 40%+ acquired-cohort activation (lower bar than viral-era 60%); 5 new users/week signup floor by Month 2; €25/month cost ceiling referenced to Phase A; 1 founder-led onboarding conversation per 10 signups. Explicit transition criterion (viral coefficient ≥0.3 for 3 consecutive weeks + sustained Phase A activation + signup floor met for last 4 weeks) gates entry to Phase B. Escape clause: 6-month Phase A timeout triggers bmad-correct-course hypothesis review. Root cause addressed: PRD previously encoded viral-era steady-state metrics uniformly across phases, making bootstrap — where solo-dev fintech products typically die — invisible in the success criteria."
  - date: "2026-04-22"
    changes: "5 Whys Deep Dive #2 (bmad-advanced-elicitation method #40). Pressure-tested the €25/month operating-cost ceiling from the pre-mortem F7 edit; discovered ~5x gap against architecture §7.4.1 cost table (architecture projects ~€120/month steady-state; €25 was written without cross-checking). Root cause: F7 paraphrased architecture's '€0/month burn tolerance' phrase without opening the cost table, conflating founder personal-tolerance with Azure infrastructure cost. Replaced single-number ceiling with phase-aware cost ceiling that matches architecture §7.4.1: ≤€100/mo during Months 1–12 Azure free-tier runway, ≤€135/mo at 10K users / ≤€250/mo at 100K users post-Month-13 cliff (when Postgres B1ms free benefit expires). Delta to €0/mo founder tolerance becomes an explicit post-launch optimization goal tracked as architecture RT-cost-reduction, not an unmet commitment. PRD and architecture numerically reconciled on cost; same phase-awareness pattern from Business Success Phase A/B applied to cost discipline."
  - date: "2026-04-22"
    changes: "Validation polish micro-pass (bmad-validate-prd step-v-13 menu option F). Four minor items resolved: (L-3) Removed 'PrimeNG' vendor name from NFR-P1 Owner bundle context column, replaced with generic 'design-system data-grid / form-control overhead' phrasing — budget number unchanged, leakage removed. (L-4) Renamed Performance NFR row 'SignalR notification delivery' to 'Real-time notification delivery' — aligns with FR50 capability framing and removes vendor name from metric label. (M-1) Replaced NFR-S2's 'appropriate work factor' with explicit parameters: bcrypt cost ≥ 12 OR Argon2id (memory ≥ 64 MiB, iterations ≥ 3, parallelism ≥ 1), with 24-month OWASP ASVS re-review cadence — makes password-hash parameters CI-testable rather than subject-to-interpretation. (M-2) Replaced NFR-A3's 'proper ARIA labels' with explicit WCAG 2.1 SC 1.3.1 (Info and Relationships) and SC 4.1.2 (Name, Role, Value) references — gives accessibility test authors a named standard to target. All four were optional-polish items from the validation report; applying them removes all residual audit-trail marks and moves Implementation Leakage and Measurability validation results from Warning/minor-findings to clean Pass."
inputDocuments:
  - product-brief-faktuboh.md
  - product-brief-faktuboh-distillate.md
  - prd-validation-report.md
documentCounts:
  briefs: 2
  research: 0
  projectDocs: 0
  projectContext: 0
classification:
  projectType: web_app
  domain: fintech
  complexity: high
  projectContext: greenfield
  target: "All individuals and small businesses globally (bilingual AR+EN from day one)"
workflowType: 'prd'
---

# Product Requirements Document - Faktuboh

**Author:** Wahid
**Date:** 2026-04-14

## Executive Summary

Faktuboh is a personal debt ledger that replaces scattered spreadsheets, chat messages, and unreliable memory with a single, clear system for tracking who owes what to whom. It supports multiple currencies and precious metals natively — a debt recorded in gold grams stays in gold grams. Users can transfer debts between parties through simple journal entries and share read-only account statement links with contacts who need no account to view them.

The product targets individuals and small businesses globally who lend and borrow regularly — whether money, gold, or other denominations. It launches as a responsive web application with full bilingual support (Arabic RTL + English LTR) from day one, with a free tier and a $2.50/month plan for power users.

The problem is personal and universal: millions of people track informal debts in Google Sheets, chat threads, or their heads. The result is forgotten debts, disputes, and strained relationships. Existing tools are either too simple (single-currency, no statements) or too complex and expensive (enterprise accounting at $500+/month). Splitwise, the closest alternative, recently paywalled core features and earned a 65% one-star rating on Trustpilot.

### What Makes This Special

Faktuboh's strength is simplicity with power. Record a debt, share a link, done. No accounting knowledge required, no app to install on the recipient's side. The user gets full financial clarity — per-contact, per-denomination balances and a complete transaction history — through an interface as simple as a notebook.

The core differentiator is a combination no competitor offers: multi-denomination tracking (including precious metals), debt transfers between parties, and shareable proof-of-debt statements — all in a lightweight, affordable package. The shareable statement link doubles as the primary organic growth channel: every shared link exposes a new potential user at a moment of high financial engagement.

## Project Classification

| Attribute | Value |
|-----------|-------|
| **Project Type** | Web Application (responsive, mobile-friendly) |
| **Domain** | Fintech — Personal Debt Ledger |
| **Complexity** | High |
| **Project Context** | Greenfield |
| **Target Audience** | All individuals and small businesses globally |
| **Localization** | Bilingual (Arabic RTL + English LTR) from day one |

## Success Criteria

### User Success

- **Activation moment (Stage 1 — personal value):** User registers, records their first debt with a named contact, and sees the balance — all within the first session. Measured by the **Activation rate** row in Business Success (60% target).
- **Viral moment (Stage 2 — social value):** User shares a statement link and the counterparty confirms the record is accurate. Typically happens in the first session, but counts separately because sharing is the behavior that drives viral growth. Measured by the **Viral signal** row in Business Success (30% target).
- **Clarity:** Users can answer "who owes me what, and what do I owe?" at a glance, broken down by contact and denomination.
- **Ease of sharing:** Generating and sharing a statement link requires no more than 2 taps/clicks from any contact view.
- **Counterparty trust:** Recipients of shared links can view a clear, professional statement without needing to create an account or install anything.

### Business Success

Business Success is measured in two **sequential phases**. Phase A (Bootstrap) runs from public launch until the transition criterion fires; Phase B (Viral-era steady state) inherits the classical SaaS metrics once the viral mechanic has been validated at small scale. Measuring the bootstrap phase on steady-state targets — or vice versa — optimizes for the wrong signal during the product's most fragile period.

#### Phase A — Bootstrap (Month 0–3 from public launch)

| Metric | Target | Why |
|--------|--------|-----|
| Acquired-cohort activation rate | ≥ **40%** first-session (record debt + named contact) | Realistic floor for cold-start traffic (SEO + direct + paid) before viral amplification is contributing. Below this, the acquisition funnel itself is broken, not just small. |
| Weekly signup floor | ≥ **5 new users/week** by end of Month 2 | Below this volume, the viral engine has no seed to amplify — mathematical impossibility of Phase B transition. |
| Monthly operating cost | ≤ **€100/month** (per Technical Success free-tier runway ceiling) — expected actual well below ceiling at Phase A volumes | Survival boundary aligned with architecture §7.4.1 cost model. Bootstrap phase occurs during the Azure 12-month free-tier runway, so actual spend should run far below the ceiling due to low usage; the ceiling exists to catch a misconfiguration that would otherwise burn the runway silently. |
| Founder-led onboarding cadence | ≥ **1 manual onboarding conversation per 10 signups** | Bootstrap learning comes from talking to users, not dashboards. The insights compound into the Phase B product; the ratio relaxes only after transition. |

**Transition criterion from Phase A → Phase B** (all must hold for ≥ 3 consecutive weeks):

1. **Viral coefficient ≥ 0.3** — at least 30% of new signups arrive via the statement-link CTA (FR38)
2. **Acquired-cohort activation ≥ 40%** sustained across Phase A
3. **Weekly signup floor met** for the last 4 weeks continuously

**Escape clause — Phase A exit without entering B:** If **6 months elapse from public launch** without the transition criterion firing, trigger a **product-hypothesis review** (invoke `bmad-correct-course`). One of three things needs to be re-litigated before further investment: the value proposition, the viral mechanic, or the target market. Continuing to pour engineering days into the existing PRD past this point is how solo-dev fintech products die silently.

#### Phase B — Viral-era steady state (entered after transition criterion fires)

| Metric | Target | Timeframe |
|--------|--------|-----------|
| Registered users | 10,000 | 6 months post-phase-B entry |
| Activation rate | 60%+ | First session (record debt + named contact) |
| Monthly active retention | 40%+ | After 90 days |
| Viral signal | 30%+ | Users who share at least one statement link |
| Revenue readiness | 500+ users hit the freemium threshold AND survey of threshold-hitting users shows 40%+ willingness to pay | Before enabling paywall |

#### Delivery milestones (phase-independent)

| Metric | Target | Timeframe |
|--------|--------|-----------|
| MVP ship milestones | Dogfood (solo-dev internal use) by day 35 from architecture completion; alpha (invite-only) by day 90; public launch by day 180 | Engineering days from 2026-04-22 architecture completion baseline |

### Technical Success

- **Data integrity:** Zero data loss — every transaction is durably persisted and auditable. No silent failures in debt recording or balance calculation.
- **Performance:** Page loads and debt recording operations complete in under 2 seconds on mobile connections. Statement link pages render in under 3 seconds.
- **Security:** All financial data encrypted at rest and in transit. Shareable links are read-only with no exposure of user credentials or private data. Protection against statement link abuse (fake debt statements).
- **Availability:** 99.9% uptime target — users must be able to record debts and view statements reliably.
- **Bilingual quality:** Full RTL layout support for Arabic — not just translated strings, but proper directional UI, number formatting, and currency display.
- **Operating cost as a product constraint (phase-aware, reconciled with architecture §7.4.1 cost table):**
  - **Months 1–12 (Azure free-tier runway):** ≤ **€100/month** at any user volume up to NFR-SC2's scaling targets — covers ACA, App Insights, AFD, SWA, Key Vault Standard, Auth0 free tier, Resend free tier. Postgres Flexible Server B1ms is free during this phase (12-month new-subscription benefit).
  - **Months 13+ (post-cliff, architecture RT-47 fires):** ≤ **€135/month** at 10,000 users AND ≤ **€250/month** at 100,000 users. The cliff is Postgres B1ms transitioning from free to ~€30/month plus any usage growth in ACA / App Insights / AFD egress.
  - Either ceiling breached triggers the architecture RT-47 descope review — not silent absorption onto the founder's personal budget. Closing the gap between these ceilings and the €0/month founder-tolerance baseline is a deliberate post-launch optimization goal, tracked as architecture revisit trigger RT-cost-reduction.

### Measurable Outcomes

- **Time to first debt:** Under 3 minutes from registration to first recorded debt.
- **Statement link conversion:** Track how many statement link recipients sign up (measures viral loop effectiveness).
- **Multi-denomination adoption:** Percentage of users who record debts in more than one currency/denomination (validates core differentiator).
- **Debt transfer usage:** Percentage of users who use debt transfer feature (validates unique capability).

## User Journeys

### Journey 1: Account Owner — First-Time Setup & Daily Use

**Persona: Nadia** — freelance graphic designer who lends and borrows across her social circle. She tracks debts in a Google Sheet with multiple tabs — one per person — but loses track when things get complex. She deals in USD, EUR, and occasionally gold (family transactions). She's frustrated that recording a single transaction means scrolling, clicking, and hoping she's in the right cell.

**Opening Scene:**
Nadia just lent her friend Rami $300 and her cousin returned 5 grams of gold he borrowed last year. She opens her Google Sheet and groans — she has to scroll past 40 rows to find Rami's section, then switch tabs for her cousin's gold account. She can see individual balances but has no way to get a quick overview of her total financial position across all contacts and denominations. She Googles "simple debt tracker app" and finds Faktuboh.

**Rising Action:**
She signs up in under a minute. The interface is clean — she adds Rami as a contact and records "$300 lent" in two taps. She adds her cousin and records "5g gold returned." Immediately, her dashboard shows: Rami owes her $300, cousin's gold balance is settled. She can see all her contacts, all denominations, at a glance — the overview her spreadsheet could never give her.

**Climax:**
Nadia shares Rami's statement link via WhatsApp. Rami opens it on his phone — no account needed — sees the clean statement showing the $300 debt, and taps "Confirm." Nadia sees "Last reconciliation: April 15, 2026" on Rami's account. For the first time, both sides agree on the number without an awkward conversation.

**Resolution:**
A week later, Nadia lends Rami another €150 and her neighbor borrows $50. She records both in seconds — no scrolling, no tab-switching, no formula anxiety. Her dashboard shows her full financial picture: who owes her what, what she owes, across all currencies and gold. She finally feels in control.

**Ongoing Use:**
Two months in, Nadia has recorded dozens of transactions with Rami. She needs to see just this month's activity — she applies a date filter. The system rolls up everything before the filter start date into a single "previous balance" per denomination, so she sees a clean summary followed by recent transactions. No scrolling through months of history.

Later, after reconciling an old debt with her neighbor, Nadia goes to her statement links and revokes the link she shared months ago — the neighbor no longer needs access, and Nadia prefers to keep her financial data private once the matter is settled.

---

### Journey 2: Account Owner — Debt Transfer & Multi-Currency Edge Case

**Persona: Karim** — small electronics shop owner who extends informal credit to regular customers. He also borrows materials from suppliers. He deals in USD, local currency, and occasionally accepts gold as collateral.

**Opening Scene:**
Karim's customer Ali owes him $200 for a phone repair. Ali says: "I can't pay you directly, but my brother Faisal owes me $200 — can you collect from him instead?" In a spreadsheet, Karim would have to manually edit two accounts and hope he doesn't mess up the formulas.

**Rising Action:**
In Faktuboh, Karim opens Ali's account, selects "Transfer Debt," picks Faisal as the new debtor, enters $200. One journal entry: Ali's balance drops to zero, Faisal now owes Karim $200. Both transactions are recorded with a clear audit trail linking them.

**Climax:**
Karim shares Faisal's statement link. Faisal sees: "Debt transferred from Ali — $200." He taps "Confirm." Karim shares Ali's updated statement too — Ali sees his balance is clear. Everyone agrees, no disputes.

**Resolution:**
At the end of the month, Karim exports his data to CSV for his own records. He can see all 30+ customer accounts at a glance, across currencies. The overview he never had with his paper ledger or spreadsheet — and he can share any customer's statement in seconds.

---

### Journey 3: Statement Recipient — Viewing & Confirming a Shared Statement

**Persona: Salma** — she borrowed money from her colleague months ago and just received a WhatsApp message with a link to "your account statement on Faktuboh."

**Opening Scene:**
Salma is skeptical — she doesn't remember exactly how much she borrowed, and she's not sure about the currency. She clicks the link, expecting to need to download an app or create an account.

**Rising Action:**
The page loads instantly in her mobile browser. She sees a clean, professional statement: her name, the lender's name, a chronological list of transactions (dates, amounts, denominations), and a clear current balance. The page is in Arabic with full RTL layout — she reads it naturally.

**Climax:**
Everything matches her memory. She sees a "Confirm" button at the bottom. She taps it — confirming that she agrees with the statement as of today's date. No account needed, no signup wall.

**Resolution:**
She notices the Faktuboh branding and a subtle CTA: "Track your own debts — sign up free." She thinks about her own lending to friends and decides to try it. The viral loop completes.

---

### Journey 4: System Admin — Platform Operations

**Persona: Operations team member** responsible for monitoring the Faktuboh platform.

**Opening Scene:**
The platform has grown to thousands of users. The admin needs to monitor system health, investigate reported issues, and manage the transition from free to freemium.

**Rising Action:**
The admin dashboard shows key metrics: active users, new registrations, statement links generated, confirmation rates, and storage usage. An alert flags unusual activity — a single account generating hundreds of statement links in an hour (potential abuse).

**Climax:**
The admin investigates, identifies the account as potentially generating fake debt statements, and takes action — flagging the account for review and temporarily suspending statement generation for that user.

**Resolution:**
The admin configures the freemium thresholds (500 transactions / 10 contacts) for new users, verifies grandfathering rules are working for existing users, and reviews the system health dashboard before the weekend.

---

### Journey 5: Account Owner — Currency & Metal Exchange

**Persona: Nadia** — (continued from Journey 1). She's been using Faktuboh for two months and has debts recorded in multiple denominations across her contacts.

**Opening Scene:**
Nadia's cousin Hassan owes her 10 grams of gold from a family loan last month. Hassan calls and says: "I don't have gold to return — can I repay you in USD at today's rate?" In her old spreadsheet, Nadia would have had to look up the gold price herself, calculate the equivalent, manually close the gold entry, and create a new USD entry — hoping she got the math right.

**Rising Action:**
In Faktuboh, Nadia opens Hassan's account and sees the 10g gold balance. She checks the current exchange rate — the system shows today's hourly rate: 1g gold = $92.50. She taps "Exchange," selects: convert 10g gold to USD. The system auto-populates $925 based on the latest rate. Hassan offered $950 to round up — Nadia overrides the rate to $95/g and confirms.

**Climax:**
One operation: Hassan's gold balance drops to zero, and a new USD debt of $950 appears. Both entries are linked in the audit trail: "Exchange: 10g gold → $950 USD at $95/g (user-adjusted from system rate $92.50/g)." The original denomination is preserved — Nadia can always see that this debt started as gold. She views the historical exchange rate used and confirms it matches what she and Hassan agreed.

**Resolution:**
Nadia shares Hassan's updated statement. He sees the exchange clearly documented — the original gold debt, the conversion rate, and the new USD balance. He taps "Confirm." A week later, Nadia notices the gold price has climbed to $98/g — she's glad she locked the rate when she did. Her dashboard still shows her full position across all denominations, with no hidden conversions.

### Journey Requirements Summary

| Journey | Key Capabilities Revealed |
|---------|--------------------------|
| **Nadia (First Use)** | Quick registration, fast debt entry (minimal taps), contact management, multi-currency balance view, dashboard overview across all contacts/denominations, statement link sharing, date-range filtering with previous balance rollup, statement link revocation |
| **Karim (Debt Transfer)** | Debt transfer between parties, journal entry audit trail, CSV export, multi-contact overview, statement sharing per contact |
| **Salma (Statement Recipient)** | Unauthenticated statement viewing, clean mobile-optimized page, confirmation button with date tracking, bilingual RTL support, sign-up CTA (viral loop) |
| **Admin (Operations)** | Admin dashboard, usage metrics, abuse detection and response, freemium threshold configuration, grandfathering management |
| **Nadia (Exchange)** | Current exchange rate lookup, currency/metal exchange with rate override, locked exchange rate at transaction time, historical rate viewing, denomination integrity preservation, exchange audit trail |

**Cross-cutting requirements revealed:**
- Minimal-tap debt entry (addresses Google Sheets scrolling pain)
- At-a-glance overview of ALL contacts and denominations (addresses "can't see the full picture" pain)
- Statement confirmation with date tracking (new feature from user conversation)
- Shareable links that work without accounts (viral growth engine)
- Currency/metal exchange with rate override and locked rate (addresses multi-denomination complexity)

### Critical Behavioral Flows

These flows specify the user experience at key transition moments that directly impact success criteria. They bridge user journeys to functional requirements.

**First-Session Onboarding (activation path for 60% target):**
After registration and language selection, the user lands on an empty dashboard with a single contextual nudge: "Lent or borrowed recently? Add it now — takes 10 seconds." The nudge links directly to the debt creation form with the minimum required fields visible (contact name, amount, denomination, lent/borrowed). After the first debt is recorded, the nudge shifts to: "Share the statement with [contact name]?" — pushing the user toward the activation moment (record debt + share link) within the first session. Once the user has recorded at least one debt and shared one link, the nudge disappears and the standard dashboard takes over. No wizard, no tutorial overlay — just a focused prompt at the right moment.

**Statement Link-to-Signup Conversion (viral loop closure):**
When a statement recipient views a shared statement page, the page displays Faktuboh branding and a contextual CTA: "Track your own debts — sign up free." If the recipient taps the CTA, they land on the registration page with their name pre-filled from the statement. After registration, their dashboard shows a prompt: "You were viewing [lender's name]'s statement — would you like to record your side of this debt?" This closes the viral loop by converting a passive viewer into an active user with a reason to engage immediately.

**Freemium Enforcement UX (monetization gate):**
When a user approaches a freemium threshold (e.g., 450 of 500 transactions or 9 of 10 contacts), the system displays a non-blocking banner: "You're approaching your free tier limit — [X] remaining." When the limit is reached, the system prevents the capped action (adding a new contact or recording a new transaction) and displays an upgrade prompt: "You've reached the free tier limit. Upgrade to Pro ($2.50/month or $20/year) to unlock unlimited contacts and transactions." Existing data remains fully accessible — viewing, sharing, and exporting are never restricted. The paywall gates creation, not access.

## Domain-Specific Requirements

### Compliance & Regulatory

- **Data Protection (GDPR):** Primary data residency in Azure Europe. The application stores personal financial data (names, debt amounts, transaction history) — full GDPR compliance required including: right to data export (already in MVP via CSV), right to deletion, consent management, and data processing transparency.
- **GDPR Right to Erasure vs. Audit Integrity:** On account deletion, the system anonymizes audit records by replacing PII fields (user name, contact names, email) with opaque identifiers (e.g., "User-7a3f", "Contact-2b1e") while retaining transaction amounts, dates, denominations, and the mathematical relationships between entries. Balances remain calculable after anonymization — the audit trail preserves financial integrity without retaining personal identity. The anonymization method must be irreversible (no lookup table mapping identifiers back to names).
- **Legal Disclaimer:** The application must display a clear legal disclaimer that tracked debts are user records, not legally binding contracts. This disclaimer must be visible during registration and on shared statement pages.
- **No Financial Services Licensing Required:** Faktuboh does not process payments, hold funds, or provide financial advice. No KYC/AML, PCI DSS, or financial services licensing applies at this stage.

### Technical Constraints

- **Hosting:** Azure, Europe region — driven by GDPR data residency requirements.
- **Audit Logging:** Required for data integrity and dispute resolution in a unilateral debt recording system. See NFR-S6 for measurable criteria. Audit log retention period: 7 years from entry creation or until account deletion triggers anonymization (whichever comes first), aligned with GDPR data minimization. Each audit entry records: timestamp, actor (user ID), action type, affected entities, before/after values, and source IP. Audit logs are accessible only to system administrators via the authenticated admin interface — no user-facing access to raw audit data.
- **Encryption:** Financial data protection mandate. See NFR-S1 for specifics.
- **Statement Link Security:** Shareable links create a unique attack surface — unauthenticated access to financial data. See FRs 40–42 and NFR-S4/S5/S8 for controls.

### Risk Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Fake debt statements** (user generates false statements to harass or defraud) | High — reputational and legal exposure | Watermark + legal disclaimer on every statement page; rate limiting on link generation; link revocation; abuse reporting mechanism |
| **Data loss** (transaction history lost or corrupted) | Critical — core trust proposition destroyed | Audit log as immutable append-only record; regular backups; database integrity checks |
| **Unilateral record manipulation** (user edits history to misrepresent debts) | Medium — undermines trust between parties | Audit trail tracks all modifications; statement confirmation with date creates a checkpoint both parties agreed to |
| **Link enumeration** (attacker guesses statement URLs) | High — exposes private financial data | Cryptographically random tokens (min 128-bit); no sequential or predictable patterns |
| **GDPR data deletion vs. audit integrity** | Medium — tension between right to deletion and immutable audit trail | Soft-delete user data; anonymize audit records on deletion request; retain transaction structure without PII |

## Innovation & Novel Patterns

### Detected Innovation Areas

**1. "Proof of Debt" as a Digital Social Primitive**
The shareable statement link is not merely a feature — it is a new category of social object. A neutral, timestamped, confirmable record between two parties that exists outside chat threads and memory. When both parties tap "Confirm," the statement becomes a lightweight digital social proof — stronger than a WhatsApp message, lighter than a legal contract. This fills a trust gap that no existing product addresses.

**2. Unique Feature Combination in an Empty Market Segment**
No consumer product combines multi-denomination tracking (including precious metals), debt transfers between parties, and shareable proof-of-debt statements. The market has bifurcated into "social expense splitting" (Splitwise, Settle Up) and "enterprise accounting" (Sage, SoftLedger) with nothing in between. Faktuboh occupies this gap deliberately.

**3. Product-Led Growth via Financial Engagement Moments**
Every shared statement link is an acquisition touchpoint delivered at a moment of high emotional engagement — when someone is looking at a debt they owe or are owed. This is not marketing bolted onto a product; it is the product's core experience generating its own distribution.

**4. From Ledger to Financial Conversation Channel (Vision)**
Debts are not just numbers — they are relationships. The confirmation flow opens a path toward the statement becoming a neutral space for financial communication between parties: promises to pay, agreed timelines, and mutual updates. This transforms Faktuboh from a passive record into an active relationship management tool.

**5. Story-Enriched Transactions**
Financial tools treat transactions as rows of numbers. Humans remember transactions as stories — "the gold from the wedding," "the loan for the laptop." An optional context/story field per transaction transforms a dry ledger into a human financial narrative. When the counterparty opens the statement, they see context that triggers memory and builds trust.

**6. Digital Social Proof Document**
When both parties confirm a statement, the system can generate a digitally signed PDF with confirmation dates — not a legal contract, but a social proof document. In many cultures, this level of documentation fills the entire gap between "nothing written" and "formal legal contract."

**7. Predictive Financial Insights (Vision)**
With usage data over time, the system can surface simple but powerful patterns: "Ali usually pays within 30 days," "Your balance with Faisal increases every month." This moves Faktuboh from **recording** to **intelligence** — not just "how much" but "what does this mean for your financial health."

### Market Context & Competitive Landscape

The competitive gap is structural, not accidental. Expense-splitting apps (Splitwise, Tricount) are built for group meals and trips — short-lived, single-currency, symmetric. Enterprise accounting (Sage, SoftLedger) is built for businesses with accountants. Neither serves the individual who lends gold to a cousin and dollars to a friend and needs to see the full picture.

Splitwise's 2026 paywall backlash (65% one-star on Trustpilot) creates a specific migration window. Faktuboh's feature set directly addresses the paywalled capabilities (multi-currency) while adding features Splitwise never offered (precious metals, debt transfers, shareable statements).

### Validation Approach

| Innovation | Validation Method | Success Signal |
|-----------|-------------------|----------------|
| Proof of Debt (social primitive) | Track statement link shares and confirmation rates | 30%+ users share links; 50%+ recipients confirm |
| Unique combination | User activation and retention vs. single-feature competitors | 60%+ first-session activation |
| Product-led growth | Measure viral coefficient (link recipients who sign up) | Viral coefficient > 0.3 |
| Story-enriched transactions | A/B test with/without story field; measure engagement | Higher counterparty confirmation rate with stories |
| Digital social proof PDF | Feature usage rate post-confirmation | 20%+ of confirmed statements generate PDF |

### Risk Mitigation

| Innovation Risk | Mitigation |
|----------------|------------|
| "Proof of Debt" misused as legal evidence in disputes | Clear disclaimer on every statement: "User record, not a legal document." Watermark on PDFs. |
| Conversation channel scope creep (becomes messaging app) | Defer to post-MVP. Keep MVP focused on confirm/deny only. |
| Predictive insights give bad financial advice | Frame as patterns, not advice. "We noticed..." not "You should..." No financial advisory language. |
| Story field used for harassment or inappropriate content | Content moderation on shared statements. Story field visible only to account owner unless shared. |

## Web Application Specific Requirements

### Project-Type Overview

Faktuboh is a Single Page Application (SPA) built with Angular 21, targeting modern browsers. The application consists of two distinct parts: static marketing/landing pages (SEO-optimized) and the authenticated SPA (no SEO needed). Real-time updates are handled via SignalR for lightweight event notifications (e.g., statement confirmations).

### Technical Architecture Considerations

**Application Structure:**
- **SPA (Angular 21):** The core application — authentication, debt management, contact management, dashboard, statement generation. No server-side rendering needed.
- **Static Landing Pages:** Home, About Us, Contact Us — server-rendered or pre-rendered for SEO. These pages serve as the public face and acquisition funnel.
- **Shared Statement Pages:** Read-only, unauthenticated — rendered server-side or as lightweight standalone pages for fast load times and broad device compatibility. Not indexed by search engines (noindex).

**Real-Time Communication:**
- SignalR for push notifications within the app (e.g., counterparty confirmed a statement, new transaction recorded).
- Lightweight usage — no complex real-time collaboration. Simple event-based notifications.

### Browser Support Matrix

| Browser | Version | Priority |
|---------|---------|----------|
| Chrome | Latest 2 versions | Primary |
| Firefox | Latest 2 versions | Primary |
| Safari | Latest 2 versions (macOS + iOS) | Primary |
| Edge | Latest 2 versions | Primary |
| Samsung Internet | Latest version | Secondary (high mobile usage in target markets) |

No IE11 or legacy browser support required.

### Responsive Design

- **Mobile-first approach:** Primary interaction expected on mobile browsers (recording debts "in the moment").
- **Breakpoints:** Mobile (< 768px), Tablet (768px–1024px), Desktop (> 1024px).
- **RTL-first responsive grid:** Layout system must handle both LTR and RTL simultaneously — not as an afterthought.
- **Touch targets:** Minimum 44x44px for all interactive elements (critical for fast debt entry UX).
- **Shared statement pages:** Must render cleanly on any device without requiring the full SPA — lightweight, responsive, fast.

### Performance Targets

See **Non-Functional Requirements → Performance** for the complete performance specification. Key web-specific targets (split per app): **public-statement initial bundle < 180 KB gzipped**, **owner auth-shell initial bundle < 250 KB gzipped** (lazy-load feature modules in both). The split reflects the hostile first-contact nature of the public surface vs the authenticated returning-user experience in the owner shell.

### SEO Strategy

- **SEO scope:** Landing pages only (Home, About Us, Contact Us).
- **Implementation:** Server-side rendering or pre-rendering for landing pages. Angular SSR (Angular Universal) or static pre-rendering at build time.
- **Shared statement pages:** `noindex, nofollow` — private financial data must not appear in search results.
- **SPA routes:** Not indexed — behind authentication.
- **Structured data:** Organization schema on landing pages for brand presence.

### Accessibility

See **Non-Functional Requirements → Accessibility** for the complete WCAG 2.1 AA specification. Web-specific consideration: RTL screen reader compatibility and focus management during Angular SPA route changes require dedicated testing.

### Implementation Considerations

- **Angular 21 module architecture:** Feature-based lazy-loaded modules (contacts, debts, statements, dashboard, admin) to minimize initial bundle size.
- **Internationalization (i18n):** Angular's built-in i18n or ngx-translate for bilingual support. RTL handled via `dir` attribute and CSS logical properties (`margin-inline-start` vs `margin-left`).
- **SignalR integration:** Angular service wrapping SignalR hub connection with automatic reconnection and token-based authentication.
- **State management:** Angular Signals as the default (per architecture D4.13). **NgRx Signal Store** is permitted as an explicit per-slice escape hatch when derived state must span ≥2 feature slices, when pagination with stale-while-revalidate exceeds `rxResource()`'s ergonomics, or when optimistic-update flows need `withEntities` patching. Slices adopting Signal Store document the trigger in their slice README.
- **Offline resilience:** Not a PWA requirement, but graceful degradation when network is intermittent (queue failed requests, show cached data).

## Project Scoping & Phased Development

### MVP Strategy & Philosophy

**MVP Approach:** Full Problem-Solving MVP — deliver the complete core value proposition from day one. The product must answer "who owes me what, in what currency, and can I prove it?" in a single, polished experience. No feature stubs or "coming soon" placeholders.

**Rationale:** The target users currently cope with Google Sheets, chat messages, and memory. A partial solution (e.g., single-currency only, or no sharing) would not be compelling enough to trigger migration from existing workarounds. The combination of multi-denomination + debt transfers + shareable statements is the minimum that makes users say "this is better than my spreadsheet."

**Freemium enforcement timing:** MVP ships with freemium thresholds **configured but disabled** (effectively unlimited at launch). The enforcement UX described in Critical Behavioral Flows is implemented and tested but dormant — activated only after the **Revenue readiness** trigger in Business Success fires (500+ users hit the configured threshold AND 40%+ willingness-to-pay survey signal). This prevents the freemium gate from throttling viral growth before revenue validation.

### MVP Feature Set (Phase 1)

**Core User Journeys Supported:**
- Journey 1 (Nadia): Registration → debt entry → dashboard overview → statement sharing → counterparty confirmation
- Journey 2 (Karim): Debt transfer between parties → audit trail → CSV export
- Journey 3 (Salma): Statement link viewing → confirmation → sign-up CTA
- Journey 4 (Admin): Basic admin dashboard → usage monitoring → freemium configuration

**Must-Have Capabilities (tiered for schedule resilience):**

Capabilities are tagged **P0** (ship-critical — no product without these; cannot descope under schedule pressure) or **P1** (ships with MVP if schedule holds; descopes cleanly to Phase 2 if solo-dev bandwidth forces triage). This split exists so that under schedule pressure there is a pre-agreed triage order rather than ad-hoc cuts that damage the product's core promise.

| Tier | Capability | Justification |
|------|-----------|---------------|
| **P0** | User registration & authentication | Gate to all functionality |
| **P0** | Contact/party management | Foundation for all debt operations |
| **P0** | Debt recording (lent/borrowed) with multi-currency & precious metals | Core value — debts preserved in original denomination |
| **P0** | Currency/metal exchange operations (locked rate) | Essential for multi-denomination users; the product promise collapses without it |
| **P0** | Debt transfer between parties (journal entry) | Key differentiator — no competitor offers this simply |
| **P0** | Per-contact account view with per-denomination balances | The "full picture" that replaces spreadsheet chaos |
| **P0** | Dashboard overview across all contacts/denominations | Addresses the #1 Google Sheets pain point |
| **P0** | Shareable statement links (read-only, unauthenticated) | Growth engine + core value proposition; no viral without this |
| **P0** | Statement confirmation button with date tracking | Trust mechanism — "last reconciliation" builds confidence |
| **P0** | Bilingual UI (Arabic RTL + English LTR) | Day-one requirement for global target market |
| **P0** | Legal disclaimer | Regulatory requirement |
| **P0** | Audit logging (immutable) | Data integrity + domain requirement |
| **P1** | Account statement report generation (rich format) | Ship as HTML statement view at MVP; richer printable/PDF format can defer to Phase 2's Digital Social Proof PDF |
| **P1** | Story/context field per transaction (optional) | Low-effort when everything compiles; can defer to Phase 2 without damaging core flows |
| **P1** | Data export (CSV) | GDPR right-to-export is required, but at MVP a simpler JSON-dump export satisfies the legal obligation if CSV slips; retain CSV as target format |
| **P1** | Admin dashboard (basic) | Can initially be replaced by direct-DB queries via Azure Portal for first ~50 users; dashboard hardens as scale demands |
| **P1** | Freemium threshold configuration | Per the **Freemium enforcement timing** note above, thresholds are implemented but disabled at launch — configuration UI can be a post-launch admin feature if schedule slips |

### Post-MVP Features

**Phase 2 — Growth:**

| Feature | Value | Depends On |
|---------|-------|------------|
| Digital social proof PDF (on mutual confirm) | Fills trust gap between "nothing" and "legal contract" | Statement confirmation |
| Advanced link controls (custom expiration windows, access logs per link, per-recipient access revocation) | Security hardening beyond the MVP baseline (basic revocation per FR40 + default 30-day expiration per NFR-S11 ship at MVP) | Statement links |
| Push notifications & payment reminders | Retention driver | Contact management |
| CSV import from spreadsheets | Migration path from Google Sheets / Splitwise | Debt recording |
| Counterparty debt acknowledgment flow (richer) | Deeper two-party trust mechanism | Statement confirmation |
| Micro-merchant tier ($10–15/month) | B2B revenue path — bulk contacts, branding, recurring credit | Freemium infrastructure |
| Abuse detection & automated response | Scale safety | Admin dashboard |
| ROSCA / rotating savings circle support | Community-driven growth channel | Multi-party debt tracking |
| Transaction attachments (images/notes) | Evidence capture — receipts, screenshots, agreements | Debt recording |

**Phase 3 — Vision:**

| Feature | Value | Depends On |
|---------|-------|------------|
| Financial conversation channel | Transform from ledger to relationship tool | Statement links + confirmation |
| Predictive financial insights | Recording → intelligence ("Ali usually pays in 30 days") | Usage data over time |
| Native mobile apps (iOS/Android) | Capture "record in the moment" use case fully | Stable API |
| Advanced price feed analytics | Historical rate trends, rate alerts, cost-basis tracking | Hourly exchange rate feeds (MVP) |
| Payment processing / settlement | Close the loop from tracking to settling | Regulatory assessment |
| Multi-user accounts / team features | Small business expansion | Permission model |
| Partnerships (remittance platforms, gold dealers) | Distribution channels | Market presence |

### Risk Mitigation Strategy

**Technical Risks:**

| Risk | Impact | Mitigation |
|------|--------|------------|
| Angular 21 + RTL complexity underestimated | Delayed launch | Prototype RTL layout early; use CSS logical properties from day one |
| Multi-currency balance calculation edge cases | Data integrity failures | Comprehensive unit tests for balance calculations; decimal precision handling |
| SignalR scaling under load | Real-time features degrade | Start with simple notification patterns; scale infrastructure as needed |
| Statement page performance on low-end devices | Poor first impression for viral targets | Lightweight standalone pages, not full SPA; test on budget Android devices |

**Market Risks:**

| Risk | Impact | Mitigation |
|------|--------|------------|
| Users don't migrate from spreadsheets | Low activation | First-session UX must be faster than opening a spreadsheet; "time to first debt" < 3 min |
| Viral loop doesn't materialize | Slow growth | Track viral coefficient early; A/B test CTA placement and messaging on statement pages |
| Precious metals feature unused | Wasted development | Build denomination system generically; gold/silver are just denominations, not special code paths |
| Freemium thresholds wrong | Revenue or growth impact | Configurable thresholds; monitor usage patterns before enabling paywall |

**Resource Risks:**

| Risk | Impact | Mitigation |
|------|--------|------------|
| Solo developer bandwidth | Slower delivery | Prioritize ruthlessly within MVP; no gold-plating; leverage code generation and frameworks |
| Scope creep from innovation ideas | MVP never ships | Innovation items clearly scoped to Phase 2/3; MVP boundary is firm |

## Functional Requirements

### User Management

- **FR1:** User can register a new account with email and password
- **FR2:** User can authenticate using their credentials
- **FR3:** User can reset their password via email. _Delivery: Auth0 hosted flow (per architecture [ADR-021](./adrs/adr-021-auth0-identity-provider.md)) — email composed, signed, and sent by Auth0; this PRD requirement is satisfied by configuration, not in-app code._
- **FR4:** User can set their preferred language (Arabic or English)
- **FR5:** User can view and edit their profile information
- **FR6:** User can delete their account and all associated data (GDPR right to deletion)
- **FR7:** User can view a legal disclaimer explaining that tracked debts are user records, not legally binding contracts

### Contact Management

- **FR8:** User can add a new contact with a name and optional details (phone, email, notes)
- **FR9:** User can edit an existing contact's information
- **FR10:** User can delete a contact (with warning if outstanding balances exist)
- **FR11:** User can view a list of all their contacts with summary balances per denomination
- **FR12:** User can search and filter contacts by name

### Debt Management

- **FR13:** User can record a new debt (lent or borrowed) with a contact, specifying amount, denomination (currency or precious metal), date, and optional story/context
- **FR14:** User can edit an existing debt entry (with audit trail preserved)
- **FR15:** User can delete a debt entry (with audit trail preserved)
- **FR16:** User can view the full transaction history with a specific contact, with the ability to filter by date range. When a date filter is applied, the system rolls up all transactions before the filter start date into a single "previous balance" per denomination
- **FR17:** User can record debts in any supported denomination — debts are preserved in their original denomination and never auto-converted

### Currency & Exchange Operations

- **FR18:** System fetches exchange rates for all supported currencies and metals every hour, using gold as the internal base unit for rate calculation
- **FR19:** User can perform a currency/metal exchange operation between two denominations for a specific contact. The system auto-populates the exchange rate from the latest hourly feed, and the user can override it manually. The rate is locked at the time of the transaction
- **FR20:** User can view the exchange rate used for any historical exchange operation
- **FR21:** System preserves denomination integrity — no implicit or automatic conversions between denominations
- **FR22:** System stores historical exchange rate data for reference and audit purposes
- **FR23:** User can view the current system exchange rate between any two supported denominations before confirming an exchange operation

### Debt Transfer

- **FR24:** User can transfer all or part of a debt from one contact to another via a journal entry
- **FR25:** System records both sides of the transfer as linked transactions with a clear audit trail
- **FR26:** User can view the transfer history showing the origin and destination of transferred debts

### Account Statements & Dashboard

- **FR27:** User can view a dashboard showing an overview of all contacts with their balances per denomination
- **FR28:** User can view a per-contact account statement with full transaction history and current balance per denomination
- **FR29:** User can generate an account statement report for a specific contact
- **FR30:** User can export their data as CSV (all contacts, all transactions)
- **FR31:** Dashboard displays the user's overall financial position across all contacts and denominations without cross-currency aggregation
- **FR32:** Dashboard displays a bar chart showing debt distribution across contacts
- **FR33:** Dashboard displays a pie chart showing debt distribution across denominations

### Statement Sharing & Confirmation

- **FR34:** User can generate a unique shareable link for any contact's account statement
- **FR35:** Statement recipient can view the shared statement in a mobile-optimized page without creating an account
- **FR36:** Statement recipient can confirm the statement by tapping a "Confirm" button, recording the confirmation date
- **FR37:** User can see the last confirmation date for each contact's statement
- **FR38:** Shared statement pages display Faktuboh branding and a sign-up CTA
- **FR39:** Shared statement pages display the legal disclaimer and a watermark indicating it is a user-generated record
- **FR40:** User can manage (view and revoke) their generated statement links
- **FR41:** System generates cryptographically random, non-guessable tokens for statement links
- **FR42:** System limits statement link generation to 20 links per user per hour. _Magic-link delivery for statement recipients (when a link expires and a fresh token is requested): Auth0 Passwordless (per architecture [ADR-021](./adrs/adr-021-auth0-identity-provider.md)) — rate limit is enforced at Auth0's tenant level in addition to our per-user cap._
- **FR52:** System prompts the user to share a statement at activation moments that drive the viral loop: (a) after each of the first 3 debts recorded per contact, (b) when a contact has recorded-but-unshared transactions more than 7 days old, (c) when a contact's balance crosses a material threshold (configurable; default: any denomination with >€50 equivalent). Nudges are non-blocking and dismissible; a user who dismisses 3 consecutive nudges for a contact is not re-nudged for that contact for 30 days.
- **FR53:** Shared statement pages display a "Report as fraudulent or harassing" affordance visible to recipients. Reports queue to the admin review flow (FR46) with context: link ID, recipient IP-hash, report reason, free-text optional. Reports do not auto-revoke the link — admin review (FR47) decides. Recipients who report see a neutral confirmation page without feedback on the link owner's identity.

### Administration & Platform Operations

- **FR43:** Admin can view a dashboard showing platform metrics (active users, registrations, statement links generated, confirmation rates)
- **FR44:** Admin can configure freemium thresholds (transaction count, contact count)
- **FR45:** Admin can manage grandfathering rules for existing users when transitioning to freemium
- **FR46:** Admin can investigate and flag accounts for potential abuse
- **FR47:** Admin can temporarily suspend statement link generation for a flagged account
- **FR48:** System enforces freemium limits for new users based on configured thresholds
- **FR49:** System logs all debt operations (create, edit, delete, transfer, exchange) in an immutable audit trail

### Real-Time Notifications

- **FR50:** User receives a real-time notification when a counterparty confirms their statement
- **FR51:** System maintains real-time push connection with automatic reconnection on network interruption

## Non-Functional Requirements

### Performance

| Metric | Target | Context |
|--------|--------|---------|
| Page load (landing pages) | FCP < 1.5s, LCP < 2.5s | SEO and first impression |
| **Public-statement bundle (NFR-P1 public)** | **initial JS < 180 KB gzipped**, LCP < 2.5 s on Slow 4G | Hostile first-contact surface; enforced by CI bundle-graph gate per architecture [ADR-024](./adrs/adr-024-minimal-apis-framework.md) cascade |
| **Owner auth-shell bundle (NFR-P1 owner)** | **initial JS < 250 KB gzipped** | Authenticated returning-user shell; design-system data-grid / form-control overhead allowed within this budget |
| SPA initial load | TTI < 3s | First-time user experience |
| Debt recording operation | < 500ms end-to-end | Core UX — must feel instant |
| Shared statement page load | < 2s | First impression for viral targets; lightweight, not full SPA |
| Dashboard rendering (with charts) | < 2s | Overview must be immediate |
| Balance calculation (per contact) | < 200ms | Including date-range rollup |
| Exchange rate feed processing | < 30s per hourly cycle | Background job, no user-facing impact |
| Real-time notification delivery | < 1s from event | Confirmation feedback for counterparty actions (channel per architecture decision) |
| CSV export generation | < 10s for accounts with up to 10,000 transactions | User-facing export operation |
| Concurrent users | 500 simultaneous users while maintaining all performance targets above | MVP launch capacity |

### Security

- **NFR-S1:** All data encrypted at rest using provider-managed keys and in transit (TLS 1.2+).
- **NFR-S2:** User passwords hashed using industry-standard adaptive password hashing with specified work factor: **bcrypt with cost ≥ 12** OR **Argon2id with memory ≥ 64 MiB, iterations ≥ 3, parallelism ≥ 1**. Hash parameters are reviewed every 24 months against current OWASP ASVS guidance (v5.x or then-current version). Passwords are never stored in plaintext.
- **NFR-S3:** Session management with session tokens stored using browser security attributes preventing XSS and CSRF access. Session timeout after 30 minutes of inactivity with auto-extension on activity.
- **NFR-S4:** Statement link tokens are cryptographically random (min 128-bit entropy), non-sequential, and non-guessable.
- **NFR-S5:** Rate limiting: max 10 failed authentication attempts per 15 minutes per IP, max 20 statement links per user per hour, max 60 public statement page views per link per minute.
- **NFR-S6:** Audit trail is append-only and immutable — no API or UI path allows modification or deletion of audit entries.
- **NFR-S7:** GDPR compliance: user data deletion anonymizes audit records (retains transaction structure without PII), exports all user data on request, and provides consent management.
- **NFR-S8:** Shared statement pages expose no internal IDs, user credentials, or data beyond the specific contact's statement.
- **NFR-S9:** Input validation on all user-facing endpoints — protection against XSS, SQL injection, and CSRF.
- **NFR-S10:** Admin operations require separate elevated authentication and are logged in the audit trail.
- **NFR-S11:** Statement links default to **30-day expiration** from generation unless the owner explicitly extends (Phase 2 feature per Post-MVP table). Expired links return a neutral "link expired — contact the sender" page without exposing prior statement data, recipient identity, or link ownership. Expiration is enforced server-side; client-visible countdown is UX only, not a security control.

### Scalability

- **NFR-SC1:** System supports 10,000 registered users with up to 500 concurrent active users at MVP launch.
- **NFR-SC2:** System handles 10x user growth (100,000 users) with infrastructure scaling and < 10% performance degradation.
- **NFR-SC3:** Database schema supports queries completing within performance targets (see Performance NFRs) when filtered by user, contact, date, and denomination across large transaction histories.
- **NFR-SC4:** Statement link pages handle traffic spikes (viral sharing scenarios) independently of the main SPA — isolated serving path. **Sustained capacity commitment: 5,000 requests per minute (~83 requests per second)** with no increase in SPA response times. WhatsApp-driven viral traffic is spiky within seconds; the architecture sizes cache and rate-limit layers against a higher internal design posture (see architecture §2.2), but **this PRD only commits to the sustained rate** — burst-specific SLAs are not part of the PRD contract.
- **NFR-SC5:** Exchange rate feed processing scales independently and does not impact user-facing operations. Measurement: during feed processing, user-facing API response times remain within performance targets (see Performance NFRs) with zero increase in error rate.
- **NFR-SC6:** Infrastructure supports horizontal scaling of web tier and vertical scaling of database tier as needed.

### Accessibility

- **NFR-A1:** WCAG 2.1 AA compliance across all user-facing pages (SPA, landing pages, shared statement pages).
- **NFR-A2:** Full keyboard navigation — all interactive elements reachable and operable without a mouse.
- **NFR-A3:** Screen reader compatibility with **ARIA labels compliant with WCAG 2.1 SC 1.3.1 (Info and Relationships) and SC 4.1.2 (Name, Role, Value)**, semantic HTML5 landmarks, and ARIA live regions for real-time notifications.
- **NFR-A4:** Color contrast ratios: 4.5:1 minimum for normal text, 3:1 for large text.
- **NFR-A5:** RTL accessibility — bidirectional text handled correctly by screen readers; logical reading order preserved.
- **NFR-A6:** Touch targets minimum 44x44px on mobile for all interactive elements.
- **NFR-A7:** Form validation errors associated with their fields and announced to assistive technology.
- **NFR-A8:** Focus management during SPA route changes, modal dialogs, and dynamic content updates.
- **NFR-A9:** **RTL quality acceptance gate.** Before MVP ships, the following named scenarios must pass visual QA on at least two RTL-native reviewer devices (native Arabic speakers, one mobile / one desktop). A failure in any scenario is a ship-blocker for the bilingual commitment in User Success:
  - (a) Mixed Arabic + Latin-numeral transaction stories with correct bidi isolation (embedded URLs, numbers, currency symbols render in the correct visual order)
  - (b) Numeral display in balances and charts — user-configurable between Latin (0-9) and Eastern-Arabic (٠-٩) numerals; default per locale
  - (c) Bidi isolation in transaction notes containing embedded Latin script or URLs — no visual order corruption
  - (d) RTL layout in dashboard bar + pie charts — legends, axes, tooltips, and labels all flow right-to-left
  - (e) RTL in CSV export — column order reversed for Arabic locale; UTF-8 with BOM for Excel-Arabic compatibility; currency symbols correctly positioned per-currency convention
  - (f) Right-aligned date-range picker with Hijri-calendar display as an optional toggle (Hijri calendar read-only at MVP; Gregorian remains primary)

### Integration

- **NFR-I1:** Exchange rate API feed must support gold, silver, and major world currencies (USD, EUR, GBP, SAR, AED, TRY, at minimum). Specific provider to be determined during technical research.
- **NFR-I2:** Exchange rate feed runs on hourly schedule with retry logic on failure (max 3 retries with exponential backoff).
- **NFR-I3:** System gracefully degrades if exchange rate feed is unavailable — uses last known rates and displays a "rates as of [timestamp]" indicator. **Rate freshness SLA:** when last-known rates exceed 4 hours old, a non-dismissible freshness banner is shown on any screen that displays or uses a rate. When rates exceed 24 hours old, **exchange operations (FR19) are disabled** with an explicit disabled-state explanation; viewing historical rates and balances remains permitted. The banner and disable state cannot be suppressed by the primary provider's outage alone.
- **NFR-I4:** Real-time connection handles network interruptions with automatic reconnection within 5 seconds.
- **NFR-I5:** CSV export format is compatible with common spreadsheet applications (Excel, Google Sheets) with proper UTF-8 encoding for Arabic content.
- **NFR-I6:** **Rate feed redundancy.** Gold-base pricing (the internal pivot per FR18) has **two independent providers** configured by MVP day 1 — one primary, one warm secondary with automatic failover within 60 minutes of primary failure. Non-gold currency rates may share a single provider at MVP with a documented revisit trigger for secondary provider onboarding when sustained MAU exceeds 5,000 or when primary-provider outages exceed 2 incidents per quarter.

### Reliability

- **NFR-R1:** 99.9% uptime target (< 8.76 hours downtime per year).
- **NFR-R2:** Zero data loss for committed transactions — every recorded debt, transfer, or exchange operation is durably persisted before confirming to the user.
- **NFR-R3:** Automated database backups with point-in-time recovery capability (minimum 30-day retention).
- **NFR-R4:** Health monitoring and alerting for critical system components (web tier, database, real-time notification service, exchange rate feed).
- **NFR-R5:** Graceful error handling — error messages in the UI include a problem description and suggested corrective action. Detailed error context in server logs. No silent failures.
- **NFR-R6:** Database integrity checks to detect and alert on balance calculation inconsistencies. An inconsistency is defined as: the sum of individual transaction amounts for a contact-denomination pair differs from the stored balance by more than the smallest unit of that denomination (e.g., $0.01 for USD, 0.001g for gold). Checks run daily; alerts trigger within 1 hour of detection.