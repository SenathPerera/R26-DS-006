@echo off
setlocal
cd /d "%~dp0"
if not exist ".venv-relay\Scripts\python.exe" (
  echo Create the local environment first:
  echo py -3.12 -m venv .venv-relay
  echo .venv-relay\Scripts\python.exe -m pip install -r services\project_monitor\requirements.txt
  exit /b 1
)
echo MindSync project monitor: http://127.0.0.1:8090
echo Read-only. Backend services must be started separately.
".venv-relay\Scripts\python.exe" -m uvicorn services.project_monitor.app:app --host 127.0.0.1 --port 8090 --no-proxy-headers
