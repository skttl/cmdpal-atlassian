using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LinksForAtlassian;

public sealed class BrokerClient(ExtensionSettings settings, LocalState state, HttpClient http)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Uri ValidateBase(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Set an HTTPS login service URL in extension settings.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
    private async Task<JsonElement> Post(Uri root, string path, object body, string? key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(root, path));
        request.Content = new StringContent(JsonSerializer.Serialize(body, Json), System.Text.Encoding.UTF8, "application/json");
        if (key is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Login service returned {(int)response.StatusCode}. Reconnect if authorization has expired.", null, response.StatusCode);
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return data.RootElement.Clone();
    }
    public async Task ConnectAsync(Product product, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = ValidateBase(settings.BrokerUrl);
            await RetirePending(cancellationToken).ConfigureAwait(false);
            var provider = LocalState.Provider(product);
            var previous = state.GetConnection(product);
            var linked = previous?.BrokerUrl == root.AbsoluteUri ? previous : null;
            JsonElement start;
            try { start = await Post(root, "v1/login", new { provider, product = product.ToString().ToLowerInvariant(), connectionId = linked?.ConnectionId }, linked?.ConnectionKey, cancellationToken).ConfigureAwait(false); }
            catch (HttpRequestException ex) when (linked is not null && ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                start = await Post(root, "v1/login", new { provider, product = product.ToString().ToLowerInvariant() }, null, cancellationToken).ConfigureAwait(false);
            }
            var browser = new Uri(start.GetProperty("browserUrl").GetString()!);
            var allowed = provider == "bitbucket" ? "bitbucket.org" : "auth.atlassian.com";
            if (browser.Scheme != "https" || browser.Host != allowed || browser.UserInfo.Length != 0) throw new InvalidOperationException("Login service returned an invalid authorization URL.");
            Process.Start(new ProcessStartInfo(browser.AbsoluteUri) { UseShellExecute = true });
            var deadline = start.GetProperty("expiresAt").GetDateTimeOffset();
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
                var response = await Post(root, "v1/login/poll", new { provider, transactionId = start.GetProperty("transactionId").GetString(), pollKey = start.GetProperty("pollKey").GetString() }, null, cancellationToken).ConfigureAwait(false);
                var status = response.GetProperty("status").GetString();
                if (status == "pending") continue;
                if (status != "complete") throw new InvalidOperationException("Login was denied or expired. Try connecting again.");
                state.SaveConnection(new Connection(provider, response.GetProperty("accountId").GetString()!, response.GetProperty("connectionId").GetString()!, response.GetProperty("connectionKey").GetString()!, response.GetProperty("accessToken").GetString()!, response.GetProperty("expiresAt").GetDateTimeOffset(), root.AbsoluteUri), retirePrevious: true);
                await RetirePending(cancellationToken).ConfigureAwait(false);
                return;
            }
            throw new TimeoutException("Login expired. Try connecting again.");
        }
        finally { gate.Release(); }
    }
    public async Task<string> GetAccessTokenAsync(Product product, CancellationToken cancellationToken, bool forceRefresh = false)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = state.GetConnection(product) ?? throw new InvalidOperationException("Connect " + product + " first.");
            await RetirePending(cancellationToken).ConfigureAwait(false);
            if (connection.BrokerUrl != ValidateBase(settings.BrokerUrl).AbsoluteUri) throw new InvalidOperationException("Login service changed. Reconnect before continuing.");
            if (!forceRefresh && connection.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return connection.AccessToken;
            var response = await Post(ValidateBase(connection.BrokerUrl), "v1/connections/refresh", new { connectionId = connection.ConnectionId }, connection.ConnectionKey, cancellationToken).ConfigureAwait(false);
            connection = connection with { AccessToken = response.GetProperty("accessToken").GetString()!, ExpiresAt = response.GetProperty("expiresAt").GetDateTimeOffset() };
            state.SaveConnection(connection);
            return connection.AccessToken;
        }
        finally { gate.Release(); }
    }
    public async Task DisconnectAsync(Product product, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RetirePending(cancellationToken).ConfigureAwait(false);
            var connection = state.GetConnection(product);
            if (connection is not null)
                await Post(ValidateBase(connection.BrokerUrl), "v1/connections/disconnect", new { connectionId = connection.ConnectionId }, connection.ConnectionKey, cancellationToken).ConfigureAwait(false);
            state.Disconnect(product);
        }
        finally { gate.Release(); }
    }
    private async Task RetirePending(CancellationToken ct)
    {
        foreach (var old in state.GetRetiredConnections())
        {
            var current = state.GetConnection(old.Provider == "bitbucket" ? Product.Bitbucket : Product.Jira);
            if (current?.ConnectionId == old.ConnectionId && current.BrokerUrl == old.BrokerUrl) continue;
            try { await Post(ValidateBase(old.BrokerUrl), "v1/connections/disconnect", new { connectionId = old.ConnectionId }, old.ConnectionKey, ct).ConfigureAwait(false); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized) { /* Already retired. */ }
            catch (HttpRequestException) { continue; }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { continue; }
            state.RemoveRetiredConnection(old);
        }
    }
    public async Task<T> WithToken<T>(Product product, Func<string,Task<T>> operation, CancellationToken ct)
    {
        try { return await operation(await GetAccessTokenAsync(product, ct).ConfigureAwait(false)).ConfigureAwait(false); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return await operation(await GetAccessTokenAsync(product, ct, true).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }
}
