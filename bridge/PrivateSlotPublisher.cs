using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace PartyPilot.Bridge;

// Managed producer for a future PartyPilot-owned process-local XInput runtime.
// The runtime acknowledges Loaded before XInput appears, then Ready after hooks bind.
internal sealed class PrivateSlotPublisher : IDisposable
{
    private readonly object _sync = new();
    private readonly MemoryMappedFile? _mapping;
    private readonly MemoryMappedFile? _configMapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly MemoryMappedViewAccessor? _configView;
    private readonly Func<bool> _processAlive;
    private readonly uint _connected;
    private bool _disposed;

    private PrivateSlotPublisher(MemoryMappedFile? mapping, MemoryMappedViewAccessor view,
        MemoryMappedFile? configMapping, MemoryMappedViewAccessor? configView,
        Func<bool> processAlive, bool includeP6)
    {
        _mapping = mapping;
        _view = view;
        _configMapping = configMapping;
        _configView = configView;
        _processAlive = processAlive;
        _connected = includeP6 ? PrivateSlotFrame.P5 | PrivateSlotFrame.P6 : PrivateSlotFrame.P5;
        var initial = new byte[PrivateSlotFrame.Size];
        PrivateSlotFrame.Initialize(initial, includeP6);
        _view.WriteArray(0, initial, 0, initial.Length);
        WritePadCore(5, default);
        if (includeP6) WritePadCore(6, default);
    }

    internal static PrivateSlotPublisher OpenForProcess(Process gameProcess, bool includeP6,
        int p5LocalIndex, int? p6LocalIndex)
    {
        ArgumentNullException.ThrowIfNull(gameProcess);
        if (!OperatingSystem.IsWindows() || gameProcess.Id <= 0 || gameProcess.HasExited)
            throw new InvalidOperationException("A live Windows Client 3 process is required.");
        // The caller must already have bound this Process object to Client 3.
        var config = PrivateSlotConfig.Encode(includeP6, p5LocalIndex, p6LocalIndex);
        var configMapping = MemoryMappedFile.CreateNew($"Local\\PartyPilot.PrivateSlots.Config.{gameProcess.Id}",
            PrivateSlotConfig.Size, MemoryMappedFileAccess.ReadWrite);
        MemoryMappedViewAccessor? configView = null;
        MemoryMappedFile? mapping = null;
        MemoryMappedViewAccessor? view = null;
        try
        {
            configView = configMapping.CreateViewAccessor(0, PrivateSlotConfig.Size, MemoryMappedFileAccess.ReadWrite);
            configView.WriteArray(0, config, 0, config.Length);
            mapping = MemoryMappedFile.CreateNew($"Local\\PartyPilot.PrivateSlots.{gameProcess.Id}",
                PrivateSlotFrame.Size, MemoryMappedFileAccess.ReadWrite);
            view = mapping.CreateViewAccessor(0, PrivateSlotFrame.Size, MemoryMappedFileAccess.ReadWrite);
            return new PrivateSlotPublisher(mapping, view, configMapping, configView,
                () => !gameProcess.HasExited, includeP6);
        }
        catch
        {
            view?.Dispose();
            mapping?.Dispose();
            configView?.Dispose();
            configMapping.Dispose();
            throw;
        }
    }

    // Anonymous view keeps the memory protocol testable without Windows.
    internal static PrivateSlotPublisher CreateForTest(MemoryMappedViewAccessor view,
        Func<bool> processAlive, bool includeP6) => new(null, view, null, null, processAlive, includeP6);

    internal bool IsReady
    {
        get
        {
            lock (_sync) return !_disposed && _processAlive() && HasValidFrame() &&
                _view.ReadInt32(12) == PrivateSlotFrame.Ready;
        }
    }

    internal bool IsLoaded
    {
        get
        {
            lock (_sync) return !_disposed && _processAlive() && HasValidFrame() &&
                _view.ReadInt32(12) is PrivateSlotFrame.Loaded or PrivateSlotFrame.Ready;
        }
    }

    internal int Status
    {
        get
        {
            lock (_sync) return !_disposed && _processAlive() && HasValidFrame()
                ? _view.ReadInt32(12) : 0;
        }
    }

    internal bool WritePad(int player, PadReport report)
    {
        lock (_sync)
        {
            if (_disposed || player is < 5 or > 6 ||
                (_connected & (player == 5 ? PrivateSlotFrame.P5 : PrivateSlotFrame.P6)) == 0 ||
                !_processAlive() || !HasValidFrame() || _view.ReadInt32(12) != PrivateSlotFrame.Ready)
                return false;
            WritePadCore(player, report);
            return true;
        }
    }

    internal void NeutralAll()
    {
        lock (_sync)
        {
            if (_disposed || !HasValidFrame()) return;
            WritePadCore(5, default);
            if ((_connected & PrivateSlotFrame.P6) != 0) WritePadCore(6, default);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (HasValidFrame())
            {
                WritePadCore(5, default);
                if ((_connected & PrivateSlotFrame.P6) != 0) WritePadCore(6, default);
                Thread.MemoryBarrier();
                _view.Write(12, PrivateSlotFrame.Stopping);
            }
            _disposed = true;
            _view.Dispose();
            _mapping?.Dispose();
            _configView?.Dispose();
            _configMapping?.Dispose();
        }
    }

    private bool HasValidFrame()
    {
        var header = new byte[PrivateSlotFrame.Size];
        _view.ReadArray(0, header, 0, header.Length);
        return PrivateSlotFrame.HasHeader(header) && _view.ReadUInt32(16) == _connected;
    }

    private void WritePadCore(int player, PadReport report)
    {
        var offset = 20 + (player - 5) * 28;
        var previous = _view.ReadUInt32(offset);
        if ((previous & 1) != 0) throw new InvalidOperationException("Private slot write was interrupted.");
        _view.Write(offset, previous + 1);
        Thread.MemoryBarrier();
        _view.Write(offset + 4, _view.ReadUInt32(offset + 4) + 1);
        _view.Write(offset + 8, report.Buttons);
        _view.Write(offset + 10, report.LeftTrigger);
        _view.Write(offset + 11, report.RightTrigger);
        _view.Write(offset + 12, report.LeftX);
        _view.Write(offset + 14, report.LeftY);
        _view.Write(offset + 16, report.RightX);
        _view.Write(offset + 18, report.RightY);
        _view.Write(offset + 20, unchecked((ulong)Environment.TickCount64));
        Thread.MemoryBarrier();
        _view.Write(offset, previous + 2);
    }
}
