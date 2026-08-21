@rem @author zenjiro 18967498922@163.com
@rem file: build release package (portable ZIP + optional Inno Setup installer)
@rem ASCII ONLY. cmd decodes this file with the codepage the console had at
@rem startup (936 here) and chcp does NOT change that.
@echo off
setlocal
cd /d "%~dp0\.."

echo ========================================
echo   Caelus Release Packager
echo ========================================
echo.

rem [1/4] clean old build
echo [1/4] cleaning old build...
del /q Caelus.exe 2>nul
del /q Caelus.tmp.exe 2>nul
del /q Caelus.ico 2>nul
if exist release rmdir /s /q release
mkdir release

rem [2/4] build release exe
echo [2/4] building Caelus.exe...
call build.cmd
if errorlevel 1 (
    echo Build failed!
    exit /b 1
)
echo.

rem [3/4] create portable package
echo [3/4] creating portable package...
mkdir "release\Caelus-1.9.0-portable"
copy /y Caelus.exe "release\Caelus-1.9.0-portable\" >nul
copy /y Caelus.ico "release\Caelus-1.9.0-portable\" >nul
copy /y LICENSE "release\Caelus-1.9.0-portable\" >nul
copy /y README.md "release\Caelus-1.9.0-portable\" >nul
echo portable > "release\Caelus-1.9.0-portable\Caelus.portable"

rem create portable zip using PowerShell
echo [3/4] packaging portable ZIP...
powershell -NoProfile -Command "Compress-Archive -Path 'release\Caelus-1.9.0-portable\*' -DestinationPath 'release\Caelus-1.9.0-portable.zip' -Force"
if errorlevel 1 (
    echo Warning: PowerShell Compress-Archive failed, portable folder is still available
) else (
    echo Portable ZIP created: release\Caelus-1.9.0-portable.zip
)
echo.

rem [4/4] try to build installer
echo [4/4] checking for Inno Setup...
set ISCC=
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
if defined ISCC (
    echo Found Inno Setup: %ISCC%
    echo Building installer...
    "%ISCC%" installer\Caelus.iss
    if errorlevel 1 (
        echo Installer build failed!
    ) else (
        echo Installer created: release\Caelus-1.9.0-Setup.exe
    )
) else (
    echo Inno Setup not found. Skipping installer build.
    echo To build installer: install Inno Setup 6 from https://jrsoftware.org/isinfo.php
    echo Then run: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\Caelus.iss
)

echo.
echo ========================================
echo   Release packaging complete!
echo ========================================
echo.
echo Output files in release\:
dir /b release\ 2>nul
echo.
endlocal
