@echo off
title Launching SelfishNet Modern...
:: Check for administrative rights
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Requesting administrative privileges...
    powershell -Command "Start-Process -Verb RunAs -FilePath '%~dp0bin\publish\SelfishNetModern.exe'"
    exit /b
)

:: Already elevated, start directly
start "" "%~dp0bin\publish\SelfishNetModern.exe"
exit /b
