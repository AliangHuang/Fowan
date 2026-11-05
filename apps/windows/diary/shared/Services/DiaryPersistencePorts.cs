using Fowan.Diary.Shared.Models;

namespace Fowan.Diary.Shared.Services;

public interface IDiaryRepository
{
    DiaryData LoadData();
    void SaveData(DiaryData data);
    DiaryAttachment ImportAttachment(string entryId, string sourcePath);
    void DeleteAttachment(DiaryAttachment attachment);
    void DeleteAttachmentDirectory(string entryId);
    string ResolveAttachmentPath(string relativePath);
}

public interface IDiarySettingsRepository
{
    DiarySettings Load();
    void Save(DiarySettings settings);
}
