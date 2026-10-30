using System.Runtime.CompilerServices;

namespace PartyPilot.Desktop;

// The supplied radar builds a chamfer signed-distance field over a terrain
// grid reduced to at most 1200 cells on its longest side. Cache that immutable
// field per candidate capture so motion redraws only resample the projection.
internal sealed class RadarTerrainDistanceField
{
    private static readonly ConditionalWeakTable<RadarTerrainCapture, RadarTerrainDistanceField> Cache = new();
    private const ushort Far = 16000;
    private readonly float[] _distance;
    private readonly int _width;
    private readonly int _height;

    public int Step { get; }

    public static RadarTerrainDistanceField For(RadarTerrainCapture capture) =>
        Cache.GetValue(capture, static source => new RadarTerrainDistanceField(source));

    private RadarTerrainDistanceField(RadarTerrainCapture capture)
    {
        Step = Math.Max(1, (Math.Max(capture.Width, capture.Height) + 1199) / 1200);
        var columns = (capture.Width + Step - 1) / Step;
        var rows = (capture.Height + Step - 1) / Step;
        _width = columns + 2;
        _height = rows + 2;
        var occupancy = new byte[_width * _height];
        for (var row = 0; row < rows; row++)
        {
            var sourceY = row * Step;
            var endY = Math.Min(sourceY + Step, capture.Height);
            for (var column = 0; column < columns; column++)
            {
                var sourceX = column * Step;
                var endX = Math.Min(sourceX + Step, capture.Width);
                var occupied = false;
                for (var y = sourceY; y < endY && !occupied; y++)
                for (var x = sourceX; x < endX; x++)
                {
                    if (!capture.Renderable(x, y)) continue;
                    occupied = true;
                    break;
                }
                if (occupied) occupancy[(row + 1) * _width + column + 1] = 1;
            }
        }
        var toEmpty = Chamfer(occupancy, 0);
        var toOccupied = Chamfer(occupancy, 1);
        _distance = new float[occupancy.Length];
        for (var index = 0; index < occupancy.Length; index++)
        {
            var value = (occupancy[index] != 0 ? toEmpty[index] : toOccupied[index]) / 3f - 0.5f;
            _distance[index] = occupancy[index] != 0 ? value : -value;
        }
    }

    public double Sample(double rawX, double rawY)
    {
        // The existing projection treats raw integer coordinates as cell
        // corners; signed-distance samples are centred on occupancy cells.
        var x = (rawX - 0.5) / Step + 1;
        var y = (rawY - 0.5) / Step + 1;
        var column = Math.Clamp((int)Math.Floor(x), 0, _width - 2);
        var row = Math.Clamp((int)Math.Floor(y), 0, _height - 2);
        var fx = Math.Clamp(x - column, 0, 1);
        var fy = Math.Clamp(y - row, 0, 1);
        var top = _distance[row * _width + column] * (1 - fx) +
            _distance[row * _width + column + 1] * fx;
        var bottom = _distance[(row + 1) * _width + column] * (1 - fx) +
            _distance[(row + 1) * _width + column + 1] * fx;
        return top * (1 - fy) + bottom * fy;
    }

    private ushort[] Chamfer(byte[] occupancy, byte source)
    {
        var result = new ushort[occupancy.Length];
        for (var index = 0; index < result.Length; index++)
            result[index] = occupancy[index] == source ? (ushort)0 : Far;
        for (var y = 0; y < _height; y++)
        for (var x = 0; x < _width; x++)
        {
            var index = y * _width + x;
            var value = result[index];
            if (x > 0) value = Math.Min(value, Add(result[index - 1], 3));
            if (y > 0)
            {
                value = Math.Min(value, Add(result[index - _width], 3));
                if (x > 0) value = Math.Min(value, Add(result[index - _width - 1], 4));
                if (x + 1 < _width) value = Math.Min(value, Add(result[index - _width + 1], 4));
            }
            result[index] = value;
        }
        for (var y = _height - 1; y >= 0; y--)
        for (var x = _width - 1; x >= 0; x--)
        {
            var index = y * _width + x;
            var value = result[index];
            if (x + 1 < _width) value = Math.Min(value, Add(result[index + 1], 3));
            if (y + 1 < _height)
            {
                value = Math.Min(value, Add(result[index + _width], 3));
                if (x > 0) value = Math.Min(value, Add(result[index + _width - 1], 4));
                if (x + 1 < _width) value = Math.Min(value, Add(result[index + _width + 1], 4));
            }
            result[index] = value;
        }
        return result;
    }

    private static ushort Add(ushort value, int amount) => (ushort)Math.Min(Far, value + amount);
}
