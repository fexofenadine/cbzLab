using System.Globalization;
using System.Text.RegularExpressions;

namespace cbzLab.ViewModels;

/// <summary>
/// Bridges a single localized date string to/from ComicInfo's separate
/// Year/Month/Day tags, using the current culture's own DateTime formatting.
/// Partial dates (bare year, or "MM/yyyy") are supported since ComicInfo
/// allows them; arbitrary-culture partial-date ordering is not attempted.
/// </summary>
public static class DateFieldHelper
{
    //empty if Year is empty - a month/day with no year isn't a meaningful partial date
    public static string FormatForDisplay(string year, string month, string day)
    {
        if (year.Length == 0)
            return "";

        if (int.TryParse(year, out var y) && int.TryParse(month, out var m) && int.TryParse(day, out var d))
        {
            try
            {
                return new DateTime(y, m, d).ToString("d", CultureInfo.CurrentCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                //out-of-range combination - fall through to looser renderings below
            }
        }

        if (int.TryParse(year, out _) && int.TryParse(month, out var mOnly) && mOnly is >= 1 and <= 12)
            return $"{mOnly:00}/{year}";

        return year;
    }

    //returns null (leave fields untouched) if input isn't a full date, bare year, or "MM/yyyy"
    public static (string Year, string Month, string Day)? Parse(string input)
    {
        input = input.Trim();
        if (input.Length == 0)
            return ("", "", "");

        //partial-date forms must be checked before DateTime.TryParse: it doesn't reject
        //"03/2019", it silently fills Day=1 and succeeds as a full date
        if (Regex.IsMatch(input, @"^\d{4}$"))
            return (input, "", "");

        var m = Regex.Match(input, @"^(\d{1,2})[/\-.](\d{4})$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var mm) && mm is >= 1 and <= 12)
            return (m.Groups[2].Value, mm.ToString(), "");

        //ISO-style year-first "2019-03" / "2019/03"
        m = Regex.Match(input, @"^(\d{4})[/\-.](\d{1,2})$");
        if (m.Success && int.TryParse(m.Groups[2].Value, out mm) && mm is >= 1 and <= 12)
            return (m.Groups[1].Value, mm.ToString(), "");

        //"March 2019", "Mar 2019", "Sept. 2019" - a month name and a year with no day
        m = Regex.Match(input, @"^([^\d\s,]+)\.?,?\s+(\d{4})$");
        if (m.Success && MonthFromName(m.Groups[1].Value) is { } named)
            return (m.Groups[2].Value, named.ToString(), "");

        //a full date has to spell out a four-digit year: DateTime.TryParse happily reads "15/03" as
        //the current year and "15/03/20" as 2020, and the date box parses as you type, so every
        //half-typed date would otherwise write a wrong year before you'd finished it
        if (!Regex.IsMatch(input, @"(?<!\d)\d{4}(?!\d)"))
            return null;

        if (DateTime.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.None, out var full))
            return (full.Year.ToString(), full.Month.ToString(), full.Day.ToString());

        return null;
    }

    //current culture's names first, then English, so "March 2019" works on any locale
    private static int? MonthFromName(string name)
    {
        name = name.TrimEnd('.');
        foreach (var culture in new[] { CultureInfo.CurrentCulture, CultureInfo.InvariantCulture })
        {
            var f = culture.DateTimeFormat;
            for (var i = 0; i < 12; i++)
            {
                if (string.Equals(f.MonthNames[i], name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(f.AbbreviatedMonthNames[i].TrimEnd('.'), name, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }
        }
        //"Sept" is a common abbreviation no culture table lists
        return name.Equals("sept", StringComparison.OrdinalIgnoreCase) ? 9 : null;
    }
}
