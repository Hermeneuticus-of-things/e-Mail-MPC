# Roadmap

This roadmap defines the product path from foundation to stable release. The sequence is intentional: security, installation, and update reliability come before feature breadth.

## Milestone v0.1 — Foundation

### Goals
- establish a secure, versioned local data model
- support safe installation on shared machines
- define reliable update and rollback behavior

### Deliverables
- encrypted local data format with migration plan
- shared-machine installer flow
- update service with validation and rollback
- recovery and restore story

### Exit criteria
- data format is documented and versioned
- install flow supports per-user and shared-user scenarios
- update flow verifies release assets before installation
- rollback path is tested and documented

## Milestone v0.2 — Core reliability

### Goals
- support stable account integration and mailbox sync
- improve search and inbox organization
- stabilize the message drafting workflow

### Deliverables
- IMAP account setup and TLS connection verification
- bounded read-only manual mailbox sync
- local search index and account filtering
- local reading of synchronized text and explicit plain-text SMTP sending
- message draft creation and persistence

### Current progress
- Done: IMAP/SMTP setup with app passwords, TLS-only connection options, remote-folder discovery, encrypted local storage, manual sync capped at 200 newest messages per folder, local reading, and explicit plain-text sending.
- Done: eM Client style query language (keyed operators, phrases, negation, OR groups), switchable relevance (bm25) / date ranking, local natural-language query interpretation, and incremental IMAP sync (UIDVALIDITY + highest-UID tracking with flag refresh and expunge removal).
- Remaining: provider-specific OAuth, attachment download and compose attachments, folder management, full-history synchronization, complete mailbox import/export, drafts, account removal UI, Sent-folder persistence, and recovery/backup.

### Exit criteria
- standard email workflows work reliably end-to-end
- search returns relevant results across stored mail data
- draft operations are stable and recoverable

## Milestone v0.3 — Product quality

### Goals
- improve day-to-day UX and public confidence
- add diagnostics and monitoring without forcing telemetry
- establish documentation and support quality

### Deliverables
- localization support
- accessibility improvements
- opt-in telemetry and diagnostics
- release quality reporting and benchmarks

### Exit criteria
- major user flows are measured and validated
- disabled telemetry remains fully non-invasive
- docs and support instructions are current and consistent

## Milestone v0.4 — Multiplatform expansion

### Goals
- validate the product on additional platforms
- reuse the same secure storage and update model
- support cross-device workflows intentionally

### Deliverables
- macOS support
- Android support
- backup and sync strategy for multi-device use

### Exit criteria
- shared storage format works across supported platforms
- update flow remains consistent across devices
- explicit sync or export policy is documented

## Milestone v0.5 — Advanced workflow

### Goals
- move from foundation to productivity enhancement
- reduce repetitive work for regular users
- improve organization and control over large inboxes

### Deliverables
- smart filtering and rules
- productivity automation
- improved attachment and message handling
- user-driven defaults and personalization

### Exit criteria
- users can automate common tasks without breaking accuracy
- advanced workflows remain optional and transparent

## Milestone v1.0 — Stable release

### Goals
- release a mature, trustworthy email client
- provide a safe, consistent public experience
- establish long-term maintenance quality

### Deliverables
- stable desktop experience
- secure public release pipeline
- rollback and incident handling process
- documented support and maintenance policy

### Exit criteria
- public release is validated and repeatable
- versioning, signing, and asset integrity checks pass consistently
- documentation, privacy, and security requirements are complete

## Strategic priorities

1. Security before features
2. Reliable update flow before broad adoption
3. Account and mailbox integrity before advanced automation
4. Shared deployment clarity before platform expansion
5. User trust as a product requirement, not a marketing claim

## Notes

The roadmap is intentionally conservative. Early milestones focus on the layer that enables every later feature: safe storage, stable install, dependable updates, and trustworthy data handling.
