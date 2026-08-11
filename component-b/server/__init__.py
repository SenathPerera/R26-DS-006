"""FastAPI backend package.

`src/` is put on the path here rather than in `main.py` so that any
submodule works when imported on its own — `server.engine` pulls in
`componentb` before `server.main` has necessarily run.
"""

import sys
from pathlib import Path

_SRC = str(Path(__file__).resolve().parents[1] / "src")
if _SRC not in sys.path:
    sys.path.insert(0, _SRC)
