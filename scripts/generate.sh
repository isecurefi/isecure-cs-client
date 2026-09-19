#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
python3 scripts/check-contract.py
dotnet tool restore
dotnet nswag run nswag.json
python3 - <<'PYCODE'
from pathlib import Path
p = Path("src/ISECure.Client/Generated/Models.g.cs")
p.write_text("\n".join(line.rstrip() for line in p.read_text().splitlines()) + "\n")
PYCODE
