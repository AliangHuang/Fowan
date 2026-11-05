using Fowan.Report.Shared;
using System.Text.Json;

namespace Fowan.Report.Windows.Platform.Windows;

/// <summary>
/// Stores generation metadata, output-file paths, and completed text-report documents.
/// Report input, Todo data, templates, examples, and custom requirements never cross
/// this adapter's boundary.
/// </summary>
internal sealed class ReportGenerationRecordStore : IReportGenerationRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _root;
    private string DataPath => Path.Combine(_root, "records.json");

    internal ReportGenerationRecordStore(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fowan", "Report", "Records");
    }

    public IReadOnlyList<ReportGenerationRecord> Load()
    {
        try
        {
            if (!File.Exists(DataPath)) return [];
            return JsonSerializer.Deserialize<List<ReportGenerationRecord>>(File.ReadAllText(DataPath), JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<ReportGenerationRecord> records)
    {
        Directory.CreateDirectory(_root);
        var temporaryPath = DataPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(records, JsonOptions));
        File.Move(temporaryPath, DataPath, overwrite: true);
    }
}
