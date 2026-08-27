@echo off
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
  echo Run setup_tracker.bat first.
  pause
  exit /b 1
)
echo Starting hand tracker. Press q on the preview window to quit.
.venv\Scripts\python.exe hand_tracker.py
pause
