using PartyPilot.Sensor;

internal static class UiItemCardTreeScannerTests
{
    internal static void Run()
    {
        const long image = 0x10000000;
        const long state = 0x20000;
        const long manager = 0x30000;
        const long root = 0x40000;
        const long container = 0x50000;
        const long card = 0x60000;
        const long context = 0x70000;
        const long item = 0x80000;
        const long owner = 0x90000;
        const long cardRva = 0x2000;
        var memory = new Dictionary<long, byte>();

        void Write(long address, long value)
        {
            foreach (var (part, index) in BitConverter.GetBytes(value).Select((part, index) => (part, index)))
                memory[address + index] = part;
        }
        byte[]? Read(long address, int size)
        {
            if (!memory.ContainsKey(address)) return null;
            return Enumerable.Range(0, size).Select(index => memory.GetValueOrDefault(address + index)).ToArray();
        }
        void Node(long address, long vtable, long parent, params long[] children)
        {
            foreach (var offset in Enumerable.Range(0, 368)) memory[address + offset] = 0;
            Write(address, vtable);
            Write(address + 8, address);
            Write(address + 136, 848);
            Write(address + 184, parent);
            Write(address + 360, 0x800);
            Write(address + 16, address + 512);
            Write(address + 24, address + 512 + children.Length * 8);
            foreach (var (child, index) in children.Select((child, index) => (child, index)))
                Write(address + 512 + index * 8, child);
        }
        UiItemCardTreeScanner.Result Scan(Func<long, int, byte[]?>? reader = null) =>
            UiItemCardTreeScanner.Scan(reader ?? Read, image, 0x10000, state, cardRva);
        void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        Write(state + 784, manager);
        Write(manager + 1672, root);
        Node(root, image + 0x1000, manager, container);
        Write(root + 144, manager);
        Node(container, image + 0x1000, root, card);
        Node(card, image + cardRva, container);
        Write(card + 752, context);
        Write(card + 760, item);
        Write(context + 768, owner);
        Require(Scan().Status == "ok" && Scan().Cards.Single() == new UiItemCardTreeScanner.Card(card, item, owner),
            "Visible attached card with item and owner was not found.");
        Write(card + 360, 0);
        Require(Scan().Cards.Length == 0, "Hidden card was accepted.");
        Write(card + 360, 0x800);
        Write(card + 184, root);
        Require(Scan().Cards.Length == 0, "Detached card was accepted.");
        Write(card + 184, container);
        Write(card, image + 0x3000);
        Require(Scan().Cards.Length == 0, "Recycled card class was accepted.");
        Write(card, image + cardRva);
        Write(root + 360, 0);
        Require(Scan().Status == "root_unavailable", "Hidden root retained old card authority.");
        Write(root + 360, 0x800);
        Write(container + 24, container + 512 + 8192 + 8);
        Require(Scan().Cards.Length == 0, "Unbounded child vector was accepted.");
        Write(container + 24, container + 520);
        Require(Scan((address, size) =>
        {
            var result = Read(address, size);
            if (address == context + 768) Write(card + 760, item + 0x1000);
            return result;
        }).Cards.Length == 0, "Card payload changed during read but was accepted.");
        Console.WriteLine("UI item-card tree scanner passed synthetic read-only checks.");
    }
}
