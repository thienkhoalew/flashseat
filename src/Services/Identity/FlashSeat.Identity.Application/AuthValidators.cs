using FluentValidation;

namespace FlashSeat.Identity.Application;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.FullName).NotEmpty().Length(2, 100);
        RuleFor(x => x.Password)
            .MinimumLength(10)
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a special character.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$").WithMessage("Verification code must be 6 digits.");
    }
}

public sealed class ResendVerificationRequestValidator : AbstractValidator<ResendVerificationRequest>
{
    public ResendVerificationRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
}

public sealed class RevokeRequestValidator : AbstractValidator<RevokeRequest>
{
    public RevokeRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
}

public sealed class GoogleAuthRequestValidator : AbstractValidator<GoogleAuthRequest>
{
    public GoogleAuthRequestValidator() => RuleFor(x => x.IdToken).NotEmpty().MaximumLength(4096);
}
