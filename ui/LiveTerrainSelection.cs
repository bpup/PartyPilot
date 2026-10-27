namespace PartyPilot.Desktop;

internal static class LiveTerrainSelection
{
    internal static bool TryCreate(PartyTopology topology, IReadOnlyList<string?> paths,
        bool classicCollisionDodge, out IReadOnlyDictionary<int, string> bindings, out string error)
    {
        bindings = new Dictionary<int, string>();
        error = "";
        var orderedTopology = topology.Id is "4-balanced" or "6-balanced";
        if (topology.Id is not ("2-split" or "3-three-singles" or "4-balanced" or "6-balanced") ||
            (!orderedTopology && topology.ClientCount != topology.Slots.Count) ||
            paths.Count < topology.ClientCount)
        {
            error = "当前布局没有可核对的逐客户端地形绑定。";
            return false;
        }
        var selected = paths.Take(topology.ClientCount).Select(path => path?.Trim() ?? "").ToArray();
        if (orderedTopology)
        {
            if (selected.All(string.IsNullOrEmpty) && !classicCollisionDodge) return true;
            error = "双角色客户端的跟随地形绑定尚未核验；请清空地形路径并关闭经典碰撞闪避。";
            return false;
        }
        if (selected.All(string.IsNullOrEmpty))
        {
            if (!classicCollisionDodge) return true;
            error = "启用碰撞闪避前，须为每个客户端填写地形绑定文件。";
            return false;
        }
        if (selected.Any(string.IsNullOrEmpty))
        {
            error = "地形模式须为当前每个客户端分别填写绑定文件。";
            return false;
        }
        if (selected.Any(path => !Path.IsPathFullyQualified(path) || !File.Exists(path)) ||
            selected.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
        {
            error = "地形绑定须是每个客户端各自存在的绝对路径，且不能重复。";
            return false;
        }
        bindings = topology.Slots.ToDictionary(slot => slot.PartyPlayer,
            slot => selected[slot.Client - 1]);
        return true;
    }
}
