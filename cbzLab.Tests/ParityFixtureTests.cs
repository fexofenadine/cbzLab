using System.Globalization;
using System.Text.Json;
using cbzLab.Services;
using cbzLab.ViewModels;

namespace cbzLab.Tests;

//runs the same json fixtures as cbzLite's node tests, so the browser edition's ports of
//ValidationService, DateFieldHelper, FilenameGuessService and SummaryHeaderParser can't
//quietly drift from the desktop originals
public class ParityFixtureTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentCulture;
    private readonly TestConfig _cfg = new();

    public ParityFixtureTests() => CultureInfo.CurrentCulture = new CultureInfo("en-GB");

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _original;
        _cfg.Dispose();
    }

    private static JsonElement Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "parity", name))).RootElement;

    private static string? Str(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [Fact]
    public void Validation_MatchesSharedFixtures()
    {
        foreach (var c in Load("validation.json").GetProperty("cases").EnumerateArray())
        {
            var tag = Str(c, "tag")!;
            var value = Str(c, "value")!;
            var check = _cfg.Validation.CheckField(tag, value);
            var label = $"{tag}='{value}'";
            Assert.True(Str(c, "problem") == check?.Problem, $"{label}: problem was '{check?.Problem}'");
            if (check is not null)
                Assert.True(Str(c, "suggestion") == check.Value.Suggestion, $"{label}: suggestion was '{check.Value.Suggestion}'");
        }
    }

    [Fact]
    public void DateParse_MatchesSharedFixtures()
    {
        foreach (var c in Load("dates.json").GetProperty("parse").EnumerateArray())
        {
            var input = Str(c, "input")!;
            var expected = c.GetProperty("expected");
            var actual = DateFieldHelper.Parse(input);
            if (expected.ValueKind == JsonValueKind.Null)
            {
                Assert.True(actual is null, $"'{input}' parsed as {actual}");
                continue;
            }
            var e = expected.EnumerateArray().Select(x => x.GetString()!).ToArray();
            Assert.True(actual == (e[0], e[1], e[2]), $"'{input}' parsed as {actual?.ToString() ?? "null"}");
        }
    }

    [Fact]
    public void DateFormat_MatchesSharedFixtures()
    {
        foreach (var c in Load("dates.json").GetProperty("format").EnumerateArray())
        {
            var actual = DateFieldHelper.FormatForDisplay(Str(c, "year")!, Str(c, "month")!, Str(c, "day")!);
            Assert.Equal(Str(c, "expected"), actual);
        }
    }

    [Fact]
    public void FilenameGuess_MatchesSharedFixtures()
    {
        foreach (var c in Load("tools.json").GetProperty("guess").EnumerateArray())
        {
            var path = Str(c, "path")!;
            var g = FilenameGuessService.FromPath(path);
            var actual = new[] { g.Series, g.Number, g.Volume, g.Year, g.Count };
            var expected = new[] { Str(c, "series"), Str(c, "number"), Str(c, "volume"), Str(c, "year"), Str(c, "count") };
            Assert.True(expected.SequenceEqual(actual), $"'{path}' guessed as [{string.Join(", ", actual.Select(v => v ?? "null"))}]");
        }
    }

    [Fact]
    public void SummaryHeader_MatchesSharedFixtures()
    {
        foreach (var c in Load("tools.json").GetProperty("summaryHeader").EnumerateArray())
        {
            var summary = Str(c, "summary")!;
            var found = SummaryHeaderParser.TryParse(summary, out var header);
            var label = summary.Replace("\n", "\\n").Replace("\r", "\\r");
            Assert.True(found == (Str(c, "publisher") is not null), $"'{label}': found was {found}");
            if (!found)
                continue;
            Assert.Equal(Str(c, "publisher"), header.Publisher);
            Assert.Equal(Str(c, "year"), header.Year);
            Assert.Equal(Str(c, "rest"), header.Summary);
        }
    }
}
