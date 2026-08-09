@echo off
REM T43: build the Inno Setup installer.
REM Prereq: T44 has produced installer\publish-output\ (run publish.cmd first).
REM Prereq: Inno Setup 6 installed locally (free, https://jrsoftware.org/isinfo.php).

setlocal

set "ISCC="
if not "%ISCC%"=="" goto :run
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
if "%ISCC%"=="" (
  echo [build-installer] Inno Setup not found. Set ISCC env var to ISCC.exe path, or install Inno Setup 6.
  exit /b 1
)

:run
if not exist "publish-output\GameSubTranslate.App.exe" (
  echo [build-installer] publish-output\GameSubTranslate.App.exe missing. Run publish.cmd first.
  exit /b 1
)

REM Read version from the same file T44 uses. Trim CR/LF + whitespace so the define stays clean.
set "VERSION="
for /f "usebackq delims=" %%V in ("..\src\GameSubTranslate.App\version.txt") do (
  if not defined VERSION set "VERSION=%%V"
)
if not defined VERSION set "VERSION=1.0.0"
echo [build-installer] version=%VERSION%

echo [build-installer] compiling GameSubTranslate.iss ...
"%ISCC%" /dMyAppVersion="%VERSION%" GameSubTranslate.iss
if errorlevel 1 (
  echo [build-installer] ISCC failed.
  exit /b 1
)

if exist "Output\GameSubTranslate-Setup-*.exe" (
  echo [build-installer] done: installer\Output\GameSubTranslate-Setup-*.exe
  exit /b 0
)

echo [build-installer] done (no output exe matched; check Output\).
exit /b 0
