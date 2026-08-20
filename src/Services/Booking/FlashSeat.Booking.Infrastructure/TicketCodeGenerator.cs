using System.Security.Cryptography;

namespace FlashSeat.Booking.Infrastructure;

public static class TicketCodeGenerator
{
    public static string Create() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public static bool TryParse(string value, out string code)
    {
        code = string.Empty;
        if (!value.StartsWith("FS1:", StringComparison.Ordinal) || value.Length != 36)
            return false;
        var raw = value[4..];
        if (!raw.All(Uri.IsHexDigit)) return false;
        code = raw.ToUpperInvariant();
        return true;
    }
}
