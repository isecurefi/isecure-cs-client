#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
python3 scripts/check-contract.py
dotnet tool restore
dotnet nswag run nswag.json
