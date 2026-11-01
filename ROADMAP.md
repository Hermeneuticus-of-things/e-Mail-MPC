# eMail MPC Roadmap

This roadmap is the implementation plan for turning eMail MPC into a complete, dependable email product. It separates the **Windows standalone client** from the later **AI/MCP** and **additional-platform** goals so that neither depends on the other to be useful.

The order is deliberate: protect mail and recoverability first, finish everyday email workflows next, then add optional automation and expand platforms. A milestone is complete only when its exit criteria are implemented, tested, and documented; a feature being listed here does not mean it is already available.

The application UI follows the [Ionuț Application Design Language](DESIGN_LANGUAGE.md): keep the email work prominent, retain useful commands, organize complete menus, and make supporting controls quiet and reachable.

## Product completion targets

### Windows standalone client — eMail MPC 1.0

A person can connect supported accounts, read and organize mail, compose and send ordinary messages, manage attachments and drafts, recover local data, and update the application safely. The client remains useful when no AI agent or AI service is installed.

### Optional agent and assistant capabilities — post-1.0 product work

Compatible agents can use explicitly authorized email tools through an MCP server. The desktop app can optionally provide an assistant. Both are off unless the user enables them, and neither is required for ordinary email use.

### Additional platforms — separate expansion

macOS and Android clients, and any cross-device sync service, are separate deliverables. They require a documented cross-platform security and data-portability design; Windows DPAPI-protected data is not itself a cross-platform format.

### Scope boundary to resolve before calling the product feature-complete

The current target is a secure email client, not a complete personal-information manager. Contacts/address book, calendar and invitations, tasks, chat, PGP/S/MIME, and proprietary non-IMAP providers are not part of the committed Windows 1.0 scope. Decide explicitly whether any of these belong in a later milestone; do not imply they are included in the email-client completion target.

## Current state

The repository currently contains a Windows WPF client with encrypted local storage, IMAP/SMTP account setup using app passwords, bounded incremental read-only IMAP sync, local reading and search, plain-text sending, saved drafts, on-demand attachment downloads, encrypted local backup/restore, and user-triggered verified update staging.

Search includes keyed operators, phrases, negation, OR groups, relevance/date sorting, and a deterministic Romanian/English query helper. The helper is not an AI model.

The current implementation is a working foundation, **not a finished email client**. Server-side mailbox mutations, OAuth, full-history synchronization, rich composition, sending attachments, persistent Sent mail, MCP, embedded AI, and macOS/Android clients are not yet shipped. See the README for the user-facing feature and limitation summary.

## Priority 0 — Finish reliability and safe recovery

**Status: In progress.** Backup/restore and verified manual update staging exist; rollback, installation validation, and migration are incomplete.

### Work

- Implement update failure detection and a tested recovery/rollback path for both installed and portable deployments.
- Define and test data/schema migration behavior, including interruption, restart, backup restoration, and compatibility with older data.
- Validate install, upgrade, uninstall, per-user install, elevated machine-wide install, and portable replacement on supported Windows versions.
- Verify update authenticity, not only transfer integrity: establish release signing and a trusted verification path before presenting releases as publisher-authenticated. SHA-256 alone does not establish who published a package.
- Make failure messages actionable; preserve the existing data and provide a recovery path when an update or migration fails.
- Document support boundaries, backup practices, and recovery steps.

### Exit criteria

- Failed update and failed migration scenarios recover without silently losing mail or credentials.
- Installer and portable upgrade paths pass repeatable Windows tests.
- Published packages are authenticated as well as checked for expected size and digest.
- Backup, restore, rollback, and migration procedures are documented and exercised.

## Priority 1 — Complete everyday Windows email workflows

**Status: Partially implemented.** The current client supports app-password IMAP/SMTP, limited sync, reading, plain-text sending, drafts, and attachment downloads.

### Accounts and authentication

- Add provider-specific OAuth where required, including secure token storage, refresh, expiry, revocation, and reauthentication.
- Exercise multiple-account setup, connection failure, credential changes, account removal, and recovery.
- Clearly show connection and authentication state without exposing secrets.

### Mailbox sync and folders

- Provide user-controlled full-history sync in addition to the current newest-200-per-folder limit; include progress, cancellation, restart/resume, and clear storage controls.
- Complete folder operations and server consistency: create/rename/delete where supported, subscribe/unsubscribe, refresh, and safe handling of provider-specific folders.
- Add explicit server-backed read/unread, flag, archive, and delete/trash operations, with conflict and retry handling. The current IMAP flow is read-only on the server.
- Keep incremental sync reliable across UIDVALIDITY resets, expunges, connection interruptions, and large mailboxes.

### Reading, composing, and sending

- Add reply, reply-all, and forward flows that preserve the intended recipients and message context.
- Add compose attachments, configurable size limits, progress, failure recovery, and a clear distinction between downloaded and remote attachments.
- Decide and implement safe HTML email display and composition, including sanitization, remote-content controls, charset handling, and plain-text fallback.
- Add a durable Sent-folder story: reconcile accepted SMTP sends with provider Sent folders and avoid accidental duplicate sends after uncertain outcomes.
- Improve draft behavior with reliable autosave/recovery while preserving the current explicit save and encrypted local storage guarantees.
- Handle message threading, common MIME structures, inline images, and malformed messages without losing the readable plain-text view.

### Mail data portability

- Implement complete import/export for the explicitly supported formats, with validation, progress, duplicate handling, and failure recovery.
- Define whether migration means moving only messages or also folders, flags, drafts, attachments, and account settings; never export credentials by default.
- Test large exports and imports and document encryption and portability limits.

### Exit criteria

- A user can complete the ordinary lifecycle—connect, sync, read, reply, attach, send, find in Sent, organize, and recover—without relying on a separate mail client for normal operations.
- Server changes are explicit, correctly synchronized, and recoverable after network failures.
- Full-history sync and import/export are bounded, resumable, and covered by integration tests.

## Priority 2 — Finish organization and daily usability

**Status: Partially implemented.** Search, filters, sorting, and a compact Hallmark-inspired workspace exist; broader organization and accessibility work remains.

### Work

- Add saved searches, reusable filters, and user-configurable sorting and views.
- Add user-defined rules with a preview/test mode, explicit enablement, and understandable explanations of each action.
- Add configurable signatures, identity defaults, and per-account composition preferences.
- Add keyboard shortcuts for frequent actions and document them; keep every action reachable through menus or another discoverable path.
- Add localization, starting with complete and consistent Romanian and English UI and support text.
- Audit keyboard navigation, focus visibility, screen-reader names/states, contrast, scaling, and reduced-motion behavior.
- Improve loading, empty, offline, error, and success states; keep errors actionable and close to the affected action.
- Add performance and memory testing for large local mailboxes and search indexes.

### Exit criteria

- Core workflows are usable with keyboard and assistive technology.
- Localization is complete for the supported languages rather than mixed-language.
- Rules and filters are transparent, reversible, and tested against representative mailbox data.
- Large-mailbox responsiveness has measured acceptance thresholds and regression coverage.

## Priority 3 — Security, release quality, and Windows 1.0

**Status: In progress.** CI and release validation exist; final hardening and release gates remain.

### Work

- Threat-model local storage, backups, logs, attachments, remote content, update installation, and any future agent/AI boundary.
- Review dependency updates, secret handling, TLS configuration, archive validation, and permissions.
- Add opt-in diagnostics only if they materially help support; document exactly what is collected and keep the default fully offline from telemetry.
- Test upgrade paths from every supported released data version, clean installs, uninstall/reinstall, and recovery after interruption.
- Establish signed releases, a repeatable release checklist, incident response, and rollback communication.
- Keep README, security policy, backup instructions, support guidance, and roadmap synchronized with shipped behavior.

### Windows 1.0 exit criteria

- Priorities 0–2 are complete for the supported Windows client scope.
- CI covers the relevant builds, tests, release metadata, and installer validation.
- Release assets are signed and verifiable; the release and rollback procedures have been rehearsed.
- Security, privacy, compatibility, accessibility, and data-recovery limitations are documented.
- No critical or high-severity known defects remain in account setup, sync, send, update, backup, or restore.

## Priority 4 — Optional MCP server for external AI agents

**Status: Not implemented.** No MCP server or agent-facing email API is currently shipped.

### Work

- Define a narrow, versioned local API and MCP tool set; begin with read-only account, folder, message, and search operations.
- Add explicit capability grants, per-tool authorization, revocation, and visible status. Do not expose credentials or unrestricted database access.
- Require clear user confirmation for sending, deleting, moving, or other consequential mailbox actions.
- Make the local transport and process boundary secure; prevent untrusted local clients from silently connecting.
- Add audit events that are useful to the user without recording message bodies or secrets unnecessarily.
- Test hostile tool arguments, malformed messages, large results, cancellation, permission denial, and server shutdown.
- Document compatible hosts, installation, permissions, data flows, and what happens when the agent is unavailable.

### Exit criteria

- MCP is opt-in and does not change standalone client behavior.
- Every exposed capability has a documented permission model and tests.
- A user can inspect, limit, and revoke agent access; consequential actions remain under user control.

## Priority 5 — Optional embedded AI assistant

**Status: Not implemented.** No model, assistant UI, or AI service is currently shipped.

### Work

- Decide and document supported inference choices: local model, remote provider, or both. Do not imply offline or private processing until the chosen implementation proves it.
- Make model/provider configuration, network use, data sent, retention implications, and cost visible before enabling a provider.
- Start with optional assistance such as search-query drafting and message summarization; require review and explicit user action before sending or changing mail.
- Keep AI output distinguishable from original message content and protect against instructions embedded in emails or attachments.
- Provide disable/reset controls and test unavailable providers, refusal, timeouts, sensitive content, and malformed output.

### Exit criteria

- AI is disabled by default, independently removable/disableable, and never required for email use.
- Data-flow and consent behavior is documented and testable.
- AI cannot send, delete, or change mailbox data without explicit user authorization.

## Priority 6 — macOS and Android, then cross-device use

**Status: Not implemented.** The current desktop application is Windows WPF and its key protection uses Windows DPAPI.

### Work

- First make a platform architecture decision covering UI framework, secure key storage, database compatibility, update delivery, and support scope.
- Design a cross-platform encrypted data and key-management model before promising that Windows backups migrate to another platform.
- Build and validate a macOS client with platform-native secure storage, packaging, updates, accessibility, and migration.
- Build and validate an Android client with mobile lifecycle, secure storage, attachment handling, and mobile-appropriate UX.
- Treat cross-device synchronization as a separate opt-in service/design: define conflict resolution, encryption/key ownership, deletion, offline behavior, and server operations before implementation.
- Provide explicit export/import or migration paths even if cloud sync is not offered.

### Exit criteria

- Each platform has a supported install, update, recovery, security, and accessibility story.
- Data can be moved only through a documented, user-controlled, tested process.
- Cross-device sync is never implied by local backups and has an explicit threat model and conflict policy.

## Priority 7 — Advanced workflows and long-term maintenance

**Status: Planned.**

- Add productivity automation only after core mailbox actions are reliable and reversible.
- Consider scheduled actions, richer rules, templates, and personalization as separate user-controlled features.
- Add optional quality reporting and benchmarks without fixing transient test counts into product documentation.
- Maintain supported provider compatibility, dependencies, security response, and release support policy.
- Reassess feature requests against privacy, reliability, accessibility, and maintenance cost before adding them.

## Delivery order and estimates

The dependency order is **Priority 0 → Priority 1 → Priority 2 → Priority 3** for the Windows standalone 1.0 client. MCP and embedded AI depend on the security and authorization work in Priority 3. macOS, Android, and cross-device sync depend on a separate platform/data design. These streams should not be treated as parallel quick additions.

For planning only, a rough estimate for one experienced developer working full-time is **4–8 months to a tested Windows 1.0**, **another 2–4 months for MCP and an optional assistant**, and **at least 9–18 additional months for macOS, Android, and secure cross-device sync**. These ranges can change substantially with provider OAuth requirements, HTML/MIME edge cases, the chosen AI deployment model, platform architecture, and test coverage. They are not delivery promises.

The repository does not yet have a staffed delivery schedule or all provider/platform decisions. Re-estimate each milestone when its design decisions and acceptance tests are agreed; report progress by the exit criteria above, not by an invented percentage.

## Explicit non-goals unless separately approved

- Requiring an AI agent, cloud account, or telemetry for ordinary email use.
- Claiming support for a provider, protocol, platform, or migration format before it is tested.
- Silently sending, deleting, moving, or exporting mail.
- Treating SHA-256 alone as publisher authentication.
- Treating a DPAPI-bound backup as portable between Windows users or devices.

## Strategic priorities

1. Protect mail and preserve user data.
2. Finish reliable everyday email before adding breadth.
3. Keep AI/MCP optional, permissioned, and transparent.
4. Prove release and recovery paths before broad distribution.
5. Expand platforms only with a real secure-storage and migration design.
6. Describe only behavior that is implemented and tested.
