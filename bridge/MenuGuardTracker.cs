namespace PartyPilot.Bridge;

internal enum MenuGuardPhase
{
    Clear,
    MenuDetected,
    Settling
}

internal readonly record struct MenuGuardChange(int Slot, MenuGuardPhase Phase);

// Tracks only menu controls seen on the physical pad. Other ways of opening a
// game menu require an independent UI signal before automation can be armed.
internal sealed class MenuGuardTracker
{
    private sealed class SlotState
    {
        public MenuGuardPhase Phase;
        public DateTime SettlesAtUtc;
    }

    private readonly Dictionary<int, SlotState> _slots = new();
    private int _observedSlot;
    private bool _lastMenu;
    private bool _lastB;

    public bool AnyActive => _slots.Values.Any(state => state.Phase != MenuGuardPhase.Clear);

    public void Reset()
    {
        _slots.Clear();
        _observedSlot = 0;
        _lastMenu = false;
        _lastB = false;
    }

    public IReadOnlyList<MenuGuardChange> Observe(int slot, ushort buttons, DateTime nowUtc)
    {
        if (slot is < 1 or > 6 || nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A paired virtual pad and UTC observation are required.");
        var changes = Advance(nowUtc).ToList();
        const ushort back = 0x0020;
        const ushort start = 0x0010;
        const ushort b = 0x2000;
        var menu = (buttons & (back | start)) is back or start;
        var bDown = (buttons & b) != 0;
        if (slot != _observedSlot)
        {
            // A held button must not open a different player's menu on focus change.
            _observedSlot = slot;
            _lastMenu = menu;
            _lastB = bDown;
            return changes;
        }
        var state = GetSlot(slot);
        if (menu && !_lastMenu)
        {
            if (state.Phase == MenuGuardPhase.MenuDetected)
                SetPhase(slot, state, MenuGuardPhase.Settling, nowUtc, changes);
            else
                SetPhase(slot, state, MenuGuardPhase.MenuDetected, nowUtc, changes);
        }
        else if (bDown && !_lastB && state.Phase == MenuGuardPhase.MenuDetected)
        {
            SetPhase(slot, state, MenuGuardPhase.Settling, nowUtc, changes);
        }
        _lastMenu = menu;
        _lastB = bDown;
        return changes;
    }

    public IReadOnlyList<MenuGuardChange> Advance(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC observation is required.");
        var changes = new List<MenuGuardChange>();
        foreach (var (slot, state) in _slots)
        {
            if (state.Phase == MenuGuardPhase.Settling && nowUtc >= state.SettlesAtUtc)
            {
                state.Phase = MenuGuardPhase.Clear;
                changes.Add(new MenuGuardChange(slot, state.Phase));
            }
        }
        return changes;
    }

    private SlotState GetSlot(int slot)
    {
        if (!_slots.TryGetValue(slot, out var state))
            _slots[slot] = state = new SlotState();
        return state;
    }

    private static void SetPhase(int slot, SlotState state, MenuGuardPhase phase,
        DateTime nowUtc, List<MenuGuardChange> changes)
    {
        state.Phase = phase;
        if (phase == MenuGuardPhase.Settling)
            state.SettlesAtUtc = nowUtc.AddMilliseconds(650);
        changes.Add(new MenuGuardChange(slot, phase));
    }
}
