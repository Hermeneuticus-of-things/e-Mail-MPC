# Security Policy

## Supported versions

The project currently focuses on the latest development branch and the most recent release line. Security updates are prioritized for current release candidates and the latest stable version.

## Reporting a vulnerability

Please do not open a public issue for security-sensitive findings.

Instead, report privately by using the repository's security reporting channel or by contacting the project maintainer directly through the official repository contact route.

Provide:

- a clear description of the issue
- affected version or branch
- reproduction steps if available
- the impact and risk level
- any suggested fix or mitigation

## Handling expectations

We aim to:

- acknowledge valid reports promptly
- investigate and triage the issue quickly
- provide a clear remediation path when a fix is available
- coordinate responsible disclosure where appropriate

## Security principles

- local mail content and its full-text index are stored in a SQLCipher-encrypted database
- the per-user database key is generated randomly and protected with Windows DPAPI
- provider passwords are stored separately as account-bound, Windows DPAPI-protected files; they are not stored in the database
- IMAP connections require explicit SSL/TLS or STARTTLS and retain MailKit's normal certificate validation
- SMTP sending is explicit, uses the configured account and provider app password, and requires SSL/TLS or STARTTLS with normal certificate validation
- synchronization opens folders read-only and does not change server messages
- if an existing database's key is missing or invalid, startup must not silently replace it
- update verification should happen before installation
- releases should be signed or otherwise checksum-verified where possible
- data handling should minimize exposure and privilege
- security-sensitive changes should be reviewed by maintainers

## Current implementation limits

This is an early development foundation, not a completed security audit. The
database key and app passwords are bound to the current Windows user profile
and are not backed up or recoverable through a recovery key. OAuth,
full-history/incremental synchronization, secure backup, account recovery, and
a complete security audit are not implemented. SMTP sending is limited to
explicit plain-text messages and is not saved locally; if the connection fails
during a send, the server may already have accepted the message. Check the
provider's Sent folder before retrying. Use a test account until these limits
have been reviewed.

## Disclosure policy

Security findings should be handled responsibly and privately until a fix is ready. Public disclosure should happen only after the issue has been mitigated or a patch has been released.
