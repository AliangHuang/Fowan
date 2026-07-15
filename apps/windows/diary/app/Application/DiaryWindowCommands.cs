using Fowan.Diary.Shared.Application;
using Fowan.Diary.Shared.Models;

namespace Fowan.Diary.Windows.Application;

internal sealed class DiaryWindowCommands(DiaryWorkspace workspace)
{
    public DiarySaveResult PersistDiary(DiaryData candidate) => workspace.SaveData(candidate);

    public DiarySaveResult PersistPreferences(DiarySettings candidate) => workspace.SaveSettings(candidate);

    public void Reload() => workspace.Reload();
}
