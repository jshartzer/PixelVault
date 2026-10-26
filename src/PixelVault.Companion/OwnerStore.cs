using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace PixelVault.Companion;

public sealed record Owner(string Name, string PasswordHash);

/// <summary>Single-owner authentication store, deliberately separate from the desktop tracker database.</summary>
public sealed class OwnerStore
{
    private readonly string databasePath;
    private readonly string tokenPath;
    private readonly PasswordHasher<string> hasher = new(Microsoft.Extensions.Options.Options.Create(
        new PasswordHasherOptions { IterationCount = 210_000 }));

    public OwnerStore(CompanionOptions options)
    {
        var root = Path.GetFullPath(options.DataRoot);
        Directory.CreateDirectory(root);
        databasePath = Path.Combine(root, "companion.sqlite");
        tokenPath = Path.Combine(root, "setup-token.txt");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(command.ExecuteScalar());
        if (version > 1) throw new InvalidOperationException("This Companion database needs a newer application version.");
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS owner(id INTEGER PRIMARY KEY CHECK(id=1), name TEXT NOT NULL, password_hash TEXT NOT NULL);
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
        if (Read() is null && !File.Exists(tokenPath))
        {
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var file = new StreamWriter(new FileStream(tokenPath, fileOptions));
            file.Write(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = databasePath, Pooling = false, DefaultTimeout = 5 }.ToString());
        connection.Open();
        return connection;
    }

    public Owner? Read()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name,password_hash FROM owner WHERE id=1";
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetString(1)) : null;
    }

    public bool Setup(string token, string name, string password)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80 || name.Any(char.IsControl)
            || password.Length is < 12 or > 128 || token.Length > 128 || !File.Exists(tokenPath)) return false;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(tokenPath).Trim()));
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied)) return false;
        using var connection = Open();
        using var command = connection.CreateCommand();
        var normalized = name.Trim();
        command.CommandText = "INSERT OR IGNORE INTO owner(id,name,password_hash) VALUES(1,$name,$hash)";
        command.Parameters.AddWithValue("$name", normalized);
        command.Parameters.AddWithValue("$hash", hasher.HashPassword(normalized, password));
        // The unique row is the final guard even when two setup requests race.
        return command.ExecuteNonQuery() == 1;
    }

    public Owner? Authenticate(string name, string password)
    {
        if (name.Length > 80 || password.Length > 128) return null;
        var owner = Read();
        // Verify even a wrong name to avoid a cheap username timing oracle.
        var result = hasher.VerifyHashedPassword(owner?.Name ?? "", owner?.PasswordHash ?? DummyHash, password);
        return owner is not null && string.Equals(owner.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)
            && result != PasswordVerificationResult.Failed ? owner : null;
    }

    private static readonly string DummyHash = new PasswordHasher<string>().HashPassword("", "unused authentication timing value");
}
