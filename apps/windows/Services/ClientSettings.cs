namespace Fowan.Windows.Services;

public sealed class ClientSettings
{
    public string Theme { get; set; } = "system";
    public string Language { get; set; } = "system";
    public bool IsSidebarCollapsed { get; set; }
    public string WorkspaceId { get; set; } = "default";
    public List<WorkspaceRegistration> Workspaces { get; set; } = [];
}

public sealed class WorkspaceRegistration
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public sealed class WorkspaceManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string? Theme { get; set; }
    public string? Language { get; set; }
    public bool? IsSidebarCollapsed { get; set; }
}
