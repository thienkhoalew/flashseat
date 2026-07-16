using FlashSeat.Identity.Application;
using FluentAssertions;
using Xunit;

namespace FlashSeat.UnitTests;

public sealed class AuthValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    [Fact]
    public void Register_rejects_weak_password()
    {
        var result = _validator.Validate(new RegisterRequest(
            "demo@flashseat.dev", "weak", "Demo Customer"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "Password");
    }

    [Fact]
    public void Register_accepts_valid_request()
    {
        var result = _validator.Validate(new RegisterRequest(
            "demo@flashseat.dev", "Demo@123456", "Demo Customer"));

        result.IsValid.Should().BeTrue();
    }
}
