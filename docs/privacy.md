# Privacy and data handling

This describes the current implementation. Before publishing a hosted service, add the operator's identity, contact address and final service URLs, and verify deployment matches this document.

## On your Windows device

The extension stores settings, selected workspace/sites and cached repository/project/space metadata in `%LOCALAPPDATA%\LinksForAtlassian`. Access tokens and broker connection keys are protected with Windows DPAPI for the current Windows user. Metadata cache files are not encrypted.

Search input for repositories/projects is matched locally. Confluence title queries are sent directly to your authorized Confluence service. The extension requests no source code, issue bodies or page bodies. Atlassian can return additional metadata in its API responses; the extension retains only identifiers, names/keys and browser links.

## On the login service

The service handles OAuth codes, access tokens during login/renewal, account identity and refresh tokens. Refresh tokens and short-lived login results are encrypted at rest. It stores hashes of connection/poll credentials. IP addresses are used by Cloudflare's rate limiter.

Login transactions expire after 10 minutes and are removed by cleanup. Login results can be retrieved once. Refresh grants expire after 90 days without use. Metadata and search queries are not sent to or stored by the broker. No application analytics are implemented.

## Disconnect and revoke

Disconnect deletes the current device's connection from the broker and removes local credentials and the provider's caches. Jira and Confluence share that connection; disconnecting it affects both products. Other devices using the same account retain their connections. The shared encrypted grant is deleted when its last connection is removed.

Disconnect does not automatically revoke the OAuth app at Atlassian. Revoke app access in your Atlassian/Bitbucket account settings to remove the grant at the provider, including access from other devices. Atlassian and Cloudflare process requests under their own terms and privacy policies. Infrastructure logs/backups are controlled by the service operator and must be disclosed before public hosting.

Reconnection preserves superseded connection credentials encrypted with Windows DPAPI until broker deletion succeeds. Deletion is retried on later connection/token/disconnect operations; the broker also expires unused grants after 90 days.
