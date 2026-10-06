type Provider = 'atlassian' | 'bitbucket';
interface Env {
  PUBLIC_BASE_URL: string;
  ENCRYPTION_KEY: string;
  ATLASSIAN_CLIENT_ID: string;
  ATLASSIAN_CLIENT_SECRET: string;
  BITBUCKET_CLIENT_ID: string;
  BITBUCKET_CLIENT_SECRET: string;
  CONNECTIONS: DurableObjectNamespace;
  RATE_LIMIT: RateLimit;
  START_LIMIT: RateLimit;
}
interface Box { iv: string; data: string }
interface Transaction { provider: Provider; scopes: string; pollHash: string; stateHash: string; expires: number; status: 'pending' | 'denied' | 'complete'; result?: Box }
interface Grant { provider: Provider; scopes: string; refresh: string; connections: string[]; touched: number }
interface Credential { hash: string; grantId: string }
interface Tokens { access_token: string; refresh_token?: string; expires_in: number }
const encoder = new TextEncoder();
const json = (value: unknown, status = 200) => Response.json(value, { status, headers: { 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer', 'X-Content-Type-Options': 'nosniff' } });
const bytes64 = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes));
const from64 = (value: string) => Uint8Array.from(atob(value), c => c.charCodeAt(0));
const random = () => bytes64(crypto.getRandomValues(new Uint8Array(32))).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
const hash = async (value: string) => bytes64(new Uint8Array(await crypto.subtle.digest('SHA-256', encoder.encode(value))));
const same = (a: string, b: string) => {
  if (a.length !== b.length) return false;
  let result = 0;
  for (let i = 0; i < a.length; i++) result |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return result === 0;
};
const provider = (value: unknown): value is Provider => value === 'atlassian' || value === 'bitbucket';
const tokenUrl = (p: Provider) => p === 'atlassian' ? 'https://auth.atlassian.com/oauth/token' : 'https://bitbucket.org/site/oauth2/access_token';
const jiraScopes = 'read:jira-work read:project:jira read:board-scope:jira-software';
const confluenceScopes = 'read:space:confluence read:content-details:confluence';

export class Connections {
  private queue: Promise<unknown> = Promise.resolve();
  constructor(private ctx: DurableObjectState, private env: Env) {}
  // ponytail: serialize all grants in one object for safe login/rotation; shard by verified account when traffic warrants it.
  fetch(request: Request): Promise<Response> {
    const result = this.queue.then(() => this.route(request));
    this.queue = result.catch(() => undefined);
    return result.catch(() => json({ error: 'Login service could not complete this request. Please retry or reconnect.' }, 503));
  }
  private async seal(value: unknown, purpose: string): Promise<Box> {
    const key = await crypto.subtle.importKey('raw', from64(this.env.ENCRYPTION_KEY), 'AES-GCM', false, ['encrypt']);
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const data = await crypto.subtle.encrypt({ name: 'AES-GCM', iv, additionalData: encoder.encode(purpose) }, key, encoder.encode(JSON.stringify(value)));
    return { iv: bytes64(iv), data: bytes64(new Uint8Array(data)) };
  }
  private async open<T>(value: Box, purpose: string): Promise<T> {
    const key = await crypto.subtle.importKey('raw', from64(this.env.ENCRYPTION_KEY), 'AES-GCM', false, ['decrypt']);
    const data = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: from64(value.iv), additionalData: encoder.encode(purpose) }, key, from64(value.data));
    return JSON.parse(new TextDecoder().decode(data)) as T;
  }
  private async exchange(p: Provider, parameters: Record<string, string>): Promise<Tokens> {
    const atlassian = p === 'atlassian';
    const response = await fetch(tokenUrl(p), {
      method: 'POST', redirect: 'error', signal: AbortSignal.timeout(15000),
      headers: atlassian ? { 'Content-Type': 'application/json' } : { 'Content-Type': 'application/x-www-form-urlencoded', Authorization: 'Basic ' + btoa(this.env.BITBUCKET_CLIENT_ID + ':' + this.env.BITBUCKET_CLIENT_SECRET) },
      body: atlassian ? JSON.stringify({ ...parameters, client_id: this.env.ATLASSIAN_CLIENT_ID, client_secret: this.env.ATLASSIAN_CLIENT_SECRET }) : new URLSearchParams(parameters).toString(),
    });
    if (!response.ok) throw new Error('Provider rejected token exchange');
    const tokens = await response.json<Tokens>();
    if (!tokens.access_token || !Number.isFinite(tokens.expires_in) || tokens.expires_in <= 0) throw new Error('Invalid token response');
    return tokens;
  }
  private async body(request: Request): Promise<Record<string, unknown>> {
    if (!request.headers.get('Content-Type')?.startsWith('application/json')) throw new Error('Invalid content type');
    const reader = request.body?.getReader();
    if (!reader) throw new Error('Missing body');
    const chunks: Uint8Array[] = [];
    let length = 0;
    while (true) {
      const part = await reader.read();
      if (part.done) break;
      length += part.value.byteLength;
      if (length > 4096) { await reader.cancel(); throw new Error('Request too large'); }
      chunks.push(part.value);
    }
    const bytes = new Uint8Array(length);
    let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    const text = new TextDecoder().decode(bytes);
    const data: unknown = JSON.parse(text);
    if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error('Invalid JSON');
    return data as Record<string, unknown>;
  }
  private async route(request: Request): Promise<Response> {
    const url = new URL(request.url);
    const root = new URL(this.env.PUBLIC_BASE_URL);
    if (root.protocol !== 'https:' || root.username || root.password || root.search || root.hash || root.pathname !== '/') return json({ error: 'Invalid service configuration' }, 503);
    if (request.method === 'GET' && url.pathname.startsWith('/callback/')) return this.callback(url);
    if (request.method !== 'POST') return json({ error: 'Method not allowed' }, 405);
    let body: Record<string, unknown>;
    try { body = await this.body(request); } catch { return json({ error: 'Invalid request' }, 400); }
    if (url.pathname === '/v1/login') {
      if (!provider(body.provider)) return json({ error: 'Unknown provider' }, 400);
      const p = body.provider;
      if (body.product !== undefined && !['jira', 'confluence', 'bitbucket'].includes(String(body.product))) return json({ error: 'Unknown product' }, 400);
      let requestedScopes = p === 'atlassian' ? 'offline_access read:me ' + (body.product === 'jira' ? jiraScopes : body.product === 'confluence' ? confluenceScopes : jiraScopes + ' ' + confluenceScopes) : '';
      // An unlinked device cannot identify the account before consent. Request both supported products so consent cannot narrow another device's grant.
      if (p === 'atlassian' && body.connectionId == null) requestedScopes = 'offline_access read:me ' + jiraScopes + ' ' + confluenceScopes;
      if (body.connectionId != null) {
        if (typeof body.connectionId !== 'string') return json({ error: 'Invalid connection' }, 400);
        const credential = await this.ctx.storage.get<Credential>('conn:' + body.connectionId);
        const bearer = request.headers.get('Authorization') ?? '';
        if (!credential || !bearer.startsWith('Bearer ') || !same(credential.hash, await hash(bearer.slice(7)))) return json({ error: 'Unauthorized' }, 401);
        const box = await this.ctx.storage.get<Box>('grant:' + credential.grantId);
        if (!box) return json({ error: 'Reconnect required' }, 401);
        const grant = await this.open<Grant>(box, 'grant:' + credential.grantId);
        if (grant.provider !== p) return json({ error: 'Provider mismatch' }, 400);
        requestedScopes = [...new Set((requestedScopes + ' ' + grant.scopes).split(' ').filter(Boolean))].join(' ');
      }
      const id = random(), pollKey = random(), state = random();
      const expires = Date.now() + 600000;
      const stateHash = await hash(state);
      const txn: Transaction = { provider: p, scopes: requestedScopes, pollHash: await hash(pollKey), stateHash, expires, status: 'pending' };
      await this.ctx.storage.put('txn:' + id, txn);
      await this.ctx.storage.put('state:' + stateHash, id);
      await this.ctx.storage.setAlarm(Date.now() + 60000);
      const browser = new URL(p === 'atlassian' ? 'https://auth.atlassian.com/authorize' : 'https://bitbucket.org/site/oauth2/authorize');
      browser.search = new URLSearchParams({ client_id: p === 'atlassian' ? this.env.ATLASSIAN_CLIENT_ID : this.env.BITBUCKET_CLIENT_ID, response_type: 'code', state, redirect_uri: root.origin + '/callback/' + p }).toString();
      if (p === 'atlassian') {
        browser.searchParams.set('audience', 'api.atlassian.com'); browser.searchParams.set('scope', requestedScopes); browser.searchParams.set('prompt', 'consent');
      }
      return json({ transactionId: id, pollKey, browserUrl: browser.href, expiresAt: new Date(expires).toISOString() });
    }
    if (url.pathname === '/v1/login/poll') {
      if (typeof body.transactionId !== 'string' || typeof body.pollKey !== 'string') return json({ error: 'Invalid request' }, 400);
      const id = body.transactionId;
      const txn = await this.ctx.storage.get<Transaction>('txn:' + id);
      if (!txn || txn.expires < Date.now()) return json({ error: 'Login expired or already delivered' }, 410);
      if (!same(txn.pollHash, await hash(body.pollKey))) return json({ error: 'Unauthorized' }, 401);
      if (txn.status !== 'complete') return json({ status: txn.status });
      const result = await this.open<Record<string, unknown>>(txn.result!, 'txn:' + id);
      await this.ctx.storage.delete('txn:' + id);
      await this.ctx.storage.delete('state:' + txn.stateHash);
      return json({ status: 'complete', ...result });
    }
    if (url.pathname === '/v1/connections/refresh' || url.pathname === '/v1/connections/disconnect') {
      if (typeof body.connectionId !== 'string') return json({ error: 'Invalid request' }, 400);
      const id = body.connectionId;
      const credential = await this.ctx.storage.get<Credential>('conn:' + id);
      const bearer = request.headers.get('Authorization') ?? '';
      if (!credential || !bearer.startsWith('Bearer ') || !same(credential.hash, await hash(bearer.slice(7)))) return json({ error: 'Unauthorized' }, 401);
      const boxed = await this.ctx.storage.get<Box>('grant:' + credential.grantId);
      if (!boxed) return json({ error: 'Reconnect required' }, 401);
      const grant = await this.open<Grant>(boxed, 'grant:' + credential.grantId);
      if (url.pathname.endsWith('/disconnect')) {
        await this.retireConnection(id);
        return json({ status: 'disconnected' });
      }
      const tokens = await this.exchange(grant.provider, { grant_type: 'refresh_token', refresh_token: grant.refresh });
      grant.refresh = tokens.refresh_token ?? grant.refresh;
      grant.touched = Date.now();
      await this.ctx.storage.put('grant:' + credential.grantId, await this.seal(grant, 'grant:' + credential.grantId));
      return json({ accessToken: tokens.access_token, expiresAt: new Date(Date.now() + tokens.expires_in * 1000).toISOString() });
    }
    return json({ error: 'Not found' }, 404);
  }
  private async callback(url: URL): Promise<Response> {
    const p = url.pathname.slice('/callback/'.length);
    if (!provider(p)) return json({ error: 'Unknown provider' }, 400);
    const state = url.searchParams.get('state');
    if (!state || state.length > 256) return json({ error: 'Invalid state' }, 400);
    const stateHash = await hash(state);
    const id = await this.ctx.storage.get<string>('state:' + stateHash);
    const txn = id ? await this.ctx.storage.get<Transaction>('txn:' + id) : undefined;
    if (!id || !txn || txn.provider !== p || txn.status !== 'pending' || txn.expires < Date.now()) return json({ error: 'Invalid or expired state' }, 400);
    await this.ctx.storage.delete('state:' + stateHash);
    if (url.searchParams.has('error')) { txn.status = 'denied'; await this.ctx.storage.put('txn:' + id, txn); return new Response('Access was denied. Return to Command Palette.', { headers: { 'Content-Type': 'text/plain', 'Cache-Control': 'no-store' } }); }
    const code = url.searchParams.get('code');
    if (!code || code.length > 2048) return json({ error: 'Missing code' }, 400);
    try {
      const tokens = await this.exchange(p, { grant_type: 'authorization_code', code, redirect_uri: new URL(this.env.PUBLIC_BASE_URL).origin + '/callback/' + p });
      if (!tokens.refresh_token) throw new Error('Missing refresh token');
      const identity = await fetch(p === 'atlassian' ? 'https://api.atlassian.com/me' : 'https://api.bitbucket.org/2.0/user', { headers: { Authorization: 'Bearer ' + tokens.access_token }, redirect: 'error', signal: AbortSignal.timeout(15000) });
      if (!identity.ok) throw new Error('Cannot identify account');
      const who = await identity.json<{ account_id?: string; uuid?: string }>();
      const accountId = p === 'atlassian' ? who.account_id : who.uuid;
      if (!accountId) throw new Error('Missing account identity');
      const grantId = await hash(p + ':' + accountId);
      const prior = await this.ctx.storage.get<Box>('grant:' + grantId);
      const grant: Grant = prior ? await this.open<Grant>(prior, 'grant:' + grantId) : { provider: p, scopes: txn.scopes, refresh: '', connections: [], touched: Date.now() };
      const connectionId = random(), connectionKey = random();
      grant.refresh = tokens.refresh_token; grant.scopes = txn.scopes; grant.touched = Date.now(); grant.connections.push(connectionId);
      await this.ctx.storage.put('grant:' + grantId, await this.seal(grant, 'grant:' + grantId));
      await this.ctx.storage.put('conn:' + connectionId, { hash: await hash(connectionKey), grantId });
      txn.status = 'complete';
      txn.result = await this.seal({ accountId, connectionId, connectionKey, accessToken: tokens.access_token, expiresAt: new Date(Date.now() + tokens.expires_in * 1000).toISOString() }, 'txn:' + id);
      await this.ctx.storage.put('txn:' + id, txn);
      return new Response('Connected. Return to Command Palette to choose your workspace or site.', { headers: { 'Content-Type': 'text/plain', 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer' } });
    } catch {
      txn.status = 'denied'; await this.ctx.storage.put('txn:' + id, txn);
      return json({ error: 'Provider login failed. Return to Command Palette and reconnect.' }, 502);
    }
  }
  alarm(): Promise<void> {
    const result = this.queue.then(() => this.cleanup());
    this.queue = result.catch(() => undefined);
    return result;
  }
  private async retireConnection(id: string): Promise<void> {
    const credential = await this.ctx.storage.get<Credential>('conn:' + id);
    await this.ctx.storage.delete('conn:' + id);
    if (!credential) return;
    const key = 'grant:' + credential.grantId;
    const box = await this.ctx.storage.get<Box>(key);
    if (!box) return;
    const grant = await this.open<Grant>(box, key);
    grant.connections = grant.connections.filter(c => c !== id);
    if (grant.connections.length === 0) await this.ctx.storage.delete(key);
    else await this.ctx.storage.put(key, await this.seal(grant, key));
  }
  private async cleanup(): Promise<void> {
    const transactions = await this.ctx.storage.list<Transaction>({ prefix: 'txn:' });
    for (const [key, txn] of transactions) if (txn.expires < Date.now()) {
      if (txn.result) {
        const result = await this.open<{ connectionId: string }>(txn.result, key);
        await this.retireConnection(result.connectionId);
      }
      await this.ctx.storage.delete(key);
      await this.ctx.storage.delete('state:' + txn.stateHash);
    }
    const grants = await this.ctx.storage.list<Box>({ prefix: 'grant:' });
    for (const [key, box] of grants) {
      const grant = await this.open<Grant>(box, key);
      if (grant.touched < Date.now() - 90 * 86400000) { for (const id of grant.connections) await this.ctx.storage.delete('conn:' + id); await this.ctx.storage.delete(key); }
    }
    if (transactions.size || grants.size) await this.ctx.storage.setAlarm(Date.now() + 60000);
  }
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    if (new URL(request.url).pathname === '/health') return json({ status: 'ok' });
    const key = request.headers.get('CF-Connecting-IP') ?? 'unknown';
    if (!(await env.RATE_LIMIT.limit({ key })).success) return json({ error: 'Too many requests' }, 429);
    if (new URL(request.url).pathname === '/v1/login' && !(await env.START_LIMIT.limit({ key })).success) return json({ error: 'Too many login attempts' }, 429);
    return env.CONNECTIONS.get(env.CONNECTIONS.idFromName('oauth')).fetch(request);
  },
};
