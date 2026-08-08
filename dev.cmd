@rem @author zenjiro 18967498922@163.com
@rem file: dev loop - kill running instance, rebuild, relaunch
@rem ASCII ONLY. cmd decodes this file with the codepage the console had at
@rem startup (936 here) and chcp does NOT change that. One UTF-8 CJK char
@rem shifts the parser and comment text gets executed as a command.
@echo off
for /f "tokens=2 delims=:" %%a in ('chcp') do set "CAELUS_OLDCP=%%a"
set "CAELUS_OLDCP=%CAELUS_OLDCP: =%"
chcp 65001 >nul
setlocal
set CAELUS_CP_OWNED=1
cd /d "%~dp0"

set OUT=Caelus.dev.exe
set MODE=%~1

rem kill the running instance first: the global single-instance mutex makes a
rem new one exit silently, and the locked exe blocks the build output.
rem prefer the global exit event (graceful restore chain, no elevation);
rem fall back to an elevated force-kill if it does not respond.
powershell -NoProfile -Command "try{[System.Threading.EventWaitHandle]::OpenExisting('Global\Caelus_Exit').Set()}catch{}" >nul 2>&1
set /a WAITED=0
:waitexit
tasklist /fi "imagename eq Caelus*" 2>nul | find /i "Caelus" >nul || goto killdone
set /a WAITED+=1
if %WAITED% geq 8 goto forcekill
ping -n 2 127.0.0.1 >nul
goto waitexit
:forcekill
echo Old instance ignored the exit signal, force-killing (next start runs crash recovery)
powershell -NoProfile -Command "Start-Process taskkill -ArgumentList '/f','/im','Caelus*' -Verb RunAs -Wait" >nul 2>&1
ping -n 2 127.0.0.1 >nul
:killdone

if /i "%MODE%"=="test" goto test

call "%~dp0build.cmd" %OUT%
if errorlevel 1 (
    echo Build failed, not launching
    call :restorecp
    exit /b 1
)
echo Launching %OUT% ...
start "" "%~dp0%OUT%"
call :restorecp
exit /b 0

:test
call "%~dp0build.cmd" Caelus.selftest.work.exe --selftest
if errorlevel 1 (
    echo Build failed, self-test skipped
    call :restorecp
    exit /b 1
)
set REPORT=%TEMP%\Caelus.selftest.txt
echo Running self-test...
start /wait "" "%~dp0Caelus.selftest.work.exe" --selftest "%REPORT%"
del "%~dp0Caelus.selftest.work.exe" >nul 2>&1
echo.
findstr /b /c:"FAIL" /c:"TOTAL" "%REPORT%"
echo Full report: %REPORT%
call :restorecp
exit /b 0

:restorecp
rem restore the host codepage: leaving the console on 65001 makes the
rem PSReadLine input thread of an interactive PowerShell host throw and die.
if defined CAELUS_OLDCP chcp %CAELUS_OLDCP% >nul 2>&1
goto :eof
