using SecRandom.Core.Services.Security.PasswordStrength;

namespace SecRandom.Core.Tests;

public class PasswordStrengthEvaluatorTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("Password1")]
    [InlineData("123456")]
    [InlineData("qwerty")]
    [InlineData("p@ssw0rd")]
    [InlineData("P@ssw0rd!")]
    [InlineData("iloveyou")]
    public void CommonPasswords_AreNeverRecommended(string password)
    {
        var result = PasswordStrengthEvaluator.Evaluate(password);

        Assert.True(result.Score <= 1, $"'{password}' scored {result.Score} ({result.Log10Guesses:F1} guesses)");
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("12345678")]
    [InlineData("aaaaaaaa")]
    [InlineData("19900101")]
    [InlineData("qwertyuiop")]
    public void PredictablePatterns_ScoreLow(string password)
    {
        var result = PasswordStrengthEvaluator.Evaluate(password);

        Assert.True(result.Score <= 2, $"'{password}' scored {result.Score}");
    }

    [Fact]
    public void TooShort_IsAlwaysWeakest()
    {
        foreach (var password in new[] { "a", "Ab1!", "Abc12" })
        {
            var result = PasswordStrengthEvaluator.Evaluate(password);

            Assert.Equal(PasswordStrengthLevel.VeryWeak, result.Level);
            Assert.Equal(PasswordStrengthIssue.TooShort, result.Issue);
            Assert.Equal(0, result.Score);
        }
    }

    [Fact]
    public void SixCharacterMinimum_IsConfirmedByTheService()
    {
        // The service still owns the only hard floor; the estimator must agree with it.
        Assert.Equal(6, PasswordStrengthEvaluator.MinimumLength);
    }

    [Fact]
    public void LongPassphrase_IsStrong()
    {
        var result = PasswordStrengthEvaluator.Evaluate("correct horse battery staple");

        Assert.True(result.Score >= 3, $"scored {result.Score} ({result.Log10Guesses:F1} guesses)");
    }

    [Fact]
    public void LongRandomPassword_IsStrong()
    {
        var result = PasswordStrengthEvaluator.Evaluate("aB3$xY9!qL2#");

        Assert.True(result.Score >= 3, $"scored {result.Score} ({result.Log10Guesses:F1} guesses)");
    }

    [Fact]
    public void LengthBeatsSubstitutions()
    {
        var leet = PasswordStrengthEvaluator.Evaluate("P@ssw0rd!");
        var passphrase = PasswordStrengthEvaluator.Evaluate("three small lamps on a wet roof");

        Assert.True(passphrase.Score > leet.Score);
    }

    [Theory]
    [InlineData("密码")]
    [InlineData("密码123")]
    [InlineData("ｐａｓｓｗｏｒｄ")]
    [InlineData("１２３４５６")]
    public void CommonNonAsciiAndFullWidthPasswords_AreWeak(string password)
    {
        var result = PasswordStrengthEvaluator.Evaluate(password);

        Assert.True(result.Score <= 2, $"'{password}' scored {result.Score}");
    }

    [Fact]
    public void DigitOnlyPasswords_AreCapped()
    {
        var result = PasswordStrengthEvaluator.Evaluate("918273645");

        Assert.True(result.Score <= 2, $"scored {result.Score}");
    }

    [Fact]
    public void EmptyAndNull_DoNotThrow()
    {
        Assert.Equal(PasswordStrengthIssue.TooShort, PasswordStrengthEvaluator.Evaluate(null).Issue);
        Assert.Equal(PasswordStrengthIssue.TooShort, PasswordStrengthEvaluator.Evaluate(string.Empty).Issue);
        Assert.Equal(PasswordStrengthLevel.VeryWeak, PasswordStrengthEvaluator.Evaluate("      ").Level);
    }

    [Fact]
    public void VeryLongInput_DoesNotThrow()
    {
        var password = string.Concat(Enumerable.Repeat("aB3$xY9!qL2#", 400));

        var result = PasswordStrengthEvaluator.Evaluate(password);

        Assert.InRange(result.Score, 0, 4);
        Assert.True(double.IsFinite(result.Log10Guesses));
    }

    [Fact]
    public void Evaluation_IsDeterministic()
    {
        var first = PasswordStrengthEvaluator.Evaluate("Zebra!Table-42");
        var second = PasswordStrengthEvaluator.Evaluate("Zebra!Table-42");

        Assert.Equal(first, second);
    }

    [Fact]
    public void StrengthLevelOrder_MatchesUiSegments()
    {
        Assert.Equal(0, (int)PasswordStrengthLevel.VeryWeak);
        Assert.Equal(4, (int)PasswordStrengthLevel.VeryStrong);
    }
}
