using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace LinksForAtlassian;

public sealed record Connection(string Provider, string AccountId, string ConnectionId, string ConnectionKey, string AccessToken, DateTimeOffset ExpiresAt, string BrokerUrl);
public sealed record CachedResources(DateTimeOffset UpdatedAt, IReadOnlyList<Resource> Items, Uri? Next = null);

public sealed class LocalState(string? directory = null)
{
    public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinksForAtlassian");
    private readonly string directory = directory ?? DirectoryPath;
    private readonly object gate = new();
    public static string Provider(Product product) => product == Product.Bitbucket ? "bitbucket" : "atlassian";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Connection? GetConnection(Product product)
    {
        lock (gate)
        {
            var path = Path.Combine(directory, Provider(product) + ".credentials");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Connection>(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser), Json);
        }
    }

    public void SaveConnection(Connection connection, bool retirePrevious = false)
    {
        lock (gate)
        {
            var previous = retirePrevious ? GetConnection(connection.Provider == "bitbucket" ? Product.Bitbucket : Product.Jira) : null;
            if (previous is not null && previous.ConnectionId != connection.ConnectionId)
                AtomicWrite(RetirementPath(previous), ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(previous, Json), null, DataProtectionScope.CurrentUser));
            AtomicWrite(Path.Combine(directory, connection.Provider + ".credentials"), ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(connection, Json), null, DataProtectionScope.CurrentUser));
        }
    }

    private string RetirementPath(Connection connection) => Path.Combine(directory, "retired-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(connection.BrokerUrl + "\n" + connection.ConnectionId))) + ".credentials");
    public IReadOnlyList<Connection> GetRetiredConnections()
    {
        lock (gate)
        {
            var result = new List<Connection>();
            if (Directory.Exists(directory)) foreach (var path in Directory.GetFiles(directory, "retired-*.credentials"))
                result.Add(JsonSerializer.Deserialize<Connection>(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser), Json)!);
            return result;
        }
    }
    public void RemoveRetiredConnection(Connection connection)
    {
        lock (gate) File.Delete(RetirementPath(connection));
    }

    public void Disconnect(Product product)
    {
        lock (gate)
        {
            File.Delete(Path.Combine(directory, Provider(product) + ".credentials"));
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.GetFiles(directory, Provider(product) + "-*.cache")) File.Delete(path);
        }
    }

    private string CachePath(ApiContext context, string query)
    {
        var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{context.Product}\n{context.AccountId}\n{context.TargetId}\n{context.SiteUrl}\n{query}")));
        return Path.Combine(directory, Provider(context.Product) + "-" + key + ".cache");
    }
    public CachedResources? GetCache(ApiContext context, string query)
    {
        lock (gate)
        {
            var path = CachePath(context, query);
            if (!File.Exists(path)) return null;
            try { return JsonSerializer.Deserialize<CachedResources>(File.ReadAllBytes(path), Json); }
            catch (JsonException) { return null; }
        }
    }
    public void SaveCache(ApiContext context, string query, IReadOnlyList<Resource> items, Uri? next = null)
    {
        lock (gate)
        {
            if (GetConnection(context.Product)?.AccountId != context.AccountId) return;
            AtomicWrite(CachePath(context, query), JsonSerializer.SerializeToUtf8Bytes(new CachedResources(DateTimeOffset.UtcNow, items, next), Json));
        }
    }
    private void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { File.Delete(temporary); }
    }
}
