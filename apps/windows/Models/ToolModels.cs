namespace Fowan.Windows.Models;

public enum ToolStatus
{
    Available,
    ComingSoon,
    Disabled,
    RequiresEngine,
    RequiresSignIn
}

public sealed record ToolCategory(string Id, string NameKey, string IconGlyph);

public sealed record ToolAction(string Id, string LabelKey, bool Enabled = true, string? DisabledReasonKey = null);

public sealed record ToolCard(
    string Id,
    string NameKey,
    string DescriptionKey,
    string IconGlyph,
    string CategoryId,
    ToolStatus Status,
    IReadOnlyList<string> RequiredCapabilities,
    ToolAction PrimaryAction,
    IReadOnlyList<ToolAction>? SecondaryActions = null);
