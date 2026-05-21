using Microsoft.AspNetCore.DataProtection;
using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Infrastructure.Services;

public class DataProtectionKeyProtector(IDataProtectionProvider provider) : IKeyProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("TaskFlow.ApiKeys.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
