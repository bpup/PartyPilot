using PartyPilot.Bridge;

internal static class ProcessLocalBindingPlanTests
{
    internal static void Run()
    {
        var games = new[] { @"C:\PoE2\PathOfExile_x64.exe",
            @"D:\PoE2\PathOfExile_x64.exe", @"E:\PoE2\PathOfExile_x64.exe" };
        var args = new[] { "--process-local-bindings",
            "1:101:1001:1,2;2:102:1002:3,4;3:103:1003:5,6",
            "--process-local-adopt", "1,3" };
        if (!ProcessLocalBindingPlan.TryParse(args, games, [1, 2, 3, 4, 5, 6],
                out var plan) || plan is null || plan.Bindings.Length != 3 ||
            !plan.Bindings[0].AdoptExisting || plan.Bindings[1].AdoptExisting ||
            !plan.Bindings[2].AdoptExisting ||
            !plan.SlotsByClient[2].SequenceEqual([3, 4]))
            throw new Exception("Three process-local clients were not bound to six distinct slots.");
        var singleArgs = new[] { "--process-local-bindings", "1:101:1001:1;2:102:1002:3" };
        if (!ProcessLocalBindingPlan.TryParse(singleArgs, games[..2], [1, 3],
                out var singles) || singles?.Bindings.Length != 2 ||
            singles.Bindings[0].Slots.Length != 1 || singles.Bindings[1].AdoptExisting)
            throw new Exception("Two single-slot clients were rejected.");
        foreach (var invalid in new[]
        {
            new[] { "--process-local-bindings", "1:101:1001:1,2;2:101:1002:3,4;3:103:1003:5,6" },
            new[] { "--process-local-bindings", "1:101:1001:1,2;2:102:1002:4,3;3:103:1003:5,6" },
            new[] { "--process-local-bindings", "1:101:1001:1,2;3:103:1003:5,6;2:102:1002:3,4" },
            new[] { "--process-local-bindings", "1:101:1001:1,2;2:102:1002:3,4;3:103:1003:5,6",
                "--process-local-adopt", "1,1" },
            new[] { "--process-local-bindings", "1:101:1001:1,2;2:102:1002:3,4;3:103:1003:5,6",
                "--process-local-adopt", "4" },
            new[] { "--process-local-adopt", "1" }
        })
            if (ProcessLocalBindingPlan.TryParse(invalid, games, [1, 2, 3, 4, 5, 6], out _))
                throw new Exception("Invalid process-local binding was accepted.");
    }
}
