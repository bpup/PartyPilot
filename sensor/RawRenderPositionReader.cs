using System.Text;

namespace PartyPilot.Sensor;

// These are the public default offsets in the supplied build. The target may
// activate a different profile at runtime, so results are diagnostic only.
public static class RawRenderPositionReader
{
    public sealed record Position(float WorldX, float WorldY, float WorldZ,
        float GridX, float GridY, string RenderComponentAddress);
    public sealed record LocalComparison(float ObservedX, float ObservedY, float ObservedZ,
        double WorldXYDistance, double GridXYDistance);

    private const long MaxPointer = 0x7FFF_FFFF_FFFF;
    private const float WorldToGridRatio = 10.869565f;
    public const int DefaultWorldPositionOffset = 312;
    public const int GameHelperWorldPositionOffset = 184;

    // The supplied offset table names both layouts. Only a unique match to
    // the independently observed local player may select a layout for P2.
    public static int? SelectUniqueOffset(long localEntity,
        Func<long, int, byte[]?> read, float observedX, float observedY, float observedZ)
    {
        var component = FindRenderComponent(localEntity, read);
        if (component == 0) return null;
        var normal = ReadPosition(component, read, DefaultWorldPositionOffset);
        var helper = ReadPosition(component, read, GameHelperWorldPositionOffset);
        var normalMatches = normal is not null &&
            CompareLocal(normal, observedX, observedY, observedZ) is { WorldXYDistance: 0 };
        var helperMatches = helper is not null &&
            CompareLocal(helper, observedX, observedY, observedZ) is { WorldXYDistance: 0 };
        if (normalMatches == helperMatches) return null;
        return normalMatches ? DefaultWorldPositionOffset : GameHelperWorldPositionOffset;
    }

    public static LocalComparison? CompareLocal(Position raw,
        float observedX, float observedY, float observedZ)
    {
        if (!float.IsFinite(observedX) || !float.IsFinite(observedY) ||
            !float.IsFinite(observedZ)) return null;
        return new LocalComparison(observedX, observedY, observedZ,
            Distance(raw.WorldX, raw.WorldY, observedX, observedY),
            Distance(raw.GridX, raw.GridY, observedX, observedY));
    }

    private static double Distance(float x1, float y1, float x2, float y2)
    {
        var dx = (double)x1 - x2;
        var dy = (double)y1 - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static Position? Read(long entity, Func<long, int, byte[]?> read)
        => Read(entity, read, DefaultWorldPositionOffset);

    public static Position? Read(long entity, Func<long, int, byte[]?> read, int positionOffset)
    {
        if (positionOffset is not (DefaultWorldPositionOffset or GameHelperWorldPositionOffset))
            return null;
        var component = FindRenderComponent(entity, read);
        return component == 0 ? null : ReadPosition(component, read, positionOffset);
    }

    private static long FindRenderComponent(long entity, Func<long, int, byte[]?> read)
    {
        if (!Plausible(entity) || !TryPointer(read, entity + 8, out var details) ||
            !TryPointer(read, details + 40, out var lookup) ||
            !TryVector(read, lookup + 40, 16, out var namesStart, out var nameCount)) return 0;

        var renderIndex = -1;
        for (var i = 0; i < nameCount; i++)
        {
            var entry = read(namesStart + i * 16L, 16);
            if (entry is not { Length: 16 }) return 0;
            var namePointer = BitConverter.ToInt64(entry, 0);
            var index = BitConverter.ToInt32(entry, 8);
            if (!Plausible(namePointer) || index is < 0 or >= 256) continue;
            var nameBytes = read(namePointer, 64);
            if (nameBytes is not { Length: 64 }) continue;
            var length = Array.IndexOf(nameBytes, (byte)0);
            if (length < 1 || length > 64 ||
                nameBytes.AsSpan(0, length).IndexOfAnyInRange((byte)0, (byte)31) >= 0 ||
                nameBytes.AsSpan(0, length).IndexOfAnyInRange((byte)127, (byte)255) >= 0) continue;
            if (Encoding.ASCII.GetString(nameBytes, 0, length) == "Render")
            {
                renderIndex = index;
                break;
            }
        }
        if (renderIndex < 0 || !TryComponentList(read, entity + 16, out var componentsStart,
                out var componentCount) || renderIndex >= componentCount ||
            !TryPointer(read, componentsStart + renderIndex * 8L, out var component) ||
            !TryPointer(read, component + 8, out var owner) || owner != entity) return 0;
        return component;
    }

    private static Position? ReadPosition(long component, Func<long, int, byte[]?> read,
        int positionOffset)
    {
        var position = read(component + positionOffset, 12);
        if (position is not { Length: 12 }) return null;
        var x = BitConverter.ToSingle(position, 0);
        var y = BitConverter.ToSingle(position, 4);
        var z = BitConverter.ToSingle(position, 8);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return null;
        return new Position(x, y, z, x / WorldToGridRatio, y / WorldToGridRatio,
            $"0x{component:X}");
    }

    private static bool TryVector(Func<long, int, byte[]?> read, long address, int stride,
        out long first, out int count)
    {
        first = 0;
        count = 0;
        var header = read(address, 24);
        if (header is not { Length: 24 }) return false;
        var start = BitConverter.ToInt64(header, 0);
        var last = BitConverter.ToInt64(header, 8);
        var capacity = BitConverter.ToInt64(header, 16);
        if (!Plausible(start) || !Plausible(last) || !Plausible(capacity) ||
            last <= start || capacity < last || (last - start) % stride != 0) return false;
        var length = (last - start) / stride;
        if (length is < 1 or > 256) return false;
        first = start;
        count = (int)length;
        return true;
    }

    private static bool TryComponentList(Func<long, int, byte[]?> read, long address,
        out long first, out int count)
    {
        first = 0;
        count = 0;
        var header = read(address, 16);
        if (header is not { Length: 16 }) return false;
        var start = BitConverter.ToInt64(header, 0);
        var last = BitConverter.ToInt64(header, 8);
        if (!Plausible(start) || !Plausible(last) || last <= start ||
            (last - start) % 8 != 0) return false;
        var length = (last - start) / 8;
        if (length is < 1 or > 256) return false;
        first = start;
        count = (int)length;
        return true;
    }

    private static bool TryPointer(Func<long, int, byte[]?> read, long address, out long value)
    {
        value = 0;
        var bytes = read(address, 8);
        if (bytes is not { Length: 8 }) return false;
        value = BitConverter.ToInt64(bytes, 0);
        return Plausible(value);
    }

    private static bool Plausible(long pointer) => pointer >= 0x10000 && pointer < MaxPointer;
}
