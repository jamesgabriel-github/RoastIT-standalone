using RoastIT.Core.Exceptions;

namespace RoastIT.Core.Rules;

/// <summary>PIN format and the user-facing messages for PIN attempts (signin spec §6, shared-ui-spec §7.2).</summary>
public static class PinRules
{
    public const int MinLength = 4;
    public const int MaxLength = 6;

    public static bool IsValid(string? pin) =>
        pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);

    public static void EnsureValid(string? pin)
    {
        if (!IsValid(pin)) throw new ValidationException("Use 4 to 6 digits.");
    }
}

public static class PinMessages
{
    public const string TooShort = "Use 4 to 6 digits.";
    public const string Mismatch = "PINs didn't match. Choose your PIN again.";
    public const string EmptyRecoveryCode = "Enter your recovery code.";

    public static string WrongPin(int triesLeft) => $"Incorrect PIN. {Tries(triesLeft)} left.";

    public static string WrongRecoveryCode(int triesLeft) => $"That recovery code isn't right. {Tries(triesLeft)} left.";

    public static string LockedOut(int secondsLeft) => $"Too many tries. Try again in {Math.Max(secondsLeft, 0)}s.";

    private static string Tries(int n) => n == 1 ? "1 try" : $"{n} tries";
}
