using System.Globalization;

namespace Fowan.Diary.Shared.Services;

public sealed class DiaryTimelineStateController
{
    public const string RangeAll = "all";
    public const string RangeToday = "today";
    public const string RangeWeek = "week";
    public const string RangeMonth = "month";
    public const string RangeYear = "year";

    public DiaryTimelineStateController(DateTime today)
    {
        AnchorDate = today.Date;
        NavigatorMonth = new DateTime(today.Year, today.Month, 1);
    }

    public string RangeId { get; private set; } = RangeAll;
    public DateTime AnchorDate { get; private set; }
    public DateTime NavigatorMonth { get; private set; }
    public DateTime? DateFilter { get; private set; }
    public DateTime? PendingScrollDate { get; private set; }

    public void Initialize(string? range, string? anchor, string? selectedDate, string? navigatorMonth)
    {
        if (IsRange(range))
        {
            RangeId = range!;
        }
        if (TryParseDate(anchor, out var anchorDate))
        {
            AnchorDate = anchorDate;
            NavigatorMonth = MonthOf(anchorDate);
        }
        if (TryParseDate(selectedDate, out var selected))
        {
            DateFilter = selected;
            RangeId = RangeAll;
            NavigatorMonth = MonthOf(selected);
        }
        if (TryParseDate(navigatorMonth, out var navigator))
        {
            NavigatorMonth = MonthOf(navigator);
        }
    }

    public void SelectRange(string rangeId)
    {
        RangeId = IsRange(rangeId) ? rangeId : RangeAll;
        DateFilter = null;
        NavigatorMonth = MonthOf(AnchorDate);
    }

    public void MoveRange(int offset)
    {
        AnchorDate = RangeId switch
        {
            RangeToday => AnchorDate.AddDays(offset),
            RangeWeek => AnchorDate.AddDays(7 * offset),
            RangeMonth => AnchorDate.AddMonths(offset),
            RangeYear => AnchorDate.AddYears(offset),
            _ => AnchorDate
        };
        if (RangeId == RangeAll)
        {
            NavigatorMonth = NavigatorMonth.AddMonths(offset);
            return;
        }
        DateFilter = null;
        NavigatorMonth = MonthOf(AnchorDate);
    }

    public void MoveNavigatorMonth(int offset) => NavigatorMonth = NavigatorMonth.AddMonths(offset);

    public void ToggleDate(DateTime date)
    {
        date = date.Date;
        if (DateFilter == date)
        {
            DateFilter = null;
        }
        else
        {
            DateFilter = date;
            AnchorDate = date;
        }
        RangeId = RangeAll;
        NavigatorMonth = MonthOf(date);
    }

    public void NavigateToDate(DateTime month, DateTime date)
    {
        RangeId = RangeAll;
        DateFilter = null;
        AnchorDate = date.Date;
        NavigatorMonth = MonthOf(month);
        PendingScrollDate = date.Date;
    }

    public void ClearPendingScroll() => PendingScrollDate = null;

    public (DateTime? Start, DateTime? End) DateWindow()
    {
        if (DateFilter is { } date)
        {
            return (date.Date, date.Date);
        }
        var anchor = AnchorDate.Date;
        var weekStart = anchor.AddDays(-((int)anchor.DayOfWeek + 6) % 7);
        return RangeId switch
        {
            RangeToday => (anchor, anchor),
            RangeWeek => (weekStart, weekStart.AddDays(6)),
            RangeMonth => (MonthOf(anchor), MonthOf(anchor).AddMonths(1).AddDays(-1)),
            RangeYear => (new DateTime(anchor.Year, 1, 1), new DateTime(anchor.Year, 12, 31)),
            _ => (null, null)
        };
    }

    private static bool IsRange(string? value) =>
        value is RangeAll or RangeToday or RangeWeek or RangeMonth or RangeYear;

    private static DateTime MonthOf(DateTime date) => new(date.Year, date.Month, 1);

    private static bool TryParseDate(string? value, out DateTime date) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
