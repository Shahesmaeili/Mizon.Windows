using Mizon.Windows.PaletteProvider;

internal static unsafe class PaletteSearchRelevanceTests
{
    private static int _checks;

    internal static int Run()
    {
        _checks = 0;
        var music = new PaletteResultData { Kind = PaletteResultKind.Music, PublicId = Guid.NewGuid(),
            Title = "After the Storm", PersianTitle = "بعد از طوفان", LikesCount = 25 };
        var english = PaletteSearchRelevance.Apply(music, "st");
        Check(english.RowTitle == "After the Storm", "English match becomes the native search row title");
        Check(english.RowSubtitle == "بعد از طوفان", "localized music title remains visible in the subtitle");
        Check(english.DisplayTitle == music.PersianTitle, "search ranking does not change preferred rich-preview title");
        var exact = PaletteSearchRelevance.Apply(music, "after the storm");
        var prefix = PaletteSearchRelevance.Apply(music, "after");
        var wholeWord = PaletteSearchRelevance.Apply(music, "storm");
        var contains = PaletteSearchRelevance.Apply(music, "torm");
        Check(exact.RelevanceScore == 1, "case-insensitive exact match receives normalized maximum relevance");
        Check(exact.RelevanceScore > prefix.RelevanceScore && prefix.RelevanceScore > wholeWord.RelevanceScore &&
            wholeWord.RelevanceScore > contains.RelevanceScore, "exact, prefix, word and substring matches have distinct truthful relevance");
        Check(PaletteSearchRelevance.Apply(music, "unrelated").RelevanceScore == 0, "unmatched text receives no relevance boost");
        var persian = PaletteSearchRelevance.Apply(music, "طوفان");
        Check(persian.RowTitle == music.PersianTitle && persian.RowSubtitle == music.Title, "Persian query keeps Persian main title and English subtitle");
        var empty = PaletteSearchRelevance.Apply(english, "   ");
        Check(empty.RelevanceScore is null && empty.MatchedTitle is null && empty.RowTitle == music.DisplayTitle,
            "clearing the query removes the search-only relevance hint");
        var client = new PaletteResultData { Kind = PaletteResultKind.Client, PublicId = Guid.NewGuid(),
            Username = "alish", FirstName = "علی", LastName = "کمالی", NickName = "نام نمایشی" };
        var username = PaletteSearchRelevance.Apply(client, "@ALISH");
        Check(username.RowTitle == "@alish" && username.RelevanceScore == 1, "username query uses the matching account identifier");
        Check(username.RowSubtitle == client.DisplayTitle, "username match preserves the display name without duplicating username");
        var name = PaletteSearchRelevance.Apply(client, "علي كمالي");
        Check(name.RowTitle == "علی کمالی" && name.RelevanceScore == 1, "Arabic and Persian yeh/kaf variants match the actual public name");
        var joined = music with { PersianTitle = "می‌خوانم" };
        Check(PaletteSearchRelevance.Apply(joined, "می خوانم").RelevanceScore == 1, "Persian half-space and whitespace normalize consistently");
        Check(PaletteSearchRelevance.Apply(music with { Title = "  After   the Storm  " }, "after the storm").RelevanceScore == 1,
            "repeated whitespace does not lose an exact title match");
        Check(new[] { english, exact, prefix, wholeWord, contains, persian, username, name }
            .All(item => item.RelevanceScore is >= 0 and <= 1), "all computed relevance hints stay within the host's supported range");
        Check(english.PublicId == music.PublicId && english.DefaultActionUri == music.DefaultActionUri,
            "query-aware presentation preserves music identity and activation");
        CheckNativeRow(english, persian);
        Console.WriteLine($"Search relevance PASS {_checks} assertions; hints do not guarantee Windows global ordering.");
        return _checks;
    }

    private static void CheckNativeRow(PaletteResultData english, PaletteResultData persian)
    {
        var baseline = NativeObject.LiveCount;
        nint factory = 0, view = 0;
        PaletteResultItem? item = null;
        try
        {
            factory = NativePaletteTestData.CreateItemFactory();
            item = PaletteData.BuildResult(factory, new PaletteResultState(english));
            view = item.AcquireView();
            Check(PaletteResultRenderingTests.ReadString(view, 10) == english.RowTitle,
                "actual native item exposes the English matching title");
            var score = ReadScore(view);
            Check(score.Type == 9, "native Score property is boxed Double rather than text or an integer");
            Check(Math.Abs(score.Value - english.RelevanceScore!.Value) < .0000001,
                "native property preserves the normalized relevance value");
            item.State.UpdateSearchSummary(persian with { LikesCount = null }, null);
            Check(PaletteResultRenderingTests.ReadString(view, 10) == persian.RowTitle,
                "reusing a result for a Persian query updates the existing native row");
            Check(Math.Abs(ReadScore(view).Value - persian.RelevanceScore!.Value) < .0000001,
                "reused native item receives the new query's score");
            Check(item.State.Result.LikesCount == 25 && item.State.Result.SearchQuery == "طوفان",
                "search-summary updates retain lazy details and refresh query metadata");
            item.State.UpdateSearchSummary(PaletteSearchRelevance.Apply(persian, null), null);
            Check(!HasScore(view), "cleared relevance removes the old native Score property");
        }
        finally
        {
            NativeMethods.Release(view);
            item?.Dispose();
            NativeMethods.Release(factory);
        }
        Check(NativeObject.LiveCount == baseline, "ranked result disposal releases its native callbacks and subscriptions");
    }

    private static (int Type, double Value) ReadScore(nint view)
    {
        nint properties = 0, name = 0, boxed = 0, value = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(view)[13])(view, &properties));
            name = NativeMethods.String("Score");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(properties)[6])(properties, name, &boxed));
            value = NativeMethods.As(boxed, new Guid("4bd682dd-7554-40e9-9a9b-82654ede7e62"));
            int type = 0; double score = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)NativeMethods.Table(value)[6])(value, &type));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, double*, int>)NativeMethods.Table(value)[16])(value, &score));
            return (type, score);
        }
        finally
        { NativeMethods.Release(value); NativeMethods.Release(boxed); NativeMethods.WindowsDeleteString(name); NativeMethods.Release(properties); }
    }

    private static bool HasScore(nint view)
    {
        nint properties = 0, name = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(view)[13])(view, &properties));
            name = NativeMethods.String("Score"); byte exists = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, byte*, int>)NativeMethods.Table(properties)[8])(properties, name, &exists));
            return exists != 0;
        }
        finally { NativeMethods.WindowsDeleteString(name); NativeMethods.Release(properties); }
    }

    private static void Check(bool value, string description)
    {
        if (!value) throw new InvalidOperationException(description);
        _checks++; Console.WriteLine("PASS " + description);
    }
}
