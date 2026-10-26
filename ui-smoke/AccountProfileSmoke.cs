using System.Text;
using PartyPilot.Desktop;

internal static class AccountProfileSmoke
{
    private sealed class TestProtector : IAccountDataProtector
    {
        public byte[] Protect(byte[] data) => data.Select(value => (byte)(value ^ 0x5a)).ToArray();
        public byte[] Unprotect(byte[] data) => Protect(data);
    }

    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-account-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AccountProfileStore(directory, new TestProtector());
            if (store.Save(2, "invalid", "synthetic-password").Success ||
                store.Save(2, "account@example.invalid", "bad\npassword").Success)
                throw new Exception("Invalid account input was accepted.");
            var saved = store.Save(2, " account@example.invalid ", "synthetic-password");
            var path = Path.Combine(directory, "client-2.account.dat");
            if (!saved.Success || !File.Exists(path) ||
                Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("synthetic-password") ||
                store.Load(2) != new AccountCredential("account@example.invalid", "synthetic-password") ||
                store.Describe(2) != "已保存 · ac***@example.invalid")
                throw new Exception("Account profile storage, load, or masking failed with a synthetic protector.");
            if (!store.Delete(2).Success || File.Exists(path) || store.Load(2) is not null)
                throw new Exception("Account profile deletion failed.");
            if (!OperatingSystem.IsWindows() &&
                (new AccountProfileStore(directory).Save(1, "synthetic@example.invalid", "synthetic-password").Success ||
                 File.Exists(Path.Combine(directory, "client-1.account.dat"))))
                throw new Exception("Account storage accepted a real profile outside Windows.");
            if (OperatingSystem.IsWindows())
            {
                var windowsStore = new AccountProfileStore(directory);
                var protectedResult = windowsStore.Save(3, "synthetic@example.invalid", "synthetic-only-password");
                var protectedPath = Path.Combine(directory, "client-3.account.dat");
                if (!protectedResult.Success ||
                    Encoding.UTF8.GetString(File.ReadAllBytes(protectedPath)).Contains("synthetic-only-password") ||
                    windowsStore.Load(3) != new AccountCredential("synthetic@example.invalid", "synthetic-only-password") ||
                    !windowsStore.Delete(3).Success)
                    throw new Exception("Windows current-user account protection failed its synthetic roundtrip.");
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
