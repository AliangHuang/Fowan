using Fowan.Report.Windows.Platform.Windows;
using Fowan.Report.Shared;
using Xunit;

namespace Fowan.Report.Windows.Tests;

public sealed class ReportPreferenceStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Fowan.Report.Preferences.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveTextKeepsUserConfirmedBlockDocumentsAtomically()
    {
        var store = new ReportPreferenceStore(_root);
        var template = ReportTextDocuments.FromMarkdown("# 本周汇报\n\n| 事项 | 状态 |\n| --- | --- |\n| 编写 | 完成 |");
        var example = ReportTextDocuments.FromMarkdown("* 已完成事项");

        store.SaveText(template, example);
        var secondTemplate = ReportTextDocuments.FromMarkdown("# 第二版");
        var secondExample = ReportTextDocuments.FromMarkdown("* 第二版示例");
        store.SaveText(secondTemplate, secondExample);

        var loaded = store.LoadText();
        Assert.Equal("# 第二版", ReportTextDocuments.ToMarkdown(loaded.Template));
        Assert.Equal("- 第二版示例", ReportTextDocuments.ToMarkdown(loaded.Example));
        Assert.True(File.Exists(Path.Combine(_root, "text-template.json")));
        Assert.True(File.Exists(Path.Combine(_root, "text-example.json")));
        Assert.False(File.Exists(Path.Combine(_root, "text-template.json.tmp")));
        Assert.False(File.Exists(Path.Combine(_root, "text-example.json.tmp")));
    }

    [Fact]
    public void LegacyRtfIsNotLoadedOrDeleted()
    {
        Directory.CreateDirectory(_root);
        var legacy = Path.Combine(_root, "text-template.rtf");
        File.WriteAllText(legacy, @"{\rtf1\ansi legacy}");

        var loaded = new ReportPreferenceStore(_root).LoadText();

        Assert.Null(loaded.Template);
        Assert.Null(loaded.Example);
        Assert.True(File.Exists(legacy));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
