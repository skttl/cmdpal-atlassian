using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace LinksForAtlassian;

internal sealed partial class AtlassianPage : DynamicListPage
{
    public Product Product { get; }
    private readonly ExtensionSettings settings;
    private readonly LocalState state;
    private readonly BrokerClient broker;
    private readonly AtlassianApi api;
    private readonly HttpClient http;
    private readonly string? space;
    private Resource[] resources = [];
    private CancellationTokenSource pending = new();
    private Uri? next;
    private string message = "";
    private bool started;
    private DateTimeOffset retryAt;
    private ApiContext? displayedContext;

    public AtlassianPage(Product product, ExtensionSettings settings, LocalState state, BrokerClient broker, AtlassianApi api, HttpClient http, string? space = null)
    {
        Product = product; this.settings = settings; this.state = state; this.broker = broker; this.api = api; this.http = http; this.space = space;
        Title = space is null ? product + " links" : "Pages in " + space;
        Name = "Open"; Id = "LinksForAtlassian." + product + "." + (space ?? "search");
    }
    private ListItem Action(string title, Func<Task> operation) => new(new AnonymousCommand(() => _ = Run(operation)) { Name = title, Result = CommandResult.KeepOpen() }) { Title = title };
    private async Task Run(Func<Task> operation)
    {
        try { await operation().ConfigureAwait(false); message = ""; Reload(true); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { message = ex.Message; RaiseItemsChanged(); }
    }
    public override IListItem[] GetItems()
    {
        if (!started) { started = true; Reload(); }
        var connection = state.GetConnection(Product);
        var context = settings.Context(Product, connection);
        if (context != displayedContext) { displayedContext = context; Reload(); }
        var items = new List<IListItem>();
        if (message.Length > 0) items.Add(new ListItem(new NoOpCommand()) { Title = message });
        if (connection is null) items.Add(Action("Connect " + Product, () => broker.ConnectAsync(Product, CancellationToken.None)));
        else
        {
            if (context is not null)
            {
                items.AddRange(resources.Where(r => r.Kind == "page" || Search.Rank(SearchText, r.Name, r.Key) < int.MaxValue).OrderBy(r => Search.Rank(SearchText, r.Name, r.Key)).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(r => r.Kind == "page" ? new ListItem(new OpenUrlCommand(r.WebUrl.AbsoluteUri)) { Title = r.Name, Subtitle = "Page" }
                        : new ListItem(new LinksPage(context, r, settings, state, broker, api, http)) { Title = r.Name, Subtitle = r.Key }));
                if (resources.Length == 0 && !IsLoading && message.Length == 0) items.Add(new ListItem(new NoOpCommand()) { Title = "No matches. Try another search or refresh." });
                items.Add(Action("Refresh now", () => { Reload(true); return Task.CompletedTask; }));
            }
            items.Add(new ListItem(new TargetPage(Product, settings, broker, http)) { Title = context is null ? "Choose workspace/site" : "Change workspace/site" });
            items.Add(Action("Reconnect / grant " + Product + " access", () => broker.ConnectAsync(Product, CancellationToken.None)));
            items.Add(Action(Product == Product.Bitbucket ? "Disconnect Bitbucket" : "Disconnect Jira and Confluence", () => broker.DisconnectAsync(Product, CancellationToken.None)));
        }
        items.Add(new ListItem(settings.Settings.SettingsPage) { Title = "Settings" });
        return items.ToArray();
    }
    public override void UpdateSearchText(string oldSearch, string newSearch) => Reload();
    public override void LoadMore() { if (next is not null && !IsLoading) Reload(false, true); }
    private void Reload(bool force = false, bool more = false)
    {
        pending.Cancel(); pending.Dispose(); pending = new CancellationTokenSource();
        _ = Load(force, more, pending.Token);
    }
    private async Task Load(bool force, bool more, CancellationToken ct)
    {
        var context = settings.Context(Product, state.GetConnection(Product));
        if (context is null) { resources = []; next = null; HasMoreItems = false; RaiseItemsChanged(); return; }
        var query = SearchText;
        IsLoading = true;
        try
        {
            var pages = Product == Product.Confluence && (query.Length > 0 || space is not null);
            var cacheKey = pages ? "pages:" + space + ":" + query : "metadata";
            if (!more)
            {
                var cached = state.GetCache(context, cacheKey);
                resources = cached?.Items.ToArray() ?? []; next = cached?.Next;
                RaiseItemsChanged();
                if (!force && cached is not null && cached.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-15)) return;
            }
            if (retryAt > DateTimeOffset.UtcNow) { message = "Rate limited. Retry after " + retryAt.ToLocalTime().ToString("T", System.Globalization.CultureInfo.CurrentCulture); return; }
            if (pages)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
                if (!more && space is null && state.GetCache(context, "metadata") is null)
                {
                    var metadata = await broker.WithToken(Product, t => api.GetResourcesAsync(context, t, "", ct), ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (settings.Context(Product, state.GetConnection(Product)) != context) return;
                    state.SaveCache(context, "metadata", metadata);
                }
                var result = await broker.WithToken(Product, t => api.GetPagesAsync(context, t, query, space, more ? next : null, ct), ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (settings.Context(Product, state.GetConnection(Product)) != context) return;
                var spaces = space is null ? state.GetCache(context, "metadata")?.Items ?? [] : [];
                resources = (more ? resources : spaces).Concat(result.Items).DistinctBy(r => (r.Kind, r.Id)).ToArray();
                next = result.Next;
            }
            else
            {
                var result = await broker.WithToken(Product, t => api.GetResourcesAsync(context, t, "", ct), ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (settings.Context(Product, state.GetConnection(Product)) != context) return;
                resources = result.ToArray(); next = null;
            }
            state.SaveCache(context, cacheKey, resources, next); message = "";
        }
        catch (OperationCanceledException) { }
        catch (RateLimitException ex) { retryAt = ex.RetryAt; message = ex.Message; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { if (!ct.IsCancellationRequested) message = ex.Message + (resources.Length > 0 ? " Showing cached results." : ""); }
        finally { if (!ct.IsCancellationRequested) { IsLoading = false; HasMoreItems = next is not null; RaiseItemsChanged(); } }
    }
}

internal sealed partial class LinksPage : ListPage
{
    private readonly IListItem[] items;
    public LinksPage(ApiContext context, Resource resource, ExtensionSettings settings, LocalState state, BrokerClient broker, AtlassianApi api, HttpClient http)
    {
        Title = resource.Name; Name = "Open";
        var list = AtlassianApi.Destinations(context, resource).Select(d => (IListItem)new ListItem(new OpenUrlCommand(d.Url.AbsoluteUri)) { Title = d.Title }).ToList();
        if (context.Product == Product.Jira) list.Add(new ListItem(new BoardsPage(context, resource, broker, api)) { Title = "Boards" });
        if (context.Product == Product.Confluence) list.Add(new ListItem(new AtlassianPage(Product.Confluence, settings, state, broker, api, http, resource.Key)) { Title = "Search pages" });
        items = list.ToArray();
    }
    public override IListItem[] GetItems() => items;
}

internal sealed partial class BoardsPage(ApiContext context, Resource project, BrokerClient broker, AtlassianApi api) : ListPage
{
    private IListItem[] items = [];
    private bool started;
    public override IListItem[] GetItems() { if (!started) { started = true; Title = "Boards for " + project.Name; _ = Load(); } return items; }
    private async Task Load()
    {
        IsLoading = true;
        try
        {
            var boards = await broker.WithToken(Product.Jira, t => api.GetBoardsAsync(context, project, t, CancellationToken.None), CancellationToken.None).ConfigureAwait(false);
            items = boards.Select(b => (IListItem)new ListItem(new BoardPage(context, b)) { Title = b.Name }).ToArray();
            if (items.Length == 0) items = [new ListItem(new NoOpCommand()) { Title = "No accessible boards" }];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { items = [new ListItem(new NoOpCommand()) { Title = ex.Message }]; }
        finally { IsLoading = false; RaiseItemsChanged(); }
    }
}
internal sealed partial class BoardPage : ListPage
{
    private readonly IListItem[] items;
    public BoardPage(ApiContext context, Board board)
    {
        Title = board.Name; Name = "Open";
        var root = "/secure/RapidBoard.jspa?rapidView=" + Uri.EscapeDataString(board.Id);
        var list = new List<IListItem> { new ListItem(new OpenUrlCommand(AtlassianApi.SiteLink(context, root).AbsoluteUri)) { Title = "Board" } };
        if (board.HasBacklog) list.Add(new ListItem(new OpenUrlCommand(AtlassianApi.SiteLink(context, root + "&view=planning").AbsoluteUri)) { Title = "Backlog" });
        items = list.ToArray();
    }
    public override IListItem[] GetItems() => items;
}
