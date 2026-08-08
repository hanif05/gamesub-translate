@echo off
REM T44: build framework-dependent Release output for the installer.
REM Output goes to installer\publish-output\ (T43's .iss expects it there).
REM Self-contained=false keeps the install < 50MB; user must have .NET 8 Desktop Runtime
REM (installer aborts with a download link if absent — see GameSubTranslate.iss).

setlocal

set "ROOT=%~dp0.."
set "OUT=%~dp0publish-output"

echo [publish] cleaning %OUT% ...
if exist "%OUT%" rmdir /s /q "%OUT%"

echo [publish] dotnet publish (Release, framework-dependent, R2R) ...
dotnet publish "%ROOT%\src\GameSubTranslate.App\GameSubTranslate.App.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -o "%OUT%" ^
  /p:PublishReadyToRun=true
if errorlevel 1 (
  echo [publish] dotnet publish failed.
  exit /b 1
)

if not exist "%OUT%\GameSubTranslate.App.exe" (
  echo [publish] expected output exe missing.
  exit /b 1
)

echo [publish] done. Run installer\build-installer.cmd next.
exit /b 0
