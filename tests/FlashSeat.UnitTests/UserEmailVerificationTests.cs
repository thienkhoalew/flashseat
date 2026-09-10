using FlashSeat.Identity.Domain;
using FluentAssertions;
using Xunit;

namespace FlashSeat.UnitTests;

public sealed class UserEmailVerificationTests
{
    [Fact]
    public void VerifyEmail_succeeds_with_valid_code_and_active_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User(Guid.NewGuid(), "test@flashseat.dev", "Test User", now);
        user.SetEmailVerificationCode("123456", now.AddMinutes(15));

        user.IsEmailVerified.Should().BeFalse();

        var success = user.VerifyEmail("123456", now.AddMinutes(5));

        success.Should().BeTrue();
        user.IsEmailVerified.Should().BeTrue();
        user.EmailVerificationCode.Should().BeNull();
        user.EmailVerificationCodeExpiresAt.Should().BeNull();
    }

    [Fact]
    public void VerifyEmail_fails_with_incorrect_code()
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User(Guid.NewGuid(), "test@flashseat.dev", "Test User", now);
        user.SetEmailVerificationCode("123456", now.AddMinutes(15));

        var success = user.VerifyEmail("654321", now.AddMinutes(5));

        success.Should().BeFalse();
        user.IsEmailVerified.Should().BeFalse();
        user.EmailVerificationCode.Should().Be("123456");
    }

    [Fact]
    public void VerifyEmail_fails_when_expired()
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User(Guid.NewGuid(), "test@flashseat.dev", "Test User", now);
        user.SetEmailVerificationCode("123456", now.AddMinutes(15));

        var success = user.VerifyEmail("123456", now.AddMinutes(16));

        success.Should().BeFalse();
        user.IsEmailVerified.Should().BeFalse();
    }

    [Fact]
    public void SetEmailVerified_clears_verification_code()
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User(Guid.NewGuid(), "test@flashseat.dev", "Test User", now);
        user.SetEmailVerificationCode("123456", now.AddMinutes(15));

        user.SetEmailVerified(true);

        user.IsEmailVerified.Should().BeTrue();
        user.EmailVerificationCode.Should().BeNull();
        user.EmailVerificationCodeExpiresAt.Should().BeNull();
    }
}
