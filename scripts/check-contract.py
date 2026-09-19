from pathlib import Path
import hashlib, json
root = Path(__file__).resolve().parent.parent
lock = json.loads((root / "contracts/source.json").read_text())
assert hashlib.sha256((root / "contracts/wsapi_v2.json").read_bytes()).hexdigest() == lock["sanitizedSha256"], "Contract digest mismatch"
print("Pinned OpenAPI contract verified")
