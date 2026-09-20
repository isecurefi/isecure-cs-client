# Changelog

## 0.1.0-preview.2 — 2026-09-20

- Add `EnrollCertificateAsync(company, wsUserId, code, cancellationToken)` for the
  configured bank, using the existing authenticated transport and typed errors.
- Add the `FileExchange --enroll-certificate` admin example and compiled recipes for
  bank enrollment and the separately enabled test simulator.
- Preserve exact enrollment inputs, tenant isolation, cancellation, credential-safe
  diagnostics and the no-automatic-write-retry policy.
- Extend live qualification to exercise C# enrollment and deny it when simulator
  access is missing or suspended.

This is a source/local-package preview. NuGet publication is deferred.

## 0.1.0-preview.1 — 2026-09-19

- Initial Experimental .NET 10 SDK: registration, SMS/TOTP authentication and verification,
  logout, certificate discovery, PGP public-key registration and signed exact-byte file exchange.
