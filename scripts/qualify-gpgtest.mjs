import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';
import { spawn, spawnSync } from 'node:child_process';
import { STS } from '@aws-sdk/client-sts';
import { DynamoDB } from '@aws-sdk/client-dynamodb';
import { CognitoIdentityProvider } from '@aws-sdk/client-cognito-identity-provider';
import { APIGateway } from '@aws-sdk/client-api-gateway';
import { Lambda } from '@aws-sdk/client-lambda';
import * as openpgp from 'openpgp';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const wsapi = path.resolve(process.env.ISECURE_WSAPI_ROOT || path.join(root, '../aws/ws-channel-api'));
const account = '589434896614', region = 'eu-west-1', pool = 'eu-west-1_QtCfMyN6J';
const apiId = '5v82uu7087', stage = 'gpgtest', base = 'https://ws-api.test.isecure.fi/v2';
const table = 'isecure-tenant-entitlements', product = 'bank-simulator';
const publicKey = fs.readFileSync(path.join(root, 'scripts/fixtures/test-public.pem'), 'utf8');
const options = { region, maxAttempts: 1, requestHandler: { connectionTimeout: 5000, requestTimeout: 30000 } };
const sts = new STS(options), ddb = new DynamoDB(options), cognito = new CognitoIdentityProvider(options), gateway = new APIGateway(options), lambda = new Lambda(options);
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
function requireThat(condition, label) { if (!condition) throw new Error(label); }
function save(file, data) { fs.writeFileSync(file, JSON.stringify(data, null, 2) + '\n', { mode: 0o600 }); fs.chmodSync(file, 0o600); }
const teardownArg = process.argv.slice(2).find(x => x.startsWith('--teardown='));
requireThat(process.argv.slice(2).every(x => x === teardownArg), 'UNKNOWN_ARGUMENT');
const retained = teardownArg ? JSON.parse(fs.readFileSync(path.resolve(teardownArg.slice('--teardown='.length)), 'utf8')) : undefined;
const runId = retained?.runId || 'cs-' + crypto.randomBytes(8).toString('hex');
requireThat(/^cs-[a-f0-9]{16}$/.test(runId), 'INVALID_CHECKPOINT_RUN_ID');
const privateRoot = path.join(root, '.private', runId);
fs.mkdirSync(privateRoot, { recursive: true, mode: 0o700 });
const checkpointFile = path.join(privateRoot, 'checkpoint.json');
if (retained) requireThat(path.resolve(teardownArg.slice('--teardown='.length)) === checkpointFile &&
  retained.tenants?.length === 2 && retained.tenants.every((t, i) => t.label === ['primary', 'secondary'][i] &&
    t.email === `${runId}-${t.label}@example.invalid` && /^[A-Za-z0-9_-]{40}$/.test(t.apiKey)), 'INVALID_CHECKPOINT');
const sourceFiles = spawnSync('git', ['ls-files', '-co', '--exclude-standard'], { cwd: root, encoding: 'utf8' });
requireThat(sourceFiles.status === 0, 'SOURCE_MANIFEST_FAILED');
const sourceHash = crypto.createHash('sha256');
for (const name of [...new Set(sourceFiles.stdout.trim().split('\n'))].sort()) {
  if (!/^(src\/|examples\/.*\.(cs|csproj|json)$|scripts\/|contracts\/|global\.json$|Directory\.Build\.props$|package(-lock)?\.json$)/.test(name)) continue;
  sourceHash.update(name + '\0'); sourceHash.update(fs.readFileSync(path.join(root, name))); sourceHash.update('\0');
}
const evidence = { schemaVersion: 1, runId, startedAt: new Date().toISOString(), account, region, stage, baseUrl: base,
  runtimeSourceSha256: sourceHash.digest('hex'), teardownOnly: !!retained,
  sourceRevision: spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).stdout.trim(), checks: [], files: [], teardown: [] };
const evidenceName = runId + (retained ? '-teardown-' + Date.now() : '') + '.json';
const fixture = retained || { runId, password: 'Aa1!' + crypto.randomBytes(24).toString('base64url'), tenants: ['primary', 'secondary'].map(label => ({
  label, email: `${runId}-${label}@example.invalid`, apiKey: crypto.randomBytes(30).toString('base64url'),
  company: `ISECure CSharp ${runId} ${label}`, name: 'Synthetic CSharp', phone: '+358400100001'
})) };
save(checkpointFile, fixture);
function pass(label) { evidence.checks.push({ label, at: new Date().toISOString() }); console.log('PASS ' + label); }

async function entitlement(tenant) {
  return (await ddb.getItem({ TableName: table, Key: { api_key: { S: tenant.apiKey }, product_id: { S: product } }, ConsistentRead: true })).Item;
}
function access(tenant, action) {
  const result = spawnSync('corepack', ['yarn', 'simbank:access', action, '--api-key=' + tenant.apiKey], { cwd: wsapi, encoding: 'utf8', timeout: 60000, env: { ...process.env, AWS_REGION: region, AWS_DEFAULT_REGION: region } });
  requireThat(result.status === 0, 'GUARDED_ENTITLEMENT_COMMAND_FAILED');
}
async function setAccess(tenant, action) {
  access(tenant, action);
  const item = await entitlement(tenant);
  requireThat(item?.status?.S === (action === 'enable' ? 'active' : 'suspended'), 'ENTITLEMENT_READBACK_FAILED');
  return item;
}
function username(t, mode) { return `e_${t.email.replace('@', '_at_')}__${mode}`; }
async function provision(t) {
  await ddb.putItem({ TableName: 'isecure-ws-channel-users', ConditionExpression: 'attribute_not_exists(email)', Item: {
    email: { S: t.email }, admin_enabled: { BOOL: true }, email_verified: { BOOL: true }, phone_number_verified: { BOOL: true },
    name: { S: t.name }, company: { S: t.company }, phone: { S: t.phone }, apikey: { S: t.apiKey }, apikey_type: { S: 'provided' }, environment: { S: 'test' }
  }});
  for (const mode of ['admin', 'data']) {
    await cognito.adminCreateUser({ UserPoolId: pool, Username: username(t, mode), MessageAction: 'SUPPRESS', UserAttributes: [
      { Name: 'email', Value: t.email }, { Name: 'phone_number', Value: t.phone }, { Name: 'name', Value: t.name },
      { Name: 'custom:apikey', Value: t.apiKey }, { Name: 'email_verified', Value: 'true' }, { Name: 'phone_number_verified', Value: 'true' }
    ] });
    await cognito.adminSetUserPassword({ UserPoolId: pool, Username: username(t, mode), Password: fixture.password, Permanent: true });
  }
  const key = await gateway.createApiKey({ name: `SDK-Qualification-${runId}-${t.label}`, description: 'Synthetic CSharp SDK qualification', enabled: true, value: t.apiKey });
  const plan = await gateway.createUsagePlan({ name: `SDK-Qualification-${runId}-${t.label}`, description: 'Synthetic CSharp SDK qualification', apiStages: [{ apiId, stage }] });
  t.gatewayKeyId = key.id; t.usagePlanId = plan.id; save(checkpointFile, fixture);
  await gateway.createUsagePlanKey({ usagePlanId: plan.id, keyId: key.id, keyType: 'API_KEY' });
  requireThat(!(await entitlement(t)), 'NEW_TENANT_ALREADY_ENTITLED');
}

function startDriver() {
  const child = spawn('dotnet', [path.join(root, 'examples/FileExchange/bin/Release/net10.0/FileExchange.dll'), '--driver'], { cwd: root, stdio: ['pipe', 'pipe', 'pipe'] });
  const queue = []; let closed = false;
  const lines = readline.createInterface({ input: child.stdout });
  lines.on('line', line => { const p = queue.shift(); if (!p) return; clearTimeout(p.timer); try { p.resolve(JSON.parse(line)); } catch { p.reject(new Error('DRIVER_RESPONSE_INVALID')); } });
  child.stderr.on('data', () => {}); // SDK errors arrive as bounded structured outcomes, not console payloads.
  child.on('exit', () => { closed = true; for (const p of queue.splice(0)) { clearTimeout(p.timer); p.reject(new Error('DRIVER_EXITED')); } });
  return {
    async call(client, operation, data = {}) {
      requireThat(!closed, 'DRIVER_CLOSED');
      return new Promise((resolve, reject) => {
        const pending = { resolve, reject, timer: setTimeout(() => { child.kill(); reject(new Error('DRIVER_TIMEOUT')); }, 45000) };
        queue.push(pending); child.stdin.write(JSON.stringify({ client, operation, ...data }) + '\n');
      });
    }, close() { child.stdin.end(); }, kill() { child.kill(); }
  };
}
let driver;
async function cs(client, operation, data = {}) {
  const result = await driver.call(client, operation, data);
  requireThat(result.ok, 'CS_' + operation.toUpperCase() + '_' + (result.error || 'FAILED'));
  return result.result;
}
async function createClient(t, mode) {
  return cs(`${t.label}-${mode}`, 'create', { baseUrl: base, publicKey, email: t.email, mode, apiKey: t.apiKey, bank: 'simulator', company: t.company, name: t.name, phone: t.phone });
}
const lastStep = new Map();
async function code(secret) {
  let step = Math.floor(Date.now() / 30000);
  while (step <= (lastStep.get(secret) ?? -1) || Date.now() % 30000 > 26000) { await sleep(1000); step = Math.floor(Date.now() / 30000); }
  let bits = 0, value = 0; const bytes = [];
  for (const c of secret) { const n = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'.indexOf(c); requireThat(n >= 0, 'TOTP_SECRET_INVALID'); value = (value << 5) | n; bits += 5; if (bits >= 8) { bits -= 8; bytes.push((value >>> bits) & 255); } }
  const counter = Buffer.alloc(8); counter.writeBigUInt64BE(BigInt(step));
  const mac = crypto.createHmac('sha1', Buffer.from(bytes)).update(counter).digest();
  const offset = mac.at(-1) & 15; lastStep.set(secret, step);
  return String((mac.readUInt32BE(offset) & 0x7fffffff) % 1000000).padStart(6, '0');
}
async function bootstrapTotp(t, clientId) {
  const auth = await cognito.adminInitiateAuth({ UserPoolId: pool, ClientId: clientId, AuthFlow: 'ADMIN_NO_SRP_AUTH', AuthParameters: { USERNAME: username(t, 'admin'), PASSWORD: fixture.password } });
  const token = auth.AuthenticationResult?.AccessToken; requireThat(token && !auth.ChallengeName, 'TOTP_BOOTSTRAP_FAILED');
  t.totpSecret = (await cognito.associateSoftwareToken({ AccessToken: token })).SecretCode;
  save(checkpointFile, fixture);
  const result = await cs(t.label + '-admin', 'verifyTotp', { accessToken: token, code: await code(t.totpSecret) });
  requireThat(result.Status === 'VerificationAccepted', 'CS_TOTP_VERIFICATION_FAILED');
}
async function login(t, mode) {
  const id = t.label + '-' + mode;
  let result = await cs(id, 'login', { password: fixture.password });
  if (mode === 'admin') {
    if (result.Status === 'NeedsMfaSelection') result = await cs(id, 'select', { method: 'Totp' });
    requireThat(result.Status === 'NeedsMfa' && result.Method === 'Totp', 'ADMIN_MFA_NOT_REQUIRED');
    result = await cs(id, 'mfa', { code: await code(t.totpSecret) });
  }
  requireThat(result.Status === 'Authenticated', 'CS_LOGIN_FAILED');
}
async function http(t, method, route, body, token) {
  const response = await fetch(base + route, { method, headers: { 'Content-Type': 'application/json', 'x-api-key': t.apiKey, ...(token ? { Authorization: token } : {}) }, body: body ? JSON.stringify(body) : undefined, redirect: 'error', signal: AbortSignal.timeout(30000) });
  const result = await response.json();
  requireThat(response.ok && result.ResponseCode === '00', 'INDEPENDENT_HTTP_' + response.status);
  return result;
}
async function independentLogin(t, mode) {
  const route = `/session/${encodeURIComponent(t.email)}/${mode}`;
  const challenge = (await http(t, 'GET', route)).Challenge;
  const encrypted = crypto.publicEncrypt({ key: publicKey, padding: crypto.constants.RSA_PKCS1_OAEP_PADDING, oaepHash: 'sha1' }, Buffer.from(fixture.password + '||' + challenge.split('|')[1])).toString('base64');
  let r = await http(t, 'POST', route, { ChResp: challenge, Encrypted: encrypted });
  if (mode === 'admin') {
    if (r.ChallengeName === 'SELECT_MFA_TYPE') r = await http(t, 'PUT', route + '/selectmfa', { MfaType: 'SOFTWARE_TOKEN_MFA', Session: r.Session });
    requireThat(r.ChallengeName === 'SOFTWARE_TOKEN_MFA', 'INDEPENDENT_MFA_NOT_REQUIRED');
    r = await http(t, 'PUT', route + '/mfacode', { Code: await code(t.totpSecret), Session: r.Session, ChallengeName: r.ChallengeName });
  }
  requireThat(r.IdToken && r.ApiKey === t.apiKey, 'INDEPENDENT_AUTH_INVALID');
  return r.IdToken;
}
async function denied(t, label) {
  const response = await driver.call(t.label + '-data', 'list', { fileType: 'camt.053.001.02', status: 'ALL' });
  requireThat(!response.ok && response.error === 'api' && response.code === '01' && response.responseText === 'Bank Simulator access is not enabled for this tenant', label + '_NOT_DENIED');
  pass(label);
}
async function renewal(t, expectedCode) {
  const result = await lambda.invoke({ FunctionName: 'cert_renewal', Qualifier: 'stage_gpgtest', InvocationType: 'RequestResponse',
    Payload: Buffer.from(JSON.stringify({ bank: 'simulator', email: t.email, production: 'false', context: { 'request-id': runId + '-renewal' } })) });
  const response = JSON.parse(Buffer.from(result.Payload || []).toString('utf8')).stdout;
  requireThat(!result.FunctionError && response?.ResponseCode === expectedCode, 'RENEWAL_OUTCOME_INVALID');
  if (expectedCode !== '00') requireThat(response.ResponseText === 'Bank Simulator access is not enabled for this tenant', 'RENEWAL_DENIAL_INVALID');
}
async function qualifyRenewal(t) {
  const before = (await cs(t.label + '-data', 'certificates')).Certs.filter(x => x.CertName.toLowerCase().includes('simulator'));
  const key = { TableName: 'isecure-ws-channel-users', Key: { email: { S: t.email } },
    ExpressionAttributeNames: { '#bank': 'simulator', '#force': 'force_renewal' }, ConditionExpression: 'attribute_exists(#bank)' };
  await ddb.updateItem({ ...key, UpdateExpression: 'SET #bank.#force = :force', ExpressionAttributeValues: { ':force': { S: 'true' } } });
  try { await renewal(t, '00'); }
  finally { await ddb.updateItem({ ...key, UpdateExpression: 'REMOVE #bank.#force' }); }
  const after = (await cs(t.label + '-data', 'certificates')).Certs.filter(x => x.CertName.toLowerCase().includes('simulator'));
  requireThat(after.length > 0 && JSON.stringify(before) !== JSON.stringify(after), 'RENEWAL_CERTIFICATE_UNCHANGED');
  pass('restored gpgtest renewal artifact renews an entitled synthetic certificate');
}
async function exactDownload(t, descriptor, token, label) {
  const actual = await cs(t.label + '-data', 'download', { fileType: descriptor.FileType, reference: descriptor.FileReference });
  const raw = await http(t, 'GET', `/files/simulator/${encodeURIComponent(descriptor.FileType)}/${encodeURIComponent(descriptor.FileReference)}`, undefined, token);
  const repeated = await cs(t.label + '-data', 'download', { fileType: descriptor.FileType, reference: descriptor.FileReference });
  const bytes = Buffer.from(raw.Content, 'base64');
  requireThat(actual.content === raw.Content && repeated.content === raw.Content && actual.sha256 === sha(bytes), label + '_BYTE_MISMATCH');
  evidence.files.push({ label, type: descriptor.FileType, bytes: bytes.length, sha256: sha(bytes) });
  return bytes;
}

async function runConsoleExample(t, pgp) {
  const template = fs.readFileSync(path.join(root, 'scripts/fixtures/synthetic-pain.001.001.09.xml'), 'utf8');
  let sequence = 0;
  const bytes = Buffer.from(template.replace(/<(MsgId|PmtInfId|InstrId|EndToEndId)>[^<]*<\/\1>/g,
    (_, tag) => `<${tag}>${runId}-${++sequence}</${tag}>`));
  const signature = await openpgp.sign({ message: await openpgp.createMessage({ binary: bytes }),
    signingKeys: await openpgp.readPrivateKey({ armoredKey: pgp.privateKey }), detached: true });
  const upload = path.join(privateRoot, 'console-upload.xml'), sig = path.join(privateRoot, 'console-upload.asc');
  const download = path.join(privateRoot, 'console-download.xml');
  fs.writeFileSync(upload, bytes, { mode: 0o600 }); fs.writeFileSync(sig, signature, { mode: 0o600 });
  const env = { ...process.env, ISECURE_BASE_URL: base, ISECURE_PUBLIC_KEY_FILE: path.join(root, 'scripts/fixtures/test-public.pem'),
    ISECURE_EMAIL: t.email, ISECURE_MODE: 'data', ISECURE_API_KEY: t.apiKey, ISECURE_BANK: 'simulator',
    ISECURE_COMPANY: t.company, ISECURE_NAME: t.name, ISECURE_PHONE: t.phone, ISECURE_PASSWORD: fixture.password,
    ISECURE_UPLOAD_FILE: upload, ISECURE_SIGNATURE_FILE: sig, ISECURE_UPLOAD_TYPE: 'pain.001.001.09',
    ISECURE_DOWNLOAD_TYPE: 'pain.002.001.10', ISECURE_DOWNLOAD_FILE: download };
  // The ordinary example is exercised as well as its JSON-lines test driver.
  const result = await new Promise((resolve, reject) => {
    const child = spawn('dotnet', [path.join(root, 'examples/FileExchange/bin/Release/net10.0/FileExchange.dll')], { cwd: root, env, stdio: 'ignore' });
    const timer = setTimeout(() => { child.kill(); reject(new Error('CONSOLE_EXAMPLE_TIMEOUT')); }, 120000);
    child.on('error', () => { clearTimeout(timer); reject(new Error('CONSOLE_EXAMPLE_START_FAILED')); });
    child.on('exit', status => { clearTimeout(timer); resolve(status); });
  });
  requireThat(result === 0 && fs.existsSync(download), 'CONSOLE_EXAMPLE_FAILED');
  const returned = fs.readFileSync(download);
  requireThat(returned.toString().includes('pain.002.001.10') && returned.toString().includes(runId), 'CONSOLE_FEEDBACK_INVALID');
  evidence.consoleExample = { uploadBytes: bytes.length, uploadSha256: sha(bytes), downloadedBytes: returned.length, downloadedSha256: sha(returned) };
  // Console logout may invalidate other sessions for this user. Authenticate afresh.
  await login(t, 'data');
  pass('ordinary C# console login/list/signed upload/download/logout workflow');
}

let failure, guarded = false;
async function qualify() {
  const mappings = await gateway.getBasePathMappings({ domainName: 'ws-api.test.isecure.fi' });
  requireThat(mappings.items.some(x => x.basePath === 'v2' && x.restApiId === apiId && x.stage === stage), 'WRONG_API_STAGE');
  const release = await lambda.getFunctionConfiguration({ FunctionName: 'index', Qualifier: 'stage_gpgtest' });
  requireThat(release.Environment?.Variables?.ISECURE_SIMHOST_FUNCTION_URL, 'GPGTEST_SIMULATOR_RELEASE_REQUIRED');
  evidence.restRelease = { version: release.Version, codeSha256: release.CodeSha256 };
  const renewalRelease = await lambda.getFunctionConfiguration({ FunctionName: 'cert_renewal', Qualifier: 'stage_gpgtest' });
  requireThat(renewalRelease.Environment?.Variables?.ISECURE_SIMHOST_FUNCTION_URL, 'GPGTEST_SIMULATOR_RENEWAL_RELEASE_REQUIRED');
  evidence.renewalRelease = { version: renewalRelease.Version, codeSha256: renewalRelease.CodeSha256 };
  pass('guarded account, region and gpgtest domain mapping');
  const clientId = (await cognito.listUserPoolClients({ UserPoolId: pool, MaxResults: 60 })).UserPoolClients.find(x => x.ClientName === 'isecure-ws-channel-lambda-login-new')?.ClientId;
  requireThat(clientId, 'LOGIN_CLIENT_NOT_FOUND');
  driver = startDriver();
  for (const t of fixture.tenants) {
    await provision(t);
    await createClient(t, 'admin'); await createClient(t, 'data');
    await bootstrapTotp(t, clientId);
    await login(t, 'admin'); await login(t, 'data');
  }
  pass('C# autonomous TOTP verification and admin/data authentication for two tenants');
  const [primary, secondary] = fixture.tenants;
  await denied(primary, 'missing entitlement denial');
  await denied(secondary, 'independent second tenant missing entitlement denial');
  await renewal(primary, '01');
  pass('missing entitlement denies simulator renewal');
  await setAccess(primary, 'enable');
  await denied(secondary, 'enabling primary does not grant second tenant access');
  await setAccess(secondary, 'enable');
  console.log('WAIT negative entitlement cache expiry (61 seconds)'); await sleep(61000);
  const tokens = {};
  for (const t of fixture.tenants) {
    const adminToken = await independentLogin(t, 'admin');
    await http(t, 'POST', '/certs/simulator', { Code: 'SIM-' + sha(t.email).slice(0, 24), Company: t.company, WsUserId: 'SIM-' + sha(t.email).slice(0, 12) }, adminToken);
    const certs = await cs(t.label + '-data', 'certificates');
    requireThat(certs.Connections?.some(x => x.Bank === 'simulator') || certs.Certs?.some(x => (x.CertName || '').includes('simulator')), 'CS_CERTIFICATE_NOT_VISIBLE');
    tokens[t.label] = await independentLogin(t, 'data');
  }
  pass('enabled entitlement after cache expiry and certificate discovery');
  const initial = {};
  for (const t of fixture.tenants) {
    const list = await cs(t.label + '-data', 'list', { fileType: 'camt.053.001.02', status: 'NEW' });
    requireThat(list.FileDescriptors.length === 1, 'INITIAL_STATEMENT_COUNT');
    initial[t.label] = list.FileDescriptors[0];
    const bytes = await exactDownload(t, initial[t.label], tokens[t.label], t.label + ' initial statement');
    requireThat(bytes.toString().includes('camt.053.001.02') && bytes.toString().includes('FI2112345600000785'), 'INITIAL_STATEMENT_INVALID');
  }
  requireThat(initial.primary.FileReference !== initial.secondary.FileReference, 'TENANT_REFERENCE_COLLISION');
  const cross = await driver.call('secondary-data', 'download', { fileType: initial.primary.FileType, reference: initial.primary.FileReference });
  requireThat(!cross.ok && cross.error === 'api' && cross.code === '01', 'CROSS_TENANT_DOWNLOAD_ALLOWED');
  pass('exact initial download/replay bytes and cross-tenant reference denial');
  const pgp = await openpgp.generateKey({ type: 'rsa', rsaBits: 2048, userIDs: [{ name: 'Synthetic CSharp', email: primary.email }] });
  fixture.pgp = pgp; save(checkpointFile, fixture);
  await cs('primary-admin', 'uploadKey', { publicKey: pgp.publicKey });
  const bytes = fs.readFileSync(path.join(root, 'scripts/fixtures/synthetic-pain.001.001.09.xml'));
  const signature = await openpgp.sign({ message: await openpgp.createMessage({ binary: bytes }), signingKeys: await openpgp.readPrivateKey({ armoredKey: pgp.privateKey }), detached: true });
  const verified = await openpgp.verify({ message: await openpgp.createMessage({ binary: bytes }), signature: await openpgp.readSignature({ armoredSignature: signature }), verificationKeys: await openpgp.readKey({ armoredKey: pgp.publicKey }) });
  await verified.signatures[0].verified;
  evidence.upload = { bytes: bytes.length, sha256: sha(bytes), signatureSha256: sha(signature) };
  await cs('primary-data', 'upload', { contents: bytes.toString('base64'), fileName: runId + '.xml', fileType: 'pain.001.001.09', signature });
  const bad = await driver.call('primary-data', 'upload', { contents: Buffer.concat([bytes, Buffer.from(' ')]).toString('base64'), fileName: runId + '-tampered.xml', fileType: 'pain.001.001.09', signature });
  requireThat(!bad.ok && bad.error === 'api' && bad.code === '01', 'TAMPERED_SIGNATURE_ACCEPTED');
  pass('C# PGP key upload, independently verified signature, signed upload and tampered-byte rejection');
  for (const type of ['pain.002.001.10', 'camt.054.001.02', 'camt.053.001.02']) {
    let descriptor; const deadline = Date.now() + 60000;
    do {
      const list = await cs('primary-data', 'list', { fileType: type, status: 'NEW' });
      descriptor = list.FileDescriptors.find(x => x.FileReference !== initial.primary.FileReference);
      if (!descriptor) await sleep(1000);
    } while (!descriptor && Date.now() < deadline);
    requireThat(descriptor, 'FEEDBACK_MISSING_' + type);
    const downloaded = await exactDownload(primary, descriptor, tokens.primary, type + ' feedback');
    requireThat(downloaded.toString().includes(type), 'FEEDBACK_TYPE_INVALID');
  }
  pass('C# feedback list/download/replay matches independent HTTP bytes and digests');
  await qualifyRenewal(primary);
  await runConsoleExample(primary, pgp);
  await setAccess(primary, 'disable');
  console.log('WAIT positive entitlement cache expiry (61 seconds)'); await sleep(61000);
  await denied(primary, 'suspended entitlement denial after cache expiry');
  await renewal(primary, '01');
  pass('suspended entitlement denies simulator renewal');
  await cs('secondary-data', 'list', { fileType: 'camt.053.001.02', status: 'ALL' });
  pass('suspending primary leaves second tenant access intact');
  for (const t of fixture.tenants) for (const mode of ['admin', 'data']) await cs(t.label + '-' + mode, 'logout');
  const after = await driver.call('primary-data', 'list', { fileType: 'camt.053.001.02', status: 'ALL' });
  requireThat(!after.ok && after.error === 'ISecureAuthException', 'LOGOUT_RETAINED_AUTHORITY');
  pass('C# logout clears local authority');
}
try {
  requireThat(process.versions.node.split('.')[0] === '24', 'NODE_24_REQUIRED');
  requireThat((await sts.getCallerIdentity({})).Account === account, 'WRONG_AWS_ACCOUNT');
  guarded = true;
  if (!retained) await qualify();
} catch (error) {
  failure = error;
  console.error('FAIL ' + (error.code || (error.name !== 'Error' ? error.name : error.message) || 'qualification failed'));
} finally {
  driver?.close();
  for (const tenant of guarded ? fixture.tenants : []) {
    try {
      const prior = await entitlement(tenant);
      if (prior) {
        const confirmed = prior.status?.S === 'suspended' ? prior : await setAccess(tenant, 'disable');
        evidence.teardown.push({ tenant: tenant.label, status: confirmed.status.S, revision: Number(confirmed.revision.N) });
      } else evidence.teardown.push({ tenant: tenant.label, status: 'missing-denies-access' });
    } catch { evidence.teardown.push({ tenant: tenant.label, status: 'FAILED' }); failure ||= new Error('TEARDOWN_FAILED'); }
  }
  evidence.completedAt = new Date().toISOString(); evidence.passed = !failure;
  fs.mkdirSync(path.join(root, 'artifacts'), { recursive: true });
  save(path.join(root, 'artifacts', evidenceName), evidence);
  fixture.phase = evidence.teardown.some(x => x.status === 'FAILED') ? 'teardown-required' : failure ? 'failed-with-teardown' : retained ? 'suspended' : 'qualified-and-suspended'; save(checkpointFile, fixture);
  console.log('EVIDENCE artifacts/' + evidenceName);
  console.log('TEARDOWN ' + evidence.teardown.map(x => x.tenant + ':' + x.status).join(', '));
}
if (failure) process.exitCode = 1;
