# TDLA
Townsville District Law Association website and C# membership portal.

## Description
A static Bootstrap homepage with ASP.NET Core 10 / MySQL account administration and protected file management.

## Project Details
The original homepage remains static, with AJAX account navigation: Files, Administration (for administrators) and Sign Out. New pages use the same Bootstrap assets and colour scheme. Anyone can register by default; administrators can review verification, full names and firms/employers, create accounts in bulk, assign roles and manage file access. Username reminders and password resets are emailed using the SMTP settings from the site's PHP mail configuration.

## Build and publish

Follow [the step-by-step Windows CLI and GitHub/Plesk guide](docs/WINDOWS-PUBLISH.md).

```bat
dotnet build src/Tdla.Web -c Release
dotnet publish src/Tdla.Web -c Release -r win-x64 --self-contained true -o artifacts/publish
```

Running requires a MySQL connection string and a persistent storage directory. Supply credentials through private configuration, never source control. See [application behaviour and database naming](docs/APPLICATION.md).

The contact form's existing URL is handled by C#; configure its recipient and SMTP credentials in the private server configuration described in the guide. Legacy PHP files are retained as source history and are not published.


