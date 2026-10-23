@echo off
rem Installs RPL Manager for this Windows user (no admin rights needed).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
pause
