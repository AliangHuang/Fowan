using System.Text.Json;

namespace Fowan.Todo.Shared.Services;

public sealed class TodoStoragePaths
{
    public const string TodoDirectoryName = "Todo";
    public const string DataFileName = "todo-data.json";
    public const string SettingsFileName = "todo-settings.json";

    public string FowanRoot { get; }
    public string TodoRoot { get; }
    public string DataPath { get; }
    public string SettingsPath { get; }
    private IReadOnlyList<string> LegacyTodoRoots { get; }

    private TodoStoragePaths(
        string fowanRoot,
        string todoRoot,
        IReadOnlyList<string> legacyTodoRoots)
    {
        FowanRoot = fowanRoot;
        TodoRoot = todoRoot;
        DataPath = Path.Combine(TodoRoot, DataFileName);
        SettingsPath = Path.Combine(TodoRoot, SettingsFileName);
        LegacyTodoRoots = legacyTodoRoots;
    }

    public static TodoStoragePaths Resolve()
    {
        var fowanRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fowan"));
        var todoRoot = Path.Combine(fowanRoot, TodoDirectoryName);
        var legacyTodoRoots = ResolveLegacyTodoRoots(fowanRoot, todoRoot);

        return new TodoStoragePaths(
            fowanRoot,
            todoRoot,
            legacyTodoRoots);
    }

    public static TodoStoragePaths ForTodoRoot(string todoRoot)
    {
        var fullTodoRoot = Path.GetFullPath(todoRoot);
        var fowanRoot = Directory.GetParent(fullTodoRoot)?.FullName ?? fullTodoRoot;
        return new TodoStoragePaths(fowanRoot, fullTodoRoot, []);
    }

    public void EnsureReady()
    {
        Directory.CreateDirectory(FowanRoot);
        Directory.CreateDirectory(TodoRoot);
        MigrateLegacyFileIfMissing(DataFileName);
        MigrateLegacyFileIfMissing(SettingsFileName);
    }

    private void MigrateLegacyFileIfMissing(string fileName)
    {
        var targetPath = Path.Combine(TodoRoot, fileName);
        if (File.Exists(targetPath))
        {
            return;
        }

        foreach (var legacyRoot in LegacyTodoRoots)
        {
            var legacyPath = Path.Combine(legacyRoot, fileName);
            if (File.Exists(legacyPath))
            {
                File.Copy(legacyPath, targetPath, overwrite: false);
                return;
            }
        }
    }

    private static IReadOnlyList<string> ResolveLegacyTodoRoots(string fowanRoot, string todoRoot)
    {
        var legacyRoots = new List<string>();
        var oldRoot = Path.Combine(fowanRoot, string.Concat("Workspace", "s"));
        var selectedRoots = new List<string>();
        var fallbackRoots = new List<string>();
        var otherRoots = new List<string>();
        var clientSettingsPath = Path.Combine(fowanRoot, "client-settings.json");

        try
        {
            if (File.Exists(clientSettingsPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(clientSettingsPath));
                var root = document.RootElement;
                var selectedId = ReadStringProperty(root, "workspaceId") ?? "default";
                if (TryReadProperty(root, "workspaces", out var registrations) &&
                    registrations.ValueKind == JsonValueKind.Array)
                {
                    foreach (var registration in registrations.EnumerateArray())
                    {
                        var id = ReadStringProperty(registration, "id");
                        var path = ReadStringProperty(registration, "path");
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            continue;
                        }

                        if (string.Equals(id, selectedId, StringComparison.OrdinalIgnoreCase))
                        {
                            AddLegacyTodoRoot(selectedRoots, todoRoot, path);
                        }
                        else if (string.Equals(id, "default", StringComparison.OrdinalIgnoreCase))
                        {
                            AddLegacyTodoRoot(fallbackRoots, todoRoot, path);
                        }
                        else
                        {
                            AddLegacyTodoRoot(otherRoots, todoRoot, path);
                        }
                    }
                }
            }
        }
        catch
        {
            // Legacy migration is best-effort; fresh installs use the fixed Todo root.
        }

        legacyRoots.AddRange(selectedRoots);
        legacyRoots.AddRange(fallbackRoots);
        AddLegacyTodoRoot(legacyRoots, todoRoot, Path.Combine(oldRoot, "Default"));
        legacyRoots.AddRange(otherRoots);
        return legacyRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddLegacyTodoRoot(ICollection<string> roots, string todoRoot, string sourceRoot)
    {
        try
        {
            var legacyTodoRoot = Path.GetFullPath(Path.Combine(sourceRoot, TodoDirectoryName));
            if (!string.Equals(legacyTodoRoot, Path.GetFullPath(todoRoot), StringComparison.OrdinalIgnoreCase))
            {
                roots.Add(legacyTodoRoot);
            }
        }
        catch
        {
            // Ignore malformed legacy paths and continue with other candidates.
        }
    }

    private static string? ReadStringProperty(JsonElement element, string name)
    {
        return TryReadProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool TryReadProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
