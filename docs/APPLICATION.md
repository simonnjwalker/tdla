# TDLA application

The home page is served as static bytes from the original `index.html`. Its only added line loads `js/navbar-session.js`, which requests `/api/session`. Signed-in users see Files and Sign Out; administrators additionally see Administration between them. Sign Out submits a POST with an anti-forgery token. The email address is not shown in the navbar. The rest of the application uses C# MVC views with the project's Bootstrap, Poppins font, coral (`#E84E4E`), dark (`#2B2A3D`) and grey styles.

## Accounts

- `/account/login`: username/password sign-in with Identity hashing, account lockout and rate limiting.
- `/account/register`: open to anyone by default, with the existing administrator enable/disable switch retained. Full name is required; firm/employer is optional so unemployed, retired and other individuals can register. Public sign-ups start unverified and receive no private-file roles until assigned by an administrator.
- `/account/registered`: post-signup confirmation explains that new website accounts cannot access files while signed in until an administrator verifies them; private-file access then depends on assigned roles.
- `/admin`: registration toggle, one/bulk account creation (up to 100 emails per request), role creation and role assignment. Invited accounts use the email address as the username and receive the Member role; administrators must verify them before signed-in file access is allowed.
- `/account/setup`: 24-hour password setup/reset tokens. Successful use invalidates the token and prior login cookies, and sends a password-change confirmation email. Administrators can also share setup links manually.
- `/account/forgot-username`: emails the stored username to the account's registered address.
- `/account/forgot-password`: emails a private confirmation/reset link; the password changes only when a valid token and new password are submitted. Both recovery forms return the same response for known and unknown email addresses.
- `/account/profile`: signed-in users can update full name and firm/employer. They cannot set their own verification or roles.
- The admin user list includes editable full name, firm/employer and **Verified**. Signed-in non-administrators must be verified before they can list or download any files, even if they have a role already. This is checked against the database on each request, so verification and revocation take effect immediately. Verification alone does not grant private-file roles.
- Built-in roles: **Member, Administrator, President, Vice-President, Treasurer, Secretary**. Committee titles do not imply administrator privileges; assign Administrator separately when needed.
- Initial administrator: `simonwalker0@gmail.com`. Its password is supplied through private bootstrap configuration, never a source-code constant. Blank-password login is not supported.
- Changing roles invalidates the affected user's session immediately. The initial admin cannot lose Administrator access through the interface, and administrators cannot remove their own Administrator role.

## Files

Administrators can list all managed files, upload, download, delete, change visibility and select download roles. Verified members see listed files they can access. Signed-out visitors can still see/download public files; unverified signed-in accounts instead see a verification notice and no files. A public file is downloadable anonymously even if hidden from lists; private access is enforced on every download. The Administrator role always has access.

Files have an optional plain-text description of up to 4,000 characters. Administrators can enter it when uploading and edit or clear it later using Save description, without changing permissions or uploaded bytes. Descriptions are shown only with files the viewer may list, are HTML-escaped, and preserve line breaks. The `FileDescription` column is added automatically with an empty default for existing files by the version 3 upgrade.

Uploaded bytes use generated storage filenames outside the web root. Downloads are attachments with `application/octet-stream`, `nosniff` and `no-store`. Original filenames are display/download metadata only. Uploaded HTML/SVG/scripts cannot execute as same-origin pages. Uploads are limited to 25 MB. This version does not include malware scanning or storage quotas.

Root `files/` assets are deliberately not exposed by the app or automatically imported. Upload any that should be managed through the administrator file page. Directory browsing and arbitrary repository static serving are not enabled.

## MySQL schema conventions

All application tables are singular and every column begins with its table name. Each table has a GUID primary key, stored as lowercase, 32-character `char(32)` without hyphens. Foreign GUID keys use the same representation.

| Table | Primary key | Example fields |
| --- | --- | --- |
| User | UserId | UserUserName, UserEmail, UserPasswordHash, UserFullName, UserEmployerName, UserVerified |
| Role | RoleId | RoleName, RoleNormalizedName |
| UserRole | UserRoleId | UserRoleUserId, UserRoleRoleId |
| UserClaim | UserClaimId | UserClaimUserId, UserClaimClaimType |
| RoleClaim | RoleClaimId | RoleClaimRoleId, RoleClaimClaimValue |
| UserLogin | UserLoginId | UserLoginUserId, UserLoginLoginProvider |
| UserToken | UserTokenId | UserTokenUserId, UserTokenName |
| File | FileId | FileName, FileDescription, FileSize, FileIsPublic, FileListed, FileRoles |
| Setting | SettingId | SettingRegistrationEnabled, SettingSchemaVersion |

Identity's default composite-key lookups are adapted in `AppUserStore` for GUID primary keys on join/token tables. Unique indexes preserve role membership and token/login uniqueness. GUIDs in URLs and generated forms use `.ToString("N")`, e.g. `/files/27c5f792c96547e9ad6eb2946adbe09b/download`. Identity cookies also receive normalized user ID claims.

## Deployment and persistence

See [the Windows build and publishing guide](WINDOWS-PUBLISH.md). Only selected static assets and the compiled application are published. MySQL, private configuration, uploads and data-protection keys live independently of deployments. SMTP settings are needed for the existing contact form, whose `/php/contact.php` action is now handled by C# without changing the HTML.

The additive v2 schema upgrade preserves existing records, adds profile/verification columns and a schema version, and enables registration once on upgrade. Later changes to the registration switch survive restarts. `Admin` is migrated to `Administrator`, preserving memberships and updating file role metadata. Existing administrator accounts are marked verified during this migration; other existing users start unverified. The MySQL account needs ALTER as well as its existing database permissions. Back up before upgrading.

Mail defaults match `php/info.rename-this-file.php`: `mail.tdla.com.au`, port 25, STARTTLS, username `admin@tdla.com.au`, sender `no-reply@tdla.com.au`, and the original sender/recipient display names. The template password is a placeholder and is not usable. The app reads literal constants from the live `php/info.php` automatically when it exists under the content root, or from `Smtp:LegacyConfigPath`. PHP is never executed. Private JSON values and environment variables override these imported settings. SMTP supports `tls` (STARTTLS), `ssl` (implicit TLS) or an empty security value for an explicitly configured unencrypted server. Certificates are validated. Recovery and contact mail use the same service.

The GitHub Pages workflow is replaced by `.github/workflows/publish.yml`; GitHub Pages cannot host the C# application. No live hosting configuration, GitHub secrets, remote repository or production database has been changed by preparing these files.
