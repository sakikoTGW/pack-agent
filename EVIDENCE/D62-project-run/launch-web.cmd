@echo off
chcp 65001 >nul
title ev-project ¡¤ web
set "PAD_INST=%~dp0"
set "PAD_ROOT=%PAD_INST%..\..\"
set "DSH_HOME=%PAD_INST%home"
set "NODE_OPTIONS="
set "NODE_PATH="
set "NODE_PRESERVE_SYMLINKS="
if exist "%DSH_HOME%\profiles\node_modules\" rmdir "%DSH_HOME%\profiles\node_modules" 2>nul
if exist "%DSH_HOME%\profiles\node_modules\" powershell -NoProfile -ExecutionPolicy Bypass -Command "function D([string]$p){ if(-not [IO.Directory]::Exists($p) -and -not [IO.File]::Exists($p)){ return }; $a=[IO.File]::GetAttributes($p); if(($a -band [IO.FileAttributes]::ReparsePoint) -ne 0){ if(($a -band [IO.FileAttributes]::Directory) -ne 0){ [IO.Directory]::Delete($p) } else { [IO.File]::Delete($p) }; return }; if(($a -band [IO.FileAttributes]::Directory) -ne 0){ foreach($c in [IO.Directory]::EnumerateFileSystemEntries($p)){ D $c }; [IO.Directory]::Delete($p) } else { [IO.File]::Delete($p) } }; D (Join-Path $env:DSH_HOME 'profiles\node_modules')"
set "DSH_TELEMETRY_DISABLED=1"
if exist "%PAD_ROOT%library\credentials.yaml" copy /y "%PAD_ROOT%library\credentials.yaml" "%DSH_HOME%\.credentials.yaml" >nul
@set "DEEPSEEK_API_KEY="
@if not exist "%DSH_HOME%\.credentials.yaml" goto :pad_cred_done
@setlocal EnableDelayedExpansion
@set "PAD_CRED_LINE="
@for /f "usebackq tokens=* delims=" %%L in (`findstr /i /b /c:"DEEPSEEK_API_KEY" "%DSH_HOME%\.credentials.yaml"`) do @set "PAD_CRED_LINE=%%L"
@if not defined PAD_CRED_LINE goto :pad_cred_endlocal
@for /f "tokens=1* delims=:" %%A in ("!PAD_CRED_LINE!") do @set "PAD_CRED_VAL=%%B"
@for /f "tokens=* delims= " %%T in ("!PAD_CRED_VAL!") do @set "PAD_CRED_VAL=%%T"
@set "PAD_CRED_VAL=!PAD_CRED_VAL:"=!"
@if /i "!PAD_CRED_VAL:~0,4!"=="REF:" @set "PAD_CRED_VAL=!PAD_CRED_VAL:~4!"
@for /f "tokens=* delims= " %%T in ("!PAD_CRED_VAL!") do @set "PAD_CRED_VAL=%%T"
@for /f "delims=" %%K in ("!PAD_CRED_VAL!") do (
  @endlocal
  @set "DEEPSEEK_API_KEY=%%K"
  @goto :pad_cred_done
)
:pad_cred_endlocal
@endlocal
:pad_cred_done
cd /d "%PAD_INST%workspace"
set "PAD_NODE=node"
if not exist "%PAD_NODE%" set "PAD_NODE=node"
set "PAD_BIN=%PAD_ROOT%versions\0.2.1-rc.2\node_modules\@deepseek-ai\dsh\lib\bin.js"
"%PAD_NODE%" "%PAD_BIN%" --profile web %*
set EXIT=%ERRORLEVEL%
if not "%EXIT%"=="0" (
  echo.
  echo [PAD] dsh exited with %EXIT%
  pause
)
