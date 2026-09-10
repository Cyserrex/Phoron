@echo off
REM Naikkan nomor versi Phoron di semua berkas sekaligus.
REM   set_version.bat 1.1.0
if "%~1"=="" (
    echo Pakai: set_version.bat ^<versi^>   contoh: set_version.bat 1.1.0
    exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0set_version.ps1" %1
exit /b %ERRORLEVEL%
