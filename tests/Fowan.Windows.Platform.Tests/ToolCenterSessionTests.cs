using System.Globalization;
using Fowan.ToolCenter.Windows.Application;
using Fowan.ToolCenter.Windows.Platform.Windows;
using Fowan.Windows.Models;
using Xunit;

namespace Fowan.Windows.Platform.Tests;

public sealed class ToolCenterSessionTests
{
    [Fact]
    public void PlanningPlaceholdersAreAbsentFromBothCatalogs()
    {
        var session = ToolCenterCompositionRoot.CreateSession(null, CultureInfo.GetCultureInfo("zh-CN"));
        string[] migratedIds = ["notes", "knowledge", "files", "global-search", "workflows", "plugins"];
        Assert.Equal("yym", Assert.Single(session.Applications).Id);
        Assert.DoesNotContain(session.Applications, app => migratedIds.Contains(app.Id));
        Assert.Equal("yym", Assert.Single(session.Search(null)).Id);
        Assert.DoesNotContain(ToolCatalog.Tools, tool => migratedIds.Contains(tool.Id));
        Assert.DoesNotContain(ToolCatalog.Tools, tool => tool.Status == ToolStatus.ComingSoon);
        var center = Assert.Single(ToolCatalog.Tools, tool => tool.Id == "tool-center");
        Assert.Equal(ToolStatus.Available, center.Status);
        Assert.True(center.PrimaryAction.Enabled);
        Assert.Contains(ToolCatalog.Tools, tool => tool.Id == "quick-capture" && tool.Status == ToolStatus.Disabled);
    }

    [Fact]
    public void SearchCombinesLocalizedTextAndCategoryWithoutChangingTheCatalog()
    {
        ToolCenterApplicationSnapshot[] source =
        [
            new("ready-a", "可用工具", "整理本地内容", "\uE8FD", "organize", "整理", true),
            new("ready-b", "Available utility", "Process local files", "\uE8B7", "knowledge", "Knowledge", true),
            new("not-ready", "未开放工具", "Unavailable", "\uE8B7", "knowledge", "知识", false)
        ];
        var session = new ToolCenterSession(new(), new Dictionary<string, string>(), source);
        Assert.Equal("ready-a", Assert.Single(session.Search("可用", "organize")).Id);
        Assert.Equal("ready-b", Assert.Single(session.Search(" READY-B ", "knowledge")).Id);
        Assert.Equal("ready-b", Assert.Single(session.Search("local files", "knowledge")).Id);
        Assert.Empty(session.Search("可用", "knowledge"));
        Assert.Empty(session.Search("未开放"));
        Assert.Empty(session.Search("not-ready"));
        Assert.Empty(session.Search("no matching application"));
        Assert.Single(session.Search(" \t ", "knowledge"));
        Assert.Equal(2, session.Search(null).Length);
        Assert.All(session.Applications, app => Assert.True(app.IsAvailable));
        source[0] = source[0] with { IsAvailable = false };
        Assert.Equal(2, session.Applications.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"Theme\":7,\"Language\":false}")]
    [InlineData("{\"Theme\":\"unknown\",\"Language\":\"../invalid\"}")]
    public void InvalidPreferencesFallBackToSystemWithoutRequiringAWrite(string? json)
    {
        var preferences = ToolCenterCompositionRoot.ParsePreferences(json);
        Assert.Equal("system", preferences.Theme);
        Assert.Equal("system", preferences.Language);
    }

    [Fact]
    public void ExplicitLanguageAndThemeOverrideTheSystemAndResourcesAreBundled()
    {
        var session = ToolCenterCompositionRoot.CreateSession(
            "{\"theme\":\"dark\",\"language\":\"en-US\"}", CultureInfo.GetCultureInfo("zh-CN"));
        Assert.Equal("dark", session.Preferences.Theme);
        Assert.Equal("Tool Center", session.Text("Center_Title"));
        Assert.Equal("No tools available yet", session.Text("Center_EmptyTitle"));
        Assert.Equal("yym", Assert.Single(session.Applications).Id);
    }
}
