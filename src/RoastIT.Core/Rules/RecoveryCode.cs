using System.Security.Cryptography;
using System.Text;

namespace RoastIT.Core.Rules;

/// <summary>
/// The 12-character recovery code, shown as XXXX-XXXX-XXXX (plan §9.4).
/// No look-alikes (0, O, 1, I, L); secure random source; compared ignoring case, spaces and dashes.
/// </summary>
public static class RecoveryCode
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 12;

    public static string Generate()
    {
        var chars = new char[Length + 2];
        var o = 0;
        for (var i = 0; i < Length; i++)
        {
            if (i > 0 && i % 4 == 0) chars[o++] = '-';
            // GetInt32 avoids modulo bias.
            chars[o++] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(chars, 0, o);
    }

    /// <summary>Uppercase, drop everything that isn't A–Z or 0–9: "rstl 7q4m-k2pd" → "RSTL7Q4MK2PD".</summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}
