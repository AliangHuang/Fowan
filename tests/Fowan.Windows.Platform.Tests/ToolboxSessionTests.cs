using Fowan.Windows.Application;
using Fowan.Windows.Services;
using Xunit;

namespace Fowan.Windows.Platform.Tests;

public sealed class ToolboxSessionTests
{
    [Fact]
    public void PinAndUpdateDecisionsArePersistedByTheSession()
    {
        var repository = new MemorySettingsRepository();
        var session = new ToolboxSession(repository);

        Assert.True(session.TogglePinned("todo"));
        Assert.Contains("todo", repository.Settings.PinnedToolIds);
        Assert.True(session.ShouldPromptForUpdate("1.2.3"));

        session.IgnoreUpdate("1.2.3");
        Assert.False(session.ShouldPromptForUpdate("1.2.3"));

        session.DisableUpdateChecks();
        Assert.False(repository.Settings.UpdateCheckEnabled);
        Assert.Equal(string.Empty, repository.Settings.IgnoredUpdateVersion);
        Assert.True(repository.SaveCount >= 3);
    }

    [Fact]
    public void FailedPersistenceDoesNotPublishOrReplaceThePreviousSnapshot()
    {
        var repository = new MemorySettingsRepository();
        var session = new ToolboxSession(repository);
        repository.FailSaves = true;
        var before = session.State;
        var published = 0;
        session.StateChanged += (_, _) => published++;

        Assert.Throws<IOException>(() => session.TogglePinned("todo"));

        Assert.Equal(before, session.State);
        Assert.Empty(session.State.PinnedToolIds);
        Assert.Equal(0, published);
    }

    [Fact]
    public void CapturesBelongToTheSessionAndSnapshotsRemainImmutable()
    {
        var session = new ToolboxSession(new MemorySettingsRepository());
        var before = session.State;

        session.AddCapture("first");

        Assert.Empty(before.Captures);
        Assert.Equal(["first"], session.State.Captures.ToArray());
    }

    private sealed class MemorySettingsRepository : IToolboxSettingsRepository
    {
        public ClientSettings Settings { get; private set; } = new();

        public bool FailSaves { get; set; }

        public int SaveCount { get; private set; }

        public ClientSettings Load() => Clone(Settings);

        public void Save(ClientSettings settings)
        {
            if (FailSaves) throw new IOException("synthetic save failure");
            Settings = Clone(settings);
            SaveCount++;
        }

        private static ClientSettings Clone(ClientSettings value) => new()
        {
            Theme = value.Theme,
            Language = value.Language,
            CloseBehavior = value.CloseBehavior,
            UserDisplayName = value.UserDisplayName,
            AvatarPath = value.AvatarPath,
            IsProfileInitialized = value.IsProfileInitialized,
            IsSidebarCollapsed = value.IsSidebarCollapsed,
            UpdateCheckEnabled = value.UpdateCheckEnabled,
            IgnoredUpdateVersion = value.IgnoredUpdateVersion,
            PinnedToolIds = [.. value.PinnedToolIds]
        };
    }
}
