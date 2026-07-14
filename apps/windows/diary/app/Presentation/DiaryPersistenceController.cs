using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Services;

namespace Fowan.Diary.Windows.Presentation;

internal sealed class DiaryPersistenceController(
    IDiaryRepository store,
    IDiarySettingsRepository settingsStore)
{
    public static DiaryPersistenceController CreateDefault() => new(new DiaryStore(), new DiarySettingsStore());

    public DiaryData LoadData() => store.LoadData();

    public DiarySettings LoadSettings() => settingsStore.Load();

    public DiaryAttachment ImportAttachment(string entryId, string sourcePath) =>
        store.ImportAttachment(entryId, sourcePath);

    public void DeleteAttachment(DiaryAttachment attachment) => store.DeleteAttachment(attachment);

    public void DeleteAttachmentDirectory(string entryId) => store.DeleteAttachmentDirectory(entryId);

    public string ResolveAttachmentPath(string relativePath) => store.ResolveAttachmentPath(relativePath);

    public string CreateEntryId() => DiaryStore.NewId("entry");

    public string CreateNotebookId() => DiaryStore.NewId("notebook");

    public string DefaultNotebookName => DiaryStore.DefaultNotebookName;

    public DiarySaveResult SaveData(DiaryData data)
    {
        try
        {
            store.SaveData(data);
            return DiarySaveResult.Success;
        }
        catch (Exception exception)
        {
            return DiarySaveResult.Failure("diary_data_save_failed", exception.Message);
        }
    }

    public DiarySaveResult SaveSettings(DiarySettings settings)
    {
        try
        {
            settingsStore.Save(settings);
            return DiarySaveResult.Success;
        }
        catch (Exception exception)
        {
            return DiarySaveResult.Failure("diary_settings_save_failed", exception.Message);
        }
    }
}

internal sealed record DiarySaveResult(bool Succeeded, string? ErrorCode, string? ErrorMessage)
{
    public static DiarySaveResult Success { get; } = new(true, null, null);

    public static DiarySaveResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage);
}
