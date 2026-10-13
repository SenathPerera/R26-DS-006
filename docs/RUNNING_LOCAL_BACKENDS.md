# Running the Local Backends

Run all commands from the repository root in PowerShell.

## Prerequisites

- Start the Ollama application and ensure `qwen2.5:3b` is installed.
- Ensure each backend's existing Python virtual environment has its dependencies installed.
- Close any backend instances that were started manually before using the launcher for the first time.

## Start

```powershell
.\demo-backends.cmd start
```

The launcher starts and health-checks:

| Service | Port |
|---|---:|
| Component B | 8000 |
| Session relay | 8080 |
| Lyria backend | 8002 |
| Component D | 8010 |
| Ollama (external prerequisite) | 11434 |

It also warms Component D's voice scorer and speech-to-text model. Before a demo, confirm the output reports:

```text
[ready] Component D warmup: scorer=True, stt=True
```

To start without repeating the model warmup:

```powershell
.\demo-backends.cmd start -SkipWarmup
```

## Check status

```powershell
.\demo-backends.cmd status
```

## Stop

```powershell
.\demo-backends.cmd stop
```

The stop command only stops processes previously launched by this script. Runtime state and service logs are stored under `.demo-runtime/`, which is excluded from Git.

For the combined backend, Metro, and Android-device workflow, use:

```powershell
.\demo-system.cmd start
```
