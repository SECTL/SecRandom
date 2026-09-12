# Data Transfer

Settings exports are portable JSON envelopes containing the non-credential `MainConfigModel` snapshot. Settings imports accept this format, a current bare settings JSON object, and v2 settings JSON. Imports replace known non-credential settings but never read, replace, or remove `data/config/security`.

All-data exports and backups are ZIP archives with `manifest.json`. The manifest records the archive kind, producer version, each file path, length, and SHA-256. Archives include non-credential settings, profiles, history, temporary state, proofs, supported assets, plugins, and plugin private configuration. Security credentials, backups, staging directories, and crash reports are excluded.

Every settings or all-data import performs a read-only inspection first. After the user confirms, the application saves active state and creates a recovery ZIP in `data/backup`. A recovery snapshot failure aborts the import. Candidate data is validated in `data/.import-staging`, then committed with rollback support. Logs are restored into a timestamped imported directory rather than replacing the active log stream.

v2 ZIP archives are identified by `version.json`. Their settings, name-keyed lists, and histories are migrated before commit. Imported records receive stable `RecordId` values. History is linked only through unambiguous legacy IDs or names. Complete v2 history JSON is retained in `data/legacy/v2-history` with a migration manifest when a field or reference cannot be represented by current models.

Diagnostic exports offer two explicit scopes. The standard package contains all logs under `data/logs` after redaction plus non-identifying runtime metadata. The extended package additionally includes a redacted settings snapshot, plugin/profile counts, and redacted crash reports. They cannot be imported and exclude profile/history content, assets, plugin private configuration, security material, environment variables, paths, user data, network data, and process details.

## Cloud sync (account)

Cloud sync has two separate channels, and neither reuses the other's transport.

The account channel stores backups in the signed-in SECTL account's cloud space through the authenticated personal cloud API (`/api/cloud/*`). It is desktop-only because the OAuth session (`data/config/sectl-auth.json`) is desktop-only, and the mobile host hides the whole cloud section. The anonymous channel stays on SecRandom Sync (`secrandom-sync.sectl.cn`) with client-side AES-256-GCM envelopes, short expiry, and a 1 MiB payload limit; it is unchanged and is never used for cloud backups.

A cloud backup is a normal SecRandom v3 archive with the `CloudBackup` kind and a restricted root set: `config/settings.json`, `list`, `history`, `TEMP`, `proofs`, `audio/music`, `CSES`, `images`, `theme`, `themes`, and `Language`. It never contains plugins, the generated voice cache (`data/audio/voice`), runtime logs, the device identity (`config/device-uuid.json`), the account token file, security credentials, backups, or staging directories. A cloud restore additionally filters the device identity out of its commit roots, so a crafted archive cannot overwrite this device's UUID either.

Because the cloud function origin limits request bodies, one backup is split into verified parts (512 KiB of archive bytes each) plus a trailing manifest recording the archive length, the whole-archive SHA-256, and every part name/length/SHA-256. Parts are uploaded first and the manifest last, so an interrupted upload can never look complete; a failed upload cleans up its own parts, and a leftover part set appears in the backup list as an incomplete backup that can be deleted or purged. A restore downloads the manifest, verifies every part and the rebuilt archive, and only then runs the normal v3 inspection, recovery snapshot, staging commit, and restart request. Cloud payloads are stored as-is: the channel is authenticated per account and a cross-device restore must not depend on a key that exists on only one machine.
