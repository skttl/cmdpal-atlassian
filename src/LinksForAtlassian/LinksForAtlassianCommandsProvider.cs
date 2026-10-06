// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Linq;
using System.Net.Http;

namespace LinksForAtlassian;

public partial class LinksForAtlassianCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallback;

    public LinksForAtlassianCommandsProvider()
    {
        DisplayName = "Links for Atlassian";
        Icon = new IconInfo("\uE71B");
        var settings = new ExtensionSettings();
        Settings = settings.Settings;
        var state = new LocalState();
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
        var broker = new BrokerClient(settings, state, http);
        var api = new AtlassianApi(http);
        var pages = Enum.GetValues<Product>().Select(p => new AtlassianPage(p, settings, state, broker, api, http)).ToArray();
        _commands = pages.Select(p => (ICommandItem)new CommandItem(p) { Title = p.Product + " links", Subtitle = "Search with " + settings.Keywords[p.Product] }).Append(new CommandItem(Settings.SettingsPage) { Title = "Links for Atlassian settings" }).ToArray();
        _fallback = pages.Select(p => (IFallbackCommandItem)new KeywordFallback(p, settings)).ToArray();
    }

    public override IFallbackCommandItem[] FallbackCommands() => _fallback;

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

}

internal sealed partial class KeywordFallback : FallbackCommandItem
{
    private readonly AtlassianPage page;
    private readonly ExtensionSettings settings;
    public KeywordFallback(AtlassianPage page, ExtensionSettings settings) : base(page, page.Product + " links", "LinksForAtlassian." + page.Product + ".fallback")
    { this.page = page; this.settings = settings; Title = ""; }
    public override void UpdateQuery(string query)
    {
        if (Search.TryParse(query, settings.Keywords, out var request) && request.Product == page.Product)
        { page.SearchText = request.Query; Title = "Search " + page.Product + ": " + request.Query; }
        else Title = "";
    }
}
