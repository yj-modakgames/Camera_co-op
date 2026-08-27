@echo off
cd /d "%~dp0"
where py >/dev/null 2>&1
if %errorlevel%==0 (set PYCMD=py) else (set PYCMD=python)
%PYCMD% --version >/dev/null 2>&1
if not %errorlevel%==0 goto nopython
echo [1/2] Creating virtual environment...
%PYCMD% -m venv .venv
if not exist ".venv\Scripts\python.exe" goto nopython
echo [2/2] Installing mediapipe + opencv (takes a few minutes)...
.venv\Scripts\python.exe -m pip install --upgrade pip
.venv\Scripts\python.exe -m pip install -r requirements.txt
if not %errorlevel%==0 goto failed
echo.
echo Setup complete. Now run: run_tracker.bat
pause
exit /b 0
:nopython
echo.
echo Python not found. Install it first: https://www.python.org/downloads/
echo Be sure to check "Add python.exe to PATH" during install.
pause
exit /b 1
:failed
echo.
echo Install failed. Check your internet connection and try again.
pause
exit /b 1
