using System;
using System.Collections.Generic;
using System.Linq;

namespace LinksForAtlassian;

public enum Product { Bitbucket, Jira, Confluence }
public sealed record SearchRequest(Product Product, string Query);

public static class Search
{
    public static readonly IReadOnlyDictionary<Product, string> Defaults = new Dictionary<Product, string>
    {
        [Product.Bitbucket] = "bb", [Product.Jira] = "ji", [Product.Confluence] = "cf",
    };

    public static bool TryParse(string input, IReadOnlyDictionary<Product, string> keywords, out SearchRequest request)
    {
        request = null!;
        var text = input.TrimStart();
        foreach (var entry in keywords)
        {
            if (!text.StartsWith(entry.Value, StringComparison.OrdinalIgnoreCase)) continue;
            if (text.Length > entry.Value.Length && !char.IsWhiteSpace(text[entry.Value.Length])) continue;
            request = new SearchRequest(entry.Key, text[entry.Value.Length..].Trim());
            return true;
        }
        return false;
    }

    public static int Rank(string query, string name, string key)
    {
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase) || key.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 0;
        return name.Contains(query, StringComparison.OrdinalIgnoreCase) || key.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 : int.MaxValue;
    }
    public static bool ValidKeywords(IReadOnlyDictionary<Product, string> keywords) =>
        keywords.Values.All(k => !string.IsNullOrWhiteSpace(k) && !k.Any(char.IsWhiteSpace)) &&
        keywords.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == keywords.Count;
}
