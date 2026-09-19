# Run from the repository root in bash or zsh. Copy to .private/environment.sh and edit.
# The RSA key below belongs to the ISECure TEST API. Production requires its own URL/key.
export ISECURE_BASE_URL='https://ws-api.test.isecure.fi/v2'
export ISECURE_PUBLIC_KEY_FILE='scripts/fixtures/test-public.pem'
export ISECURE_EMAIL='you@example.com'
export ISECURE_API_KEY='your-tenant-api-key'
export ISECURE_BANK='nordea'
export ISECURE_COMPANY='Your company'
export ISECURE_NAME='Your name'
export ISECURE_PHONE='+358401234567'
export ISECURE_MODE='data'
# Quickstart prompts for the password and any MFA code without echoing them.
# For unattended execution, supply ISECURE_PASSWORD and ISECURE_MFA_CODE externally.
