using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using GameSubTranslate.App.Overlay;
using GameSubTranslate.App.Settings;
using GameSubTranslate.Capture;
using GameSubTranslate.Config;
using GameSubTranslate.Hotkeys;
using GameSubTranslate.Ocr;
using GameSubTranslate.Pipeline;
using GameSubTranslate.Profiles;
using GameSubTranslate.Storage;
using GameSubTranslate.Translation;

namespace GameSubTranslate.App;

/// <summary>
/// Minimal assert-style self-checks for WPF windows, run via CLI arg.
/// Usage: dotnet run --project src/GameSubTranslate.App -- --selfcheck-t14
/// </summary>
internal static class SelfChecks
{
    public static int Run(string which) => which switch
    {
        "--selfcheck-t14" => SelfCheckT14(),
        "--selfcheck-t15" => SelfCheckT15(),
        "--selfcheck-t18" => SelfCheckT18(),
        "--selfcheck-t19" => SelfCheckT19(),
        "--selfcheck-t22" => SelfCheckT22(),
        "--selfcheck-t23" => SelfCheckT23(),
        "--selfcheck-t25" => SelfCheckT25(),
        "--selfcheck-t35" => SelfCheckT35(),
        _ => SelfCheckT14(),
    };

    private static int SelfCheckT14()
    {
        int fails = 0;
        var w = new OverlayWindow(new AppSettings());

        void Check(bool ok, string what)
        {
            if (ok) return;
            Console.WriteLine($"FAIL: {what}");
            fails++;
        }

        Check(w.WindowStyle == WindowStyle.None, "WindowStyle != None");
        Check(w.Topmost, "Topmost not set");
        Check(!w.ShowInTaskbar, "ShowInTaskbar not hidden");
        Check(w.AllowsTransparency, "AllowsTransparency not set");

        // Show() forces HWND creation → SourceInitialized → click-through style applied.
        w.ShowOverlay();
        var hwnd = new WindowInteropHelper(w).Handle;
        int style = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        Check((style & Win32.WS_EX_TRANSPARENT) != 0, "WS_EX_TRANSPARENT (click-through) not applied");
        Check((style & Win32.WS_EX_LAYERED) != 0, "WS_EX_LAYERED not applied");

        w.Close();
        Console.WriteLine(fails == 0
            ? "PASS: OverlayWindow transparent + topmost + click-through"
            : $"FAIL: {fails} overlay checks failed");
        return fails == 0 ? 0 : 1;
    }

    private static int SelfCheckT15()
    {
        int fails = 0;
        var settings = new AppSettings
        {
            OverlayFontFamily = "Consolas",
            OverlayFontSize = 27,
            OverlayTextColor = "#00FF00",
            OverlayBgColor = "#80102030",
            OverlayOpacity = 0.7,
        };
        var w = new OverlayWindow(settings);

        void Check(bool ok, string what)
        {
            if (ok) return;
            Console.WriteLine($"FAIL: {what}");
            fails++;
        }

        // Show first so elements load + binding attaches, then flush DataBind queue.
        w.ShowOverlay();
        var card = (Border)w.FindName("TextCard");
        var tb = (TextBlock)w.FindName("Subtitle");
        w.ShowText("Halo dunia");
        w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);

        Check(w.ViewModel.Text == "Halo dunia", "ViewModel.Text not set");
        Check(tb.Text == "Halo dunia", "TextBlock binding not updated");
        Check(card.Visibility == Visibility.Visible, "TextCard not visible on non-empty text");
        Check(tb.FontFamily.Source == "Consolas", "FontFamily not applied from settings");
        Check(tb.FontSize == 27, "FontSize not applied from settings");
        Check(w.Opacity == 0.7, "window Opacity not applied from settings");

        // Empty text → card collapses (no empty box floating over the game).
        w.ShowText("");
        Check(card.Visibility == Visibility.Collapsed, "TextCard not collapsed on empty text");

        // Text survives show/hide cycle.
        w.ShowText("Teks bertahan");
        w.HideOverlay();
        Check(w.ViewModel.Text == "Teks bertahan", "text lost after hide/show");

        w.Close();
        Console.WriteLine(fails == 0
            ? "PASS: OverlayWindow text rendering + settings-driven style"
            : $"FAIL: {fails} text/style checks failed");
        return fails == 0 ? 0 : 1;
    }

    private static int SelfCheckT18()
    {
        int fails = 0;

        void Check(bool ok, string what)
        {
            if (ok) return;
            Console.WriteLine($"FAIL: {what}");
            fails++;
        }

        // TryParse: parse "Ctrl+Alt+T" spec back into modifiers + key.
        Check(GlobalHotkeyManager.TryParse("Ctrl+Alt+T", out var mods, out var key),
            "TryParse 'Ctrl+Alt+T' failed");
        Check((mods & ModifierKeys.Control) != 0 && (mods & ModifierKeys.Alt) != 0,
            "Ctrl+Alt modifiers not parsed");
        Check(key == Key.T, "key T not parsed");
        Check(!GlobalHotkeyManager.TryParse("garbage", out _, out _), "TryParse garbage should fail");

        // Register → fire → callback runs. Unregister → fire → callback must NOT run.
        using var mgr = new GlobalHotkeyManager();
        int calls = 0;
        Check(mgr.Register("Test", ModifierKeys.Control | ModifierKeys.Alt, Key.T, () => calls++),
            "Register failed");
        Check(!mgr.Register("Test", ModifierKeys.None, Key.X, () => { }),
            "duplicate id should fail");
        mgr.FireForTest("Test");
        Check(calls == 1, "callback not fired after register");
        Check(mgr.Unregister("Test"), "Unregister failed");
        mgr.FireForTest("Test");
        Check(calls == 1, "callback fired after unregister");

        Console.WriteLine(fails == 0
            ? "PASS: GlobalHotkeyManager register/fire/unregister"
            : $"FAIL: {fails} hotkey checks failed");
        return fails == 0 ? 0 : 1;
    }

    private static int SelfCheckT19()
    {
        int fails = 0;

        void Check(bool ok, string what)
        {
            if (ok) return;
            Console.WriteLine($"FAIL: {what}");
            fails++;
        }

        var overlay = new OverlayWindow(new AppSettings());
        overlay.ShowText("teks bertahan");
        overlay.ShowOverlay();
        Check(overlay.IsVisible, "overlay not visible after ShowOverlay");

        overlay.HideOverlay();
        Check(!overlay.IsVisible, "overlay still visible after HideOverlay");

        // Text state survives hide → shown again.
        overlay.ShowOverlay();
        Check(overlay.IsVisible, "overlay not visible on second Show");
        Check(overlay.ViewModel.Text == "teks bertahan", "text state reset across hide/show");

        overlay.Close();
        Console.WriteLine(fails == 0
            ? "PASS: overlay show/hide toggle keeps text state"
            : $"FAIL: {fails} toggle checks failed");
        return fails == 0 ? 0 : 1;
    }

    private static int SelfCheckT23()
    {
        int fails = 0;
        void Check(bool ok, string what)
        {
            if (ok) return;
            Console.WriteLine($"FAIL: {what}");
            fails++;
        }

        // Hotkey spec round-trips through TryParse/Format (the path Settings "Change" uses).
        Check(GlobalHotkeyManager.TryParse("Ctrl+Alt+T", out var mods, out var key), "parse Ctrl+Alt+T");
        var spec = GlobalHotkeyManager.Format(mods, key);
        Check(spec == "Ctrl+Alt+T", $"Format(parse) != original: got {spec}");
        Check(GlobalHotkeyManager.Format(ModifierKeys.Control | ModifierKeys.Shift, Key.F1) == "Ctrl+Shift+F1",
            "Format Ctrl+Shift+F1 wrong");

        // Settings round-trips including the T23 overlay position fields.
        var dir = Path.Combine(Path.GetTempPath(), "gst-selfcheck-t23");
        Directory.CreateDirectory(dir);
        var store = new SettingsStore(Path.Combine(dir, "settings.json"));
        var s = new AppSettings
        {
            ApiKey = "sk-secret",
            OverlayX = 123.5,
            OverlayY = 456.75,
            HotkeyToggleOverlay = "Ctrl+Shift+U",
            OverlayOpacity = 0.6,
        };
        store.Save(s);
        var back = store.Load();
        Check(back.ApiKey == "sk-secret", "ApiKey DPAPI round-trip failed");
        Check(back.OverlayX == 123.5 && back.OverlayY == 456.75, "OverlayX/Y not persisted");
        Check(back.HotkeyToggleOverlay == "Ctrl+Shift+U", "hotkey not persisted");
        Check(back.OverlayOpacity == 0.6, "opacity not persisted");
        var raw = File.ReadAllText(store.FilePath);
        Check(!raw.Contains("sk-secret"), "ApiKey stored in plaintext");
        File.Delete(store.FilePath);

        // Smoke-test the settings window itself: instantiates all tabs + palette without throwing.
        var settings = new SettingsWindow(overlay: null);
        Check(settings.Tabs.Items.Count == 6, $"expected 6 tabs, got {settings.Tabs.Items.Count}");
        settings.Show();
        settings.Close();

        Console.WriteLine(fails == 0
            ? "PASS: SettingsWindow tabs + hotkey format + settings round-trip"
            : $"FAIL: {fails} settings checks failed");
        return fails == 0 ? 0 : 1;
    }

    private static int SelfCheckT25()
    {
        int fails = 0;
        void Check(bool ok, string what)
        {
            if (ok) return;
            Console.WriteLine($"FAIL: {what}");
            fails++;
        }

        // Watcher logic: simulated foreground exe → matching profile (case-insensitive) fires onProfileLoaded.
        var profiles = new[]
        {
            new GameProfile { Id = 1, Name = "Game A", ExecutableName = "game_a.exe" },
            new GameProfile { Id = 2, Name = "Game B", ExecutableName = "" },
            new GameProfile { Id = 3, Name = "Game C", ExecutableName = "other.exe" },
        };
        var loaded = new List<int>();
        using (var watcher = new ForegroundWatcher(
                   foreground: () => "Game_A.EXE",
                   profiles: () => profiles,
                   onProfileLoaded: id => loaded.Add(id)))
        {
            watcher.Start(intervalMs: 1000);
            Thread.Sleep(1200); // one poll fires
        }
        Check(loaded.SequenceEqual(new[] { 1 }), $"expected profile 1 loaded, got [{string.Join(",", loaded)}]");

        // Same exe twice → fires once (transition-only).
        loaded.Clear();
        using (var watcher = new ForegroundWatcher(
                   foreground: () => "game_a.exe",
                   profiles: () => profiles,
                   onProfileLoaded: id => loaded.Add(id)))
        {
            watcher.Start(intervalMs: 100);
            Thread.Sleep(350); // ~3 polls, same exe → only first fires
        }
        Check(loaded.SequenceEqual(new[] { 1 }), $"transition-only failed: [{string.Join(",", loaded)}]");

        // No match → nothing fires.
        loaded.Clear();
        using (var watcher = new ForegroundWatcher(
                   foreground: () => "unknown.exe",
                   profiles: () => profiles,
                   onProfileLoaded: id => loaded.Add(id)))
        {
            watcher.Start(intervalMs: 100);
            Thread.Sleep(250);
        }
        Check(loaded.Count == 0, $"no-match fired {loaded.Count} time(s)");

        // GetForegroundExe returns a real process name in this environment.
        var fg = ForegroundWatcher.GetForegroundExe();
        Check(!string.IsNullOrWhiteSpace(fg), $"GetForegroundExe returned '{fg}'");

        // MainWindow.SelectProfile wires the selection (via temp DB, no DB pollution).
        // Note: MainWindow restores the last-active profile from AppSettings on construction,
        // so we don't assert a null start — just that SelectProfile lands on the target id.
        var dir = Path.Combine(Path.GetTempPath(), "gst-selfcheck-t25-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var db = new Database(Path.Combine(dir, "profiles.db"));
        db.EnsureSchema();
        var repo = new ProfileRepository(db);
        int pid = repo.Create(new GameProfile { Name = "Sel", ExecutableName = "sel.exe" });
        var w = new MainWindow(db, owner: null);
        w.SelectProfile(pid);
        Check(w.ActiveProfileId() == pid, $"SelectProfile failed (id {w.ActiveProfileId()})");
        w.Close();
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { /* file still locked this run — harmless */ }

        Console.WriteLine(fails == 0
            ? "PASS: ForegroundWatcher exe->profile + MainWindow.SelectProfile"
            : $"FAIL: {fails} auto-load checks failed");
        return fails == 0 ? 0 : 1;
    }

    private static int SelfCheckT22()
    {
        int fails = 0;
        void Check(bool ok, string what)
        {
            if (ok) return;
            Console.WriteLine($"FAIL: {what}");
            fails++;
        }

        var cap = new FakeCapture(() => "subtitle manual");
        var ocr = new FakeOcr(cap);
        var trans = new FakeTranslator();
        var results = new List<string>();
        using var pipe = new TranslatePipeline(cap, ocr, trans, cache: null,
            x: 0, y: 0, w: 100, h: 30, intervalMs: 25, t => results.Add(t));

        // CaptureOnce bypasses the loop (never started) and change detection.
        var result = pipe.CaptureOnceAsync().GetAwaiter().GetResult();
        Check(result == "hasil terjemahan", $"CaptureOnce returned {result}");
        Check(trans.Attempts == 1, $"translate not called exactly once ({trans.Attempts})");
        Check(results.Count == 1 && results[0] == "hasil terjemahan", "onTranslated not fired for manual capture");
        Check(!pipe.IsRunning, "CaptureOnce must not start the loop");

        // Empty frame → null, no translate call.
        var empty = new FakeCapture(() => "");
        var ocrEmpty = new FakeOcr(empty);
        var transEmpty = new FakeTranslator();
        using var pipe2 = new TranslatePipeline(empty, ocrEmpty, transEmpty, cache: null,
            x: 0, y: 0, w: 100, h: 30, intervalMs: 25, _ => { });
        var r2 = pipe2.CaptureOnceAsync().GetAwaiter().GetResult();
        Check(r2 is null && transEmpty.Attempts == 0, $"empty frame should not translate (result={r2}, attempts={transEmpty.Attempts})");

        Console.WriteLine(fails == 0
            ? "PASS: pipeline CaptureOnce bypasses loop + fires callback once"
            : $"FAIL: {fails} CaptureOnce checks failed");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>
    /// T35: profile pipeline under stable (idle) load for N seconds. Sample memory and
    /// handle count at start + every sample-interval. Asserts no unbounded growth —
    /// anything more than 30% growth over the run is flagged (real runs scale this
    /// threshold down). Returns 0 if stable, 1 if leak-shaped.
    ///
    /// Usage: dotnet run --project src/GameSubTranslate.App -- --selfcheck-t35 [seconds] [intervalMs]
    /// Default: 30 seconds, sample every 2 seconds.
    /// </summary>
    private static int SelfCheckT35()
    {
        int fails = 0;
        void Check(bool ok, string what)
        {
            if (ok) return;
            Console.WriteLine($"FAIL: {what}");
            fails++;
        }

        int totalSecs = 30;
        int sampleMs = 2000;
        // crude CLI parse — keep self-check dependency-free
        var cli = Environment.GetCommandLineArgs();
        for (int i = 0; i < cli.Length - 1; i++)
        {
            if (cli[i] == "--selfcheck-t35-secs" && int.TryParse(cli[i + 1], out var s)) totalSecs = s;
            if (cli[i] == "--selfcheck-t35-sample-ms" && int.TryParse(cli[i + 1], out var m)) sampleMs = m;
        }

        var cap = new FakeCapture(() => "stable subtitle line");
        var ocr = new FakeOcr(cap);
        var trans = new FakeTranslator();
        using var pipe = new TranslatePipeline(cap, ocr, trans, cache: null,
            x: 0, y: 0, w: 100, h: 30,
            intervalMs: 50, // tight loop — we want hundreds of iterations in N seconds
            t => { /* swallow */ },
            idleIntervalMs: 50, idleThreshold: 3, idleWindowMs: 1000);

        using var proc = System.Diagnostics.Process.GetCurrentProcess();
        long startMb = proc.WorkingSet64 / (1024 * 1024);
        long startHandles = proc.HandleCount;
        long startGc = GC.GetTotalMemory(forceFullCollection: true);
        Console.WriteLine($"[t35] start: RSS={startMb}MB handles={startHandles} gc={startGc / 1024}KB");

        pipe.Start();
        var deadline = DateTime.UtcNow.AddSeconds(totalSecs);
        long maxMb = startMb, maxHandles = startHandles;
        // Skip the first sample — JIT, module loads, and class init in the first ~1s
        // inflate handle count by ~60 (WPF resource caches). That's startup cost,
        // not a per-tick leak. Real leaks are linear over time.
        bool warmupDone = false;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(sampleMs);
            proc.Refresh();
            long mb = proc.WorkingSet64 / (1024 * 1024);
            if (mb > maxMb) maxMb = mb;
            long h = proc.HandleCount;
            if (h > maxHandles) maxHandles = h;
            if (!warmupDone && (deadline - DateTime.UtcNow).TotalSeconds < totalSecs - 2.0)
            {
                startHandles = h; // rebase after warmup
                warmupDone = true;
            }
            Console.WriteLine($"[t35] +{(deadline - DateTime.UtcNow).TotalSeconds:F0}s: RSS={mb}MB handles={h}");
        }
        pipe.Stop();

        long endMb = proc.WorkingSet64 / (1024 * 1024);
        long endHandles = proc.HandleCount;
        long endGc = GC.GetTotalMemory(forceFullCollection: true);
        Console.WriteLine($"[t35] end:   RSS={endMb}MB handles={endHandles} gc={endGc / 1024}KB");

        // Threshold: RSS may grow up to 30% (GC jitter, JIT, Bitmap reuse). More than that
        // is leak-shaped. Handles should be flat (no native resource accumulation).
        long rssGrowth = endMb - startMb;
        long handleGrowth = endHandles - startHandles;
        Check(rssGrowth <= Math.Max(20, startMb / 3),
            $"RSS grew {rssGrowth}MB (start={startMb}MB, end={endMb}MB, peak={maxMb}MB) — looks like a leak");
        Check(handleGrowth <= 10,
            $"Handles grew {handleGrowth} (start={startHandles}, end={endHandles}, peak={maxHandles}) — native resource leak");

        Console.WriteLine(fails == 0
            ? $"PASS: t35 stable for {totalSecs}s (RSS +{rssGrowth}MB, handles +{handleGrowth})"
            : $"FAIL: {fails} t35 checks failed");
        return fails == 0 ? 0 : 1;
    }

    // ---------- Fakes for pipeline checks (mirrors GameSubTranslate.Prototype.SelfChecks) ----------

    private sealed class FakeCapture : IScreenCapture
    {
        private readonly Func<string> _frame;
        private byte[]? _cached;
        private string? _cachedText;
        public FakeCapture(Func<string> frame) => _frame = frame;
        public byte[] CaptureRegion(int x, int y, int w, int h)
        {
            // Cache one PNG per text value — FakeCapture is the test harness, not the
            // SUT. Allocating a fresh Bitmap per tick pollutes the T35 profile with
            // GDI+ handle noise that has nothing to do with the pipeline.
            var text = _frame();
            if (_cached is null || _cachedText != text)
            {
                _cached = MakeFrame(text);
                _cachedText = text;
            }
            return _cached;
        }
        public string CurrentText() => _frame();
        public void Dispose() { }
    }

    private sealed class FakeOcr : IOcrEngine
    {
        private readonly FakeCapture _cap;
        public FakeOcr(FakeCapture cap) => _cap = cap;
        public string Recognize(byte[] pngBytes) => _cap.CurrentText();
    }

    private sealed class FakeTranslator : TranslationClient
    {
        public int Attempts;
        public string? Result = "hasil terjemahan";
        public FakeTranslator() : base("k", "https://api.example.com", "m", "auto", "id") { }
        // Completed task, no yield: the self-check runs on the WPF UI thread, and awaiting a
        // Task.Yield() would hop back to the (blocked) dispatcher → deadlock.
        public override Task<string?> TranslateAsync(string text, CancellationToken ct)
        {
            Attempts++;
            return Task.FromResult(Result);
        }
    }

    private static byte[] MakeFrame(string text)
    {
        const int w = 400, h = 60;
        using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.White);
            using var font = new System.Drawing.Font("Arial", 18f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.DrawString(text, font, brush, 10, 10);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }
}
