# Deployment

## Local (demo / viva)

Run the backend on the laptop; all clients join over local WiFi.
No internet dependency — nothing to fail if venue WiFi is poor.

```bash
uvicorn server.main:app --host 0.0.0.0 --port 8000
```

Find the laptop IP:

```bash
# macOS / Linux
ipconfig getifaddr en0   ||  hostname -I
# Windows
ipconfig
```

Point clients at `ws://<that-ip>:8000/stream`.

## Required artifacts

Export these from the training notebook before the server will run:

```
artifacts/models/cnn_population.keras
artifacts/scalers/feature_scaler.pkl
artifacts/config/model_config.json
```

The scaler must be the exact object fitted during training.

## Before trusting live output

Run the parity test. If streaming and batch predictions disagree on
the same input, there is a bug — find it before hardware is involved.

```bash
pytest tests/test_parity.py -v
```
