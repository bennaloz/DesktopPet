@echo off
rem Editor di revisione delle animazioni: apre il browser su http://127.0.0.1:8765/ (Ctrl+C per chiudere).
cd /d "%~dp0"
start "" http://127.0.0.1:8765/
python tools\review\serve.py 8765
