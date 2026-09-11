@echo off
REM ============================================================
REM  Phoron - buat installer (Phoron-<versi>-Setup.exe)
REM
REM  Syarat:
REM      - .NET SDK 8 (build menargetkan .NET Framework 4.8)
REM      - Inno Setup 6  (winget install JRSoftware.InnoSetup)
REM
REM  Set PHORON_NOPAUSE=1 untuk jalan tanpa "tekan sembarang tombol" (CI).
REM ============================================================
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "VERSION=1.4.1"
set "SETUP=installer\Output\Phoron-%VERSION%-Setup.exe"

rem Ikon dirakit ulang dari logo lebih dulu: installer dan exe harus memakai
rem ikon yang sama dengan berkas sumbernya, bukan sisa rakitan lama - dan pada
rem klon bersih berkasnya memang belum ada sama sekali.
if exist "assets\phoron-logo.png" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "assets\make_icon.ps1" >nul || goto :err
)

echo.
echo === [1/3] Membangun Phoron.exe ===
REM build.bat dipaksa tidak jeda supaya rantai build tidak berhenti di tengah
REM menunggu tombol; setelan pemanggil dikembalikan setelahnya agar skrip ini
REM sendiri tetap berhenti di akhir kalau diklik dua kali.
set "OUTER_NOPAUSE=%PHORON_NOPAUSE%"
set "PHORON_NOPAUSE=1"
REM Jalur lengkap, bukan nama polos: kalau NoDefaultCurrentDirectoryInExePath
REM aktif (lazim di PC yang dikeraskan kebijakan grup), cmd tidak lagi mencari
REM batch di folder kerja dan panggilan ini gagal "is not recognized".
call "%~dp0build.bat"
set "BUILD_RC=%ERRORLEVEL%"
set "PHORON_NOPAUSE=%OUTER_NOPAUSE%"
if not "%BUILD_RC%"=="0" goto :err

echo.
echo === [2/3] Mencari Inno Setup ===
set "ISCC="
for %%P in (
    "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
    "%ProgramFiles%\Inno Setup 6\ISCC.exe"
    "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
) do if not defined ISCC if exist %%P set "ISCC=%%~P"
if not defined ISCC (
    echo Inno Setup 6 tidak ditemukan.
    echo Pasang dengan:  winget install JRSoftware.InnoSetup
    goto :err
)
echo Ditemukan: %ISCC%

echo.
echo === [3/3] Merakit installer ===
"%ISCC%" /Q "installer\setup.iss" || goto :err
if not exist "%SETUP%" (
    echo Installer tidak terbentuk di %SETUP%.
    goto :err
)

echo.
echo Selesai: %~dp0%SETUP%
for %%F in ("%SETUP%") do echo Ukuran: %%~zF bytes
if not "%PHORON_NOPAUSE%"=="1" pause
exit /b 0

:err
echo.
echo GAGAL.
if not "%PHORON_NOPAUSE%"=="1" pause
exit /b 1
