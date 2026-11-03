@echo off
setlocal
set "ROOT=%~dp0"
set "PROJECT=%ROOT%HoFaceStudio.slnx"
set "EXE=%ROOT%src\HoFace.Studio\bin\Release\net10.0-windows\HoFaceStudio.exe"
if not exist "%EXE%" (
  dotnet build "%PROJECT%" -c Release --nologo -v minimal
  if errorlevel 1 (
    echo HoFaceStudio build failed.
    pause
    exit /b 1
  )
)
start "HoFaceStudio" "%EXE%"
endlocal
