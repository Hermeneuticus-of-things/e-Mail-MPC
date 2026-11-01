# Roadmap

This roadmap defines the product path from foundation to stable release. The sequence is intentional: security, installation, and update reliability come before feature breadth.

The application UI follows the general [Ionuț Application Design Language](DESIGN_LANGUAGE.md): preserve all needed commands and settings, organize complete menus, and keep the app's core email work visually primary.

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

### Current progress
- Done: a Windows x64 self-contained build path, portable ZIP profile mode, and a per-user/elevated Inno Setup wizard that keeps application data outside the install directory.
- Done: encrypted local backup and restore with DPAPI-protected key/credential preservation, archive integrity checks, validation before replacement, and same-user restore requirements.
- Done: a user-triggered update check that pins stable manifest and asset URLs, verifies release version, size, and SHA-256, offers the verified setup wizard for installed builds, and stages the verified ZIP for portable builds without background polling.
- Remaining: automatic recovery/rollback when installation fails, installation testing across supported Windows configurations, and a tested data migration path.

## Milestone v0.2 — Core reliability

### Goals
- support stable account integration and mailbox sync
- improve search and inbox organization
- stabilize the message drafting workflow

### Deliverables
- IMAP account setup and TLS connection verification
- bounded read-only manual mailbox sync
- incremental IMAP sync with UIDVALIDITY recovery
- local search index and account filtering
- local reading of synchronized text and explicit plain-text SMTP sending
- on-demand attachment download and explicit save-to-file
- encrypted per-account message draft creation, editing, persistence and deletion

### Current progress
- Done: IMAP/SMTP setup with app passwords, TLS-only connection options, remote-folder discovery, encrypted local storage, manual sync capped at 200 newest messages per folder, local reading, and explicit plain-text sending.
- Done: eM Client style query language (keyed operators, phrases, negation, OR groups), switchable relevance (bm25) / date ranking, local natural-language query interpretation, incremental IMAP sync (UIDVALIDITY + highest-UID tracking with flag refresh and expunge removal), and encrypted on-demand attachment download/save (25 MiB per file).
- Done: search-parity hardening for negated OR groups, repeated structured filters, and maximum-date bounds, with regression coverage.
- Done: manually saved encrypted per-account drafts with edit, reopen, send cleanup, delete, and schema migration coverage.
- Done: Hallmark-inspired WPF mail workspace with account and folder filters, resizable message-list/reader panes, empty states, and confirmed account removal that preserves local messages, downloaded attachments, and drafts.
- Remaining: provider-specific OAuth, compose attachments, folder management, full-history synchronization, complete mailbox import/export, and Sent-folder persistence.

### Next work, in roadmap order

1. Implement and test automatic update rollback and failure recovery.
2. Validate installer and data-migration behavior across supported Windows configurations.
3. Finish core mailbox workflows: OAuth, compose attachments, folder management, full-history sync, import/export, and Sent-folder persistence.
4. Complete product-quality work, then evaluate cross-platform expansion and advanced workflows, including optional MCP and embedded AI, after the security and core-reliability gates are met.

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
- optional MCP server for compatible AI-agent clients
- optional built-in AI assistant
- continued standalone email workflows when AI features are unavailable or disabled

### Exit criteria
- users can automate common tasks without breaking accuracy
- advanced workflows remain optional and transparent
- AI and agent features document data flows, permissions, and online/offline requirements before accessing mailbox content
- users can use the email client without enabling an agent or embedded AI

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
