using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace LinksForAtlassian;

public sealed class ExtensionSettings : JsonSettingsManager
{
    private readonly Dictionary<Product, TextSetting> keywords = [];
    private readonly Dictionary<Product, TextSetting> targets = [];
    private readonly Dictionary<Product, TextSetting> sites = [];
    private readonly TextSetting broker = new("broker", "Login service URL", "HTTPS address of the configured OAuth broker", "");

    public ExtensionSettings()
    {
        Directory.CreateDirectory(LocalState.DirectoryPath);
        FilePath = Path.Combine(LocalState.DirectoryPath, "settings.json");
        Settings.Add(broker);
        foreach (var product in Enum.GetValues<Product>())
        {
            keywords[product] = new TextSetting(product + ".keyword", product + " keyword", "One unique word", Search.Defaults[product]);
            targets[product] = new TextSetting(product + ".target", product == Product.Bitbucket ? "Workspace" : product + " cloud ID", "Selected after login; leave blank to choose", "");
            sites[product] = new TextSetting(product + ".site", product + " site", "Selected after login", product == Product.Bitbucket ? "https://bitbucket.org" : "");
            Settings.Add(keywords[product]); Settings.Add(targets[product]); Settings.Add(sites[product]);
        }
        LoadSettings();
        Settings.SettingsChanged += (_, _) => SaveSettings();
    }
    public string BrokerUrl => broker.Value ?? "";
    public IReadOnlyDictionary<Product, string> Keywords
    {
        get
        {
            var result = new Dictionary<Product, string>();
            foreach (var entry in keywords) result[entry.Key] = entry.Value.Value ?? "";
            return Search.ValidKeywords(result) ? result : Search.Defaults;
        }
    }
    public ApiContext? Context(Product product, Connection? connection)
    {
        if (connection is null || string.IsNullOrWhiteSpace(targets[product].Value) || !Uri.TryCreate(sites[product].Value, UriKind.Absolute, out var site)) return null;
        return new ApiContext(product, connection.AccountId, targets[product].Value!, site);
    }
    public void SelectTarget(Product product, string targetId, Uri site)
    {
        targets[product].Value = targetId;
        sites[product].Value = site.AbsoluteUri;
        SaveSettings();
    }
}
