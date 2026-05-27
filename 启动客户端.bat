@echo off
chcp 65001 >nul
title NATConsole 客户端（全部隧道）
cd /d "%~dp0"
if not exist "NATConsole.exe" ( echo 找不到 NATConsole.exe & pause & exit /b 1 )
echo 读取 appsettings.json 中 Client.Tunnels 列表，一次启动全部隧道
echo.
NATConsole.exe client
pause
