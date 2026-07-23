using System.Text.Json;
using Fowan.Report.Shared;
using Fowan.Report.Shared.Application.Ports;

namespace Fowan.Report.Windows.Platform.Windows;

/// <summary>Stores only user-confirmed template preferences, never Todo, report output, or AI request data.</summary>
internal sealed class ReportPreferenceStore : IReportPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fowan", "Report", "Preferences");
    private string MetadataPath => Path.Combine(_root, "preferences.json");
    private string TextTemplatePath => Path.Combine(_root, "text-template.json");
    private string TextExamplePath => Path.Combine(_root, "text-example.json");

    internal ReportPreferenceStore(string? root = null)
    {
        if (!string.IsNullOrWhiteSpace(root)) _root = root;
    }

    public ReportFilePreferences Load()
    {
        try
        {
            if (!File.Exists(MetadataPath)) return new(null, null);
            var value = JsonSerializer.Deserialize<ReportFilePreferences>(File.ReadAllText(MetadataPath), JsonOptions) ?? new(null, null);
            return new(Existing(value.TemplateFileName), Existing(value.ExampleFileName));
        }
        catch
        {
            return new(null, null);
        }
    }

    public void Save(string templatePath, string? examplePath)
    {
        Directory.CreateDirectory(_root);
        var templateName = CopyControlled(templatePath, "template");
        var exampleName = string.IsNullOrWhiteSpace(examplePath) ? null : CopyControlled(examplePath, "example");
        var metadata = JsonSerializer.Serialize(new ReportFilePreferences(templateName, exampleName), JsonOptions);
        var temporary = MetadataPath + ".tmp";
        File.WriteAllText(temporary, metadata);
        if (File.Exists(MetadataPath)) File.Replace(temporary, MetadataPath, null);
        else File.Move(temporary, MetadataPath);
    }

    public ReportTextPreferences LoadText()
    {
        try
        {
            return new(
                LoadDocument(TextTemplatePath),
                LoadDocument(TextExamplePath));
        }
        catch
        {
            return new(null, null);
        }
    }

    public void SaveText(ReportTextDocument template, ReportTextDocument example)
    {
        Directory.CreateDirectory(_root);
        WriteAtomically(TextTemplatePath, JsonSerializer.Serialize(ReportTextDocuments.Normalize(template), JsonOptions));
        WriteAtomically(TextExamplePath, JsonSerializer.Serialize(ReportTextDocuments.Normalize(example), JsonOptions));
    }

    private static ReportTextDocument? LoadDocument(string path)
    {
        if (!File.Exists(path)) return null;
        var document = JsonSerializer.Deserialize<ReportTextDocument>(File.ReadAllText(path), JsonOptions);
        return document is null ? null : ReportTextDocuments.Normalize(document);
    }

    private string? Existing(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name) return null;
        return File.Exists(Path.Combine(_root, name)) ? Path.Combine(_root, name) : null;
    }

    private string CopyControlled(string source, string stem)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not ".docx" and not ".xlsx") throw new InvalidOperationException("偏好仅支持 .docx 或 .xlsx 文件。");
        var name = stem + extension;
        var target = Path.Combine(_root, name);
        var temporary = target + ".tmp";
        File.Copy(source, temporary, overwrite: true);
        if (File.Exists(target)) File.Replace(temporary, target, null);
        else File.Move(temporary, target);
        return name;
    }

    private static void WriteAtomically(string target, string content)
    {
        var temporary = target + ".tmp";
        File.WriteAllText(temporary, content);
        if (File.Exists(target)) File.Replace(temporary, target, null);
        else File.Move(temporary, target);
    }
}
