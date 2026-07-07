namespace Fowan.Windows.Models;

public static class ToolCatalog
{
    private const string CurrentReleaseDate = "2026-07-07";

    public static IReadOnlyList<ToolCategory> Categories { get; } =
    [
        new("all", "Category_AllTools", "\uECA5"),
        new("capture", "Category_Capture", "\uE722"),
        new("organize", "Category_Organize", "\uE8B7"),
        new("knowledge", "Category_Knowledge", "\uE8D2"),
        new("automation", "Category_Automation", "\uE8F1"),
        new("system", "Category_System", "\uE713")
    ];

    public static IReadOnlyList<ToolCard> Tools { get; } =
    [
        Available("toolbox-home", "Tool_ToolboxHome", "Tool_ToolboxHome_Description", "\uE80F", "system", "Action_Open", ProductVersion, CurrentReleaseDate),
        Disabled("quick-capture", "Tool_QuickCapture", "Tool_QuickCapture_Description", "\uE722", "capture", ProductVersion, CurrentReleaseDate),
        Available("todo", "Tool_Todo", "Tool_Todo_Description", "\uE8FD", "organize", "Action_Open", ProductVersion, CurrentReleaseDate),
        Planned("notes", "Tool_Notes", "Tool_Notes_Description", "\uE70B", "organize"),
        Planned("knowledge", "Tool_Knowledge", "Tool_Knowledge_Description", "\uE8D2", "knowledge"),
        Planned("files", "Tool_Files", "Tool_Files_Description", "\uE8B7", "knowledge"),
        Planned("global-search", "Tool_GlobalSearch", "Tool_GlobalSearch_Description", "\uE721", "knowledge"),
        Planned("workflows", "Tool_Workflows", "Tool_Workflows_Description", "\uE8F1", "automation"),
        Planned("ai", "Tool_AI", "Tool_AI_Description", "\uE950", "automation"),
        Planned("plugins", "Tool_Plugins", "Tool_Plugins_Description", "\uECAA", "automation"),
        Available("settings", "Tool_Settings", "Tool_Settings_Description", "\uE713", "system", "Action_Open", ProductVersion, CurrentReleaseDate),
        Available("diagnostics", "Tool_Diagnostics", "Tool_Diagnostics_Description", "\uE9D9", "system", "Action_Open", ProductVersion, CurrentReleaseDate)
    ];

    private static string ProductVersion
    {
        get
        {
            var version = typeof(ToolCatalog).Assembly.GetName().Version;
            return version is null
                ? "0.1.1"
                : version.Revision > 0 ? version.ToString(4) : version.ToString(3);
        }
    }

    private static ToolCard Available(
        string id,
        string nameKey,
        string descriptionKey,
        string iconGlyph,
        string categoryId,
        string actionKey,
        string version,
        string updatedAt)
    {
        return new(
            id,
            nameKey,
            descriptionKey,
            iconGlyph,
            categoryId,
            ToolStatus.Available,
            ["app.health"],
            new ToolAction("open", actionKey),
            [new ToolAction("pin", "Action_Pin")],
            version,
            updatedAt);
    }

    private static ToolCard Disabled(
        string id,
        string nameKey,
        string descriptionKey,
        string iconGlyph,
        string categoryId,
        string version,
        string updatedAt)
    {
        return new(
            id,
            nameKey,
            descriptionKey,
            iconGlyph,
            categoryId,
            ToolStatus.Disabled,
            [],
            new ToolAction("disabled", "Action_Disabled", Enabled: false),
            Version: version,
            UpdatedAt: updatedAt);
    }

    private static ToolCard Planned(
        string id,
        string nameKey,
        string descriptionKey,
        string iconGlyph,
        string categoryId)
    {
        return new(
            id,
            nameKey,
            descriptionKey,
            iconGlyph,
            categoryId,
            ToolStatus.ComingSoon,
            [],
            new ToolAction("planned", "Action_Planned", Enabled: false, DisabledReasonKey: "Tool_PlannedReason"));
    }
}
