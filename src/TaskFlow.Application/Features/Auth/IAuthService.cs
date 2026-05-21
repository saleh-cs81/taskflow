namespace TaskFlow.Application.Features.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ip, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ip, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, string? ip, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
}
