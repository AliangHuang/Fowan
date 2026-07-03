using System.Text.Json;

namespace Fowan.Windows.Services;

public sealed class WorkspaceService
{
    public const string DefaultWorkspaceId = "default";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string WorkspaceRoot { get; } = Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Fowan",
        "Workspaces"));

    public bool EnsureInitialized(ClientSettings settings)
    {
        var changed = NormalizeSettings(settings);
        Directory.CreateDirectory(WorkspaceRoot);

        var defaultRegistration = settings.Workspaces.FirstOrDefault(workspace => workspace.Id == DefaultWorkspaceId);
        if (defaultRegistration is null)
        {
            defaultRegistration = new WorkspaceRegistration
            {
                Id = DefaultWorkspaceId,
                Name = "Default Workspace",
                Path = DefaultWorkspacePath()
            };
            settings.Workspaces.Insert(0, defaultRegistration);
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(defaultRegistration.Name))
        {
            defaultRegistration.Name = "Default Workspace";
            changed = true;
        }

        var defaultPath = DefaultWorkspacePath();
        if (string.IsNullOrWhiteSpace(defaultRegistration.Path) ||
            !string.Equals(Path.GetFullPath(defaultRegistration.Path), Path.GetFullPath(defaultPath), StringComparison.OrdinalIgnoreCase))
        {
            defaultRegistration.Path = defaultPath;
            changed = true;
        }

        foreach (var workspace in settings.Workspaces.ToList())
        {
            if (string.IsNullOrWhiteSpace(workspace.Id) ||
                string.IsNullOrWhiteSpace(workspace.Path))
            {
                settings.Workspaces.Remove(workspace);
                changed = true;
                continue;
            }

            var fullPath = Path.GetFullPath(workspace.Path);
            if (!string.Equals(workspace.Path, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                workspace.Path = fullPath;
                changed = true;
            }

            EnsureManifest(workspace);
        }

        if (settings.Workspaces.All(workspace => workspace.Id != settings.WorkspaceId))
        {
            settings.WorkspaceId = DefaultWorkspaceId;
            changed = true;
        }

        return changed;
    }

    public ClientSettings LoadEffectiveSettings(ClientSettings userSettings, bool ensureInitialized = true)
    {
        if (ensureInitialized)
        {
            EnsureInitialized(userSettings);
        }

        var effective = Clone(userSettings);
        var workspace = SelectedWorkspace(userSettings, ensureInitialized: false);
        var manifest = ReadManifest(workspace.Path);
        if (manifest is null)
        {
            return effective;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Theme))
        {
            effective.Theme = manifest.Theme;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Language))
        {
            effective.Language = manifest.Language;
        }

        if (manifest.IsSidebarCollapsed.HasValue)
        {
            effective.IsSidebarCollapsed = manifest.IsSidebarCollapsed.Value;
        }

        return effective;
    }

    public WorkspaceRegistration SelectedWorkspace(ClientSettings settings, bool ensureInitialized = true)
    {
        if (ensureInitialized)
        {
            EnsureInitialized(settings);
        }

        return settings.Workspaces.FirstOrDefault(workspace => workspace.Id == settings.WorkspaceId) ??
            settings.Workspaces.First(workspace => workspace.Id == DefaultWorkspaceId);
    }

    public WorkspaceRegistration CreateWorkspace(string name, string? existingDirectory)
    {
        var workspaceName = CleanWorkspaceName(name);
        var workspacePath = string.IsNullOrWhiteSpace(existingDirectory)
            ? UniqueWorkspacePath(workspaceName)
            : Path.GetFullPath(existingDirectory);

        Directory.CreateDirectory(workspacePath);

        var manifest = ReadManifest(workspacePath) ?? new WorkspaceManifest
        {
            CreatedAt = DateTimeOffset.Now
        };
        if (string.IsNullOrWhiteSpace(manifest.WorkspaceId))
        {
            manifest.WorkspaceId = NewWorkspaceId();
        }

        manifest.SchemaVersion = 1;
        manifest.Name = workspaceName;
        WriteManifest(workspacePath, manifest);

        return new WorkspaceRegistration
        {
            Id = manifest.WorkspaceId,
            Name = workspaceName,
            Path = workspacePath
        };
    }

    public string PreviewNewWorkspaceDisplayPath(string name)
    {
        return Path.Combine(WorkspaceRoot, SafeDirectoryName(name));
    }

    public void RegisterWorkspace(ClientSettings settings, WorkspaceRegistration registration)
    {
        EnsureInitialized(settings);
        registration.Path = Path.GetFullPath(registration.Path);
        settings.Workspaces.RemoveAll(workspace =>
            workspace.Id == registration.Id ||
            string.Equals(Path.GetFullPath(workspace.Path), registration.Path, StringComparison.OrdinalIgnoreCase));
        settings.Workspaces.Add(registration);
        settings.WorkspaceId = registration.Id;
        EnsureManifest(registration);
    }

    public WorkspaceManifest? ReadManifest(string workspacePath)
    {
        try
        {
            var path = ManifestPath(workspacePath);
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<WorkspaceManifest>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public string WorkspaceDisplayName(WorkspaceRegistration registration)
    {
        var manifestName = ReadManifest(registration.Path)?.Name;
        return string.IsNullOrWhiteSpace(manifestName) ? registration.Name : manifestName;
    }

    private void EnsureManifest(WorkspaceRegistration registration)
    {
        Directory.CreateDirectory(registration.Path);
        var manifest = ReadManifest(registration.Path) ?? new WorkspaceManifest
        {
            WorkspaceId = registration.Id,
            Name = registration.Name,
            CreatedAt = DateTimeOffset.Now
        };

        var changed = false;
        if (string.IsNullOrWhiteSpace(manifest.WorkspaceId))
        {
            manifest.WorkspaceId = registration.Id;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            manifest.Name = registration.Name;
            changed = true;
        }

        if (manifest.SchemaVersion <= 0)
        {
            manifest.SchemaVersion = 1;
            changed = true;
        }

        if (!File.Exists(ManifestPath(registration.Path)) || changed)
        {
            WriteManifest(registration.Path, manifest);
        }
    }

    private void WriteManifest(string workspacePath, WorkspaceManifest manifest)
    {
        var metadataDirectory = Path.Combine(workspacePath, ".fowan");
        Directory.CreateDirectory(metadataDirectory);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(ManifestPath(workspacePath), json);
    }

    private string DefaultWorkspacePath()
    {
        return Path.Combine(WorkspaceRoot, "Default");
    }

    private string UniqueWorkspacePath(string workspaceName)
    {
        Directory.CreateDirectory(WorkspaceRoot);
        var baseName = SafeDirectoryName(workspaceName);
        var candidate = Path.Combine(WorkspaceRoot, baseName);
        var index = 2;
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(WorkspaceRoot, $"{baseName}-{index}");
            index++;
        }

        return candidate;
    }

    private static string ManifestPath(string workspacePath)
    {
        return Path.Combine(workspacePath, ".fowan", "workspace.json");
    }

    private static string CleanWorkspaceName(string name)
    {
        var cleaned = name.Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Workspace" : cleaned;
    }

    private static string SafeDirectoryName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(CleanWorkspaceName(name)
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray())
            .Trim('.', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "Workspace" : safe;
    }

    private static string NewWorkspaceId()
    {
        return $"ws-{Guid.NewGuid():N}"[..15];
    }

    private static bool NormalizeSettings(ClientSettings settings)
    {
        var changed = false;
        if (settings.Workspaces is null)
        {
            settings.Workspaces = [];
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.Theme))
        {
            settings.Theme = "system";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.Language))
        {
            settings.Language = "system";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.WorkspaceId))
        {
            settings.WorkspaceId = DefaultWorkspaceId;
            changed = true;
        }

        return changed;
    }

    private static ClientSettings Clone(ClientSettings settings)
    {
        return new ClientSettings
        {
            Theme = settings.Theme,
            Language = settings.Language,
            IsSidebarCollapsed = settings.IsSidebarCollapsed,
            WorkspaceId = settings.WorkspaceId,
            Workspaces = settings.Workspaces
                .Select(workspace => new WorkspaceRegistration
                {
                    Id = workspace.Id,
                    Name = workspace.Name,
                    Path = workspace.Path
                })
                .ToList()
        };
    }
}
