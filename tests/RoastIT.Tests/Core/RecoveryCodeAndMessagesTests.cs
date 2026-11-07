using RoastIT.Core.Rules;

namespace RoastIT.Tests.Core;

public class RecoveryCodeAndMessagesTests
{
    [Fact]
    public void Generated_codes_use_12_characters_in_three_groups_without_look_alikes()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = RecoveryCode.Generate();
            Assert.Matches("^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$", code);
            Assert.DoesNotContain(code.Replace("-", ""), c => "01OIL".Contains(c));
        }
    }

    [Fact]
    public void Generated_codes_differ()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => RecoveryCode.Generate()).ToHashSet();
        Assert.Equal(50, codes.Count);
    }

    [Theory]
    [InlineData("rstl 7q4m-k2pd", "RSTL7Q4MK2PD")]
    [InlineData("RSTL-7Q4M-K2PD", "RSTL7Q4MK2PD")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void Normalize_ignores_case_spaces_and_dashes(string? input, string expected)
    {
        Assert.Equal(expected, RecoveryCode.Normalize(input));
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("123456", true)]
    [InlineData("123", false)]
    [InlineData("1234567", false)]
    [InlineData("12 4", false)]
    [InlineData("12a4", false)]
    [InlineData("١٢٣٤", false)] // non-ASCII digits are not accepted
    [InlineData(null, false)]
    public void Pins_are_4_to_6_ascii_digits(string? pin, bool valid)
    {
        Assert.Equal(valid, PinRules.IsValid(pin));
    }

    [Fact]
    public void Messages_match_the_spec_wording()
    {
        Assert.Equal("Incorrect PIN. 3 tries left.", PinMessages.WrongPin(3));
        Assert.Equal("Incorrect PIN. 1 try left.", PinMessages.WrongPin(1));
        Assert.Equal("That recovery code isn't right. 2 tries left.", PinMessages.WrongRecoveryCode(2));
        Assert.Equal("Too many tries. Try again in 27s.", PinMessages.LockedOut(27));
        Assert.Equal("Too many tries. Try again in 0s.", PinMessages.LockedOut(-1));
    }
}
