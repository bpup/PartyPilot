using System.Globalization;
using System.Text.RegularExpressions;

namespace PartyPilot.Bridge;

internal static class ControllerDeviceId
{
    internal static bool TryReadVidPid(string deviceInstance, out ushort vendor, out ushort product)
    {
        vendor = product = 0;
        if (string.IsNullOrWhiteSpace(deviceInstance)) return false;
        var match = Regex.Match(deviceInstance, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})(?![0-9A-F])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success &&
            ushort.TryParse(match.Groups[1].Value, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out vendor) &&
            ushort.TryParse(match.Groups[2].Value, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out product);
    }
}
