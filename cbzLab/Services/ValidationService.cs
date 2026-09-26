using System.Globalization;
using cbzLab.Models;

namespace cbzLab.Services;

/// <summary>Save-time type validation, driven by schema.json's constraints block. Empty values are always valid.</summary>
public class ValidationService
{
    private readonly SchemaService _schema;

    public ValidationService(SchemaService schema)
    {
        _schema = schema;
    }

    public List<ValidationError> Validate(string fileName, IReadOnlyDictionary<string, string> values)
    {
        var errors = new List<ValidationError>();
        var c = _schema.Constraints;

        foreach (var tag in c.IntFields.Concat(c.FloatFields))
        {
            if (!values.TryGetValue(tag, out var value))
                continue;
            if (CheckField(tag, value) is { } check)
                errors.Add(new ValidationError(fileName, tag, LabelFor(tag), check.Problem, check.Suggestion));
        }

        return errors;
    }

    //used for live as-you-type feedback too; Validate() above is built on this same check
    public (string Problem, string Suggestion)? CheckField(string tag, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var c = _schema.Constraints;

        if (c.IntFields.Contains(tag))
        {
            var hint = c.IntHints.TryGetValue(tag, out var h) ? h : "a whole number";
            if (!long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                return ($"'{value}' is not a whole number.", $"Enter {hint}.");
            if (c.IntRanges.TryGetValue(tag, out var r) && r.Count == 2 && (number < r[0] || number > r[1]))
            {
                var allowed = r[1] >= int.MaxValue ? $"{r[0]} or more" : $"a value from {r[0]} to {r[1]}";
                return ($"{number} is outside the allowed range.", $"Enter {allowed}.");
            }
            return null;
        }

        if (c.FloatFields.Contains(tag))
        {
            if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return ($"'{value}' is not a number.", "Enter a decimal number, e.g. 4.5.");
            if (c.FloatRanges.TryGetValue(tag, out var range) && range.Count == 2
                && (parsed < range[0] || parsed > range[1]))
                return ($"{parsed} is outside the allowed range.", $"Enter a value between {range[0]} and {range[1]}.");
            return null;
        }

        return null;
    }

    private string LabelFor(string tag) => _schema.GetField(tag)?.Label ?? tag;
}
