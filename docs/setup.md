# Developer setup

The desktop extension contains no OAuth client secret. It calls provider APIs directly using a DPAPI-protected access token. The broker exchanges authorization codes and renews tokens.

## OAuth apps

Create a Bitbucket Cloud OAuth consumer with **Account: Read** and **Repositories: Read**, and callback `https://YOUR-BROKER/callback/bitbucket`. The account permission supports account identity and workspace discovery. Do not enable write permissions.

Create one distributable Atlassian OAuth 2.0 3LO app with callback `https://YOUR-BROKER/callback/atlassian`. Enable the read scopes used by the implementation:

- Common: `read:me`, `offline_access`.
- Jira: `read:jira-work`, `read:project:jira`, `read:board-scope:jira-software`.
- Confluence: `read:space:confluence`, `read:content-details:confluence`.

Initial Atlassian authorization requests both products' read scopes: the account is unknown before consent, and a narrower consent could replace another device's grant. Reconnecting an authenticated account preserves its existing scopes. Verify app distribution/sharing settings before users outside the developer account can connect.

References: [Atlassian 3LO](https://developer.atlassian.com/cloud/jira/platform/oauth-2-3lo-apps/), [Bitbucket OAuth](https://support.atlassian.com/bitbucket-cloud/docs/use-oauth-on-bitbucket-cloud/), [Jira project search](https://developer.atlassian.com/cloud/jira/platform/rest/v3/api-group-projects/), [Jira boards](https://developer.atlassian.com/cloud/jira/software/rest/api-group-board/), [Confluence spaces](https://developer.atlassian.com/cloud/confluence/rest/v2/api-group-space/), [Confluence title search](https://developer.atlassian.com/cloud/confluence/rest/v1/api-group-content/).

## Cloudflare

Authenticate with `npx wrangler login` in `broker/`. Set `PUBLIC_BASE_URL` in Wrangler variables to the exact HTTPS origin, with no subpath. Use the same origin in both OAuth app callback registrations.

Configure these secrets using `npx wrangler secret put NAME`, with values entered into the CLI prompt:

```text
ATLASSIAN_CLIENT_ID
ATLASSIAN_CLIENT_SECRET
BITBUCKET_CLIENT_ID
BITBUCKET_CLIENT_SECRET
ENCRYPTION_KEY
```

Generate `ENCRYPTION_KEY` as a cryptographically random 32-byte key encoded in base64. Keep a secure backup; changing it without re-encrypting stored grants makes existing connections unusable. For the initial release, planned rotation means disconnecting existing clients, replacing the key and reconnecting them. Never paste secrets into issues, source files or build logs.

```powershell
npm ci
npm test
npm run typecheck
npx wrangler deploy --dry-run
# Deploy only after secrets/callbacks are configured and reviewed.
npx wrangler deploy
```

The Worker has rate limits of 120 requests/minute and 10 login starts/minute per client IP. Telemetry/logging for the service is disabled in Wrangler configuration. Avoid enabling HTTP request logging for OAuth callback URLs.

One Durable Object serializes token exchange/rotation across accounts, with encrypted per-account grant records. This deliberate initial limit can be replaced by account sharding when throughput requires it. Jira and Confluence never keep independent copies of a rotating refresh token. Metadata/search requests bypass the broker.

## Desktop contract

All token responses use `Cache-Control: no-store`. Connection keys travel in the Authorization header. Login poll keys are distinct from browser OAuth state.

| Request | Response |
| --- | --- |
| `POST /v1/login` with `provider` and `product` | Transaction ID, poll key, provider browser URL and expiry |
| `POST /v1/login/poll` with transaction ID/poll key | Pending, denied, or one-time connection credentials |
| `POST /v1/connections/refresh` with connection ID and bearer connection key | Access token and expiry |
| `POST /v1/connections/disconnect` with the same credentials | Deletes this connection, deleting the grant when no clients remain |

Providers are `atlassian` and `bitbucket`; products are `jira`, `confluence`, `bitbucket`. Poll credentials expire after 10 minutes. Unused grants expire after 90 days. The desktop refuses to send an existing connection key to a newly configured broker.

## Required real-account checks

Before testing against an organization's site, its site administrator must authorize the OAuth app. If the consent page says the site admin must authorize it, the user cannot complete consent until that approval is in place. The administrator can review the app in Atlassian Administration → Apps → the selected site → Connected apps. See [Atlassian's instructions](https://support.atlassian.com/atlassian-cloud/kb/your-site-admin-must-authorize-this-app-error-in-atlassian-cloud-apps/). Enable app sharing before testing with accounts other than the app owner.

Verify Bitbucket login/workspace discovery, Jira project/board discovery, and Confluence space/page search with the registered apps. Verify granting Confluence access after Jira and renewing either connection. Check cancellation, denied consent, restart, disconnect, offline cache and rate limiting. These checks cannot be substituted with a successful build or mocked token exchange.
