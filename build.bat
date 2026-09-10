@echo off
rem Pembangun Phoron. Tanpa argumen = build Release ke dist\.
rem   build.bat          - build Release, salin Phoron.exe ke dist\
rem   build.bat debug    - build Debug
rem   build.bat test     - build lalu jalankan harness uji
rem   build.bat live     - uji ujung-ke-ujung (menyalakan Apache sungguhan)
rem   build.bat run      - build Debug lalu jalankan aplikasinya
rem   build.bat clean    - buang bin/obj/dist
setlocal
set ROOT=%~dp0
set ROOT=%ROOT:~0,-1%
cd /d "%ROOT%"

rem Ikon TIDAK disimpan di repo - ia dirakit dari assets\phoron-logo.png. Pada
rem klon bersih berkasnya belum ada, sedangkan ApplicationIcon di csproj
rem membutuhkannya, jadi dirakit di sini sebelum apa pun dibangun.
if /i not "%1"=="clean" (
    if not exist "assets\phoron.ico" (
        if exist "assets\phoron-logo.png" (
            echo == Merakit ikon dari logo ==
            powershell -NoProfile -ExecutionPolicy Bypass -File "assets\make_icon.ps1" || goto :err
        )
    )
)

if /i "%1"=="clean" goto :clean
if /i "%1"=="test" goto :test
if /i "%1"=="live" goto :live
if /i "%1"=="run" goto :run
if /i "%1"=="debug" goto :debug

echo == Build Release ==
dotnet build "src\Phoron.App\Phoron.App.csproj" -c Release --nologo || goto :err
if not exist "dist" mkdir "dist"
copy /y "src\Phoron.App\bin\Release\net48\Phoron.exe" "dist\Phoron.exe" >nul || goto :err
rem Berkas .config (174 byte) menyatakan runtime yang dibutuhkan. Tanpa itu, di
rem komputer yang hanya punya .NET Framework 4.0-4.7 aplikasinya tetap mulai
rem lalu gagal di tengah dengan galat yang tidak menjelaskan apa pun.
copy /y "src\Phoron.App\bin\Release\net48\Phoron.exe.config" "dist\Phoron.exe.config" >nul || goto :err
echo.
echo Selesai: %ROOT%\dist\Phoron.exe
for %%F in ("dist\Phoron.exe") do echo Ukuran: %%~zF bytes
goto :done

:debug
dotnet build "src\Phoron.App\Phoron.App.csproj" -c Debug --nologo || goto :err
goto :done

:test
dotnet build "src\Phoron.Tests\Phoron.Tests.csproj" -c Debug --nologo || goto :err
"src\Phoron.Tests\bin\Debug\net48\Phoron.Tests.exe" || goto :err
goto :done

:live
dotnet build "src\Phoron.Tests\Phoron.Tests.csproj" -c Debug --nologo || goto :err
"src\Phoron.Tests\bin\Debug\net48\Phoron.Tests.exe" live || goto :err
goto :done

:run
dotnet build "src\Phoron.App\Phoron.App.csproj" -c Debug --nologo || goto :err
start "" "src\Phoron.App\bin\Debug\net48\Phoron.exe"
goto :done

:clean
for /d %%D in (src\*) do (
    if exist "%%D\bin" rd /s /q "%%D\bin"
    if exist "%%D\obj" rd /s /q "%%D\obj"
)
if exist dist rd /s /q dist
echo Bersih.
goto :done

:err
echo.
echo GAGAL.
if not "%PHORON_NOPAUSE%"=="1" pause
exit /b 1

:done
if not "%PHORON_NOPAUSE%"=="1" pause
exit /b 0
