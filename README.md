# Links for Atlassian

Quick links to Bitbucket Cloud repositories, Jira Cloud projects and Confluence Cloud spaces and pages, from PowerToys Command Palette.

| Type | Find | Open |
| --- | --- | --- |
| `bb dans` | Repositories by name or slug | Overview, Source, Pull requests, Branches, Pipelines |
| `ji dans` | Projects by name or key | Overview, Issues, Boards and supported Backlogs |
| `cf dans` | Spaces and page titles | Space overview, space pages or a page in your browser |

Keywords can be changed in settings. Select a search command to open the results page; the query after the keyword is carried into that page. Real screenshots and a demo will be added after the installed UI has been verified.

## Development status

This is the initial implementation. Public OAuth hosting, real-account acceptance testing, package signing and Store publication are still being configured. There is no Store listing yet.

## Build

Requires Windows 11, PowerToys Command Palette, the SDK specified in `global.json`, Visual Studio with the Windows App SDK/WinUI build tools, and Windows SDK 10.0.26100. The extension starts from Microsoft's PowerToys **v0.100.2** template and CmdPal SDK **0.11.260520004**.

```powershell
dotnet run --project tests/SearchChecks
./scripts/build.ps1 -Architecture x64
./scripts/build.ps1 -Architecture ARM64
```

Packages appear under `artifacts/`. Without a configured signing certificate these are development build outputs; they are not ordinary double-click installers.

Broker checks require Node 22:

```powershell
cd broker
npm ci
npm test
npm run typecheck
```

## First connection

1. Install a development package using the instructions in [release.md](docs/release.md).
2. Open **Links for Atlassian settings** and enter your configured HTTPS login service URL.
3. Open Bitbucket, Jira or Confluence links and choose **Connect**.
4. Grant read access in your browser, return to Command Palette and choose a workspace or site.
5. Search and select the destination to open in your browser.

The published extension will use the publisher's configured login service. Users will not need their own OAuth apps or API tokens. The developer setup above applies while public hosting is being prepared.

Jira and Confluence use the same Atlassian OAuth app/account grant. Their selected sites are independent. Atlassian login requests read permissions for both products so connecting another device cannot narrow an existing account grant. Disconnecting that Atlassian connection removes both products' local tokens and caches. It does not disconnect Bitbucket.

## Troubleshooting

- **Site admin must authorize this app:** ask your organization's site administrator to approve Links for Atlassian for the selected site. A normal user cannot approve the app on behalf of the site. See [Atlassian's admin instructions](https://support.atlassian.com/atlassian-cloud/kb/your-site-admin-must-authorize-this-app-error-in-atlassian-cloud-apps/).
- **No workspace/site:** reconnect to grant access, then choose the target again.
- **Expired authorization:** use Reconnect / grant access.
- **Offline or throttled:** existing cached metadata remains visible. Use Refresh now after the displayed retry time.
- **Extension missing:** install/deploy the package, then use Command Palette's Reload extensions command. A successful build alone does not register the extension.

Repository/project metadata is refreshed after 15 minutes or on demand. Confluence page search uses the API after a short typing pause. Jira board IDs come from the API; Backlog is currently offered for Scrum boards.

## Documentation and contributions

- [OAuth and developer setup](docs/setup.md)
- [Privacy and data handling](docs/privacy.md)
- [Release, signing and Store/gallery publication](docs/release.md)
- [Issues](https://github.com/skttl/cmdpal-atlassian/issues)

Cloud services only. This extension does not modify repositories, run pipelines, edit issues or download source code/page bodies. MIT license. Independent project; not affiliated with or endorsed by Atlassian.
