using System.Globalization;

namespace DuoWall.Core.Widgets;

public sealed record CalendarMonthCell(
    DateTime Date,
    bool IsCurrentMonth,
    bool IsToday,
    bool IsWeekend,
    string? HolidayName);

/// <summary>
/// Generates Monday-first 6x7 calendar grids and detects weekends & public holidays.
/// </summary>
public static class CalendarHolidayHelper
{
    private static readonly Dictionary<(int Month, int Day), string> FixedPublicHolidays = new()
    {
        [(1, 1)] = "New Year's Day",
        [(2, 5)] = "Kashmir Solidarity Day",
        [(3, 23)] = "Pakistan Day",
        [(5, 1)] = "Labour Day",
        [(7, 4)] = "Independence Day (US)",
        [(8, 14)] = "Independence Day (PK)",
        [(9, 6)] = "Defence Day",
        [(11, 9)] = "Iqbal Day",
        [(12, 25)] = "Quaid-e-Azam / Christmas Day",
        [(12, 31)] = "New Year's Eve"
    };

    public static string? GetHolidayName(DateTime date, IReadOnlyDictionary<string, string>? customHolidays = null)
    {
        if (customHolidays != null && customHolidays.TryGetValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out var customName) && !string.IsNullOrWhiteSpace(customName))
            return customName;

        return FixedPublicHolidays.TryGetValue((date.Month, date.Day), out var holiday) ? holiday : null;
    }

    public static List<CalendarMonthCell> BuildMondayFirstMonthGrid(
        int year,
        int month,
        DateTime todayLocal,
        IReadOnlyDictionary<string, string>? customHolidays = null)
    {
        var firstOfMonth = new DateTime(year, month, 1);
        // Monday = 0, Tuesday = 1, ..., Sunday = 6
        int offsetFromMonday = ((int)firstOfMonth.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        DateTime gridStart = firstOfMonth.AddDays(-offsetFromMonday);

        var cells = new List<CalendarMonthCell>(42);
        for (int i = 0; i < 42; i++)
        {
            DateTime d = gridStart.AddDays(i);
            bool isWeekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            string? holiday = GetHolidayName(d, customHolidays);
            cells.Add(new CalendarMonthCell(
                Date: d,
                IsCurrentMonth: d.Month == month && d.Year == year,
                IsToday: d.Date == todayLocal.Date,
                IsWeekend: isWeekend,
                HolidayName: holiday));
        }

        return cells;
    }
}

