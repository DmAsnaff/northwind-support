namespace SupportDesk.Application.Abstractions;

/// <summary>
/// Hashes and verifies passwords using a salted key-derivation function.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}

