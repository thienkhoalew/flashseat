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

    [Fact]
    public void Google_auth_rejects_empty_id_token()
    {
        var validator = new GoogleAuthRequestValidator();
        var result = validator.Validate(new GoogleAuthRequest(string.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(GoogleAuthRequest.IdToken));
    }

    [Fact]
    public void Google_auth_accepts_valid_id_token()
    {
        var validator = new GoogleAuthRequestValidator();
        var result = validator.Validate(new GoogleAuthRequest("valid-google-id-token"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_email_rejects_invalid_code()
    {
        var validator = new VerifyEmailRequestValidator();
        var result = validator.Validate(new VerifyEmailRequest("demo@flashseat.dev", "12345"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(VerifyEmailRequest.Code));
    }

    [Fact]
    public void Verify_email_accepts_valid_request()
    {
        var validator = new VerifyEmailRequestValidator();
        var result = validator.Validate(new VerifyEmailRequest("demo@flashseat.dev", "123456"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Resend_verification_validates_email()
    {
        var validator = new ResendVerificationRequestValidator();
        var result = validator.Validate(new ResendVerificationRequest("invalid-email"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(ResendVerificationRequest.Email));
    }
}
