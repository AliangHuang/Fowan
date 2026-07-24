using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace Fowan.Architecture.Tests;

public sealed class RepositoryArchitectureTests
{
    private static readonly Lazy<Task<Solution>> RepositorySolution = new(LoadSolutionAsync);

    [Fact]
    public async Task Production_sources_follow_semantic_architecture_rules()
    {
        var root = RepositoryRoot();
        var solution = await RepositorySolution.Value;
        var violations = new List<ArchitectureViolation>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            foreach (var tree in compilation!.SyntaxTrees.Where(tree => InProduction(root, tree.FilePath)))
                violations.AddRange(ArchitectureRules.Analyze(
                    compilation.GetSemanticModel(tree), Path.GetRelativePath(root, tree.FilePath)));
        }
        Assert.True(violations.Count == 0,
            "Semantic architecture violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Distinct()));
    }

    [Fact]
    public async Task Project_references_follow_layer_direction()
    {
        var solution = await RepositorySolution.Value;
        var violations = new List<string>();
        foreach (var project in solution.Projects)
        {
            var path = project.FilePath?.Replace('\\', '/') ?? string.Empty;
            if (!path.Contains("/Application/", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("/shared/", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var reference in project.ProjectReferences)
            {
                var target = solution.GetProject(reference.ProjectId)?.FilePath?.Replace('\\', '/') ?? string.Empty;
                if (target.Contains("/Presentation/", StringComparison.OrdinalIgnoreCase) ||
                    target.Contains("/Platform/Windows/", StringComparison.OrdinalIgnoreCase))
                    violations.Add($"{project.Name} -> {target}");
            }
        }
        Assert.Empty(violations);
    }

    [Fact]
    public async Task Manifest_state_owners_resolve_to_application_types()
    {
        var root = RepositoryRoot();
        var solution = await RepositorySolution.Value;
        var symbols = new List<INamedTypeSymbol>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is not null) CollectTypes(compilation.Assembly.GlobalNamespace, symbols);
        }
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "component-manifest.json")));
        foreach (var component in document.RootElement.GetProperty("components").EnumerateArray())
        {
            if (!component.TryGetProperty("stateOwner", out var ownerElement) || ownerElement.ValueKind == JsonValueKind.Null) continue;
            var owner = ownerElement.GetString();
            var matches = symbols.Where(type => type.Name == owner).ToList();
            Assert.True(matches.Count == 1, $"State owner '{owner}' must resolve to exactly one type; found {matches.Count}.");
            Assert.Contains(".Application", matches[0].ContainingNamespace.ToDisplayString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Semantic_fixtures_reject_partial_alias_fully_qualified_and_indirect_bypasses()
    {
        const string source = """
            namespace Microsoft.UI.Xaml { public class Window { } }
            namespace Demo.Models { public class TodoData { public string Title { get; set; } = ""; } }
            namespace Demo.Services { public class TodoStore { } }
            namespace Demo.Presentation {
              using Alias = Demo.Models.TodoData;
              public partial class BadWindow : Microsoft.UI.Xaml.Window { }
              public partial class BadWindow { private Alias _state = new(); }
              public class BadPresenter {
                private Alias _state = new();
                void Mutate(Demo.Models.TodoData value) { value.Title = "bad"; }
                void Run(Alias value) { _state.Title = "bad"; Mutate(_state); _ = new Demo.Services.TodoStore(); }
              }
            }
            """;
        var compilation = FixtureCompilation(source);
        var tree = compilation.SyntaxTrees.Single();
        var errors = compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
        var rules = ArchitectureRules.Analyze(compilation.GetSemanticModel(tree), "apps/windows/demo/Presentation/Bad.cs")
            .Select(item => item.Rule).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("window-state-owner", rules);
        Assert.Contains("presentation-domain-mutation", rules);
        Assert.Contains("presentation-indirect-mutation", rules);
        Assert.Contains("presentation-infrastructure", rules);
    }

    [Fact]
    public void Every_production_source_is_owned_by_a_manifest_component()
    {
        var root = RepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "component-manifest.json")));
        var owners = document.RootElement.GetProperty("components").EnumerateArray()
            .Select(component => Path.GetFullPath(Path.Combine(root, component.GetProperty("path").GetString()!))).ToList();
        var unowned = Directory.EnumerateFiles(Path.Combine(root, "apps", "windows"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !Generated(path))
            .Where(path => !owners.Any(owner => path.StartsWith(owner + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .Select(path => Path.GetRelativePath(root, path)).ToList();
        Assert.Empty(unowned);
    }

    private static CSharpCompilation FixtureCompilation(string source) => CSharpCompilation.Create(
        "ArchitectureFixture", [CSharpSyntaxTree.ParseText(source, path: "Bad.cs")],
        [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static async Task<Solution> LoadSolutionAsync()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            var sdkRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "sdk");
            var sdkPath = Directory.EnumerateDirectories(sdkRoot)
                .Where(path => File.Exists(Path.Combine(path, "MSBuild.dll")))
                .OrderByDescending(path => Version.TryParse(Path.GetFileName(path), out var version) ? version : new Version())
                .FirstOrDefault() ?? throw new InvalidOperationException($"No .NET SDK MSBuild found under {sdkRoot}.");
            MSBuildLocator.RegisterMSBuildPath(sdkPath);
        }
        using var workspace = MSBuildWorkspace.Create();
        workspace.LoadMetadataForReferencedProjects = false;
        workspace.WorkspaceFailed += (_, args) =>
        {
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                throw new InvalidOperationException(args.Diagnostic.Message);
        };
        return await workspace.OpenSolutionAsync(Path.Combine(RepositoryRoot(), "Fowan.sln"));
    }

    private static void CollectTypes(INamespaceSymbol ns, ICollection<INamedTypeSymbol> types)
    {
        foreach (var type in ns.GetTypeMembers()) types.Add(type);
        foreach (var child in ns.GetNamespaceMembers()) CollectTypes(child, types);
    }

    private static bool InProduction(string root, string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.GetFullPath(path).StartsWith(Path.Combine(root, "apps", "windows") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !Generated(path);

    private static bool Generated(string path) => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Fowan.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate Fowan repository root.");
    }
}
