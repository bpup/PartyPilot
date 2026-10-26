using PartyPilot.Sensor;

internal static class UiItemCardVtableLocatorTests
{
    internal static void Run()
    {
        var bytes = Convert.FromHexString((
            "48 8D 05 11 22 33 44 48 89 06 48 8D 05 55 66 77 88 48 89 86 28 02 00 00 " +
            "48 8D 05 99 AA BB CC 48 89 86 30 02 00 00 48 8D 05 DD EE FF 00 " +
            "48 89 86 C8 02 00 00 48 89 BE D0 02 00 00 48 89 BE D8 02 00 00 " +
            "48 89 BE E0 02 00 00 40 88 BE E8 02 00 00 48 89 9E F0 02 00 00 " +
            "4C 89 B6 F8 02 00 00").Replace(" ", ""));
        var padded = new byte[bytes.Length + 20];
        bytes.CopyTo(padded, 10);
        if (!UiItemCardVtableLocator.FindPattern(padded).SequenceEqual([10]))
            throw new Exception("Constructor signature was not recognized.");
        padded[10 + 22] ^= 1;
        if (UiItemCardVtableLocator.FindPattern(padded).Any())
            throw new Exception("Altered item-card constructor was accepted.");
        Console.WriteLine("UI item-card vtable signature checks passed.");
    }
}
