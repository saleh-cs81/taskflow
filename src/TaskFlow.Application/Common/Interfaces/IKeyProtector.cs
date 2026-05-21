namespace TaskFlow.Application.Common.Interfaces;

// Encrypts/decrypts sensitive values (e.g. third-party API keys) at rest.
public interface IKeyProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
