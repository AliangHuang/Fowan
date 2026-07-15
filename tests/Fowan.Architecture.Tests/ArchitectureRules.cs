using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Fowan.Architecture.Tests;

internal sealed record ArchitectureViolation(string Rule, string Path, int Line, string Detail)
{
    public override string ToString() => $"{Rule}: {Path}:{Line}: {Detail}";
}

internal static class ArchitectureRules
{
    private static readonly string[] MutatingNames =
    [
        "Add", "Clear", "Delete", "Insert", "Mutate", "Persist", "Remove", "Save", "Set", "Toggle", "Update"
    ];
    private static readonly HashSet<string> PlatformTypes = new(StringComparer.Ordinal)
    {
        "Process", "ProcessStartInfo", "FileOpenPicker", "FileSavePicker", "Clipboard"
    };
    private static readonly HashSet<string> PlatformPorts = new(StringComparer.Ordinal)
    {
        "IUiDispatcher", "IProcessLauncher", "IClipboardService", "IFileDialogService", "ITrayService",
        "IWindowHost", "IAiCoreProcessLauncher", "IAiApplicationLauncher", "IStickyProcessCoordinator",
        "IStickyMainProcessCoordinator"
    };

    public static IReadOnlyList<ArchitectureViolation> Analyze(
        SemanticModel model,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var path = relativePath.Replace('\\', '/');
        var root = model.SyntaxTree.GetRoot(cancellationToken);
        var violations = new List<ArchitectureViolation>();
        ValidateNamespace(path, root, violations);
        ValidateDependencies(path, model, root, violations, cancellationToken);
        ValidateOperations(path, model, root, violations, cancellationToken);
        ValidateWindows(path, model, root, violations, cancellationToken);
        ValidatePortLocations(path, model, root, violations, cancellationToken);
        return violations;
    }

    private static void ValidateNamespace(string path, SyntaxNode root, ICollection<ArchitectureViolation> violations)
    {
        var expected = path switch
        {
            var value when value.Contains("/Application/Ports/", StringComparison.Ordinal) => ".Application.Ports",
            var value when value.Contains("/Application/", StringComparison.Ordinal) => ".Application",
            var value when value.Contains("/Presentation/", StringComparison.Ordinal) => ".Presentation",
            var value when value.Contains("/Coordination/", StringComparison.Ordinal) => ".Coordination",
            var value when value.Contains("/Platform/Windows/", StringComparison.Ordinal) => ".Platform.Windows",
            _ => null
        };
        if (expected is null) return;
        var declaration = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        var actual = declaration?.Name.ToString() ?? string.Empty;
        if (!actual.Contains(expected, StringComparison.Ordinal) &&
            !(expected == ".Application.Ports" && actual.Contains(".AppPorts", StringComparison.Ordinal)))
        {
            Add(violations, "layer-namespace", path, declaration ?? root,
                $"Files in this directory must use a namespace containing '{expected}'.");
        }
    }

    private static void ValidateDependencies(
        string path, SemanticModel model, SyntaxNode root,
        ICollection<ArchitectureViolation> violations, CancellationToken cancellationToken)
    {
        var application = path.Contains("/Application/", StringComparison.Ordinal);
        var shared = path.Contains("/shared/", StringComparison.OrdinalIgnoreCase);
        var presentation = path.Contains("/Presentation/", StringComparison.Ordinal);
        if (!application && !presentation) return;

        foreach (var node in root.DescendantNodes().Where(node => node is TypeSyntax or IdentifierNameSyntax))
        {
            var symbol = model.GetSymbolInfo(node, cancellationToken).Symbol;
            var type = symbol switch
            {
                ITypeSymbol value => value,
                IMethodSymbol value => value.ContainingType,
                IPropertySymbol value => value.Type,
                IFieldSymbol value => value.Type,
                _ => null
            };
            if (type is null) continue;
            var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (application && (ns.Contains(".Presentation", StringComparison.Ordinal) ||
                                ns.Contains(".Platform.Windows", StringComparison.Ordinal) ||
                                ns.StartsWith("Microsoft.UI", StringComparison.Ordinal) ||
                                ns.StartsWith("System.Windows", StringComparison.Ordinal)))
            {
                Add(violations, "application-dependency", path, node,
                    $"Application code must not depend on '{type.ToDisplayString()}'.");
            }
            if (shared && (ns.StartsWith("Microsoft.UI", StringComparison.Ordinal) ||
                           ns.StartsWith("System.Windows", StringComparison.Ordinal)))
                Add(violations, "shared-ui-dependency", path, node,
                    $"Shared domain/application code must not depend on '{type.ToDisplayString()}'.");
            if (presentation && (ns.Contains(".Platform.Windows", StringComparison.Ordinal) || IsInfrastructure(type)))
            {
                Add(violations, "presentation-dependency", path, node,
                    $"Presentation code must use an application port instead of '{type.ToDisplayString()}'.");
            }
        }
    }

    private static void ValidateOperations(
        string path, SemanticModel model, SyntaxNode root,
        ICollection<ArchitectureViolation> violations, CancellationToken cancellationToken)
    {
        var presentation = path.Contains("/Presentation/", StringComparison.Ordinal);
        var windowFile = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Any(type => IsWindow(model.GetDeclaredSymbol(type, cancellationToken) as INamedTypeSymbol));
        foreach (var body in root.DescendantNodes().Where(node => node is MethodDeclarationSyntax or ConstructorDeclarationSyntax or AccessorDeclarationSyntax))
        {
            var operation = model.GetOperation(body, cancellationToken);
            if (operation is null) continue;
            foreach (var child in operation.DescendantsAndSelf())
            {
                if (child is ISimpleAssignmentOperation assignment && presentation && IsPersistentDomainTarget(assignment.Target))
                    Add(violations, "presentation-domain-mutation", path, assignment.Syntax,
                        $"Presentation must not assign persistent domain type '{assignment.Target.Type?.ToDisplayString()}'.");

                if (child is IInvocationOperation invocation)
                {
                    var methodName = invocation.TargetMethod.Name;
                    if ((presentation || windowFile) && methodName is "SaveData" or "SaveSettings")
                        Add(violations, presentation ? "presentation-command-bypass" : "window-command-bypass",
                            path, invocation.Syntax, $"Use a typed command instead of invoking '{methodName}'.");

                    if (presentation && Mutating(methodName) && invocation.Arguments.Any(arg => ContainsDomainField(arg.Value)))
                        Add(violations, "presentation-indirect-mutation", path, invocation.Syntax,
                            $"Presentation must not pass a domain object to mutating operation '{methodName}'.");
                }
                if (child is IObjectCreationOperation creation && presentation && IsInfrastructure(creation.Type))
                    Add(violations, "presentation-infrastructure", path, creation.Syntax,
                        $"Presentation must not construct '{creation.Type?.ToDisplayString()}'.");

                if (!PlatformAllowed(path) && child is IObjectCreationOperation platformCreation && PlatformTypes.Contains(platformCreation.Type?.Name ?? string.Empty))
                    Add(violations, "platform-boundary", path, platformCreation.Syntax,
                        $"Platform type '{platformCreation.Type?.ToDisplayString()}' must stay behind a Platform/Windows adapter.");
                if (!PlatformAllowed(path) && child is IInvocationOperation platformCall &&
                    (PlatformTypes.Contains(platformCall.TargetMethod.ContainingType.Name) ||
                     platformCall.TargetMethod.Name is "OpenExisting" or "Shell_NotifyIcon" or "TrackPopupMenu"))
                    Add(violations, "platform-boundary", path, platformCall.Syntax,
                        $"Platform call '{platformCall.TargetMethod.ToDisplayString()}' must stay behind a Platform/Windows adapter.");
            }
        }
    }

    private static void ValidatePortLocations(string path, SemanticModel model, SyntaxNode root,
        ICollection<ArchitectureViolation> violations, CancellationToken cancellationToken)
    {
        foreach (var declaration in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
        {
            var symbol = model.GetDeclaredSymbol(declaration, cancellationToken);
            if (symbol is null || !PlatformPorts.Contains(symbol.Name)) continue;
            if (!path.Contains("/platform/contracts/", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("/Application/Ports/", StringComparison.Ordinal))
                Add(violations, "platform-port-location", path, declaration,
                    $"Platform port '{symbol.Name}' must be declared in contracts or Application/Ports.");
        }
    }

    private static bool PlatformAllowed(string path) =>
        path.Contains("/Platform/Windows/", StringComparison.Ordinal) ||
        path.EndsWith("/App.xaml.cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("/Program.cs", StringComparison.OrdinalIgnoreCase);

    private static void ValidateWindows(
        string path, SemanticModel model, SyntaxNode root,
        ICollection<ArchitectureViolation> violations, CancellationToken cancellationToken)
    {
        foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var type = model.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;
            if (type is null) continue;
            var isWindow = IsWindow(type);
            // GetMembers merges every partial declaration; a split file cannot hide a prohibited field.
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsImplicitlyDeclared))
            {
                if (IsDomain(field.Type) && (isWindow || path.Contains("/Presentation/", StringComparison.Ordinal)))
                    AddSymbol(violations, isWindow ? "window-state-owner" : "presentation-domain-state", path, field,
                        $"UI field '{field.Name}' owns domain state '{field.Type.ToDisplayString()}'.");
                if (isWindow && IsInfrastructure(field.Type))
                    AddSymbol(violations, "window-infrastructure", path, field,
                        $"Window must use a registered Session/Workspace or port instead of '{field.Type.ToDisplayString()}'.");
            }
        }
    }

    private static bool Mutating(string name) => MutatingNames.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
    private static bool IsPersistentDomainTarget(IOperation target) => target switch
    {
        IPropertyReferenceOperation property => ContainsDomainField(property.Instance),
        IFieldReferenceOperation field => IsDomain(field.Field.Type),
        IArrayElementReferenceOperation array => ContainsDomainField(array.ArrayReference),
        _ => false
    };
    private static bool ContainsDomainField(IOperation? operation) => operation is not null &&
        operation.DescendantsAndSelf().OfType<IFieldReferenceOperation>().Any(field => IsDomain(field.Field.Type));
    private static bool IsDomain(ITypeSymbol? type)
    {
        if (type is null) return false;
        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return type.IsReferenceType && ns.Contains(".Models", StringComparison.Ordinal) &&
               !type.Name.EndsWith("Snapshot", StringComparison.Ordinal) &&
               !type.Name.EndsWith("Selection", StringComparison.Ordinal);
    }

    private static bool IsInfrastructure(ITypeSymbol? type)
    {
        if (type is null) return false;
        var name = type.Name;
        return name.EndsWith("Repository", StringComparison.Ordinal) ||
               name.EndsWith("Store", StringComparison.Ordinal) ||
               name is "AiCoreClient" or "AiCoreApi";
    }

    private static bool IsWindow(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.Name == "Window") return true;
        return false;
    }

    private static void Add(ICollection<ArchitectureViolation> violations, string rule, string path, SyntaxNode node, string detail)
    {
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        violations.Add(new(rule, path, line, detail));
    }

    private static void AddSymbol(ICollection<ArchitectureViolation> violations, string rule, string fallbackPath, ISymbol symbol, string detail)
    {
        var location = symbol.Locations.FirstOrDefault(value => value.IsInSource);
        var span = location?.GetLineSpan();
        violations.Add(new(rule, span?.Path ?? fallbackPath, (span?.StartLinePosition.Line ?? 0) + 1, detail));
    }
}
