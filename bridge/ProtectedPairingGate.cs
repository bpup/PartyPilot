namespace PartyPilot.Bridge;

internal enum ProtectedPairingPhase { Idle, Preparing, Pulsing, InGame, Settling, Complete }

internal readonly record struct ProtectedPairingObservation(XInputGamepad Forwarded, int ReleasedSlot, bool Completed);

// Login readiness is supplied by the visual tracker or its explicitly marked timeout path.
internal sealed class ProtectedPairingGate
{
    private const ushort A = 0x1000;
    private const ushort FocusHotkeys = 0x00c0;
    private static readonly TimeSpan ArmSettle = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan NeutralSettle = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FinalSettle = TimeSpan.FromMilliseconds(400);

    private int _slot;
    private DateTime? _foregroundSince;
    private DateTime? _neutralSince;
    private DateTime _pulseUntil;
    private bool _wasADown;
    private bool _requireRelease;

    public ProtectedPairingPhase Phase { get; private set; } = ProtectedPairingPhase.Idle;

    public bool Arm(int slot)
    {
        if (slot is < 1 or > 4 || Phase is not (ProtectedPairingPhase.Idle or ProtectedPairingPhase.InGame)) return false;
        _slot = slot;
        _foregroundSince = null;
        _neutralSince = null;
        _wasADown = false;
        _requireRelease = true;
        Phase = ProtectedPairingPhase.Preparing;
        return true;
    }

    public bool Finish()
    {
        if (Phase != ProtectedPairingPhase.InGame) return false;
        Phase = ProtectedPairingPhase.Settling;
        _neutralSince = null;
        return true;
    }

    public ProtectedPairingObservation Observe(XInputGamepad physical, DateTime nowUtc,
        bool targetForeground, bool loginReady)
    {
        var aDown = (physical.Buttons & A) != 0;
        var neutral = IsNeutral(physical);
        switch (Phase)
        {
            case ProtectedPairingPhase.Preparing:
            {
                if (!targetForeground)
                {
                    _foregroundSince = null;
                    _neutralSince = null;
                    if (aDown) _requireRelease = true;
                    _wasADown = aDown;
                    return default;
                }
                _foregroundSince ??= nowUtc;
                if (!loginReady)
                {
                    _neutralSince = null;
                    if (aDown) _requireRelease = true;
                    _wasADown = aDown;
                    return default;
                }
                if (_requireRelease)
                {
                    if (neutral) { _requireRelease = false; _neutralSince = nowUtc; }
                    else _neutralSince = null;
                }
                else if (neutral)
                {
                    _neutralSince ??= nowUtc;
                }
                else if (!aDown)
                {
                    _neutralSince = null;
                }
                var risingA = aDown && !_wasADown;
                _wasADown = aDown;
                if (risingA && !_requireRelease && _neutralSince.HasValue &&
                    nowUtc - _foregroundSince.Value >= ArmSettle && nowUtc - _neutralSince.Value >= NeutralSettle)
                {
                    Phase = ProtectedPairingPhase.Pulsing;
                    _pulseUntil = nowUtc + TimeSpan.FromMilliseconds(_slot % 2 == 0 ? 24 : 350);
                    _requireRelease = true;
                    return new ProtectedPairingObservation(new XInputGamepad { Buttons = A }, 0, false);
                }
                if (risingA) { _requireRelease = true; _neutralSince = null; }
                return default;
            }
            case ProtectedPairingPhase.Pulsing:
                if (!targetForeground)
                {
                    Phase = ProtectedPairingPhase.Preparing;
                    _foregroundSince = null;
                    _neutralSince = null;
                    _requireRelease = true;
                    _wasADown = aDown;
                    return default;
                }
                _wasADown = aDown;
                if (nowUtc < _pulseUntil)
                    return new ProtectedPairingObservation(new XInputGamepad { Buttons = A }, 0, false);
                Phase = ProtectedPairingPhase.InGame;
                var releasedSlot = _slot;
                return new ProtectedPairingObservation(MaskHeldAAndFocus(physical), releasedSlot, false);
            case ProtectedPairingPhase.InGame:
                if (!targetForeground)
                {
                    _requireRelease = true;
                    _wasADown = aDown;
                    return default;
                }
                if (!aDown) _requireRelease = false;
                _wasADown = aDown;
                return new ProtectedPairingObservation(MaskHeldAAndFocus(physical), 0, false);
            case ProtectedPairingPhase.Settling:
                if (!neutral) _neutralSince = null;
                else _neutralSince ??= nowUtc;
                if (_neutralSince.HasValue && nowUtc - _neutralSince.Value >= FinalSettle)
                {
                    Phase = ProtectedPairingPhase.Complete;
                    return new ProtectedPairingObservation(physical, 0, true);
                }
                return default;
            case ProtectedPairingPhase.Complete:
                return new ProtectedPairingObservation(physical, 0, false);
            default:
                return default;
        }
    }

    private XInputGamepad MaskHeldAAndFocus(XInputGamepad physical)
    {
        physical.Buttons &= unchecked((ushort)~FocusHotkeys);
        if (_requireRelease) physical.Buttons &= unchecked((ushort)~A);
        return physical;
    }

    private static bool IsNeutral(XInputGamepad pad) =>
        pad.Buttons == 0 && pad.LeftTrigger <= 30 && pad.RightTrigger <= 30 &&
        Math.Abs((int)pad.LeftX) <= 8000 && Math.Abs((int)pad.LeftY) <= 8000 &&
        Math.Abs((int)pad.RightX) <= 8000 && Math.Abs((int)pad.RightY) <= 8000;
}
