namespace Mizon.Windows.PaletteProvider;

// A search action is not a fabricated music/profile result. It carries only the
// user's query; opening Mizon retries the same search through the existing UI.
internal sealed record PaletteSearchFallback(string Query)
{
    internal const string Title = "جستجو در میزون";
    internal string Key => "search/" + Uri.EscapeDataString(Query);
    internal string LaunchUri => "mizon://search?query=" + Uri.EscapeDataString(Query);

    internal static PaletteSearchFallback? Create(string query)
    {
        query = query.Trim();
        return query.Length is >= 1 and <= 120 && !query.Any(char.IsControl) ? new(query) : null;
    }

    internal static PaletteSearchFallback? FromKey(string key)
    {
        if (!key.StartsWith("search/", StringComparison.Ordinal) || key.Length > 1500) return null;
        var item = Create(Uri.UnescapeDataString(key[7..]));
        return item?.Key == key ? item : null;
    }

    internal nint BuildView(nint factory, string? reason = null)
    {
        var subtitle = string.IsNullOrEmpty(reason) ? $"«{Query}» · Mizon" : $"«{Query}» · {reason}";
        var item = PaletteData.CreateMutableItem(factory, Key, Title, subtitle, LaunchUri);
        try
        {
            PalettePresentation.ConfigureSearchFallback(item);
            PaletteData.SetSubtitle(item, "جستجو", subtitle);
            return PaletteData.GetView(item);
        }
        finally { NativeMethods.Release(item); }
    }
}
