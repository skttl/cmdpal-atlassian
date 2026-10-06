# Links for Atlassian implementation plan

> For agentic workers: use `superpowers:executing-plans` for execution in this session, or `superpowers:subagent-driven-development` if the user selects delegation. Track completion with the checkboxes below.

**Goal:** Build a public Command Palette extension for Bitbucket repositories, Jira projects and Confluence spaces/pages, with browser OAuth and repeatable distribution.

**Architecture:** One C# extension calls Atlassian APIs directly and uses the CmdPal toolkit for settings, navigation and browser commands. One Cloudflare Worker keeps OAuth secrets and encrypted refresh tokens. A static GitHub Pages site explains installation, use and data handling.

**Tech Stack:** Official CmdPal C# template, its compatible stable .NET/Windows SDK versions, TypeScript, Cloudflare Workers/Durable Objects, GitHub Actions and static HTML/CSS. Local tooling currently includes .NET 10.0.401 and Node 22.21.1.

**Spec:** [Approved design with subsequent agreed additions](2026-10-06-atlassian-command-palette-design.md). [Repository research](2026-10-06-cmdpal-extension-research.md). Copy both into the repository's `docs/` at execution time.

## Global constraints

- Product `Links for Atlassian`; repository `cmdpal-atlassian`; destination `C:\Workspaces\skttl\cmdpal-atlassian`.
- Keywords `bb`, `ji`, `cf`, configurable and matched as whole words.
- Cloud only; one selected workspace and one selected site per product. No customer-specific defaults.
- Read metadata only. Never fetch source code, issue bodies or Confluence page bodies.
- OAuth client secrets stay server-side. Access tokens and broker credentials use current-user DPAPI locally.
- Refresh tokens use encrypted Durable Object storage; serialize rotation and persist replacement before returning success.
- Repository/project metadata cache lifetime 15 minutes; manual refresh; preserve old data after failed refresh.
- English public documentation. Use an original icon and accurate screenshots. No unverified Store/WinGet links.
- No public deployment, account registration or submission is counted as complete without actual setup and verification.

## Review focus

- A user changes workspace/site while a request is running: old results must not enter the new context.
- Keywords overlap, contain whitespace or match the beginning of an ordinary word: reject invalid settings and avoid false activation.
- Pagination or returned links target another host: never forward credentials to an untrusted host.
- Jira and Confluence share an Atlassian OAuth grant: keep one rotating-token authority, including when scopes are expanded.
- The provider denies access or a refresh token is unusable: show an actionable error without deleting unrelated connections or presenting an empty success.

## File map

Retain the official template's COM registration, manifest, launch settings and publish profiles. Put the extension in `src/LinksForAtlassian/`. Use toolkit types rather than a new UI framework. Add files only as their task requires them:

- `Search.cs`: keyword parsing and result ranking, with no Windows dependency.
- `AtlassianApi.cs`: HTTP, pagination, metadata models and destination links.
- `LocalState.cs`: configuration context, cache and DPAPI credentials.
- `BrokerClient.cs`: browser login, polling, refresh and disconnect.
- `Pages/AtlassianPage.cs`: product results, nested links and connection actions.
- `LinksForAtlassianCommandsProvider.cs`: top-level and fallback commands.
- `broker/src/index.ts`, `oauth.ts`, `connection.ts`: routes, OAuth transaction handling and encrypted connection storage.
- `tests/SearchChecks/`: a small runnable C# check using shared pure source files.
- `broker/test/oauth.test.ts`: Worker checks using Cloudflare's supported local test runtime.
- `scripts/build.ps1`: reproducible desktop/package build shared by local development and Actions.
- `.github/workflows/{ci,release,pages}.yml`; `docs/{setup,privacy,release}.md`; `docs/site/{index,privacy}.html`.

## Task 1: Official template, build and keyword proof

**Files:** Template files under `src/LinksForAtlassian/`, root solution/build/package configuration, `global.json`, `.gitignore`, `Search.cs`, commands provider, `Pages/AtlassianPage.cs`, `tests/SearchChecks/Program.cs` and its project file.

**Interfaces:** `Product { Bitbucket, Jira, Confluence }`; `SearchRequest(Product Product, string Query)`; `Search.TryParse(string input, IReadOnlyDictionary<Product,string> keywords, out SearchRequest request)` returns bool. `Search.Rank(string query, string name, string key)` returns an integer, prefix matches before substring matches, unmatched last.

- [ ] Inspect inherited instructions, installed PowerToys version, Visual Studio/MSBuild and Windows SDK. Initialize the authorized directory and Git branch `codex/initial-extension` without modifying another project.
- [ ] Generate the official template using the installed CmdPal generator or the matching official template source. Inspect generation parameters before creating files. Keep compatible package versions; do not guess a template NuGet package or upgrade to preview packages.
- [ ] Add a runnable check asserting `bb dans` parses to Bitbucket/`dans`, `ji dans` to Jira, `cf dans` to Confluence, `jira` does not activate `ji`, matching ignores case and prefix rank precedes substring rank. Reject duplicate keywords and keywords containing whitespace. Run `dotnet run --project tests/SearchChecks`; first confirm a failing assertion before implementing the pure functions.
- [ ] Build the official project in Release/x64 using its supported MSBuild toolchain. Configure the stable SDK required by the generated project in `global.json`.
- [ ] Add the three search entry points with explicit development-only sample results and links. Implement SDK fallback handling and query handoff. Do not expose sample data in a release build.
- [ ] Install the development package and verify `bb dans`, `ji dans`, `cf dans` in Command Palette. Record whether Enter is required to open the result page; verify the query survives. Automated parsing checks alone do not prove this UI flow.
- [ ] Commit the buildable shell and checks. Resolve template/build problems before adding login dependencies.

## Task 2: API metadata and local state

**Files:** `AtlassianApi.cs`, `LocalState.cs`, `tests/SearchChecks/Program.cs` and linked pure URL/cache logic when needed.

**Interfaces:** `Destination(string Title, Uri Url)`; `Resource(string Id, string Name, string Key, Uri WebUrl)`; `ApiContext(Product Product, string AccountId, string TargetId, Uri SiteUrl)`; `AtlassianApi.GetResourcesAsync(ApiContext context, string accessToken, string query, CancellationToken cancellationToken)` returns `Task<IReadOnlyList<Resource>>`; `GetDestinationsAsync(ApiContext context, Resource resource, string accessToken, CancellationToken cancellationToken)` returns `Task<IReadOnlyList<Destination>>`.

- [ ] Read the current official API references for Bitbucket repositories/workspaces, Jira project search/Agile boards and Confluence spaces/title search. Record endpoints and the least read scopes in `docs/setup.md`. Confirm OAuth availability before choosing an endpoint.
- [ ] Implement HTTP with `HttpClient` and JSON with `System.Text.Json`. Follow pagination only on provider-authorized API hosts and paths. Build escaped browser URLs from validated site addresses and returned identifiers. Use returned Confluence web links when available.
- [ ] Add checks for names containing spaces/Unicode, escaped CQL quotes, rejected external pagination URLs and URL escaping. Use a small fake HTTP handler for pagination, 403, 429/Retry-After and cancellation. Run the checks and confirm all assertions pass.
- [ ] Implement cache keyed by account, product and target. Replace a cache only after a successful complete metadata refresh, using an atomic file replacement. Discard in-flight results when context changes. Test a site switch during a pending refresh and preservation after failure.
- [ ] Store access tokens and broker credentials in a separate DPAPI-protected file with no logging. Test protection round-trip on Windows. Use toolkit settings for nonsecret options. Commit this task.

## Task 3: OAuth broker

**Files:** `broker/package.json`, lockfile, TypeScript configuration, `wrangler.jsonc`, `src/index.ts`, `src/oauth.ts`, `src/connection.ts`, `test/oauth.test.ts`, `docs/setup.md`.

**Interfaces:** `POST /v1/login` accepts a known provider and selected products, returns `{ transactionId, pollKey, browserUrl, expiresAt }`; `POST /v1/login/poll` accepts transaction ID/poll key, returns pending or a one-time `{ connectionId, connectionKey, accessToken, expiresAt }`; fixed provider callback routes; `POST /v1/connections/refresh` and `/disconnect` authenticate a connection key in the Authorization header. Responses carrying tokens use `Cache-Control: no-store`.

- [ ] Define the C#/TypeScript contract together in `docs/setup.md`. Bound request lengths and allow only supported providers. Use cryptographically random state, poll keys and connection keys; transactions expire after 10 minutes. Never place bearer tokens in browser URLs.
- [ ] Add failing Worker checks for wrong state, expired transaction, denied consent, wrong poll key, repeated delivery, wrong connection key and concurrent refresh. Mock provider token exchanges, not the broker's own state machine.
- [ ] Implement official Bitbucket OAuth and Atlassian 3LO exchanges. Store secrets as Worker bindings. Encrypt refresh tokens with authenticated encryption and store key hashes. Apply endpoint rate limits and keep token-bearing responses/logs private.
- [ ] Establish account/grant identity from a documented provider endpoint. Consolidate Atlassian refresh state for Jira/Confluence using the same app/account grant, serialize scope expansion with refresh, and keep each selected site separate locally. Add a check covering login to Confluence after Jira and simultaneous renewal. Disconnect semantics must explicitly state when a shared grant disconnects both products.
- [ ] Run `npm ci`, the configured typecheck and `npm test` in `broker/`. Confirm token replacement persists before success. Provider failure or failed storage must not report successful renewal. Implement transaction cleanup and disconnect deletion.
- [ ] Document secrets, callbacks, encryption-key rotation procedure, rate-limit bindings and deployment commands. Validate configuration with a local dry-run. No production deployment in this task. Commit.

## Task 4: Connect real login and product navigation

**Files:** `BrokerClient.cs`, `LocalState.cs`, `Pages/AtlassianPage.cs`, commands provider, `docs/setup.md`.

**Interfaces:** `BrokerClient.ConnectAsync(Product product, CancellationToken cancellationToken)` returns `Task`; `GetAccessTokenAsync(Product product, CancellationToken cancellationToken)` returns `Task<string>`; `DisconnectAsync(Product product, CancellationToken cancellationToken)` returns `Task`. Broker contract comes from Task 3; API calls use Task 2.

- [ ] Implement system-browser login, bounded polling and cancellation. Save one-time credentials with DPAPI before reporting connection success. Discover and select workspace/site through APIs. A cancelled login must leave existing working credentials intact.
- [ ] Wire repository/project/space lists to real metadata. Repository menu includes Overview, Source, Pull requests, Branches and Pipelines. Discover Jira boards and expose Board/Backlog only where supported. Confluence title search uses a 250 ms debounce and cancellation; cache query results for 15 minutes and support pagination without fetching page bodies.
- [ ] Display connect, empty, permission, offline and throttled states. Refresh a rejected access token once, then report the error. Provide manual refresh and disconnect. Verify changing site while a request is running does not show old results.
- [ ] Run desktop build, C# checks and Worker checks. With registered apps and a development broker, manually verify both OAuth providers, all three products, restart, denied consent and disconnect. If credentials are unavailable, keep this acceptance check explicitly pending.
- [ ] Remove development-only sample paths from production results. Commit the connected flow.

## Task 5: Repeatable build and GitHub release

**Files:** `scripts/build.ps1`, `.github/workflows/ci.yml`, `.github/workflows/release.yml`, `docs/release.md`, package/publish configuration.

**Interfaces:** `scripts/build.ps1 -Architecture x64|ARM64 -Version <four-part-version> -OutputDirectory <path>` builds the extension and installer package; release tags use `vMAJOR.MINOR.PATCH` and map to manifest `MAJOR.MINOR.PATCH.0`.

- [ ] Build both architectures locally or on available Windows CI runners. Use the same script in CI and release. CI runs desktop checks and broker typecheck/tests with read-only repository permissions.
- [ ] Create tag-triggered release with version consistency checks, x64/ARM64 packages and SHA256 checksums attached to a GitHub Release. Pin actions to verified commit SHAs; grant `contents: write` only to the publishing job. A normal PR must not access signing secrets or publish.
- [ ] Separate Store upload packages from signed sideload packages. Use a persistent publisher identity and configured signing credentials; never create a new trust certificate on each release or attach private keys. Document missing external signing configuration plainly.
- [ ] Run actionlint on authored workflows, build-script checks and package inspection. Test installation/reinstallation on a clean Windows user and CmdPal discovery. Do not call an unsigned build a ready-to-install public release.
- [ ] Commit workflows and release documentation. Uploads to the public repository begin only once that repository and credentials are supplied/configured.

## Task 6: README, website and listing preparation

**Files:** `README.md`, `LICENSE`, `CHANGELOG.md`, `docs/setup.md`, `docs/privacy.md`, `docs/release.md`, `docs/site/index.html`, `docs/site/privacy.html`, `.github/workflows/pages.yml`, `docs/gallery/extension.json`, original icon and screenshots.

- [ ] Write an English README with description, actual demo, installation, first login, `bb`/`ji`/`cf`, requirements, troubleshooting and developer links. Add an MIT license unless the owner chooses another license before publication. Avoid claiming official Atlassian affiliation.
- [ ] Document actual local metadata, server token storage, retention, deletion and shared-grant disconnect behavior. Include service/operator contact details once the publisher supplies them; missing identity is a publication blocker, not placeholder public copy.
- [ ] Build two static HTML pages with accessible navigation and verified links. Pages deployment needs only read access, Pages write and OIDC permissions. It must not deploy the OAuth broker. Run actionlint and verify Pages works under a repository subpath.
- [ ] Prepare gallery metadata with display name `Links for Atlassian`, stable ID matching publisher/folder, original PNG icon within gallery limits and real screenshots. Obtain actual publisher and Store product IDs before final validation; do not invent identifiers.
- [ ] Verify all README/site install links against actual artifacts. Commit documentation and assets.

## Task 7: External setup and acceptance

**Files:** Manifest identity, broker deployment configuration, listing metadata and `docs/release.md` acceptance record.

- [ ] Publisher creates or provides access to a public GitHub repository, Cloudflare hosting and the two OAuth app registrations. Configure secrets without exposing them in the chat or logs. Start with a development deployment and run Task 4's real-login checks.
- [ ] Reserve Store product `Links for Atlassian`, use its exact package identity, and submit the tested MSIX bundle, descriptions, privacy URL and reviewer instructions. Store approval is an external outcome.
- [ ] Deploy public broker/Pages and publish a verified release only with concrete authorization and working configuration. Record tested versions and remaining limitations.
- [ ] After Store approval, submit the gallery PR with its product ID. Gallery inclusion requires maintainer review and catalog regeneration; do not claim inclusion when merely opening a PR.
- [ ] Final review covers secret exposure, OAuth state/rotation, customer-independent configuration, keyword UX, install/update behavior and all five review-focus checks. Re-run affected checks after any fix.

## Execution choice

Recommended: native execution in this session. The C# and broker contracts are tightly connected, and a small number of coherent commits is easier to verify than parallel implementation. Delegated execution is available if the user prefers independent task reviewers. Public services and Store/gallery submissions remain separate from local implementation acceptance.
