"""Put `src/` on the import path for every pytest invocation.

Without this, `componentb` only imports when `server/__init__.py` happens
to have been imported first — which made `pytest tests/test_parity.py`
(the command README and docs/DEPLOYMENT.md both tell you to run) fail with
ModuleNotFoundError, while a full `pytest` run passed. Test outcome must
not depend on collection order.
"""

import sys
from pathlib import Path

SRC = Path(__file__).resolve().parent / "src"
if str(SRC) not in sys.path:
    sys.path.insert(0, str(SRC))
