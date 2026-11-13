using System.Text.RegularExpressions;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Tests.Shared;

namespace ERP.MES.Desktop.Tests;

// Source-level guards for two owner rules of 2026-11-13: no button label ends with an ellipsis (prose may), and the official application name is used everywhere it is shown.
public partial class UserInterfaceTextTests
{
    private static readonly string[] Ellipses = ["…", "...", "⋯"];

    private static string DesktopDirectory => Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Desktop");

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(DesktopDirectory, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static bool EndsWithEllipsis(string text) => Ellipses.Any(e => text.TrimEnd().EndsWith(e, StringComparison.Ordinal));

    [GeneratedRegex(@"<Button\b(?:[^<>]|\n)*?>", RegexOptions.Singleline)]
    private static partial Regex ButtonTag();

    [GeneratedRegex(@"\bContent=""([^""]*)""")]
    private static partial Regex ContentAttribute();

    [GeneratedRegex(@"<Button\b[^>]*[^/]>([^<]+)</Button>")]
    private static partial Regex ButtonInnerText();

    [GeneratedRegex(@"new\s+Button\s*\{[^}]*?Content\s*=\s*""([^""]*)""")]
    private static partial Regex CodeButton();

    [GeneratedRegex(@"(?:Button|Btn)\w*\.Content\s*=\s*""([^""]*)""")]
    private static partial Regex CodeButtonAssignment();

    [Fact]
    public void No_button_label_in_xaml_or_code_ends_with_an_ellipsis()
    {
        var offenders = new List<string>();
        foreach (var file in Files("*.xaml"))
        {
            var text = File.ReadAllText(file);
            foreach (Match tag in ButtonTag().Matches(text))
            {
                foreach (Match content in ContentAttribute().Matches(tag.Value))
                {
                    if (EndsWithEllipsis(content.Groups[1].Value))
                    {
                        offenders.Add(Path.GetFileName(file) + ": " + content.Groups[1].Value);
                    }
                }
            }

            offenders.AddRange(ButtonInnerText().Matches(text).Where(m => EndsWithEllipsis(m.Groups[1].Value)).Select(m => Path.GetFileName(file) + ": " + m.Groups[1].Value));
        }

        foreach (var file in Files("*.cs"))
        {
            var text = File.ReadAllText(file);
            offenders.AddRange(CodeButton().Matches(text).Concat(CodeButtonAssignment().Matches(text)).Where(m => EndsWithEllipsis(m.Groups[1].Value)).Select(m => Path.GetFileName(file) + ": " + m.Groups[1].Value));
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_scan_does_recognise_an_ellipsis_in_a_button_label()
    {
        Assert.True(ButtonTag().Matches("<Button Content=\"توقف…\" Padding=\"1\" />").Count == 1);
        Assert.True(EndsWithEllipsis("توقف…"));
        Assert.True(EndsWithEllipsis("انتخاب فایل..."));
        Assert.False(EndsWithEllipsis("توقف"));
    }

    [Fact]
    public void The_official_application_name_is_the_shown_name_and_the_old_title_is_gone()
    {
        Assert.Equal("برنامه جامع مدیریت و کنترل تولید ایران‌تول", AppInfo.Name);
        Assert.StartsWith(AppInfo.Name, AppInfo.DemoTitle, StringComparison.Ordinal);

        foreach (var file in Files("*.cs").Concat(Files("*.xaml")))
        {
            Assert.DoesNotContain("سامانه ERP/MES", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_offline_package_files_and_the_project_carry_the_official_name()
    {
        foreach (var relative in new[]
        {
            Path.Combine("tools", "offline-demo", "package", "DEMO-ACCOUNTS.txt"),
            Path.Combine("tools", "offline-demo", "package", "scripts", "Start-Demo.ps1"),
            Path.Combine("tools", "offline-demo", "package", "scripts", "Check-Demo.ps1"),
            Path.Combine("tools", "offline-demo", "Build-OfflineDemo.ps1"),
            Path.Combine("src", "ERP.MES.Desktop", "ERP.MES.Desktop.csproj"),
        })
        {
            Assert.Contains(AppInfo.Name, File.ReadAllText(Path.Combine(SolutionLayout.Root, relative)), StringComparison.Ordinal);
        }
    }
}
