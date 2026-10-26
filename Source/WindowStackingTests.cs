namespace HoverDeck;

internal static class WindowStackingTests
{
    public static void Run(Action<string, bool> check)
    {
        // Z-order front to back: an unrelated app (900), then the big client (1, layer 1) above the small one (2, layer 2).
        var z = new FakeOrder(900, 1, 2);
        var layers = new Dictionary<nint, int> { [1] = 1, [2] = 2 };
        check("A higher layer is moved in front of a lower layer it was behind", WindowStacking.Apply(z, layers) && z.Order.SequenceEqual(new nint[] { 900, 2, 1 }));
        check("Layer ordering keeps other applications in front of the game windows", z.Order[0] == 900);
        int moves = z.Moves; WindowStacking.Apply(z, layers);
        check("A correct layer order is left alone (no constant re-stacking)", z.Moves == moves);
        z.Order.Remove(1); z.Order.Insert(0, 1); // the big client is clicked and jumps to the very front
        WindowStacking.Apply(z, layers);
        check("Clicking the lower-layer client cannot bring it in front of the higher layer", z.Order.SequenceEqual(new nint[] { 2, 1, 900 }));
        var same = new FakeOrder(1, 2); moves = same.Moves;
        check("Clients on the same layer keep normal window behaviour", !WindowStacking.Apply(same, new Dictionary<nint, int> { [1] = 1, [2] = 1 }) && same.Moves == moves);
        var three = new FakeOrder(3, 1, 2);
        WindowStacking.Apply(three, new Dictionary<nint, int> { [1] = 1, [2] = 3, [3] = 2 });
        check("Three layers are stacked strictly by layer", three.Order.SequenceEqual(new nint[] { 2, 3, 1 }));
        var hidden = new FakeOrder(1, 2); hidden.Hidden.Add(2);
        check("Minimized clients are ignored by layer ordering", !WindowStacking.Apply(hidden, layers));

        // Taskbar coverage must lift higher layers together with a covered fullscreen client.
        var big = new GameWindow(601, 61, 2001, @"C:\Fixture\Wow.exe", "Big", new(0, 0, 2560, 1440), false);
        var small = big with { Handle = 602, ProcessId = 62, Started = 2002, Bounds = new(1800, 800, 400, 300) };
        var other = big with { Handle = 603, ProcessId = 63, Started = 2003, Bounds = new(0, 0, 400, 300) };
        var backend = new LayerBackend(big, small, other) { Foreground = big.Handle };
        var saved = new List<WindowLayerSnapshot>(); var coverage = new TaskbarCoverage(backend, values => saved = values);
        var stack = new Dictionary<nint, int> { [big.Handle] = 1, [small.Handle] = 2, [other.Handle] = 1 };
        coverage.Update(new(new[] { big, small, other }, new[] { big }, stack));
        check("Covering the taskbar also lifts clients on higher layers, and only those", backend.Topmost.SetEquals(new[] { big.Handle, small.Handle }) && saved.Count == 2);
        backend.Foreground = 999; coverage.Update(new(new[] { big, small, other }, new[] { big }, stack));
        check("Leaving the game restores the lifted higher-layer clients too", backend.Topmost.Count == 0 && saved.Count == 0);
        backend.Foreground = big.Handle;
        coverage.Update(new(new[] { big, small, other }, Array.Empty<GameWindow>(), stack));
        check("While playing, higher layers stay in the front band even without taskbar coverage (no flicker on click)", backend.Topmost.SetEquals(new[] { small.Handle }) && saved.Count == 1);
        coverage.Update(new(new[] { big, small, other }, Array.Empty<GameWindow>(), new Dictionary<nint, int> { [big.Handle] = 1, [small.Handle] = 1, [other.Handle] = 1 }));
        check("Clients all on one layer are never put in the front band", backend.Topmost.Count == 0 && saved.Count == 0);
        backend.Foreground = 999; coverage.Update(new(new[] { big, small, other }, Array.Empty<GameWindow>(), stack));
        check("Switching to another app takes higher layers out of the front band", backend.Topmost.Count == 0);
        backend.Foreground = big.Handle;
        coverage.Update(new(new[] { big, small, other }, new[] { big }, stack, new HashSet<nint> { big.Handle }));
        check("A full-display fullscreen client under a higher layer is not lifted; the higher layer is", backend.Topmost.SetEquals(new[] { small.Handle }));
        var partial = big with { Bounds = new(0, 0, 1280, 1440) };
        coverage.Update(new(new[] { partial, small, other }, new[] { partial }, stack, new HashSet<nint>()));
        check("A partial-display fullscreen client is still lifted above the taskbar", backend.Topmost.Contains(partial.Handle));
        coverage.Update(new(new[] { big, small, other }, new[] { big }, new Dictionary<nint, int> { [big.Handle] = 1, [small.Handle] = 1, [other.Handle] = 1 }, new HashSet<nint> { big.Handle }));
        check("Without layers, a full-display fullscreen client keeps its taskbar coverage", backend.Topmost.SetEquals(new[] { big.Handle }));
        backend.Foreground = 999; coverage.Update(CoverageTargets.Empty);
    }

    private sealed class FakeOrder(params nint[] frontToBack) : IWindowOrderBackend
    {
        public readonly List<nint> Order = frontToBack.ToList();
        public readonly HashSet<nint> Hidden = new();
        public int Moves;
        public IReadOnlyList<nint> FrontToBack(IReadOnlyCollection<nint> handles) => Order.Where(handles.Contains).ToList();
        public nint WindowAbove(nint handle) { int i = Order.IndexOf(handle); return i > 0 ? Order[i - 1] : 0; }
        public bool IsTopmost(nint handle) => false;
        public bool CanStack(nint handle) => !Hidden.Contains(handle);
        public void PlaceBelow(nint handle, nint above) { Moves++; Order.Remove(handle); Order.Insert(above == 0 ? 0 : Order.IndexOf(above) + 1, handle); }
    }
    private sealed class LayerBackend(params GameWindow[] windows) : IWindowLayerBackend
    {
        private readonly Dictionary<nint, GameWindow> live = windows.ToDictionary(w => w.Handle);
        public readonly HashSet<nint> Topmost = new();
        public nint Foreground { get; set; }
        public bool Exists(WindowLayerSnapshot window) => live.TryGetValue((nint)window.Handle, out var w) && w.ProcessId == window.ProcessId && w.Started == window.Started;
        public bool CanCover(nint handle) => true;
        public bool IsTopmost(nint handle) => Topmost.Contains(handle);
        public bool TaskbarAbove(nint handle) => false;
        public void SetTopmost(nint handle, bool value) { if (value) Topmost.Add(handle); else Topmost.Remove(handle); }
    }
}
