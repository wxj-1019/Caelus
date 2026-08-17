@rem @author zenjiro 18967498922@163.com
@rem file: build Caelus (WPF main host), icon, and manifest
@rem ASCII ONLY. cmd decodes this file with the codepage the console had at
@rem startup (936 here) and chcp does NOT change that. One UTF-8 CJK char
@rem shifts the parser and comment text gets executed as a command.
@echo off
rem when called from dev.cmd it owns the codepage; do not restore it early
rem or the caller's remaining output lands on the wrong codepage.
if defined CAELUS_CP_OWNED goto cpready
for /f "tokens=2 delims=:" %%a in ('chcp') do set "CAELUS_OLDCP=%%a"
set "CAELUS_OLDCP=%CAELUS_OLDCP: =%"
chcp 65001 >nul
:cpready
setlocal
cd /d "%~dp0"
rem 32-bit MSBuild required: on 25H2 the WPF assemblies were native-AOT'd and
rem the 64-bit XAML compiler cannot load them (see Caelus.Wpf.csproj notes).
set MSB=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe
if not exist "%MSB%" (
    echo MSBuild.exe not found - install .NET Framework 4.x
    exit /b 1
)
set OUT=Caelus.exe
if not "%~1"=="" set OUT=%~1
set NAME=%~n1
if "%NAME%"=="" set NAME=Caelus
if /i "%~2"=="--selftest" goto selftest

rem [1/3] build iconless temp exe without the admin manifest so --genicon
rem can run without a UAC prompt, then generate Caelus.ico from IconArt.
"%MSB%" wpf\Caelus.Wpf.csproj /p:Configuration=Release /p:AssemblyName=Caelus.tmp /p:OutputPath=..\ /p:IntermediateOutputPath=obj\ReleaseMain\ /p:ApplicationManifest= /v:m /nologo
if errorlevel 1 goto err
echo [2/3] generating Caelus.ico...
.\Caelus.tmp.exe --genicon

rem [3/3] build final exe with icon + requireAdministrator manifest
"%MSB%" wpf\Caelus.Wpf.csproj /p:Configuration=Release /p:AssemblyName=%NAME% /p:OutputPath=..\ /p:IntermediateOutputPath=obj\ReleaseMain\ /v:m /nologo
if errorlevel 1 goto err
del Caelus.tmp.exe >nul 2>&1
echo.
echo Build OK -^> %OUT%
call :restorecp
exit /b 0

:selftest
rem selftest build: links tests\*.cs and the WinForms UI sources so the 225
rem self-tests keep running inside the WPF host (single pass, no icon needed)
"%MSB%" wpf\Caelus.Wpf.csproj /p:Configuration=Release /p:AssemblyName=%NAME% /p:OutputPath=..\ /p:IntermediateOutputPath=obj\ReleaseTest\ /p:DefineConstants=CAELUS_SELFTEST /p:CaelusSelfTest=true /v:m /nologo
if errorlevel 1 goto err
echo.
echo Build OK -^> %OUT%
call :restorecp
exit /b 0

:err
echo Build failed
call :restorecp
exit /b 1

:restorecp
if defined CAELUS_CP_OWNED goto :eof
if defined CAELUS_OLDCP chcp %CAELUS_OLDCP% >nul 2>&1
goto :eof
