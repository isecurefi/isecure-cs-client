// Offline interoperability and command-line checks. No AWS credentials or ISECure account.
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import * as openpgp from 'openpgp';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'isecure-signing-'));
fs.chmodSync(temporary, 0o700);
const cleanEnv = Object.fromEntries(Object.entries(process.env).filter(([name]) => !name.startsWith('ISECURE_')));
const run = (project, args, env = {}) => spawnSync('dotnet', [path.join(root, `examples/${project}/bin/Release/net10.0/${project}.dll`), ...args],
  { cwd: root, encoding: 'utf8', env: { ...cleanEnv, ...env }, timeout: 30000 });
try {
  const passphrase = 'synthetic-ä-🔑-' + crypto.randomBytes(12).toString('hex');
  const generated = await openpgp.generateKey({ type: 'rsa', rsaBits: 2048, passphrase,
    userIDs: [{ name: 'Synthetic signing check', email: 'signing@example.invalid' }] });
  const publicKey = await openpgp.readKey({ armoredKey: generated.publicKey });
  const fingerprint = (await publicKey.getSigningKey()).getFingerprint();
  const bytes = Buffer.from([0, 255, 13, 10, 0xc3, 0xa4, 10, 13, 0, 127]);
  const input = path.join(temporary, 'binary.dat'), key = path.join(temporary, 'secret.asc'), signature = path.join(temporary, 'signature.asc');
  fs.writeFileSync(input, bytes, { mode: 0o600 });
  fs.writeFileSync(key, generated.privateKey, { mode: 0o600 });
  const env = { ISECURE_PGP_PASSPHRASE: passphrase, ISECURE_PGP_SIGNING_FINGERPRINT: fingerprint };
  const listed = run('SignFile', ['--list-keys', key]);
  assert.equal(listed.status, 0);
  assert.ok(listed.stdout.toLowerCase().split(/\s+/).includes(fingerprint));
  const signed = run('SignFile', [input, key, signature], env);
  assert.equal(signed.status, 0, signed.stderr);
  assert.ok(signed.stdout.includes(crypto.createHash('sha256').update(bytes).digest('hex')));
  const armoredSignature = fs.readFileSync(signature, 'utf8');
  const verify = async payload => {
    const result = await openpgp.verify({ message: await openpgp.createMessage({ binary: payload }),
      signature: await openpgp.readSignature({ armoredSignature }), verificationKeys: publicKey });
    assert.equal(result.signatures.length, 1);
    await result.signatures[0].verified;
  };
  await verify(bytes);
  const tampered = Buffer.from(bytes); tampered[0] ^= 1;
  await assert.rejects(() => verify(tampered));
  const overwrite = run('SignFile', [input, key, signature], env);
  assert.notEqual(overwrite.status, 0);
  assert.equal(fs.readFileSync(signature, 'utf8'), armoredSignature);
  const wrong = run('SignFile', [input, key, path.join(temporary, 'wrong.asc')], { ...env, ISECURE_PGP_PASSPHRASE: 'incorrect' });
  assert.notEqual(wrong.status, 0);
  assert.equal(fs.existsSync(path.join(temporary, 'wrong.asc')), false);
  for (const result of [signed, listed, overwrite, wrong]) {
    assert.ok(!result.stdout.includes(passphrase) && !result.stderr.includes(passphrase));
    assert.ok(!result.stdout.includes('PRIVATE KEY') && !result.stderr.includes('PRIVATE KEY'));
  }
  for (const project of ['Quickstart', 'FileExchange', 'SignFile']) assert.equal(run(project, ['--help']).status, 0);
  const missing = run('Quickstart', []);
  assert.equal(missing.status, 2);
  assert.ok(missing.stderr.includes('ISECURE_BASE_URL'));
  const missingUpload = run('FileExchange', [], { ISECURE_MODE: 'data' });
  assert.notEqual(missingUpload.status, 0);
  assert.ok(missingUpload.stderr.includes('ISECURE_UPLOAD_FILE'));
  const wrongMode = run('FileExchange', ['--register-key', key], { ISECURE_MODE: 'data' });
  assert.notEqual(wrongMode.status, 0);
  assert.ok(wrongMode.stderr.includes('ISECURE_MODE=admin'));
  const existing = run('FileExchange', [], { ISECURE_MODE: 'data', ISECURE_UPLOAD_FILE: input,
    ISECURE_SIGNATURE_FILE: signature, ISECURE_UPLOAD_TYPE: 'type', ISECURE_DOWNLOAD_TYPE: 'response', ISECURE_DOWNLOAD_FILE: input });
  assert.notEqual(existing.status, 0);
  assert.ok(existing.stderr.includes('new file in an existing directory'));
  assert.deepEqual(fs.readFileSync(input), bytes);
  console.log('PASS C# signature interoperability, binary bytes, UTF-8 passphrase, tamper rejection, overwrite protection, safe output and CLI onboarding failures');
} finally {
  fs.rmSync(temporary, { recursive: true, force: true });
}
