# Build and publish TDLA from Windows Command Prompt

This guide applies only to this repository and `tdla.com.au`. The application uses C# / ASP.NET Core 10 and MySQL. `index.html` is a static file; the only addition is a script that fetches the navbar account state.

## 1. Confirm the Plesk prerequisites

Sign in to the supplied [Plesk control panel](https://sknet311.ozpanelhosting.com:8443/smb/web/view), then select **tdla.com.au**.

Your settings indicate Windows hosting at `27.50.90.34`, with HTTPS and a Let's Encrypt certificate. The displayed **ASP.NET 4.8.0** setting refers to classic .NET Framework. It does not establish whether ASP.NET Core applications can run.

Ask the hosting provider to confirm:

1. ASP.NET Core Module V2 / the .NET 10 Hosting Bundle is installed and supports a **64-bit, self-contained ASP.NET Core 10** app. Self-contained deployment includes the runtime but still needs the IIS hosting module.
2. The site's IIS application pool permits 64-bit processes.
3. **Web Deploy publishing** is enabled for this subscription. Download the publishing settings from Plesk. Record its Web Deploy endpoint, IIS application/site name, publishing username and password. The port-8443 control-panel URL is **not** a publishing endpoint; use the exact endpoint provided by the host, often HTTPS port 8172 with `/msdeploy.axd?site=...`.
4. A writable private directory exists outside the document root, and the site's application pool identity can modify it. Ask for the real absolute path; do not assume the example below matches the server.
5. The MySQL host, port, database name, username, password and TLS requirements are available. Use MySQL 8.0+; development and CI exercise MySQL 8.4. For a first installation, have Plesk create a **new, empty database** and grant the application user CREATE, ALTER and normal CRUD permissions within it. Existing TDLA application databases can be upgraded without recreating their data.

If ASP.NET Core or Web Deploy is unavailable, the supplied automated deployment cannot be activated until the provider enables it. Do not select .NET Framework 4.8 as a substitute for .NET 10.

## 2. Install local tools

Open **Command Prompt (`cmd.exe`)**. All commands in this guide use Command Prompt syntax. Use `cmd /v:off` to keep delayed variable expansion disabled when entering passwords containing `!`.

```bat
winget install --id Microsoft.DotNet.SDK.10 --exact
winget install --id Git.Git --exact
winget install --id GitHub.cli --exact
winget install --id Python.Python.3.13 --exact
```

Close and reopen the terminal, then check:

```bat
dotnet --info
git --version
gh --version
python --version
cd /d C:\snjw\code\tdla
dotnet restore src/Tdla.Web
dotnet build src/Tdla.Web -c Release
```

Use the latest patched .NET 10 SDK. GitHub Actions automatically installs the current `10.0.x` SDK. Windows development does not require Visual Studio.

## 3. Run locally with MySQL

In this prepared checkout, a project-specific local MySQL container (`tdla-project-mysql`) and ignored `.local/tdla.private.json` have already been configured, and the requested administrator login has been verified. Start that database if it is stopped, then run:

```bat
docker start tdla-project-mysql
scripts\start-local.cmd
```

Open `http://localhost:5080`. For a fresh checkout on another machine, use the setup instructions below instead; the private config and local database are not in GitHub.

Use a dedicated local MySQL database. If Docker Desktop is installed and running, create one:

```bat
docker run --name tdla-dev-mysql -e MYSQL_RANDOM_ROOT_PASSWORD=yes -e MYSQL_DATABASE=tdla -e MYSQL_USER=tdla -e MYSQL_PASSWORD=tdla-local-dev-only -p 127.0.0.1:33316:3306 -d mysql:8.4
```

Wait for MySQL to finish initializing. If this port is occupied, choose another and change the connection string below. These are local development credentials only.

```bat
set "ASPNETCORE_ENVIRONMENT=Development"
set "ConnectionStrings__Default=Server=127.0.0.1;Port=33316;Database=tdla;User=tdla;Password=tdla-local-dev-only;SslMode=Required"
set "Storage__Path=%CD%\.local\data"
set "Site__PublicUrl=http://localhost:5080"
set /p "Bootstrap__AdminPassword=Enter the chosen administrator password: "
dotnet run --project src/Tdla.Web -- --urls http://localhost:5080
```

The `set /p` prompt displays what you type. Open `http://localhost:5080`. Sign in at `/account/login` with **simonwalker0@gmail.com** and the administrator password you entered. For this project, enter the password you specified in the chat; it is intentionally absent from this guide and source control.

The admin account is created once. `Bootstrap__AdminPassword` sets a password only if it has none, so subsequent deployments cannot accidentally overwrite an existing password. If the setting is absent, the admin account has no password and cannot sign in until a setup link or password is explicitly issued.

After stopping the server with Ctrl+C:

```bat
set "Bootstrap__AdminPassword="
```

For an explicit administrator password change, enter the password at the prompt, then run:

```bat
set /p "Bootstrap__AdminPassword=Enter the new administrator password: "
dotnet run --project src/Tdla.Web -- --set-admin-password
set "Bootstrap__AdminPassword="
```

Alternatively, `dotnet run --project src/Tdla.Web -- --setup-admin` prints a private 24-hour password setup/reset link using `Site__PublicUrl`. Run it only against the intended database and persistent key directory. Never put its output in public logs.

## 4. Test and produce a publishing folder

Integration tests create accounts, change the registration setting and upload/delete test files. Run them against a **separate disposable database**, never the live database. Set `ConnectionStrings__Default` to that database first. The initial test administrator password is generated from `TDLA_TEST_PASSWORD`, or uses the test-only default in the script.

```bat
dotnet build src/Tdla.Web -c Release
python scripts/integration-test.py
dotnet publish src/Tdla.Web -c Release -r win-x64 --self-contained true -o artifacts/publish
```

Publish the **contents of `artifacts/publish`** to the Plesk document root. It contains the C# application, generated IIS `web.config`, compiled views and selected static assets. It excludes PHP source, the Git repository, the original `files` directory and private data.

Existing files in the repository's `files` directory can be uploaded through **Files → Upload** after signing in. Choose their visibility and download roles explicitly. They are not copied into a public static folder. Existing server files are preserved by the deployment script: before first deployment, move any sensitive legacy files out of public directories using Plesk File Manager.

## 5. Configure private production settings in Plesk

1. In Plesk File Manager, create the provider-approved directory outside the document root, such as `private`. Create `tdla-data` within it.
2. Copy the structure from `deploy/tdla.private.example.json` into a file named `tdla.private.json` in that private directory. Enter the real MySQL connection string, actual storage path, `https://tdla.com.au`, and your chosen administrator password under `Bootstrap:AdminPassword`.
3. Set `AllowedHosts` to `tdla.com.au;www.tdla.com.au` in that private JSON file if both hostnames are forwarded to the app.
4. Configure mail for account recovery and the contact form. The defaults are copied from `php/info.rename-this-file.php`: host `mail.tdla.com.au`, port `25`, security `tls`, username `admin@tdla.com.au`, sender `no-reply@tdla.com.au`, recipient `secretary.tdla@gmail.com`, and the template's display names. Set `Smtp:LegacyConfigPath` to the **actual live `info.php` path** to import its real password and other constants. The example private JSON shows this option. The app also detects `php/info.php` directly under its content root automatically. Imported PHP is read as configuration text, never executed or copied into public assets. Alternatively omit `LegacyConfigPath` and set `Smtp:Password` privately. Private `Smtp` values and environment variables override imports. Do not use the template's `PASSWORD_GOES_HERE` value.
5. Give the site's application pool identity read permission on the private config and modify permission on `tdla-data`. Restrict access to that private directory to the site identity and administrators. The data-protection key files secure login cookies and setup tokens.
6. Record the exact full path to the JSON file, for example `C:\Inetpub\vhosts\tdla.com.au\private\tdla.private.json`.

JSON uses doubled backslashes in Windows paths. Never commit the populated private file or publishing credentials. The example has placeholders only. On first successful startup the app initializes its tables in the empty database. After the administrator can sign in, remove `Bootstrap:AdminPassword` from the private file; the stored password hash remains.

## 6. Push this project to GitHub

From the project directory:

```bat
gh auth login
git status
git remote -v
```

Review this project's existing `origin`. If it is already the intended repository, keep it. Otherwise create a **new empty private repository** in GitHub for this project, copy its URL, and use one of these commands as appropriate:

```bat
REM Use this only if origin does not exist:
git remote add origin https://github.com/YOUR_ACCOUNT/YOUR_NEW_REPOSITORY.git

REM Or use this if origin exists and needs to point to the new repository:
git remote set-url origin https://github.com/YOUR_ACCOUNT/YOUR_NEW_REPOSITORY.git
```

Then review and commit:

```bat
git add .
git diff --cached --stat
git diff --cached
git commit -m "Add ASP.NET Core membership and file management"
git branch -M main
git push -u origin main
```

Your pre-existing homepage and image edits are still present and will be included by `git add .`; review them before committing. No commit or remote push is performed automatically by the app.

## 7. Configure the GitHub publishing workflow

In GitHub, create an environment named **production** under repository **Settings → Environments**. Add these environment secrets (the values come from Plesk's publishing settings):

| Secret | Value |
| --- | --- |
| `PLESK_SERVER` | Exact HTTPS Web Deploy endpoint, including any site query string |
| `PLESK_SITE` | Exact IIS site/application name from the publishing settings |
| `PLESK_USERNAME` | Web Deploy publishing username |
| `PLESK_PASSWORD` | Web Deploy publishing password |

Add these environment variables:

| Variable | Value |
| --- | --- |
| `TDLA_CONFIG_FILE` | Full server path to the private JSON config file |
| `PLESK_DEPLOY_ENABLED` | `true` once the host and private configuration are ready |

You can enter secrets from Command Prompt without embedding them in commands:

```bat
gh secret set PLESK_SERVER --env production
gh secret set PLESK_SITE --env production
gh secret set PLESK_USERNAME --env production
gh secret set PLESK_PASSWORD --env production
gh variable set TDLA_CONFIG_FILE --env production --body "C:\Inetpub\vhosts\tdla.com.au\private\tdla.private.json"
gh variable set PLESK_DEPLOY_ENABLED --env production --body "true"
gh workflow run publish.yml --ref main
gh run list --workflow publish.yml
gh run watch
```

Replace the example private path with the actual path supplied by the host. The script validates HTTPS certificates; the publishing endpoint must have a trusted certificate. Provider values containing apostrophes or line breaks are rejected explicitly by this script; use compatible publishing credentials.

Pushes to `main` now build and test against a disposable MySQL instance, publish a self-contained Windows artifact, and deploy using Web Deploy. Pull requests run tests but do not deploy. If `PLESK_DEPLOY_ENABLED` is absent, builds and artifacts still work, but deployment is skipped.

The workflow temporarily takes the application offline during file synchronization, preserves existing server files, and then checks `https://tdla.com.au/health`. It does not publish the private config or storage directory.

## 8. Check the live site and maintain it

1. Open the homepage and confirm its existing layout. Check that **Sign in** appears in the navbar.
2. Sign in as the administrator. Check the homepage navbar shows **Files → Administration → Sign Out**, and that Sign Out ends the session. Other users see Files and Sign Out.
3. Create one account and a bulk batch from `/admin`; share each private setup link with the intended person. Links are displayed for manual delivery; automated invitation emails are not implemented.
4. Upload a private file with a description, edit that description, assign its roles, and test access from a separate signed-out browser. A signed-in non-administrator must also be marked Verified to access files; roles alone are not enough.
5. Test public/listed and public/hidden files. Hidden controls listing only; public hidden files are still downloadable by URL.
6. Test Forgot username, Forgot password and the password-change confirmation email after SMTP is configured. Submit a contact-form test as well. Recovery uses the registered account email address and does not disclose account existence on the website.
7. Back up the MySQL database, private configuration and the whole `tdla-data` directory together. The latter holds uploaded bytes and persistent security keys.

For later changes:

```bat
dotnet build src/Tdla.Web -c Release
git add .
git diff --cached
git commit -m "Describe the change"
git push
gh run watch
```

The app uses EF's `EnsureCreated` for an empty initial database and additive, versioned upgrades. Version 2 adds the profile/verification fields, renames Admin to Administrator and opens registration once. Version 3 adds `FileDescription`, defaulting to empty for existing files, without changing registration settings. Existing accounts and uploaded files are preserved. Back up first and ensure the database user has ALTER permission. Run a single application instance during initial schema creation. A rollback requires redeploying the previous artifact and, if schema/data changed, restoring a matching backup.

If deployment fails, inspect Actions logs first. Typical causes are an incorrect publishing endpoint/site name, Web Deploy not enabled, an untrusted endpoint certificate, missing ASP.NET Core Module, an incorrect private-config path, MySQL connection/TLS failures, or missing directory permissions. Do not work around certificate failures by disabling validation.

## References

- [Microsoft .NET releases](https://dotnet.microsoft.com/en-us/download/dotnet)
- [ASP.NET Core hosting on IIS](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
- [Plesk ASP.NET Core support](https://support.plesk.com/hc/en-us/articles/12377600431511-ASP-NET-Core-support-in-Plesk)
- [Plesk Web Deploy setup](https://support.plesk.com/hc/en-us/articles/12377590962071-How-to-configure-Visual-Studio-on-remote-computer-to-perform-Web-Deploy-to-Plesk-for-Windows-server)
- [MySQL Entity Framework provider](https://www.nuget.org/packages/MySql.EntityFrameworkCore/10.0.9)
