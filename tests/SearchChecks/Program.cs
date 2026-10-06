using LinksForAtlassian;

var failures = 0;
void Check(string name, Func<bool> assertion)
{
    try { if (!assertion()) throw new InvalidOperationException("Assertion failed"); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Check("Bitbucket query handoff", () => Search.TryParse("bb dash", Search.Defaults, out var r) && r == new SearchRequest(Product.Bitbucket, "dash"));
Check("Jira query handoff", () => Search.TryParse("ji dash", Search.Defaults, out var r) && r.Product == Product.Jira && r.Query == "dash");
Check("Confluence query handoff", () => Search.TryParse("cf dash", Search.Defaults, out var r) && r.Product == Product.Confluence);
Check("Whole word keyword", () => !Search.TryParse("jira", Search.Defaults, out _) && !Search.TryParse("bbdash", Search.Defaults, out _));
Check("Case and whitespace", () => Search.TryParse("  BB\tDashboard  ", Search.Defaults, out var r) && r.Query == "Dashboard");
Check("Empty query", () => Search.TryParse("ji", Search.Defaults, out var r) && r.Query == "");
Check("Prefix before substring", () => Search.Rank("dash", "Dashboard", "d") < Search.Rank("dash", "Other Dashboard", "o"));
Check("Match ignores case", () => Search.Rank("DASH", "Dashboard", "d") == 0);
Check("Unmatched excluded", () => Search.Rank("absent", "Dashboard", "d") == int.MaxValue);
Check("Default keywords valid", () => Search.ValidKeywords(Search.Defaults));
Check("Duplicate keywords rejected", () => !Search.ValidKeywords(new Dictionary<Product,string> { [Product.Jira] = "ji", [Product.Bitbucket] = "JI" }));
Check("Whitespace keywords rejected", () => !Search.ValidKeywords(new Dictionary<Product,string> { [Product.Jira] = "j i" }));
var context = new ApiContext(Product.Bitbucket, "account", "team", new Uri("https://bitbucket.org"));
Check("API accepts provider host", () => AtlassianApi.SafeApiUri(new Uri("https://api.bitbucket.org/2.0/repositories/team?page=2"), context));
Check("API rejects token forwarding", () => !AtlassianApi.SafeApiUri(new Uri("https://evil.example/2.0/repositories/team"), context));
Check("API rejects another workspace", () => !AtlassianApi.SafeApiUri(new Uri("https://api.bitbucket.org/2.0/repositories/other"), context));
Check("API rejects HTTP and credentials", () => !AtlassianApi.SafeApiUri(new Uri("http://api.bitbucket.org/2.0/repositories/team"), context) && !AtlassianApi.SafeApiUri(new Uri("https://user@api.bitbucket.org/2.0/repositories/team"), context));
Check("CQL escapes quote and slash", () => AtlassianApi.CqlText("a\"b\\c") == "\"a\\\"b\\\\c\"");
var requests = new List<Uri>();
using var http = new HttpClient(new FakeHandler(request =>
{
    requests.Add(request.RequestUri!);
    var page = requests.Count == 1
        ? """{"values":[{"uuid":"1","name":"Dashboard","slug":"dashboard"}],"next":"https://api.bitbucket.org/2.0/repositories/team?page=2"}"""
        : """{"values":[{"uuid":"2","name":"Other","slug":"other"}]}""";
    return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(page) };
}));
var resources = await new AtlassianApi(http).GetResourcesAsync(context, "test-token", "", CancellationToken.None);
Check("All repository pages retrieved", () => resources.Count == 2 && requests.Count == 2 && resources[0].WebUrl.AbsoluteUri == "https://bitbucket.org/team/dashboard/");
using var forbidden = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)));
try { await new AtlassianApi(forbidden).GetResourcesAsync(context, "test", "", CancellationToken.None); Check("Permission error propagated", () => false); }
catch (HttpRequestException ex) { Check("Permission error propagated", () => ex.StatusCode == System.Net.HttpStatusCode.Forbidden); }
var testDir = Path.Combine(Path.GetTempPath(), "links-atlassian-checks-" + Guid.NewGuid().ToString("N"));
var local = new LocalState(testDir);
var credential = new Connection("bitbucket", "account", "id", "key-private", "access-private", DateTimeOffset.UtcNow.AddHours(1), "https://login.example/");
local.SaveConnection(credential);
Check("Credentials DPAPI round-trip", () => local.GetConnection(Product.Bitbucket) == credential);
Check("Credentials not plaintext", () => !System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(testDir, "bitbucket.credentials"))).Contains("access-private", StringComparison.Ordinal));
var replacement = credential with { ConnectionId = "replacement" };
local.SaveConnection(replacement, retirePrevious: true);
Check("Replacement keeps encrypted retirement across restart", () => new LocalState(testDir).GetRetiredConnections().Single() == credential && local.GetConnection(Product.Bitbucket) == replacement);
local.RemoveRetiredConnection(credential);
Check("Successful retirement removes pending credential", () => local.GetRetiredConnections().Count == 0);
local.SaveCache(context, "metadata", resources);
Check("Cache round-trip", () => local.GetCache(context, "metadata")?.Items.Count == 2);
Check("Cache isolates account and target", () => local.GetCache(context with { TargetId = "another" }, "metadata") is null && local.GetCache(context with { AccountId = "another" }, "metadata") is null);
var cursor = new Uri("https://api.bitbucket.org/2.0/repositories/team?page=2");
local.SaveCache(context, "query", resources, cursor);
Check("Query cache retains pagination cursor", () => local.GetCache(context, "query")?.Next == cursor);
local.Disconnect(Product.Bitbucket);
Check("Disconnect removes credentials and cache", () => local.GetConnection(Product.Bitbucket) is null && local.GetCache(context, "metadata") is null);
local.SaveCache(context, "metadata", resources);
Check("Late refresh cannot recreate disconnected cache", () => local.GetCache(context, "metadata") is null);
foreach (var file in Directory.GetFiles(testDir)) File.Delete(file);
Directory.Delete(testDir);
Environment.ExitCode = failures == 0 ? 0 : 1;

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(reply(request));
    }
}
