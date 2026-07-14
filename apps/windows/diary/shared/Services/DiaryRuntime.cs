namespace Fowan.Diary.Shared.Services;

public static class DiaryRuntime
{
    public static DateTime Today
    {
        get
        {
            var overrideValue = Environment.GetEnvironmentVariable("FOWAN_DIARY_TODAY");
            return DateTime.TryParseExact(
                overrideValue,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var overridden)
                ? overridden.Date
                : DateTime.Today;
        }
    }
}
