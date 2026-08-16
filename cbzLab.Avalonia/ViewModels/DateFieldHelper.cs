using System.Globalization;
using System.Text.RegularExpressions;

namespace cbzLab.ViewModels;

/// <summary>
/// Bridges a single localized date string to/from ComicInfo's separate
/// Year/Month/Day tags. Full dates parse and format via the current
/// culture's own DateTime handling — so this correctly follows whatever
/// format the OS is set to (dd/MM/yyyy on an Australian machine, MM/dd/yyyy
/// on a US one, etc.) without hardcoding any particular locale.
///
/// Anything that isn't a recognizable full date doesn't just fail: a bare
/// 4-digit number is read as year-only, and "MM/yyyy" as year+month, both
/// common for older or uncertain publication dates where a full date
/// genuinely isn't known — ComicInfo's schema explicitly allows partial
/// dates, and this needs to keep supporting that, not just full dates.
///
/// This deliberately does not try to derive a fully generic partial-date
/// ordering from arbitrary cultures' own full-date patterns (e.g. working
/// out whether a bare two-part "07/2015" should be read as month/year or
/// year/month based on the current culture's day-vs-month ordering) — that
/// is a lot of complexity for a case (year+month, no day) that is rare in
/// practice compared to either a full date or year-only. "MM/yyyy" is used
/// as one fixed, documented convention for that one partial case.
/// </summary>
public static class DateFieldHelper
{
    /// <summary>
    /// Composes a display string from Year/Month/Day, in whichever of the
    /// three is actually populated. Empty if Year itself is empty — ComicInfo
    /// dates are always anchored on year; a month or day with no year isn't
    /// a meaningful partial date.
    /// </summary>
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
                //an out-of-range combination (bad data from elsewhere) falls
                //through to the looser renderings below rather than throwing
            }
        }

        if (int.TryParse(year, out _) && int.TryParse(month, out var mOnly) && mOnly is >= 1 and <= 12)
            return $"{mOnly:00}/{year}";

        return year;
    }

    /// <summary>
    /// Parses user input back into (Year, Month, Day) — each may be empty
    /// for a partial date. Returns null if the input isn't recognized as a
    /// full date, a bare year, or "MM/yyyy" — the caller should leave the
    /// underlying fields untouched in that case rather than overwrite good
    /// data with a failed guess.
    /// </summary>
    public static (string Year, string Month, string Day)? Parse(string input)
    {
        input = input.Trim();
        if (input.Length == 0)
            return ("", "", "");

        //partial-date forms (year-only, "MM/yyyy") are checked BEFORE
        //DateTime.TryParse, not after - .NET's parser doesn't reject a
        //2-component input like "03/2019", it silently fills in the missing
        //day as 1 and succeeds as a "full" date, which used to make every
        //partial date the user actually typed get written with a fabricated
        //Day=1 instead of staying genuinely dayless. Confirmed by reading the
        //real saved ComicInfo.xml before this fix: a "03/2019" edit wrote
        //<Day>1</Day>, never reaching the regex below at all.
        if (Regex.IsMatch(input, @"^\d{4}$"))
            return (input, "", "");

        var m = Regex.Match(input, @"^(\d{1,2})[/\-](\d{4})$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var mm) && mm is >= 1 and <= 12)
            return (m.Groups[2].Value, mm.ToString(), "");

        if (DateTime.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.None, out var full))
            return (full.Year.ToString(), full.Month.ToString(), full.Day.ToString());

        return null;
    }
}
