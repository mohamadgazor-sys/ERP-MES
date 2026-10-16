using System.Globalization;

namespace ERP.MES.Application.Requests;

/// <summary>
/// Reads a quantity typed by a person. UI input convenience, consistent with the date input of Decision Log #73 (and still to be confirmed for numbers): Latin, Persian
/// (U+06F0 to U+06F9) and Arabic-Indic (U+0660 to U+0669) digits, one decimal separator ("." or the Arabic decimal separator U+066B), white space around the text ignored.
/// No sign, no exponent, no thousands separator. The value is an exact decimal: nothing is rounded (Decision Log #58 G3). Whether it is greater than zero is a Domain rule.
/// </summary>
public static class QuantityText
{
    public static bool TryParse(string? text, out decimal quantity)
    {
        quantity = 0;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        var buffer = new char[trimmed.Length];
        var separators = 0;
        var digits = 0;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c is >= '0' and <= '9')
            {
                buffer[i] = c;
                digits++;
            }
            else if (c is >= '۰' and <= '۹')
            {
                buffer[i] = (char)('0' + (c - '۰'));
                digits++;
            }
            else if (c is >= '٠' and <= '٩')
            {
                buffer[i] = (char)('0' + (c - '٠'));
                digits++;
            }
            else if (c is '.' or '٫')
            {
                buffer[i] = '.';
                separators++;
            }
            else
            {
                return false;
            }
        }

        if (digits == 0 || separators > 1 || buffer[0] == '.' || buffer[^1] == '.')
        {
            return false;
        }

        return decimal.TryParse(new string(buffer), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out quantity);
    }
}
