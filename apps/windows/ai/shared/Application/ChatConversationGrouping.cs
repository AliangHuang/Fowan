using Fowan.Ai.Shared.Models;
using System.Globalization;

namespace Fowan.Ai.Shared.Application;

public enum AiChatEmptyStateKind
{
    ConfigurationRequired,
    ModelRequired,
    Welcome
}

public sealed class AiConversationGroup(string label) : List<AiConversationSummary>
{
    public string Label { get; } = label;
}

public static class ChatConversationGrouping
{
    public static AiChatEmptyStateKind EmptyState(bool hasCredential, bool hasModel) =>
        !hasCredential ? AiChatEmptyStateKind.ConfigurationRequired :
        !hasModel ? AiChatEmptyStateKind.ModelRequired :
        AiChatEmptyStateKind.Welcome;

    public static IReadOnlyList<AiConversationGroup> Group(
        IEnumerable<AiConversationSummary> conversations,
        DateTimeOffset now,
        string todayLabel,
        string yesterdayLabel,
        CultureInfo culture)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var yesterday = today.AddDays(-1);

        return conversations
            .OrderByDescending(item => ParseUpdatedAt(item.UpdatedAt))
            .GroupBy(item => DateOnly.FromDateTime(ParseUpdatedAt(item.UpdatedAt).LocalDateTime))
            .OrderByDescending(group => group.Key)
            .Select(group => CreateGroup(group, today, yesterday, todayLabel, yesterdayLabel, culture))
            .ToArray();
    }

    private static AiConversationGroup CreateGroup(
        IGrouping<DateOnly, AiConversationSummary> group,
        DateOnly today,
        DateOnly yesterday,
        string todayLabel,
        string yesterdayLabel,
        CultureInfo culture)
    {
        var result = new AiConversationGroup(LabelFor(group.Key, today, yesterday, todayLabel, yesterdayLabel, culture));
        result.AddRange(group.OrderByDescending(item => ParseUpdatedAt(item.UpdatedAt)));
        return result;
    }

    private static string LabelFor(
        DateOnly date,
        DateOnly today,
        DateOnly yesterday,
        string todayLabel,
        string yesterdayLabel,
        CultureInfo culture) =>
        date == today ? todayLabel :
        date == yesterday ? yesterdayLabel :
        date.ToString("yyyy-MM-dd", culture);

    private static DateTimeOffset ParseUpdatedAt(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
}
