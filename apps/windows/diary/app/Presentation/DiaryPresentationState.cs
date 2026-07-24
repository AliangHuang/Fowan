using Fowan.Diary.Shared.Models;

namespace Fowan.Diary.Windows.Presentation;

internal sealed class DiaryPresentationState
{
    public string? SelectedEntryId { get; private set; }
    public string? DraftEntryId { get; private set; }

    public DiaryEntry? SelectedEntry(DiaryData data) => Find(data, SelectedEntryId);
    public DiaryEntry? DraftEntry(DiaryData data) => Find(data, DraftEntryId);

    public void Select(DiaryEntry? entry) => SelectedEntryId = entry?.Id;
    public void SetDraft(DiaryEntry? entry) => DraftEntryId = entry?.Id;

    public void Restore(DiaryData data)
    {
        var draft = data.Entries.Where(entry => entry.IsDraft)
            .OrderByDescending(entry => entry.UpdatedAt).FirstOrDefault();
        SetDraft(draft);
        Select(data.Entries.FirstOrDefault(entry => entry.IsFavorite)
            ?? data.Entries.Where(entry => !entry.IsDraft)
                .OrderByDescending(entry => entry.UpdatedAt).FirstOrDefault());
    }

    private static DiaryEntry? Find(DiaryData data, string? id) => id is null
        ? null
        : data.Entries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));
}
