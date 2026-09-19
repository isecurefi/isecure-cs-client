# Run from the repository root. Copy to .private/environment.ps1 and edit.
# TEST URL and RSA public key must stay paired. Production has its own URL/key.
$env:ISECURE_BASE_URL = 'https://ws-api.test.isecure.fi/v2'
$env:ISECURE_PUBLIC_KEY_FILE = 'scripts/fixtures/test-public.pem'
$env:ISECURE_EMAIL = 'you@example.com'
$env:ISECURE_API_KEY = 'your-tenant-api-key'
$env:ISECURE_BANK = 'nordea'
$env:ISECURE_COMPANY = 'Your company'
$env:ISECURE_NAME = 'Your name'
$env:ISECURE_PHONE = '+358401234567'
$env:ISECURE_MODE = 'data'
# Quickstart prompts for password/MFA without echoing. Supply ISECURE_PASSWORD and
# ISECURE_MFA_CODE externally for unattended runs; do not store them in this template.
