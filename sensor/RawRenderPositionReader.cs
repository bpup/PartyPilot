namespace PartyPilot.Sensor;

// These are the public default offsets in the supplied build. The target may
// activate a different profile at runtime, so results are diagnostic only.
public static class RawRenderPositionReader
{
    public sealed record Position(float WorldX, float WorldY, float WorldZ,
        float GridX, float GridY, string RenderComponentAddress);
    public sealed record LocalComparison(float ObservedX, float ObservedY, float ObservedZ,
        double WorldXYDistance, double GridXYDistance);

    private const float WorldToGridRatio = 10.869565f;
    public const int DefaultWorldPositionOffset = 312;
    public const int GameHelperWorldPositionOffset = 184;

    // The supplied probe reads the active default layout at +312. Prefer it
    // when both static layouts agree with the independently observed player;
    // the alternate layout remains a candidate only if the default disagrees.
    public static int? SelectObservedOffset(long localEntity,
        Func<long, int, byte[]?> read, float observedX, float observedY, float observedZ)
    {
        var component = RawEntityComponentReader.Find(localEntity, "Render", read);
        if (component == 0) return null;
        var normal = ReadPosition(component, read, DefaultWorldPositionOffset);
        var helper = ReadPosition(component, read, GameHelperWorldPositionOffset);
        var normalMatches = normal is not null &&
            CompareLocal(normal, observedX, observedY, observedZ) is { WorldXYDistance: 0 };
        var helperMatches = helper is not null &&
            CompareLocal(helper, observedX, observedY, observedZ) is { WorldXYDistance: 0 };
        if (normalMatches) return DefaultWorldPositionOffset;
        return helperMatches ? GameHelperWorldPositionOffset : null;
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
        var component = RawEntityComponentReader.Find(entity, "Render", read);
        return component == 0 ? null : ReadPosition(component, read, positionOffset);
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

}
