using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace LinksForAtlassian;

internal sealed partial class TargetPage(Product product, ExtensionSettings settings, BrokerClient broker, HttpClient http) : ListPage
{
    private IListItem[] items = [];
    private bool started;
    public override IListItem[] GetItems() { if (!started) { started = true; Title = "Choose workspace/site"; _ = Load(); } return items; }
    private async Task Load()
    {
        IsLoading = true;
        try
        {
            var token = await broker.GetAccessTokenAsync(product, CancellationToken.None).ConfigureAwait(false);
            string? uri = product == Product.Bitbucket ? "https://api.bitbucket.org/2.0/user/permissions/workspaces?pagelen=100" : "https://api.atlassian.com/oauth/token/accessible-resources";
            var list = new List<IListItem>();
            var seen = new HashSet<string>();
            while (uri is not null)
            {
                var address = new Uri(uri);
                if (!seen.Add(uri) || address.Scheme != "https" || address.UserInfo.Length != 0 || !address.IsDefaultPort || address.Host != (product == Product.Bitbucket ? "api.bitbucket.org" : "api.atlassian.com")) throw new InvalidOperationException("Unsafe workspace/site pagination link.");
                using var request = new HttpRequestMessage(HttpMethod.Get, address);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await http.SendAsync(request).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                var rows = product == Product.Bitbucket ? data.RootElement.GetProperty("values") : data.RootElement;
                foreach (var row in rows.EnumerateArray())
                {
                    var value = product == Product.Bitbucket ? row.GetProperty("workspace") : row;
                    if (product != Product.Bitbucket && !value.GetProperty("scopes").EnumerateArray().Any(s => s.GetString()!.Contains(product == Product.Jira ? "jira" : "confluence", StringComparison.Ordinal))) continue;
                    var id = value.GetProperty(product == Product.Bitbucket ? "slug" : "id").GetString()!;
                    var site = product == Product.Bitbucket ? new Uri("https://bitbucket.org") : new Uri(value.GetProperty("url").GetString()!);
                    list.Add(new ListItem(new AnonymousCommand(() => settings.SelectTarget(product, id, site)) { Name = "Select", Result = CommandResult.GoBack() }) { Title = value.GetProperty("name").GetString()!, Subtitle = site.AbsoluteUri });
                }
                uri = product == Product.Bitbucket && data.RootElement.TryGetProperty("next", out var n) ? n.GetString() : null;
            }
            items = list.Count == 0 ? [new ListItem(new NoOpCommand()) { Title = "No authorized workspace/site. Reconnect to grant access." }] : list.ToArray();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { items = [new ListItem(new NoOpCommand()) { Title = ex.Message }]; }
        finally { IsLoading = false; RaiseItemsChanged(); }
    }
}
