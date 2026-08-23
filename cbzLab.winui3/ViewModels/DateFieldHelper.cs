using System.Globalization;
using System.Text.RegularExpressions;

namespace cbzLab.ViewModels;

/// <summary>
/// Bridges a single localized date string to/from ComicInfo's separate Year/
/// Month/Day tags, via the current culture's own DateTime formatting. Also
/// accepts partial dates ComicInfo allows: a bare year, or "MM/yyyy" as one
/// fixed convention for year+month (deliberately not culture-derived — too
/// rare a case to justify the ambiguity).
/// </summary>
public static class DateFieldHelper
{
    //empty if Year is empty — a month/day with no year isn't a meaningful partial date
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
                //bad data from elsewhere — fall through to looser renderings
            }
        }

        if (int.TryParse(year, out _) && int.TryParse(month, out var mOnly) && mOnly is >= 1 and <= 12)
            return $"{mOnly:00}/{year}";

        return year;
    }

    //returns null if unrecognized (not a full date, bare year, or "MM/yyyy") so
    //the caller leaves existing fields untouched rather than overwrite with a
    //failed guess
    public static (string Year, string Month, string Day)? Parse(string input)
    {
        input = input.Trim();
        if (input.Length == 0)
            return ("", "", "");

        if (DateTime.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.None, out var full))
            return (full.Year.ToString(), full.Month.ToString(), full.Day.ToString());

        if (Regex.IsMatch(input, @"^\d{4}$"))
            return (input, "", "");

        var m = Regex.Match(input, @"^(\d{1,2})[/\-](\d{4})$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var mm) && mm is >= 1 and <= 12)
            return (m.Groups[2].Value, mm.ToString(), "");

        return null;
    }
}
