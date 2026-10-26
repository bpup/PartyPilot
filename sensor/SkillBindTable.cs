using System.Buffers.Binary;

namespace PartyPilot.Sensor;

// Read-only decoding of the 24 bind entries observed in the supplied 0.6.17 build.
// The caller must independently prove the player-data pointer and active offset.
public static class SkillBindTable
{
    private static readonly string[] Buttons =
        ["RB", "RT", "LB", "A", "X", "Y", "B", "", "D-pad Left", "D-pad Up", "D-pad Right", "D-pad Down"];

    public sealed record Entry(int Slot, string Identity, string Kind, string? Button,
        bool ButtonConfirmed, int? GemTile, int? ActionIndex, string? ReservedAction)
    {
        public string? MatchedRawSkillId { get; init; }
        public string? MatchedSkillName { get; init; }
    }

    public sealed record PanelEntry(int Slot, string SlotSource, string RawSkillId, string SkillName);

    public sealed record Result(string Status, Entry[] Entries);

    public static Result DecodeForObservedOwner(ReadOnlySpan<byte> data, long observedOwner,
        long expectedPlayer)
    {
        if (observedOwner < 0x10000 || expectedPlayer < 0x10000 ||
            observedOwner != expectedPlayer)
            return new("owner_unverified", []);
        return Decode(data);
    }

    public static Result Decode(ReadOnlySpan<byte> data, IReadOnlyList<PanelEntry>? panel = null)
    {
        if (data.Length != 148) return new("invalid_length", []);
        var entries = new List<Entry>();
        for (var slot = 0; slot < 24; slot++)
        {
            var id = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice((13 + slot) * 4, 4));
            if (id == 0) continue;
            if ((id & 0x8000_0000) == 0) return new("invalid_entry", []);

            var baseSlot = slot % 12;
            var button = Buttons[baseSlot];
            // The target's discovery layer confirms only the first seven buttons.
            var confirmed = baseSlot <= 6;
            var mappedButton = button.Length == 0 ? null : slot < 12 ? button : "LT+" + button;
            string kind;
            int? gemTile = null;
            int? actionIndex = null;
            string? reservedAction = null;
            if (id >> 24 == 133)
            {
                kind = "gem";
                gemTile = (int)((id >> 16) & 0xff);
                actionIndex = (int)((id >> 12) & 0xf);
            }
            else if (id >> 16 == 32776)
            {
                kind = "inherent_action";
                var action = (int)((id >> 8) & 0xff);
                reservedAction = action switch
                {
                    14 => "Interact",
                    4 => "Dodge Roll",
                    6 => "Town Portal",
                    13 or 27 => "Weapon Swap",
                    _ => null
                };
            }
            else if (((id >> 16) & 0x7f) == 64)
                kind = "weapon_granted";
            else
                kind = "unknown";
            var entry = new Entry(slot, id.ToString("X8"), kind, mappedButton,
                confirmed && mappedButton != null, gemTile, actionIndex, reservedAction);
            if (kind == "gem" && panel != null)
            {
                var matched = panel.FirstOrDefault(item =>
                    item.Slot == (item.SlotSource == "bind" ? slot : gemTile) &&
                    (item.SlotSource == "bind" || item.SlotSource == "panel"));
                if (matched != null && !string.IsNullOrWhiteSpace(matched.RawSkillId))
                    entry = entry with
                    {
                        MatchedRawSkillId = matched.RawSkillId,
                        MatchedSkillName = matched.SkillName
                    };
            }
            entries.Add(entry);
        }
        return entries.Count == 0
            ? new("no_bindings", [])
            : new("decoded_unverified_source", [.. entries]);
    }
}
