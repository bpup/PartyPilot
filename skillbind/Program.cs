using System.Globalization;
using System.Text.Json;
using PartyPilot.Sensor;

if (args.Length != 3 || !TryAddress(args[1], out var owner) ||
    !TryAddress(args[2], out var player))
{
    Console.Error.WriteLine("Usage: PartyPilot.SkillBind <148-byte bind table file> <observed owner hex> <expected player hex>");
    return 2;
}

try
{
    var data = File.ReadAllBytes(args[0]);
    var result = SkillBindTable.DecodeForObservedOwner(data, owner, player);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    return result.Status == "decoded_unverified_source" ? 0 : 1;
}
catch (IOException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static bool TryAddress(string value, out long address) =>
    long.TryParse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value,
        NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
