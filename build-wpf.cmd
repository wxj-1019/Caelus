@rem @author zenjiro 18967498922@163.com
@rem file: build the WPF preview host (CaelusWpf.exe)
@rem ASCII ONLY - see build.cmd header for the codepage trap. One UTF-8 CJK
@rem char in a .cmd shifts the parser and comment text gets run as a command.
@echo off
setlocal
cd /d "%~dp0"
set MSB=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe
if not exist "%MSB%" (
    echo MSBuild.exe not found - install .NET Framework 4.x
    exit /b 1
)
"%MSB%" wpf\Caelus.Wpf.csproj /p:Configuration=Release /v:m /nologo
if errorlevel 1 (
    echo WPF build failed
    exit /b 1
)
echo.
echo WPF Build OK -^> wpf\bin\Release\CaelusWpf.exe
