# Data Transfer

Settings exports are portable JSON envelopes containing the non-credential `MainConfigModel` snapshot. Settings imports accept this format, a current bare settings JSON object, and v2 settings JSON. Imports replace known non-credential settings but never read, replace, or remove `data/config/security`.

All-data exports and backups are ZIP archives with `manifest.json`. The manifest records the archive kind, producer version, each file path, length, and SHA-256. Archives include non-credential settings, profiles, history, temporary state, proofs, supported assets, plugins, and plugin private configuration. Security credentials, backups, staging directories, and crash reports are excluded.

Every settings or all-data import performs a read-only inspection first. After the user confirms, the application saves active state and creates a recovery ZIP in `data/backup`. A recovery snapshot failure aborts the import. Candidate data is validated in `data/.import-staging`, then committed with rollback support. Logs are restored into a timestamped imported directory rather than replacing the active log stream.

v2 ZIP archives are identified by `version.json`. Their settings, name-keyed lists, and histories are migrated before commit. Imported records receive stable `RecordId` values. History is linked only through unambiguous legacy IDs or names. Complete v2 history JSON is retained in `data/legacy/v2-history` with a migration manifest when a field or reference cannot be represented by current models.

Diagnostic exports offer two explicit scopes. The standard package contains all logs under `data/logs` after redaction plus non-identifying runtime metadata. The extended package additionally includes a redacted settings snapshot, plugin/profile counts, and redacted crash reports. They cannot be imported and exclude profile/history content, assets, plugin private configuration, security material, environment variables, paths, user data, network data, and process details.
