namespace TaskFlow.Application.Features.Auth;

public record RegisterRequest(
    string CompanyName,
    string FullName,
    string Email,
    string Password,
    string Locale = "en");

public record LoginRequest(string Email, string Password);

public record RefreshRequest(string RefreshToken);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresUtc,
    UserDto User);

public record UserDto(
    long Id,
    long TenantId,
    string Email,
    string FullName,
    string Locale,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);
