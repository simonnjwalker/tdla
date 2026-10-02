@echo off
setlocal DisableDelayedExpansion
for %%I in ("%~dp0..") do set "TDLA_PROJECT_ROOT=%%~fI"
set "TDLA_CONFIG_FILE=%TDLA_PROJECT_ROOT%\.local\tdla.private.json"
if not exist "%TDLA_CONFIG_FILE%" (
    echo Local configuration is missing. Follow docs\WINDOWS-PUBLISH.md to configure MySQL and private settings.
    exit /b 1
)
set "ASPNETCORE_ENVIRONMENT=Development"
set "Site__PublicUrl=http://localhost:5080"
dotnet run --project "%TDLA_PROJECT_ROOT%\src\Tdla.Web" --no-launch-profile -- --urls http://localhost:5080
exit /b %ERRORLEVEL%
