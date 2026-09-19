# Quickstart

This read-only console logs in with a data account, handles an offered SMS/TOTP step,
lists the number of visible certificates, and logs out. An empty certificate list is
successful: no bank connection or payment upload is needed for this first call.

Follow the [root TL;DR](../../README.md#tldr-make-your-first-api-call) to configure the
test URL, RSA public key and account settings. From the repository root:

```sh
dotnet run --project examples/Quickstart -c Release
```

Password and MFA prompts hide input. For an unattended run set `ISECURE_PASSWORD` and
`ISECURE_MFA_CODE` through your secret provider. Ctrl+C cancels prompts and requests.
Exit codes: 0 for a successful connection, 1 for API/transport failure, 2 for missing
configuration or unfinished authentication, and 130 for cancellation.

The [complete Program.cs](Program.cs) has no dependency on another example helper.
Copy it into a .NET 10 console application that references `ISECure.Client`, or adapt
it using the [authentication guide](../../docs/authentication.md).
