using ERP.MES.Desktop.ViewModels;
using Xunit;

namespace ERP.MES.Desktop.Tests;

public sealed class StageTitleSearchTests
{
    private static readonly string[] Catalog = ["تراشکاری", "فرزکاری", "فرزکاری دقیق", "سنگ‌زنی"];

    [Fact]
    public void Typing_narrows_the_list_to_titles_containing_the_text()
    {
        Assert.Equal(["فرزکاری", "فرزکاری دقیق"], StageTitleSearch.Filter(Catalog, "فرز"));
        Assert.Equal(Catalog, StageTitleSearch.Filter(Catalog, "  "));
        Assert.Empty(StageTitleSearch.Filter(Catalog, "جوشکاری"));
    }

    [Fact]
    public void Enter_or_Tab_commit_an_exact_title_the_highlighted_one_or_the_single_match()
    {
        Assert.Equal("فرزکاری", StageTitleSearch.Resolve(Catalog, "فرزکاری"));
        Assert.Equal("فرزکاری دقیق", StageTitleSearch.Resolve(Catalog, "فرز", "فرزکاری دقیق"));
        Assert.Equal("سنگ‌زنی", StageTitleSearch.Resolve(Catalog, "سنگ"));
    }

    [Fact]
    public void An_arbitrary_ambiguous_or_empty_text_is_never_committed()
    {
        Assert.Null(StageTitleSearch.Resolve(Catalog, "جوشکاری"));
        Assert.Null(StageTitleSearch.Resolve(Catalog, "فرز"));
        Assert.Null(StageTitleSearch.Resolve(Catalog, ""));
        Assert.Null(StageTitleSearch.Resolve(Catalog, "x", "not in catalog"));
    }

    [Fact]
    public void A_retired_title_is_not_in_the_catalog_and_so_is_not_committable()
    {
        string[] active = ["تراشکاری"];
        Assert.Null(StageTitleSearch.Resolve(active, "فرزکاری"));
    }
}
