using Fowan.Ai.Shared.Application;
using Fowan.Ai.Shared.Models;
using System.Globalization;
using Xunit;

namespace Fowan.Ai.Shared.Tests;

public sealed class ChatConversationGroupingTests
{
    [Theory]
    [InlineData(false, false, AiChatEmptyStateKind.ConfigurationRequired)]
    [InlineData(true, false, AiChatEmptyStateKind.ModelRequired)]
    [InlineData(true, true, AiChatEmptyStateKind.Welcome)]
    public void SelectsExactlyOneEmptyState(bool hasCredential, bool hasModel, AiChatEmptyStateKind expected)
    {
        Assert.Equal(expected, ChatConversationGrouping.EmptyState(hasCredential, hasModel));
    }

    [Fact]
    public void GroupsAndSortsConversationsByLocalUpdatedDate()
    {
        var groups = ChatConversationGrouping.Group(
            [
                Conversation("today-old", "2026-07-16T09:00:00+08:00"),
                Conversation("yesterday", "2026-07-15T15:00:00+08:00"),
                Conversation("today-new", "2026-07-16T14:00:00+08:00"),
                Conversation("older", "2026-07-12T12:00:00+08:00")
            ],
            new DateTimeOffset(2026, 7, 16, 16, 0, 0, TimeSpan.FromHours(8)),
            "今天",
            "昨天",
            CultureInfo.GetCultureInfo("zh-CN"));

        Assert.Collection(groups,
            today =>
            {
                Assert.Equal("今天", today.Label);
                Assert.Equal(["today-new", "today-old"], today.Select(item => item.Id));
            },
            yesterday => Assert.Equal("昨天", yesterday.Label),
            older => Assert.Equal("2026-07-12", older.Label));
    }

    private static AiConversationSummary Conversation(string id, string updatedAt) => new(id, id, updatedAt, updatedAt);
}
