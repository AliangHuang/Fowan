using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Services;

namespace Fowan.Diary.Shared.Application;

[Flags]
public enum DiaryChangeSet
{
    None = 0,
    Entries = 1,
    Metadata = 2,
    Attachments = 4,
    Settings = 8,
    All = Entries | Metadata | Attachments | Settings
}

public sealed record DiarySaveResult(bool Succeeded, string? ErrorCode, string? ErrorMessage)
{
    public static DiarySaveResult Success { get; } = new(true, null, null);
    public static DiarySaveResult Failure(string errorCode, string errorMessage) => new(false, errorCode, errorMessage);
}

public sealed class DiaryWorkspace : IDiaryCommands
{
    private readonly IDiaryRepository _repository;
    private readonly IDiarySettingsRepository _settingsRepository;
    private DiaryData _data;
    private DiarySettings _settings;

    public DiaryWorkspace(IDiaryRepository repository, IDiarySettingsRepository settingsRepository)
    {
        _repository = repository;
        _settingsRepository = settingsRepository;
        _data = Clone(repository.LoadData());
        _settings = Clone(settingsRepository.Load());
        State = DiarySnapshot.From(_data, _settings);
    }

    public static DiaryWorkspace CreateDefault() => new(new DiaryStore(), new DiarySettingsStore());

    public DiarySnapshot State { get; private set; }
    public string DefaultNotebookName => DiaryStore.DefaultNotebookName;
    public event EventHandler<DiaryChangeSet>? Changed;

    public DiaryData QueryData() => State.ToQueryData();
    public DiarySettings QuerySettings() => State.ToQuerySettings();

    public void Reload()
    {
        var data = Clone(_repository.LoadData());
        var settings = Clone(_settingsRepository.Load());
        Commit(data, settings, DiaryChangeSet.All);
    }

    public string CreateEntryId() => DiaryStore.NewId("entry");
    public string CreateNotebookId() => DiaryStore.NewId("notebook");
    public DiaryAttachment ImportAttachment(string entryId, string sourcePath) => _repository.ImportAttachment(entryId, sourcePath);
    public void DeleteAttachment(DiaryAttachment attachment) => _repository.DeleteAttachment(attachment);
    public void DeleteAttachmentDirectory(string entryId) => _repository.DeleteAttachmentDirectory(entryId);
    public string ResolveAttachmentPath(string relativePath) => _repository.ResolveAttachmentPath(relativePath);

    public DiarySaveResult SaveData(DiaryData candidate)
    {
        try
        {
            var next = Clone(candidate);
            _repository.SaveData(next);
            Commit(next, _settings, DiaryChangeSet.Entries | DiaryChangeSet.Metadata | DiaryChangeSet.Attachments);
            return DiarySaveResult.Success;
        }
        catch (Exception exception)
        {
            return DiarySaveResult.Failure("diary_data_save_failed", exception.Message);
        }
    }

    public DiarySaveResult SaveSettings(DiarySettings candidate)
    {
        try
        {
            var next = Clone(candidate);
            _settingsRepository.Save(next);
            Commit(_data, next, DiaryChangeSet.Settings);
            return DiarySaveResult.Success;
        }
        catch (Exception exception)
        {
            return DiarySaveResult.Failure("diary_settings_save_failed", exception.Message);
        }
    }

    public DiaryEntrySnapshot EnsureDraft(DiaryDraftInput input)
    {
        var existing = State.Entries.FirstOrDefault(entry => entry.IsDraft);
        if (existing is not null) return existing;

        var candidate = QueryData();
        var now = DateTimeOffset.Now;
        var draft = new DiaryEntry
        {
            Id = CreateEntryId(), Title = "未命名日记", NotebookId = input.NotebookId,
            Mood = input.Mood, Weather = input.Weather, Location = input.Location,
            IsDraft = true, CreatedAt = now, UpdatedAt = now
        };
        candidate.Entries.Insert(0, draft);
        var result = SaveData(candidate);
        if (!result.Succeeded) throw new InvalidOperationException(result.ErrorCode, new IOException(result.ErrorMessage));
        return State.Entries.First(entry => entry.Id == draft.Id);
    }

    public DiarySaveResult UpdateDraftText(string entryId, string body) => ChangeEntry(entryId, entry =>
    {
        entry.Body = body;
        entry.Title = DiaryText.InferTitle(body);
        entry.UpdatedAt = DateTimeOffset.Now;
    });

    public DiarySaveResult UpdateEntryMetadata(string entryId, string? mood = null, string? weather = null,
        DiaryWeatherDetails? weatherDetails = null, string? location = null, DiaryLocationDetails? locationDetails = null) =>
        ChangeEntry(entryId, entry =>
        {
            if (mood is not null) entry.Mood = mood;
            if (weather is not null) { entry.Weather = weather; entry.WeatherDetails = Clone(weatherDetails); }
            if (location is not null) { entry.Location = location; entry.LocationDetails = Clone(locationDetails); }
            entry.UpdatedAt = DateTimeOffset.Now;
        });

    public DiarySaveResult FinalizeDraft(string entryId)
    {
        var dataResult = ChangeEntry(entryId, entry =>
        {
            entry.Title = DiaryText.InferTitle(entry.Body);
            entry.IsDraft = false;
            entry.UpdatedAt = DateTimeOffset.Now;
        });
        return dataResult.Succeeded ? SetCurrentView(DiaryViewIds.Today) : dataResult;
    }

    public DiarySaveResult ToggleFavorite(string entryId) => ChangeEntry(entryId, entry =>
    {
        entry.IsFavorite = !entry.IsFavorite;
        entry.UpdatedAt = DateTimeOffset.Now;
    });

    public DiarySaveResult SetCurrentView(string viewId) => ChangeSettings(settings => settings.CurrentViewId = viewId);
    public DiarySaveResult SetTimelineNotebook(string notebookId) => ChangeSettings(settings => settings.TimelineNotebookId = notebookId);

    private DiarySaveResult ChangeEntry(string entryId, Action<DiaryEntry> change)
    {
        var candidate = QueryData();
        var entry = candidate.Entries.FirstOrDefault(value => string.Equals(value.Id, entryId, StringComparison.Ordinal));
        if (entry is null) return DiarySaveResult.Failure("diary_entry_not_found", "The diary entry does not exist.");
        change(entry);
        return SaveData(candidate);
    }

    private DiarySaveResult ChangeSettings(Action<DiarySettings> change)
    {
        var candidate = QuerySettings();
        change(candidate);
        return SaveSettings(candidate);
    }

    private void Commit(DiaryData data, DiarySettings settings, DiaryChangeSet changes)
    {
        _data = Clone(data);
        _settings = Clone(settings);
        State = DiarySnapshot.From(_data, _settings);
        Changed?.Invoke(this, changes);
    }

    private static DiaryData Clone(DiaryData value) => DiarySnapshot.From(value, new DiarySettings()).ToQueryData();
    private static DiarySettings Clone(DiarySettings value) => DiarySnapshot.From(new DiaryData(), value).ToQuerySettings();
    private static DiaryLocationDetails? Clone(DiaryLocationDetails? value) => value is null ? null : new()
    {
        Source = value.Source, Latitude = value.Latitude, Longitude = value.Longitude, ResolvedAt = value.ResolvedAt
    };
    private static DiaryWeatherDetails? Clone(DiaryWeatherDetails? value) => value is null ? null : new()
    {
        Source = value.Source, TemperatureCelsius = value.TemperatureCelsius, WeatherCode = value.WeatherCode,
        Latitude = value.Latitude, Longitude = value.Longitude, FetchedAt = value.FetchedAt
    };
}
