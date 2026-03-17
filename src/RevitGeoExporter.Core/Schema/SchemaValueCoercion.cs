using System;
using System.Globalization;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Schema;

public static class SchemaValueCoercion
{
    public static bool TryCoerce(object? value, ExportAttributeType targetType, out object? coercedValue, out string? failureReason)
    {
        failureReason = null;
        coercedValue = null;

        if (value == null)
        {
            return true;
        }

        switch (targetType)
        {
            case ExportAttributeType.Text:
                coercedValue = ToText(value);
                return true;
            case ExportAttributeType.Integer:
                return TryCoerceInteger(value, out coercedValue, out failureReason);
            case ExportAttributeType.Real:
                return TryCoerceReal(value, out coercedValue, out failureReason);
            case ExportAttributeType.Boolean:
                return TryCoerceBoolean(value, out coercedValue, out failureReason);
            default:
                failureReason = $"Unsupported target type '{targetType}'.";
                return false;
        }
    }

    private static string ToText(object value)
    {
        return value switch
        {
            bool boolValue => boolValue ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static bool TryCoerceInteger(object value, out object? coercedValue, out string? failureReason)
    {
        failureReason = null;
        switch (value)
        {
            case byte byteValue:
                coercedValue = (long)byteValue;
                return true;
            case short shortValue:
                coercedValue = (long)shortValue;
                return true;
            case int intValue:
                coercedValue = (long)intValue;
                return true;
            case long longValue:
                coercedValue = longValue;
                return true;
            case float floatValue when IsWholeNumber(floatValue):
                coercedValue = Convert.ToInt64(floatValue);
                return true;
            case double doubleValue when IsWholeNumber(doubleValue):
                coercedValue = Convert.ToInt64(doubleValue);
                return true;
            case decimal decimalValue when decimal.Truncate(decimalValue) == decimalValue:
                coercedValue = decimal.ToInt64(decimalValue);
                return true;
            case string stringValue when long.TryParse(stringValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed):
                coercedValue = parsed;
                return true;
            default:
                coercedValue = null;
                failureReason = $"Value '{value}' could not be converted to an integer.";
                return false;
        }
    }

    private static bool TryCoerceReal(object value, out object? coercedValue, out string? failureReason)
    {
        failureReason = null;
        switch (value)
        {
            case byte byteValue:
                coercedValue = (double)byteValue;
                return true;
            case short shortValue:
                coercedValue = (double)shortValue;
                return true;
            case int intValue:
                coercedValue = (double)intValue;
                return true;
            case long longValue:
                coercedValue = (double)longValue;
                return true;
            case float floatValue:
                coercedValue = (double)floatValue;
                return true;
            case double doubleValue:
                coercedValue = doubleValue;
                return true;
            case decimal decimalValue:
                coercedValue = (double)decimalValue;
                return true;
            case string stringValue when double.TryParse(stringValue.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double parsed):
                coercedValue = parsed;
                return true;
            default:
                coercedValue = null;
                failureReason = $"Value '{value}' could not be converted to a real number.";
                return false;
        }
    }

    private static bool TryCoerceBoolean(object value, out object? coercedValue, out string? failureReason)
    {
        failureReason = null;
        switch (value)
        {
            case bool boolValue:
                coercedValue = boolValue;
                return true;
            case byte byteValue when byteValue == 0 || byteValue == 1:
                coercedValue = byteValue == 1;
                return true;
            case short shortValue when shortValue == 0 || shortValue == 1:
                coercedValue = shortValue == 1;
                return true;
            case int intValue when intValue == 0 || intValue == 1:
                coercedValue = intValue == 1;
                return true;
            case long longValue when longValue == 0 || longValue == 1:
                coercedValue = longValue == 1;
                return true;
            case string stringValue:
                string normalized = stringValue.Trim();
                if (bool.TryParse(normalized, out bool parsedBool))
                {
                    coercedValue = parsedBool;
                    return true;
                }

                if (normalized == "1" || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    coercedValue = true;
                    return true;
                }

                if (normalized == "0" || normalized.Equals("no", StringComparison.OrdinalIgnoreCase))
                {
                    coercedValue = false;
                    return true;
                }

                break;
        }

        coercedValue = null;
        failureReason = $"Value '{value}' could not be converted to a boolean.";
        return false;
    }

    private static bool IsWholeNumber(double value)
    {
        return Math.Abs(value - Math.Round(value)) <= 1e-9d;
    }
}
