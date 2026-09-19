# Reproducible contract generation

`contracts/source.json` pins the upstream WS API 2.10.0 commit, original SHA-256,
sanitation policy and committed schema SHA-256. Documentation and example payloads
are excluded to avoid distributing upstream example credentials. Paths, parameter
locations, required fields, responses and definitions remain authoritative.

`dotnet-tools.json` pins NSwag 14.7.1. `bash scripts/generate.sh` verifies the local
schema digest, restores that exact tool and emits System.Text.Json DTOs. CI rejects
generated-file drift. Normal generation does not fetch a new API contract.

Only DTOs are generated: HTTP execution, authentication and session isolation belong
to the SDK's single transport/facade. This avoids generated transport bypasses.
Models for operations outside the preview do not imply a supported facade method.

To update the contract deliberately, fetch an exact upstream commit, apply the
recorded sanitation policy, review structural changes, update both digests and
regenerate. Do not change a contract hash merely to silence the verifier.

Build: `dotnet restore --locked-mode`, `dotnet build --no-restore -c Release`,
`dotnet test --no-build -c Release`. Network access is required only for pinned
NuGet tool/package restore; no credentials or AWS account are needed for unit CI.
