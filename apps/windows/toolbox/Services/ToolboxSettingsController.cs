namespace Fowan.Windows.Services;

public interface IToolboxSettingsRepository
{
    ClientSettings Load();
    void Save(ClientSettings settings);
}

internal sealed class ToolboxSettingsController(IToolboxSettingsRepository repository)
{
    public static ToolboxSettingsController CreateDefault() => new(new SettingsStore());

    public ClientSettings Load() => repository.Load();

    public void Save(ClientSettings settings) => repository.Save(settings);

    public bool Normalize(ClientSettings settings) => SettingsStore.Normalize(settings);
}
