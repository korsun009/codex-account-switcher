using System.Text;

namespace CodexAccountSwitcher.Core;

public static class TextEncodingRepair
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static TextEncodingRepair()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string Repair(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (LooksLikeWindows1251Mojibake(value) && TryDecode(value, Encoding.GetEncoding(1251), out var windows1251))
        {
            return windows1251;
        }

        if ((value.Contains('Ð') || value.Contains('Ñ')) && TryDecode(value, Encoding.Latin1, out var latin1))
        {
            return latin1;
        }

        return value;
    }

    private static bool LooksLikeWindows1251Mojibake(string value)
    {
        var markerCount = value.Count(character => character is 'Р' or 'С');
        return value.Length >= 8 && markerCount >= 2;
    }

    private static bool TryDecode(string value, Encoding sourceEncoding, out string repaired)
    {
        repaired = value;
        try
        {
            var candidate = StrictUtf8.GetString(sourceEncoding.GetBytes(value));
            if (candidate == value || candidate.Any(character => character == '\uFFFD'))
            {
                return false;
            }

            var meaningful = candidate.Count(character => char.IsLetterOrDigit(character));
            if (meaningful == 0)
            {
                return false;
            }

            repaired = candidate;
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
