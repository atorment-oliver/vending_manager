using System.Security.Cryptography;
using System.Text.Json;

namespace VendingManager;

public sealed class AppConfig
{
    public List<VendingConfig> Vendings { get; set; } = [];
    public static AppConfig Load(string path)
    {
        if (!File.Exists(path)) return new AppConfig();
        try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig(); }
        catch { return new AppConfig(); }
    }
    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}

public sealed class VendingConfig
{
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string LogPath { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    public void SetPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        PasswordSalt = Convert.ToBase64String(salt);
        PasswordHash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32));
    }
    public bool Verify(string username, string password)
    {
        if (!string.Equals(Username, username, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var salt = Convert.FromBase64String(PasswordSalt);
            var expected = Convert.FromBase64String(PasswordHash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }
}

public sealed record CreditRecord(
    string Id, DateTimeOffset At, string Bank, string User, decimal Amount, string Status, string Detail);

public sealed record DailySummary(DateOnly Date, decimal Total, decimal Success, decimal Error, decimal Pending, decimal Unknown);
