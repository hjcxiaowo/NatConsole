@echo off
chcp 65001 >nul
title NATConsole 服务端
cd /d "%~dp0"
if not exist "NATConsole.exe" ( echo 找不到 NATConsole.exe & pause & exit /b 1 )
echo Linux/Docker 部署一般用 server；Windows 本地调试可用此脚本
echo.
NATConsole.exe server
pause
