# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project uses [Conventional Commits](https://www.conventionalcommits.org/) for release notes.

## [Unreleased]

### Added
- Windows desktop application shell targeting .NET 10 and WPF
- Core library, solution structure, and unit-test project
- SQLCipher-backed local mail database with Windows DPAPI-protected key
- IMAP app-password account setup with TLS verification and folder discovery
- Account-bound DPAPI protection for saved provider app passwords
- Read-only manual IMAP sync for the 200 newest messages in each folder; retrieves text bodies only and skips body parts over 2 MiB
- Local message reading from the encrypted store without changing server read state
- Explicit plain-text SMTP compose and send for app-password accounts with TLS-only transport
- SMTP delivery-status warning on connection errors to reduce duplicate retries
- Account selector and account-scoped local search
- Atomic batch message persistence and account/folder metadata persistence
- Encrypted account and folder metadata with account-scoped message search
- Local full-text message search with parameterized filters
- Version 1-to-3 database migration preserving messages and rebuilding the full-text index
- Windows desktop search screen for locally stored messages
- Search criteria validation and encrypted database test coverage
- Search behavior specification based on public and local evidence
- Message preview and full local message retrieval
- SMTP transport security and authentication guard tests
- project foundation and public documentation
- roadmap and release planning structure
- eM Client style query language for local search: keyed operators (from:, to:, cc:, bcc:, in:, is:, has:, after:, before:, subject:, body:), quoted phrases, negation, and OR groups, with a safe literal fallback
- relevance ranking for full-text search results via FTS5 bm25 with a newest-first/relevance sort switch in the desktop UI and per-result relevance scores
- local natural-language query interpretation (Romanian and English patterns) producing an editable query plan shown in the desktop UI, with a safe literal fallback when nothing is recognized
- incremental IMAP synchronization: per-folder UIDVALIDITY and highest-UID tracking (schema v4), UID-range fetch for new mail only, bounded flag refresh with removal of server-expunged messages, and automatic bounded resync when UIDVALIDITY changes
- secure update and release validation policy documentation
- contribution and security guidance

### Changed
- standardized release and maintenance workflow expectations

### Fixed
- none yet

## [0.1.0] - 2026-10-28

### Added
- initial project foundation
- secure local-data design goals
- installation and update architecture planning
- structured product roadmap for the first product milestones
- public repository documentation structure

### Changed
- repository organization aligned to release-ready documentation practices

### Fixed
- none yet

---

## Release note conventions

This project follows conventional commit categories:

- `feat:` for new features
- `fix:` for bug fixes
- `docs:` for documentation updates
- `refactor:` for internal cleanup without user-facing behavior changes
- `chore:` for maintenance work
- `security:` for security-sensitive changes

When a release is cut, the generated changelog should summarize the merged work into release-ready notes.
