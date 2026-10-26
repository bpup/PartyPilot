using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record AccountCredential(string Email, string Password);
internal sealed record AccountProfileResult(bool Success, string Message);

internal interface IAccountDataProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

internal sealed class WindowsAccountDataProtector : IAccountDataProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PartyPilot|account-profile|v1");

    public byte[] Protect(byte[] data)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] data)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
    }
}

internal sealed class AccountProfileStore
{
    private readonly string _directory;
    private readonly IAccountDataProtector _protector;

    internal AccountProfileStore(string? directory = null, IAccountDataProtector? protector = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PartyPilot", "AccountProfiles");
        _protector = protector ?? new WindowsAccountDataProtector();
    }

    internal AccountProfileResult Save(int client, string? email, string? password)
    {
        var path = PathFor(client);
        if (_protector is WindowsAccountDataProtector && !OperatingSystem.IsWindows())
            return new(false, "账号资料仅能在 Windows 上保存。");
        email = email?.Trim();
        if (!IsValidEmail(email) || string.IsNullOrEmpty(password) ||
            password.Length > 1024 || password.Any(char.IsControl))
            return new(false, "请输入有效的邮箱和密码。密码最长 1024 字符。");
        byte[]? plain = null;
        byte[]? encrypted = null;
        string? temporary = null;
        try
        {
            plain = JsonSerializer.SerializeToUtf8Bytes(new AccountCredential(email!, password));
            encrypted = _protector.Protect(plain);
            Directory.CreateDirectory(_directory);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, path, overwrite: true);
            return new(true, $"客户端 {client} 的账号资料已保存到当前 Windows 用户。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return new(false, $"客户端 {client} 的账号资料保存失败。");
        }
        finally
        {
            if (temporary is not null)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    internal AccountCredential? Load(int client)
    {
        var path = PathFor(client);
        if (!File.Exists(path) ||
            _protector is WindowsAccountDataProtector && !OperatingSystem.IsWindows()) return null;
        byte[]? encrypted = null;
        byte[]? plain = null;
        try
        {
            encrypted = File.ReadAllBytes(path);
            plain = _protector.Unprotect(encrypted);
            var credential = JsonSerializer.Deserialize<AccountCredential>(plain);
            return credential is not null && IsValidEmail(credential.Email) &&
                   !string.IsNullOrEmpty(credential.Password) ? credential : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            CryptographicException or JsonException)
        {
            return null;
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    internal AccountProfileResult Delete(int client)
    {
        var path = PathFor(client);
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return new(true, $"客户端 {client} 的账号资料已删除。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(false, $"客户端 {client} 的账号资料无法删除。");
        }
    }

    internal string Describe(int client)
    {
        var credential = Load(client);
        if (credential is null) return "未保存受保护的账号资料";
        var at = credential.Email.IndexOf('@');
        return at > 0
            ? $"已保存 · {credential.Email[..Math.Min(2, at)]}***{credential.Email[at..]}"
            : "已保存账号资料";
    }

    internal static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254 || value.Any(char.IsControl))
            return false;
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 3 &&
               value.IndexOf('.', at + 2) > at + 1;
    }

    private string PathFor(int client)
    {
        if (client is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(client));
        return Path.Combine(_directory, $"client-{client}.account.dat");
    }
}
