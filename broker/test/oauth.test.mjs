import test from 'node:test';
import assert from 'node:assert/strict';
import { Connections } from '../dist/index.js';

class Storage {
  data = new Map();
  async get(key) { return structuredClone(this.data.get(key)); }
  async put(key, value) { this.data.set(key, structuredClone(value)); }
  async delete(key) { return this.data.delete(key); }
  async list({ prefix = '' } = {}) { return new Map([...this.data].filter(([k]) => k.startsWith(prefix))); }
  async setAlarm() {}
}
const env = {
  PUBLIC_BASE_URL: 'https://login.example',
  ENCRYPTION_KEY: btoa('12345678901234567890123456789012'),
  ATLASSIAN_CLIENT_ID: 'client', ATLASSIAN_CLIENT_SECRET: 'secret',
  BITBUCKET_CLIENT_ID: 'client', BITBUCKET_CLIENT_SECRET: 'secret',
};
const post = (path, body, key) => new Request('https://login.example' + path, { method: 'POST', headers: { 'Content-Type': 'application/json', ...(key ? { Authorization: 'Bearer ' + key } : {}) }, body: JSON.stringify(body) });
const start = async (broker) => {
  const response = await broker.fetch(post('/v1/login', { provider: 'atlassian' }));
  assert.equal(response.status, 200);
  return response.json();
};
const callback = (value, state, code = 'code') => new Request(`https://login.example/callback/atlassian?code=${code}&state=${state ?? new URL(value.browserUrl).searchParams.get('state')}`);

test('unknown provider is rejected', async () => {
  const broker = new Connections({ storage: new Storage() }, env);
  assert.equal((await broker.fetch(post('/v1/login', { provider: 'unknown' }))).status, 400);
});

test('unconfigured provider cannot start a login', async () => {
  const broker = new Connections({ storage: new Storage() }, { ...env, ATLASSIAN_CLIENT_SECRET: '' });
  assert.equal((await broker.fetch(post('/v1/login', { provider: 'atlassian' }))).status, 503);
});
test('wrong state and wrong poll key cannot retrieve a login', async () => {
  const broker = new Connections({ storage: new Storage() }, env);
  const value = await start(broker);
  assert.equal((await broker.fetch(callback(value, 'wrong'))).status, 400);
  assert.equal((await broker.fetch(post('/v1/login/poll', { provider: 'atlassian', transactionId: value.transactionId, pollKey: 'wrong' }))).status, 401);
});
test('expired login cannot be completed', async () => {
  const storage = new Storage();
  const broker = new Connections({ storage }, env);
  const value = await start(broker);
  const txn = await storage.get('txn:' + value.transactionId);
  txn.expires = 0;
  await storage.put('txn:' + value.transactionId, txn);
  assert.equal((await broker.fetch(callback(value))).status, 400);
});

test('first desktop login accepts a null prior connection', async () => {
  const broker = new Connections({ storage: new Storage() }, env);
  const response = await broker.fetch(post('/v1/login', { provider: 'atlassian', product: 'jira', connectionId: null }));
  assert.equal(response.status, 200);
  const scope = new URL((await response.json()).browserUrl).searchParams.get('scope');
  assert.ok(scope.includes('read:jira-work') && scope.includes('read:space:confluence'));
});

test('undelivered completed login is removed at transaction expiry', async () => {
  const storage = new Storage();
  const broker = new Connections({ storage }, env);
  const original = globalThis.fetch;
  globalThis.fetch = async url => String(url).endsWith('/me') ? Response.json({ account_id: 'abandoned-account' }) : Response.json({ access_token: 'token', refresh_token: 'refresh', expires_in: 3600 });
  try {
    const value = await start(broker);
    assert.equal((await broker.fetch(callback(value))).status, 200);
    const txn = await storage.get('txn:' + value.transactionId);
    txn.expires = 0;
    await storage.put('txn:' + value.transactionId, txn);
    await broker.alarm();
    assert.equal((await storage.list({ prefix: 'conn:' })).size, 0);
    assert.equal((await storage.list({ prefix: 'grant:' })).size, 0);
  } finally { globalThis.fetch = original; }
});

test('adding Confluence preserves Jira scopes for the same authenticated grant', async () => {
  const storage = new Storage();
  const broker = new Connections({ storage }, env);
  const original = globalThis.fetch;
  globalThis.fetch = async url => String(url).endsWith('/me') ? Response.json({ account_id: 'same-account' }) : Response.json({ access_token: 'token', refresh_token: 'refresh', expires_in: 3600 });
  try {
    const value = await (await broker.fetch(post('/v1/login', { provider: 'atlassian', product: 'jira' }))).json();
    assert.equal((await broker.fetch(callback(value))).status, 200);
    const connection = await (await broker.fetch(post('/v1/login/poll', { transactionId: value.transactionId, pollKey: value.pollKey }))).json();
    const added = await (await broker.fetch(post('/v1/login', { provider: 'atlassian', product: 'confluence', connectionId: connection.connectionId }, connection.connectionKey))).json();
    const scope = new URL(added.browserUrl).searchParams.get('scope');
    assert.ok(scope.includes('read:jira-work') && scope.includes('read:space:confluence'));
    assert.equal((await broker.fetch(post('/v1/login', { provider: 'atlassian', product: 'confluence', connectionId: connection.connectionId }, 'wrong'))).status, 401);
  } finally { globalThis.fetch = original; }
});

test('login is delivered once; shared grant refresh is serialized and encrypted', async () => {
  const storage = new Storage();
  const broker = new Connections({ storage }, env);
  const original = globalThis.fetch;
  let refreshes = 0;
  globalThis.fetch = async (request, options) => {
    const url = String(request);
    if (url.endsWith('/me')) return Response.json({ account_id: 'account-123' });
    const body = JSON.parse(options.body);
    if (body.grant_type === 'refresh_token') {
      assert.equal(body.refresh_token, 'refresh-' + refreshes);
      refreshes++;
      await new Promise(resolve => setTimeout(resolve, 10));
    }
    return Response.json({ access_token: 'access-' + refreshes, refresh_token: 'refresh-' + refreshes, expires_in: 3600 });
  };
  try {
    const value = await start(broker);
    assert.equal((await broker.fetch(callback(value))).status, 200);
    const poll = () => broker.fetch(post('/v1/login/poll', { provider: 'atlassian', transactionId: value.transactionId, pollKey: value.pollKey }));
    const result = await (await poll()).json();
    assert.equal(result.status, 'complete');
    assert.equal((await poll()).status, 410);
    assert.equal((await broker.fetch(post('/v1/connections/refresh', { connectionId: result.connectionId }, 'wrong'))).status, 401);
    const replies = await Promise.all([1, 2].map(() => broker.fetch(post('/v1/connections/refresh', { connectionId: result.connectionId }, result.connectionKey))));
    assert.deepEqual(replies.map(r => r.status), [200, 200]);
    assert.equal(refreshes, 2);
    const stored = JSON.stringify([...storage.data]);
    assert.ok(!stored.includes('refresh-2') && !stored.includes(result.connectionKey));
    assert.equal((await broker.fetch(post('/v1/connections/disconnect', { connectionId: result.connectionId }, result.connectionKey))).status, 200);
    assert.equal((await broker.fetch(post('/v1/connections/refresh', { connectionId: result.connectionId }, result.connectionKey))).status, 401);
  } finally { globalThis.fetch = original; }
});
