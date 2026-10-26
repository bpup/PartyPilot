using PartyPilot.Desktop;

internal static class AutoPartyInviteSmoke
{
    public static void Run()
    {
        var topology = PartyTopologies.DefaultFor(5);
        if (!AutoPartyInviteNames.TryCreate(topology,
                ["主角", "备用角色", "P3", "P4", "隊員五"], out var names) ||
            names?.ForClient(1) != "主角" || names.ForClient(2) != "P3" ||
            names.ForClient(3) != "隊員五")
            throw new Exception("Auto Party did not choose the first exact name for each client.");
        if (AutoPartyInviteNames.TryCreate(topology,
                ["主角", "备用角色", "P3", "P4", " "], out _) ||
            AutoPartyInviteNames.TryCreate(topology,
                ["主角", "备用角色", "P3", "P4", "Other/Invite"], out _) ||
            AutoPartyInviteNames.TryCreate(topology,
                ["主角", "备用角色", "P3", "P4", "主角"], out _) ||
            AutoPartyInviteNames.TryCreate(topology, ["主角"], out _))
            throw new Exception("Auto Party accepted a missing or unsafe client invite name.");
        if (!PoeChatInviteService.IsValidInviteName("  仁亼几・工丅  ") ||
            PoeChatInviteService.IsValidInviteName("P2\n/invite Other") ||
            PoeChatInviteService.IsValidInviteName("P2\\Other") ||
            PoeChatInviteService.IsValidInviteName(new string('a', 65)) ||
            PoeChatInviteService.IsValidInviteName("  "))
            throw new Exception("Invite-name safety or Unicode handling changed.");
        if (IntPtr.Size == 8 && PoeChatInviteService.NativeInputSize != 40)
            throw new Exception("Native SendInput layout is wrong for Windows x64.");
    }
}
