using Fowan.Windows.Models;
using Fowan.Windows.Services;

namespace Fowan.Windows.Presentation;

internal enum ToolViewMode
{
    Grid,
    List
}

internal enum ToolSortMode
{
    Name,
    Status,
    Category
}

internal sealed class ToolboxPresentationState
{
    public string SelectedCategoryId { get; set; } = "all";
    public string SearchText { get; set; } = string.Empty;
    public bool HasVisibleTools { get; set; } = true;
    public ToolViewMode ViewMode { get; set; } = ToolViewMode.Grid;
    public ToolSortMode SortMode { get; set; } = ToolSortMode.Name;
    public ToolCard SelectedTool { get; set; } = ToolCatalog.Tools.First(tool => tool.Id == "settings");
}
