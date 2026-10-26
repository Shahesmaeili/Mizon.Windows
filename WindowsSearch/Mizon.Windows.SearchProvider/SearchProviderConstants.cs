using System;

namespace Mizon.Windows.SearchProvider;

internal static class SearchProviderConstants
{
    internal const string ProtocolName = "mizon";
    internal const string RootUrl = "mizon://localhost/music";
    internal const string SampleMusicUrl = "mizon://localhost/music/0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72";
    internal const string SampleMusicTitle = "Mizon Test Music";
    internal const string SampleMusicContent = "Mizon Test Music. A single hard-coded music item supplied by the Mizon Windows Search provider.";

    internal static readonly Guid ClassId = new("CC183E81-8D59-4EA1-8D35-DBF80CB424E7");
    internal static readonly Guid IUnknownId = new("00000000-0000-0000-C000-000000000046");
    internal static readonly Guid IClassFactoryId = new("00000001-0000-0000-C000-000000000046");
    internal static readonly Guid ISearchProtocolId = new("C73106BA-AC80-11D1-8DF3-00C04FB6EF4F");
    internal static readonly Guid IUrlAccessorId = new("0B63E318-9CCC-11D0-BCDB-00805FCCCE04");
    internal static readonly Guid IFilterId = new("89BCB740-6119-101A-BCB7-00DD010655AF");
}
