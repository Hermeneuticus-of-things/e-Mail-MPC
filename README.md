# eMail MPC

A secure, fast, and privacy-conscious desktop email client designed for users who want a clean workflow, strong local control, and dependable cross-device reliability.

This project is built around the idea that email should be safe by default, fast to use, and flexible enough to support both personal and professional workflows without forcing users to understand low-level technical details.

## Why this project exists

Most people use email every day, but many desktop clients are either too heavy, too cluttered, or too opaque about security and data handling. eMail MPC aims to provide a streamlined alternative focused on:

- local-first privacy
- strong encryption for stored content
- dependable sync and update flows
- modern email workflow with search, drafting, and organization
- simple deployment for individuals and shared machines

## Who it is for

This project is intended for:

- individuals who want a simpler, less noisy email client
- professionals who need reliable inbox management
- organizations that want shared deployment and secure local storage
- users who care about privacy and explicit control over their data

## Product direction

The planned product is a desktop email client for reading, organizing, searching,
and sending mail. The current build connects to compatible IMAP/SMTP providers
with app passwords and supports bounded, manual, read-only synchronization,
local message reading, and explicit plain-text sending. OAuth, full mailbox
migration, drafts, updates, and mobile support remain roadmap work.

## How it works

The product architecture is built around a few core principles:

1. Local-first data handling
   - messages are stored locally in a versioned encrypted structure
   - the app controls how content is written and restored

2. Secure update model
   - releases are versioned and validated before installation
   - rollback is supported to avoid broken updates

3. Shared deployment model
   - installation can be done once for all users on a machine
   - updates can continue without repeated admin intervention

4. Extensible mailbox backend
   - support for IMAP, Exchange, OAuth, and future providers can be layered into a stable core

## Implemented foundation

- Windows .NET 10/WPF application shell
- SQLCipher-encrypted local database and Windows DPAPI-protected key
- local full-text search UI and core search filters
- encrypted account and folder metadata with account-scoped message search
- versioned migrations that preserve existing mail and rebuild the search index
- IMAP account setup with explicit SSL/TLS or STARTTLS and provider-issued app passwords
- IMAP connection verification and remote folder discovery
- on-demand, read-only indexing of up to the 200 newest messages per folder
- text-only body retrieval; attachments are not downloaded, and body parts over 2 MiB are skipped
- local reading of synchronized message text without changing server read state
- explicit plain-text SMTP sending over certificate-validated SSL/TLS or STARTTLS
- separate DPAPI-protected account secrets, bound to the current Windows user and account ID

OAuth, HTML compose, attachments, drafts, import/export, incremental/full-history
sync, automatic updates, and telemetry are not implemented. Sent messages are
not stored in a local Sent folder; check the provider's Sent folder after sending.
Synchronization is deliberately capped at 200 newest messages in each folder
per request; it is not a complete mailbox migration or a production sync engine.

## Roadmap

The project follows a staged roadmap based on product stability, not feature stacking without foundation.

### v0.1 — Foundation
- encrypted local storage
- shared installation support
- automated update system with rollback

### v0.2 — Core reliability
- secure account and sync engine
- mailbox indexing and search
- message draft management
- update validation and release confidence checks

### v0.3 — Product polish
- localization and accessibility
- improved sorting and prioritization
- quality metrics and diagnostics
- user-facing reporting and release transparency

### v0.4 — Multiplatform expansion
- macOS client support
- Android support
- sync and backup across devices

### v0.5 — Advanced workflow
- smart rules and automation
- productivity features
- better attachment and document handling

### v1.0 — Stable release
- fully validated desktop experience
- secure release pipeline and rollback support
- cross-platform readiness
- public-quality documentation and support processes

For the full plan, see [ROADMAP.md](ROADMAP.md).

## Installation

There is no installer or production release yet. Build and run the Windows
development app using the instructions below.

To add an account, choose **Add account**, enter the provider's IMAP/SMTP server
settings and a provider-issued app password, then choose **Verify and add**.
The app tests IMAP over certificate-validated TLS before saving the account.
Use **Sync mail** to download the newest messages; IMAP folders are opened
read-only, and messages on the server are not changed. Gmail app passwords
require an eligible account with two-step verification enabled. Microsoft 365
accounts requiring OAuth are not supported by this initial connection flow.
For generic IMAP accounts, obtain the correct server hostname and app password
from the provider; the Gmail values shown in the form are only defaults.
Select a search result to read its locally stored text. Choose a specific
account and **Compose** to send a plain-text message through its configured
SMTP endpoint. The app does not create drafts or save a local Sent copy, and a
connection failure during sending can leave delivery status uncertain; check
the provider's Sent folder before retrying.

## Development

This project is designed to support a clear, maintainable engineering process:

- versioned release metadata from a single source of truth in [version.json](version.json)
- public update metadata in [release-manifest.json](release-manifest.json)
- CI validation for build, metadata, and release consistency
- automated changelog generation from conventional commits
- documented release and rollback procedures

### Build the Windows app

Requirements: Windows and the .NET 10 SDK.

```powershell
dotnet build EMailMPC.slnx
dotnet test EMailMPC.slnx
dotnet run --project src/EMailMPC.Desktop/EMailMPC.Desktop.csproj
```

The application stores its encrypted database and DPAPI-protected account
secrets in the current user's `%LOCALAPPDATA%\eMail MPC\data` directory.
Back up any important messages independently; this development build does not
provide an export, recovery key, or database restore workflow.

The current build initializes an encrypted local database, searches and reads
locally synchronized mail, and can connect to compatible IMAP/SMTP servers
using app passwords. It does not perform OAuth, import eM Client data, or modify
messages on the server. Use a copy/test account until the sync and migration
limits are acceptable for your needs.

## Contributing

Contributions are welcome. For changes to the project:

- open or review an issue before starting large work
- keep changes focused and documented
- follow the project’s release and testing conventions
- ensure security-sensitive changes are reviewed carefully

## Security

Security is a first-class requirement.

- local data should remain encrypted by default
- update verification must happen before installation
- privileged actions should be minimized and explicit
- external dependencies should be reviewed regularly

See [SECURITY.md](SECURITY.md) for reporting and handling guidance.

## Reporting issues

Please use the GitHub issue tracker to report bugs, releases issues, feature requests, or security concerns.

Before filing a report, include:

- the exact version used
- the operating system and environment
- steps to reproduce the problem
- expected versus actual behavior
- any relevant logs, screenshots, or error details

## License

No license has been selected yet. No `LICENSE` file is present; do not assume
the project is licensed for reuse or distribution.

## Project status

The Windows .NET 10/WPF application shell is in active development. The current
build initializes a SQLCipher-encrypted local database and a Windows DPAPI
protected database key and has IMAP/SMTP app-password setup, bounded manual
sync, local reading, and explicit plain-text sending. It does not support
OAuth, full-history synchronization, or migration from eM Client. Do not rely
on it as your only mail client or as a complete backup.

The roadmap prioritizes secure storage, account connectivity, synchronization,
and data migration before broadening features or platforms.

## Related documents

- [ROADMAP.md](ROADMAP.md)
- [SEARCH_FEATURE_SPEC.md](SEARCH_FEATURE_SPEC.md)
- [CHANGELOG.md](CHANGELOG.md)
- [SECURITY.md](SECURITY.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
