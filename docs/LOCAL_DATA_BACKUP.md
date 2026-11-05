# Local data backup and restore

eMail MPC can create an `.empbackup` archive from **File → Back up local data** and restore one from **File → Restore local data**.

## What the archive contains

- A consistent SQLCipher snapshot of local messages, folders, drafts, attachment metadata, and downloaded attachment content.
- The database key protected by Windows DPAPI.
- Account credentials protected by Windows DPAPI.
- A versioned manifest with sizes and SHA-256 hashes for each payload file.

The database and credentials remain encrypted in the archive. Keep the archive private; its protected keys can be used by the Windows user profile that created it.

## Restore requirements and behavior

- Restore on the Windows user profile that can decrypt the DPAPI-protected keys in the archive. This is a local recovery format, **not** a cross-device transfer or a recovery key.
- Restore replaces the current local database, database key, and protected account credentials with the contents of the selected archive. Confirm the prompt only when replacement is intended.
- The archive is validated and the encrypted database is opened before the current data directory is replaced.
- After successful restore, eMail MPC closes. Start it again to open the restored data.
- If validation fails before replacement, the existing data directory remains in place.
- Account settings and credentials are restored, but the provider may still require reauthentication.

## Recovery practice

Create backups regularly and keep a copy outside the application data directory. Verify that a recent archive exists before replacing application data or moving to a new installation. Protect the backup location with the same care as the Windows user profile.

If the Windows profile cannot decrypt the archived DPAPI key, this backup cannot recover the database. The application does not currently offer password-based portable backups or a separate recovery-key mechanism.

When an existing database needs a schema upgrade, eMail MPC first creates a verified encrypted snapshot beside the database named `mail.db.pre-migration-<version>-<timestamp>-<id>.db`. The migration runs transactionally: if it fails, the database remains at its prior schema version and can be retried on the next launch after the underlying problem is corrected. The snapshot uses the same SQLCipher key and remains protected by the same Windows profile. It is retained after migration; keep it until the upgraded application has opened and the mail data has been checked.

To restore one, choose **File → Restore pre-migration snapshot...** and select a snapshot from the current eMail MPC data directory. The app validates and migrates a staged copy before replacing the current database; the selected snapshot itself and the current database key and account credentials are left unchanged. The application closes after the restore so it can reopen the database cleanly. If the file cannot be validated or the replacement fails, the service attempts to return the prior database and retains recovery files if rollback itself fails. This action only rolls back mail data, not the installed application version, and is not a substitute for a regular `.empbackup` stored elsewhere.
