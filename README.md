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

## What the app does

The application is designed to support the core daily email workflow:

- read and organize messages
- search across inbox and folders
- compose and send messages
- manage drafts and replies
- store email data safely on disk
- handle updates in a controlled and verifiable way
- support future mobile and cross-device use cases

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

## Key features

- secure local storage for messages and metadata
- encrypted file format with migration support
- versioned release flow and update validation
- shared machine installation support
- fast inbox search and filtering
- message composition and drafting workflow
- future-ready architecture for desktop and mobile clients
- optional telemetry with explicit opt-in and anonymization

## Example workflow

1. Install the application on the workstation.
2. Add an email account using a supported protocol.
3. Read, search, and organize messages in the inbox.
4. Draft replies or new messages directly in the app.
5. Keep data protected through local encryption and controlled updates.
6. Export or save content explicitly when needed.

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

To install the application, follow the release instructions for your platform and use the official signed release assets published in GitHub Releases.

Current release management expectations:

- semantic versioning with tags like `vX.Y.Z`
- release artifacts tied to signed metadata
- installation and update flows validated before publishing
- rollback path preserved when a release fails verification

## Development

This project is designed to support a clear, maintainable engineering process:

- versioned release metadata from a single source of truth in [version.json](version.json)
- public update metadata in [release-manifest.json](release-manifest.json)
- CI validation for build, metadata, and release consistency
- automated changelog generation from conventional commits
- documented release and rollback procedures

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

This project is distributed under the terms of the repository license. See [LICENSE](LICENSE) for details.

## Project status

This project is in active product definition and foundation-building stages. The roadmap prioritizes secure storage, install/update reliability, and release integrity before expanding into broader feature sets and multi-platform delivery.

## Related documents

- [ROADMAP.md](ROADMAP.md)
- [CHANGELOG.md](CHANGELOG.md)
- [SECURITY.md](SECURITY.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
