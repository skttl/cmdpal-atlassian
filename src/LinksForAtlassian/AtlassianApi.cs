using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LinksForAtlassian;

public sealed record ApiContext(Product Product, string AccountId, string TargetId, Uri SiteUrl);
public sealed record Resource(string Id, string Name, string Key, Uri WebUrl, string Kind = "resource");
public sealed record Destination(string Title, Uri Url);
public sealed record ResourcePage(IReadOnlyList<Resource> Items, Uri? Next);
public sealed record Board(string Id, string Name, bool HasBacklog);

public sealed class AtlassianApi(HttpClient http)
{
    public static string CqlText(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    private static string Escape(string text) => Uri.EscapeDataString(text);
    private static Uri Root(ApiContext c) => c.Product == Product.Bitbucket
        ? new Uri("https://api.bitbucket.org/2.0/")
        : new Uri($"https://api.atlassian.com/ex/{(c.Product == Product.Jira ? "jira" : "confluence")}/{Escape(c.TargetId)}/");

    public static bool SafeApiUri(Uri uri, ApiContext context)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        var root = Root(context);
        if (uri.Host != root.Host || !uri.AbsolutePath.StartsWith(root.AbsolutePath, StringComparison.Ordinal)) return false;
        if (context.Product == Product.Bitbucket)
        {
            var path = $"/2.0/repositories/{Escape(context.TargetId)}";
            return uri.AbsolutePath == path || uri.AbsolutePath.StartsWith(path + "/", StringComparison.Ordinal);
        }
        return true;
    }

    public static Uri SiteLink(ApiContext context, string path)
    {
        if (context.SiteUrl.Scheme != "https" || !context.SiteUrl.IsDefaultPort || context.SiteUrl.UserInfo.Length != 0) throw new InvalidOperationException("Site must be an HTTPS address.");
        var uri = new Uri(context.SiteUrl, path);
        if (uri.Host != context.SiteUrl.Host || uri.Scheme != "https") throw new InvalidOperationException("Unsafe site link.");
        return uri;
    }

    public async Task<JsonElement> GetJsonAsync(ApiContext context, Uri uri, string token, CancellationToken cancellationToken)
    {
        if (!SafeApiUri(uri, context)) throw new InvalidOperationException("Provider returned an unsafe API link.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(60);
            throw new RateLimitException(DateTimeOffset.UtcNow + (delay > TimeSpan.Zero ? delay : TimeSpan.Zero));
        }
        response.EnsureSuccessStatusCode();
        using var data = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        return data.RootElement.Clone();
    }

    public async Task<IReadOnlyList<Resource>> GetResourcesAsync(ApiContext context, string accessToken, string query, CancellationToken cancellationToken)
    {
        var result = new List<Resource>();
        Uri? next = context.Product switch
        {
            Product.Bitbucket => new Uri(Root(context), $"repositories/{Escape(context.TargetId)}?pagelen=100&fields=values.uuid,values.name,values.slug,next"),
            Product.Jira => new Uri(Root(context), "rest/api/3/project/search?maxResults=100&startAt=0"),
            _ => new Uri(Root(context), "wiki/api/v2/spaces?limit=100"),
        };
        var seen = new HashSet<Uri>();
        while (next is not null)
        {
            if (!seen.Add(next)) throw new InvalidOperationException("Provider repeated a pagination link.");
            var data = await GetJsonAsync(context, next, accessToken, cancellationToken).ConfigureAwait(false);
            var rows = data.GetProperty(context.Product == Product.Confluence ? "results" : "values");
            foreach (var item in rows.EnumerateArray())
            {
                var key = item.GetProperty(context.Product == Product.Bitbucket ? "slug" : "key").GetString()!;
                var id = item.GetProperty(context.Product == Product.Bitbucket ? "uuid" : "id").ToString();
                var path = context.Product switch
                {
                    Product.Bitbucket => $"/{Escape(context.TargetId)}/{Escape(key)}/",
                    Product.Jira => $"/plugins/servlet/project-config/{Escape(key)}/summary",
                    _ => $"/wiki/spaces/{Escape(key)}/overview",
                };
                result.Add(new Resource(id, item.GetProperty("name").GetString()!, key, SiteLink(context, path), context.Product == Product.Confluence ? "space" : "resource"));
            }
            next = Next(context, data);
            if (context.Product == Product.Jira && !data.GetProperty("isLast").GetBoolean())
                next = new Uri(Root(context), $"rest/api/3/project/search?maxResults=100&startAt={data.GetProperty("startAt").GetInt32() + rows.GetArrayLength()}");
        }
        return result;
    }

    private static Uri? Next(ApiContext context, JsonElement data)
    {
        string? next = null;
        if (data.TryGetProperty("next", out var n)) next = n.GetString();
        else if (data.TryGetProperty("_links", out var links) && links.TryGetProperty("next", out n)) next = n.GetString();
        if (string.IsNullOrEmpty(next)) return null;
        if (Uri.TryCreate(next, UriKind.Absolute, out var absolute) && absolute.Scheme == "https") return absolute;
        var path = next.TrimStart('/');
        if (context.Product == Product.Confluence && path.StartsWith("rest/", StringComparison.Ordinal)) path = "wiki/" + path;
        var root = Root(context);
        return next.StartsWith("/ex/", StringComparison.Ordinal) ? new Uri(root.GetLeftPart(UriPartial.Authority) + next) : new Uri(root, path);
    }

    public async Task<ResourcePage> GetPagesAsync(ApiContext context, string token, string query, string? spaceKey, Uri? next, CancellationToken cancellationToken)
    {
        var cql = "type=page";
        if (!string.IsNullOrWhiteSpace(query)) cql += " AND title ~ " + CqlText(query);
        if (spaceKey is not null) cql += " AND space = " + CqlText(spaceKey);
        var uri = next ?? new Uri(Root(context), "wiki/rest/api/content/search?limit=25&cql=" + Escape(cql));
        var data = await GetJsonAsync(context, uri, token, cancellationToken).ConfigureAwait(false);
        var items = data.GetProperty("results").EnumerateArray().Select(p => new Resource(p.GetProperty("id").GetString()!, p.GetProperty("title").GetString()!, "", SiteLink(context, "/wiki" + p.GetProperty("_links").GetProperty("webui").GetString()), "page")).ToArray();
        return new ResourcePage(items, Next(context, data));
    }

    public async Task<IReadOnlyList<Board>> GetBoardsAsync(ApiContext context, Resource project, string token, CancellationToken cancellationToken)
    {
        var result = new List<Board>();
        var offset = 0;
        while (true)
        {
            var data = await GetJsonAsync(context, new Uri(Root(context), $"rest/agile/1.0/board?projectKeyOrId={Escape(project.Id)}&startAt={offset}&maxResults=100"), token, cancellationToken).ConfigureAwait(false);
            var rows = data.GetProperty("values");
            foreach (var b in rows.EnumerateArray())
                result.Add(new Board(b.GetProperty("id").ToString(), b.GetProperty("name").GetString()!, b.GetProperty("type").GetString() == "scrum"));
            if (data.GetProperty("isLast").GetBoolean()) return result;
            if (rows.GetArrayLength() == 0) throw new InvalidOperationException("Provider returned incomplete board pagination.");
            offset += rows.GetArrayLength();
        }
    }

    public static IReadOnlyList<Destination> Destinations(ApiContext context, Resource resource)
    {
        if (context.Product == Product.Bitbucket)
            return new[] { ("Overview", "overview"), ("Source", "src"), ("Pull requests", "pull-requests"), ("Branches", "branches"), ("Pipelines", "pipelines") }.Select(p => new Destination(p.Item1, new Uri(resource.WebUrl, p.Item2))).ToArray();
        if (context.Product == Product.Jira)
            return [new("Overview", resource.WebUrl), new("Issues", SiteLink(context, "/issues/?jql=" + Escape("project = " + CqlText(resource.Key))))];
        return [new("Space overview", resource.WebUrl)];
    }
}

public sealed class RateLimitException(DateTimeOffset retryAt) : HttpRequestException("Provider rate limit reached. Try again after " + retryAt.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), null, HttpStatusCode.TooManyRequests)
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}
