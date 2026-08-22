using System.Globalization;
using cbzLab.ViewModels;

namespace cbzLab.Avalonia.Tests;

//Parse()/FormatForDisplay() both key off CurrentCulture for full-date parsing/rendering,
//so every test pins the thread to en-GB (day/month/year) rather than whatever culture the
//machine or CI runner happens to default to
public class DateFieldHelperTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentCulture;

    public DateFieldHelperTests() => CultureInfo.CurrentCulture = new CultureInfo("en-GB");
    public void Dispose() => CultureInfo.CurrentCulture = _original;

    [Fact]
    public void Parse_EmptyInput_ReturnsAllBlank()
    {
        var result = DateFieldHelper.Parse("");
        Assert.Equal(("", "", ""), result);
    }

    [Fact]
    public void Parse_FullDate_ReturnsYearMonthDay()
    {
        var result = DateFieldHelper.Parse("25/12/2020");
        Assert.Equal(("2020", "12", "25"), result);
    }

    [Fact]
    public void Parse_BareYear_ReturnsYearOnly()
    {
        var result = DateFieldHelper.Parse("1999");
        Assert.Equal(("1999", "", ""), result);
    }

    //regression test for the slice-18 bug: DateTime.TryParse doesn't reject "03/2019", it
    //silently fills Day=1 and succeeds as a full date - the MM/yyyy regex must run first
    [Fact]
    public void Parse_MonthYear_DoesNotFabricateADay()
    {
        var result = DateFieldHelper.Parse("03/2019");
        Assert.Equal(("2019", "3", ""), result);
    }

    [Fact]
    public void Parse_MonthYearWithDash_IsAlsoAccepted()
    {
        var result = DateFieldHelper.Parse("7-2021");
        Assert.Equal(("2021", "7", ""), result);
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("banana")]
    public void Parse_Unparseable_ReturnsNull(string input)
    {
        Assert.Null(DateFieldHelper.Parse(input));
    }

    [Fact]
    public void FormatForDisplay_NoYear_ReturnsEmpty()
    {
        Assert.Equal("", DateFieldHelper.FormatForDisplay("", "3", "25"));
    }

    [Fact]
    public void FormatForDisplay_YearOnly_ReturnsBareYear()
    {
        Assert.Equal("1999", DateFieldHelper.FormatForDisplay("1999", "", ""));
    }

    [Fact]
    public void FormatForDisplay_YearAndMonth_ReturnsMmSlashYyyy()
    {
        Assert.Equal("03/2019", DateFieldHelper.FormatForDisplay("2019", "3", ""));
    }

    [Fact]
    public void FormatForDisplay_FullDate_RoundTripsThroughParse()
    {
        var display = DateFieldHelper.FormatForDisplay("2020", "12", "25");
        var parsed = DateFieldHelper.Parse(display);
        Assert.Equal(("2020", "12", "25"), parsed);
    }
}
