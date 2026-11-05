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
- message drafts and downloaded attachment content are stored in that same encrypted database
- the per-user database key is generated randomly and protected with Windows DPAPI
- provider passwords are stored separately as account-bound, Windows DPAPI-protected files; they are not stored in the database
- local recovery archives contain an SQLCipher snapshot and DPAPI-protected database/account keys, with SHA-256 integrity checks for every archive entry
- local backup restore is supported only for the Windows user profile that can decrypt the protected keys; it is not cross-device portability
- IMAP connections require explicit SSL/TLS or STARTTLS and retain MailKit's normal certificate validation
- SMTP sending is explicit, uses the configured account and provider app password, and requires SSL/TLS or STARTTLS with normal certificate validation
- synchronization opens folders read-only and does not change server messages
- downloaded attachments are stored in the SQLCipher database with a 25 MiB per-file limit and are never opened automatically
- exporting an attachment writes a regular, unencrypted file only to a path explicitly selected by the user
- if an existing database's key is missing or invalid, startup must not silently replace it
- update checks are user-triggered; the stable manifest pins the setup URL, and the installer size and SHA-256 are verified before the app offers to launch it
- release checksums detect download corruption but do not authenticate the publisher; release CI publishes GitHub build-provenance attestations that users can verify with `gh attestation verify <asset> -R Hermeneuticus-of-things/e-Mail-MPC`
- the desktop updater does not yet verify GitHub attestations or an Authenticode publisher signature; never interpret its SHA-256 check as publisher authentication
- releases should be signed or otherwise checksum-verified where possible
- data handling should minimize exposure and privilege
- security-sensitive changes should be reviewed by maintainers

## Current implementation limits

This is an early development foundation, not a completed security audit. The
database key and app passwords are bound to the current Windows user profile.
Encrypted local backups preserve the DPAPI-protected key and account secrets,
but can only be restored where that same user profile can decrypt them; there
is no cross-device recovery key. OAuth, full-history synchronization, and a
complete security audit are not implemented. SMTP sending is limited to
explicit plain-text messages and is not saved locally; if the connection fails
during a send, the server may already have accepted the message. Check the
provider's Sent folder before retrying. Use a test account until these limits
have been reviewed.

## Disclosure policy

Security findings should be handled responsibly and privately until a fix is ready. Public disclosure should happen only after the issue has been mitigated or a patch has been released.
