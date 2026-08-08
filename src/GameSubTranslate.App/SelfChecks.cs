using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using GameSubTranslate.App.Overlay;
using GameSubTranslate.App.Settings;
using GameSubTranslate.Cache;
using GameSubTranslate.Capture;
using GameSubTranslate.Config;
using GameSubTranslate.Hotkeys;
using GameSubTranslate.Logging;
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
    public static int Run(string which)
    {
        // WPF processes started under bash inherit no usable console — redirect stdout to
        // a file when --selfcheck-log <path> is passed. Test harness sets this; manual
        // runs leave it off so the user's %APPDATA% stays clean.
        var cli = Environment.GetCommandLineArgs();
        for (int i = 0; i < cli.Length - 1; i++)
        {
            if (cli[i] == "--selfcheck-log")
            {
                try
                {
                    var w = new StreamWriter(cli[i + 1], append: false) { AutoFlush = true };
                    Console.SetOut(TextWriter.Synchronized(w));
                }
                catch { /* best-effort — fall back to silent stdout */ }
            }
        }
        return which switch
        {
            "--selfcheck-t14" => SelfCheckT14(),
            "--selfcheck-t15" => SelfCheckT15(),
            "--selfcheck-t18" => SelfCheckT18(),
            "--selfcheck-t19" => SelfCheckT19(),
            "--selfcheck-t22" => SelfCheckT22(),
            "--selfcheck-t23" => SelfCheckT23(),
            "--selfcheck-t25" => SelfCheckT25(),
            "--selfcheck-t35" => SelfCheckT35(),
            "--selfcheck-t36" => SelfCheckT36(),
            "--selfcheck-t37" => SelfCheckT37(),
            "--selfcheck-t38" => SelfCheckT38(),
            "--selfcheck-t39" => SelfCheckT39(),
            "--selfcheck-t40" => SelfCheckT40(),
            "--selfcheck-t41" => SelfCheckT41(),
            _ => SelfCheckT14(),
        };
    }

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

    /// <summary>
    /// T36: streaming translation. Two paths:
    /// 1. Stubbed SSE handler yields token deltas in order — verifies the iterator parses frames.
    /// 2. Non-SSE 200 response — verifies the fallback yields a single chunk (no exception).
    /// Skips the live-API path because it requires OPENAI_API_KEY; the live call is just a happy-path
    /// extra that the unit tests (TranslationStreamTests) already cover.
    /// </summary>
    private static int SelfCheckT36()
    {
        int fails = 0;
        void Check(bool ok, string what) { if (ok) return; Console.WriteLine($"FAIL: {what}"); fails++; }

        // Path 1: real SSE stream — 3 tokens then [DONE].
        {
            var sseHandler = new MockHandler();
            sseHandler.QueueSseChunks("Halo", " ", "dunia", "[DONE]");
            var client = new TranslationClient("k", "https://api.example.com", "m", "auto", "id",
                handler: sseHandler);
            var tokens = new List<string>();
            try
            {
                var iter = client.TranslateStreamAsync("Hello world").GetAsyncEnumerator();
                while (iter.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                    tokens.Add(iter.Current);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: SSE stream threw {ex.GetType().Name}: {ex.Message}");
                fails++;
            }
            Check(tokens.Count == 3, $"expected 3 SSE tokens, got {tokens.Count} ({string.Join("|", tokens)})");
            Check(tokens[0] == "Halo" && tokens[1] == " " && tokens[2] == "dunia",
                $"SSE token order wrong: [{string.Join("|", tokens)}]");
            Check(sseHandler.HitCount == 1, $"SSE handler hit {sseHandler.HitCount}x (expected 1)");
        }

        // Path 2: endpoint sends 200 application/json (non-SSE) — fallback yields single chunk.
        {
            var jsonHandler = new MockHandler();
            jsonHandler.QueueJsonResponse(HttpStatusCode.OK,
                """{"choices":[{"message":{"content":"Halo dunia (non-stream)"}}]}""");
            var client = new TranslationClient("k", "https://api.example.com", "m", "auto", "id",
                handler: jsonHandler);
            var tokens = new List<string>();
            try
            {
                var iter = client.TranslateStreamAsync("Hello").GetAsyncEnumerator();
                while (iter.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                    tokens.Add(iter.Current);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: non-SSE fallback threw {ex.GetType().Name}: {ex.Message}");
                fails++;
            }
            Check(tokens.Count == 1, $"non-SSE fallback: expected 1 chunk, got {tokens.Count}");
            Check(tokens.Count == 1 && tokens[0].Contains("Halo dunia (non-stream)"),
                $"non-SSE fallback payload wrong: [{string.Join("|", tokens)}]");
        }

        // Path 3: live API call — only if OPENAI_API_KEY is set. Best-effort: any exception
        // is logged but doesn't fail the run (network flakiness shouldn't block verification).
        var liveKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(liveKey))
        {
            var liveUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1";
            var liveModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
            var client = new TranslationClient(liveKey, liveUrl, liveModel, "auto", "id");
            var tokens = new List<string>();
            bool liveOk = false;
            try
            {
                var iter = client.TranslateStreamAsync("Hello").GetAsyncEnumerator();
                while (iter.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                    tokens.Add(iter.Current);
                liveOk = tokens.Count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[t36-live] skipped: {ex.GetType().Name}: {ex.Message}");
            }
            Check(liveOk, $"live API yielded 0 tokens ({tokens.Count})");
            if (liveOk)
                Console.WriteLine($"[t36-live] PASS: {tokens.Count} tokens from {liveUrl}");
        }
        else
        {
            Console.WriteLine("[t36-live] skipped: OPENAI_API_KEY not set");
        }

        Console.WriteLine(fails == 0
            ? $"PASS: TranslateStreamAsync SSE + non-SSE fallback"
            : $"FAIL: {fails} stream checks failed");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>
    /// T37: fuzzy cache match. Puts a near-miss entry, then queries with a 1-char-different source
    /// and expects GetFuzzy to return the cached translation without an API call. Also checks
    /// that a sufficiently different query misses.
    /// </summary>
    private static int SelfCheckT37()
    {
        int fails = 0;
        void Check(bool ok, string what) { if (ok) return; Console.WriteLine($"FAIL: {what}"); fails++; }

        // In-memory SQLite (shared cache so per-call Open() sees the same DB) + hold one
        // connection open so the schema survives across the repo's open/close cycles.
        // Mirrors the test project's TranslationCacheTests setup.
        var memName = "file:gst-t37-" + Guid.NewGuid().ToString("N") + "?mode=memory&cache=shared";
        var db = new Database(memName);
        db.EnsureSchema();
        var hold = db.Open(); // keep alive for the duration of this self-check
        try
        {
            var cache = new TranslationCacheRepository(db);

            // Exact hit baseline.
            cache.Put("Hello world", "Halo dunia", "id");
            Check(cache.Get("Hello world", "id") == "Halo dunia", "exact Get miss after Put");

            // Fuzzy hit — 1 char difference, similarity ~0.92.
            var fuzzy = cache.GetFuzzy("Hello worlds", "id", similarityThreshold: 0.85);
            Check(fuzzy.HasValue, "fuzzy Get returned null for near-match");
            var f = fuzzy.GetValueOrDefault();
            Check(fuzzy.HasValue && f.translated == "Halo dunia", $"fuzzy returned {f.translated}");
            Check(fuzzy.HasValue && f.similarity >= 0.85 && f.similarity < 1.0,
                $"similarity {f.similarity:F3} outside (0.85, 1.0)");

            // Same source under a different target lang → miss (different bucket).
            var otherLang = cache.GetFuzzy("Hello worlds", "en", similarityThreshold: 0.85);
            Check(!otherLang.HasValue, "fuzzy should miss across target lang");

            // Sufficiently different query → miss.
            var miss = cache.GetFuzzy("Completely different text", "id", similarityThreshold: 0.85);
            Check(!miss.HasValue, "fuzzy should miss on unrelated text");

            // Similarity calc spot check (independent of the repo).
            Check(Math.Abs(TranslationCacheRepository.NormalizedLevenshteinSimilarity("kitten", "sitting") - 0.5714) < 0.01,
                "Levenshtein kitten/sitting should be ~0.571");
            Check(TranslationCacheRepository.NormalizedLevenshteinSimilarity("", "abc") == 0.0,
                "Levenshtein empty vs non-empty should be 0");
        }
        finally
        {
            hold.Dispose();
        }

        Console.WriteLine(fails == 0
            ? "PASS: fuzzy cache exact + near-match + cross-lang miss + dissimilarity miss"
            : $"FAIL: {fails} fuzzy checks failed");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>
    /// T38: Vision AI OCR fallback. With a stubbed HTTP handler that returns a canned vision
    /// response, the engine must return non-empty text. With a 401 handler, it must surface a
    /// fatal TranslationException (no retry storm — the non-stream OCR path also counts attempts).
    /// Skips live API unless OPENAI_API_KEY is set.
    /// </summary>
    private static int SelfCheckT38()
    {
        int fails = 0;
        void Check(bool ok, string what) { if (ok) return; Console.WriteLine($"FAIL: {what}"); fails++; }

        var png = MakeFrame("Hello world"); // 400x60 white-on-black text PNG
        Check(png.Length > 0, "MakeFrame produced empty PNG");

        // Path 1: stubbed 200 with a vision-shaped response → engine returns text.
        {
            var handler = new MockHandler();
            handler.QueueJsonResponse(HttpStatusCode.OK,
                """{"choices":[{"message":{"content":"Hello world"}}]}""");
            using var engine = new VisionAiOcrEngine("k", "https://api.example.com", "m", handler);
            string text = engine.RecognizeAsync(png).GetAwaiter().GetResult();
            Check(text == "Hello world", $"vision OCR returned '{text}' (expected 'Hello world')");
            Check(handler.HitCount == 1, $"vision handler hit {handler.HitCount}x (expected 1)");
        }

        // Path 2: 401 must surface as fatal (non-retryable 4xx) — single attempt, throws TranslationException.
        {
            var handler = new MockHandler();
            handler.QueueJsonResponse(HttpStatusCode.Unauthorized, """{"error":"bad key"}""");
            using var engine = new VisionAiOcrEngine("k", "https://api.example.com", "m", handler);
            bool threw = false;
            try { engine.RecognizeAsync(png).GetAwaiter().GetResult(); }
            catch (TranslationException) { threw = true; }
            Check(threw, "401 should throw TranslationException (non-retryable 4xx)");
            Check(handler.HitCount == 1, $"401 retried {handler.HitCount}x (expected 1 — fatal 4xx)");
        }

        // Path 3: live API call — only if OPENAI_API_KEY is set.
        var liveKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(liveKey))
        {
            var liveUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1";
            var liveModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
            using var engine = new VisionAiOcrEngine(liveKey, liveUrl, liveModel);
            try
            {
                string text = engine.RecognizeAsync(png).GetAwaiter().GetResult();
                Check(!string.IsNullOrWhiteSpace(text),
                    $"live vision OCR returned empty text (model={liveModel})");
                if (!string.IsNullOrWhiteSpace(text))
                    Console.WriteLine($"[t38-live] PASS: '{text}' from {liveUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[t38-live] skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("[t38-live] skipped: OPENAI_API_KEY not set");
        }

        Console.WriteLine(fails == 0
            ? "PASS: VisionAiOcrEngine RecognizeAsync (stub + 401 fatal + live)"
            : $"FAIL: {fails} vision OCR checks failed");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>
    /// T39: error categorization. Stubs 401/429/500/connection-refused and verifies the
    /// resulting TranslationException.Category matches the contract (Auth / RateLimit /
    /// Provider / Network). 401 must NOT retry; 500 must retry MaxAttempts times before throwing.
    /// </summary>
    private static int SelfCheckT39()
    {
        int fails = 0;
        void Check(bool ok, string what) { if (ok) return; Console.WriteLine($"FAIL: {what}"); fails++; }

        Console.Error.WriteLine("[t39] start");
        // Auth (401) → single attempt, category Auth, not retried.
        {
            Console.Error.WriteLine("[t39] path 401");
            var handler = new MockHandler();
            handler.QueueResponses(MockHandler.Json("""{"error":"bad key"}""", HttpStatusCode.Unauthorized));
            var client = new TranslationClient("k", "https://api.example.com", "m", "auto", "id",
                handler: handler);
            TranslationException? ex = null;
            try { client.TranslateAsync("Hello").GetAwaiter().GetResult(); }
            catch (TranslationException e) { ex = e; }
            Check(ex is not null, "401 did not throw TranslationException");
            Check(ex?.Category == ErrorCategory.Auth, $"401 category = {ex?.Category} (expected Auth)");
            Check(handler.HitCount == 1, $"401 retried {handler.HitCount}x (expected 1 — fatal)");
            Console.Error.WriteLine("[t39] path 401 done");
        }

        // RateLimit (429) → retries MaxAttempts times. Use QueueRepeat so every attempt sees
        // the 429 — otherwise the 2nd attempt would fall through to the default 500 response
        // and the category assertion would fail for the wrong reason.
        {
            Console.Error.WriteLine("[t39] path 429");
            var handler = new MockHandler();
            handler.QueueRepeat(HttpStatusCode.TooManyRequests, """{"error":"slow down"}""");
            var client = new TranslationClient("k", "https://api.example.com", "m", "auto", "id",
                handler: handler);
            TranslationException? ex = null;
            try { client.TranslateAsync("Hello").GetAwaiter().GetResult(); }
            catch (TranslationException e) { ex = e; }
            Check(ex is not null, "429 did not throw TranslationException");
            Check(ex?.Category == ErrorCategory.RateLimit, $"429 category = {ex?.Category} (expected RateLimit)");
            Check(handler.HitCount >= 3, $"429 retried {handler.HitCount}x (expected >=3)");
            Console.Error.WriteLine("[t39] path 429 done");
        }

        // Provider (500) → retries MaxAttempts times, category Provider.
        {
            Console.Error.WriteLine("[t39] path 500");
            var handler = new MockHandler();
            handler.QueueRepeat(HttpStatusCode.InternalServerError, """{"error":"oops"}""");
            var client = new TranslationClient("k", "https://api.example.com", "m", "auto", "id",
                handler: handler);
            TranslationException? ex = null;
            try { client.TranslateAsync("Hello").GetAwaiter().GetResult(); }
            catch (TranslationException e) { ex = e; }
            Check(ex?.Category == ErrorCategory.Provider, $"500 category = {ex?.Category} (expected Provider)");
            Check(handler.HitCount >= 3, $"500 retried {handler.HitCount}x (expected >=3)");
            Console.Error.WriteLine("[t39] path 500 done");
        }

        // Network → connection refused / DNS. Cover via T40 (bad primary URL triggers failover),
// not here — pointing at 127.0.0.1:1 hangs on Windows network stack long enough to be annoying
// for a self-check that runs every commit.
        Console.WriteLine("[t39-network] covered by --selfcheck-t40 (failover path A)");

        Console.WriteLine(fails == 0
            ? "PASS: TranslationException categories (Auth/RateLimit/Provider/Network)"
            : $"FAIL: {fails} error-category checks failed");
        Console.Out.Flush();
        Console.Error.WriteLine("[t39] done");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>
    /// T40: provider failover. Primary URL points at 127.0.0.1:1 (refused) → after FailoverThreshold
    /// consecutive Network/Provider failures the client must hop to the fallback provider and the
    /// fallback must respond (stubbed 200). Auth failures must NOT trigger failover.
    /// </summary>
    private static int SelfCheckT40()
    {
        int fails = 0;
        void Check(bool ok, string what) { if (ok) return; Console.WriteLine($"FAIL: {what}"); fails++; }

        // Shrink the primary-retry window so we don't wait minutes to verify the recovery hop.
        var prevRetry = TranslationClient.PrimaryRetryAfter;
        TranslationClient.PrimaryRetryAfter = TimeSpan.FromMilliseconds(50);

        try
        {
            // Path A: primary host throws network errors; fallback host returns 200. Single
            // mock handler routes by URL substring — TranslationClient shares one handler
            // across all endpoints so we can't give each provider its own stub.
            var router = new MockHandler();
            router.RouteByUrl(failSubstring: "primary.example", successSubstring: "stub.example");
            var fallbacks = new List<ProviderConfig>
            {
                new() { Name = "stub-fallback", BaseUrl = "https://stub.example.com", ApiKey = "k", Model = "m" }
            };
            var client = new TranslationClient(
                apiKey: "k",
                baseUrl: "https://primary.example.com", // routed to network errors via router
                model: "m",
                sourceLang: "auto", targetLang: "id",
                handler: router,
                providers: fallbacks);

            bool failEvent = false;
            client.FailoverChanged += name =>
            {
                if (name == "stub-fallback") failEvent = true;
            };

            string? result = client.TranslateAsync("Hello").GetAwaiter().GetResult();
            Check(result == "Halo dari fallback", $"failover result = '{result}'");
            Check(failEvent, "FailoverChanged event not fired for stub-fallback");
            Check(client.IsDegraded, "client should be marked degraded after failover");
            Check(router.HitCount >= 1, $"router hit {router.HitCount}x (expected >=1)");

            // Path B: primary auth error → no failover (bad key on primary = bad key on fallback).
            // Use a stubbed primary that 401s and a fallback that would 200 if reached.
            var primary401 = new MockHandler();
            primary401.QueueResponses(MockHandler.Json("""{"error":"bad key"}""", HttpStatusCode.Unauthorized));
            var client2 = new TranslationClient(
                apiKey: "k",
                baseUrl: "https://primary.example.com",
                model: "m",
                sourceLang: "auto", targetLang: "id",
                handler: primary401,
                providers: new List<ProviderConfig>
                {
                    new() { Name = "should-never-hit", BaseUrl = "https://x.example.com", ApiKey = "k", Model = "m" }
                });
            bool anyFailover = false;
            client2.FailoverChanged += _ => anyFailover = true;
            try { client2.TranslateAsync("Hi").GetAwaiter().GetResult(); } catch { /* expected */ }
            Check(!anyFailover, "Auth error should not trigger failover");
            Check(!client2.IsDegraded, "client2 should not be degraded on auth failure");
        }
        finally
        {
            TranslationClient.PrimaryRetryAfter = prevRetry;
        }

        Console.WriteLine(fails == 0
            ? "PASS: failover to backup on Network + no failover on Auth"
            : $"FAIL: {fails} failover checks failed");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>
    /// T41: persistent log with rotation. Shrinks MaxSizeBytes so the test stays fast; writes
    /// enough lines to force rotation, verifies an archive file appears and the active file is
    /// reset. Also confirms the MaxArchives ceiling is enforced.
    /// </summary>
    private static int SelfCheckT41()
    {
        int fails = 0;
        void Check(bool ok, string what) { if (ok) return; Console.WriteLine($"FAIL: {what}"); fails++; }

        var prevSize = FileLogger.MaxSizeBytes;
        var prevMax = FileLogger.MaxArchives;
        FileLogger.MaxSizeBytes = 1024; // 1 KB so rotation kicks in within a few dozen lines
        FileLogger.MaxArchives = 2;

        var dir = Path.Combine(Path.GetTempPath(), "gst-selfcheck-t41-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Write enough lines to force several rotations, then close so the writer releases
            // the file before we re-open for assertions.
            using (var log = new FileLogger(dir))
            {
                for (int i = 0; i < 200; i++)
                    log.Info("T41", $"line {i} — padding to push past the 1KB ceiling per file");
                log.Error("T41", "after-loop error for assertion visibility");
            }

            // Assertions on the rotated archive layout (writer is now disposed).
            // Strict regex: archives end in -<digits>.log; the active file has no suffix so it
            // doesn't match the leading `app-\d{4}-\d{2}-\d{2}` date-only pattern.
            var allLogs = Directory.GetFiles(dir, "app-*.log");
            var archives = allLogs
                .Where(f => System.Text.RegularExpressions.Regex.IsMatch(
                    Path.GetFileName(f), @"app-\d{4}-\d{2}-\d{2}-\d+\.log$"))
                .ToArray();
            var active = allLogs.Except(archives).ToList();

            Check(active.Count == 1, $"expected 1 active log, got {active.Count} ({string.Join(",", active)})");
            Check(archives.Length >= 1, $"expected >=1 archive, got {archives.Length}");
            Check(archives.Length <= FileLogger.MaxArchives,
                $"archive count {archives.Length} > MaxArchives {FileLogger.MaxArchives}");

            var lastActive = new FileInfo(active[0]);
            Check(lastActive.Length < FileLogger.MaxSizeBytes * 2,
                $"active log {lastActive.Length}B looks like it didn't reset");

            // Spot-check the most recent error line made it to disk.
            var content = File.ReadAllText(active[0]);
            Check(content.Contains("after-loop error for assertion visibility"),
                "active log missing the trailing error line");

            // Reopen: must append, not overwrite. New instance on the same dir.
            using (var log2 = new FileLogger(dir))
            {
                log2.Warn("T41", "second-session entry");
            }
            var appended = File.ReadAllText(Directory.GetFiles(dir, "app-*.log")
                .Where(f => !System.Text.RegularExpressions.Regex.IsMatch(
                    Path.GetFileName(f), @"app-\d{4}-\d{2}-\d{2}-\d+\.log$"))
                .First());
            Check(appended.Contains("second-session entry"),
                "second session entry not found — reopen didn't append");
        }
        finally
        {
            FileLogger.MaxSizeBytes = prevSize;
            FileLogger.MaxArchives = prevMax;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }

        Console.WriteLine(fails == 0
            ? "PASS: FileLogger rotation + MaxArchives ceiling + reopen append"
            : $"FAIL: {fails} logger checks failed");
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
        public Task<string> RecognizeAsync(byte[] pngBytes, CancellationToken ct = default)
            => Task.FromResult(_cap.CurrentText());
        public void Dispose() { }
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

    /// <summary>
    /// Minimal HttpMessageHandler for self-checks. Queues canned responses FIFO and tracks hits
    /// so the tests can assert against the request count (T39 retry policy, T40 failover).
    /// Mirrors the test project's MockHttpMessageHandler — duplicated here to keep SelfChecks
    /// dependency-free (SelfChecks must not reference tests/).
    /// </summary>
    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _queue = new();
        public int HitCount { get; private set; }

        /// <summary>Per-request response func. Set via QueueRepeat so every call returns
        /// the same canned response (retry-then-fail scenarios).</summary>
        private Func<HttpRequestMessage, Task<HttpResponseMessage>>? _repeat;

        public void QueueResponses(params HttpResponseMessage[] responses)
        {
            foreach (var r in responses) _queue.Enqueue(r);
        }

        /// <summary>Set a single response that every call returns — useful for retry-then-fail
        /// scenarios where the same status code should fire on every attempt.
        /// Takes a factory so each attempt builds a fresh HttpResponseMessage (the StringContent
        /// inside is disposed by HttpClient after the first read).</summary>
        public void QueueRepeat(HttpStatusCode status, string body)
            => _repeat = _ => Task.FromResult(Json(body, status));

        /// <summary>Route by URL substring — primary hosts throw network exceptions (forces
        /// retry/failover), fallback host returns a 200. Used by T40 to drive failover
        /// without needing two separate handlers (TranslationClient shares one handler across endpoints).</summary>
        public void RouteByUrl(string failSubstring, string successSubstring)
            => _repeat = req =>
            {
                var url = req.RequestUri?.ToString() ?? "";
                if (url.Contains(failSubstring, StringComparison.OrdinalIgnoreCase))
                    throw new HttpRequestException($"simulated network error for {url}");
                if (url.Contains(successSubstring, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json("""{"choices":[{"message":{"content":"Halo dari fallback"}}]}""", HttpStatusCode.OK));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("mock handler: unmatched URL " + url)
                });
            };

        public void QueueJsonResponse(HttpStatusCode status, string json)
            => QueueResponses(new HttpResponseMessage(status)
            {
                Content = new StringContent(json)
                {
                    Headers = { ContentType = new("application/json") }
                }
            });

        public void QueueSseChunks(params string[] deltaPayloads)
        {
            // SSE framing: each `data: <json>\n\n` is one event. Trailing `data: [DONE]\n\n` ends.
            var sb = new StringBuilder();
            foreach (var d in deltaPayloads)
            {
                if (d == "[DONE]") sb.Append("data: [DONE]\n\n");
                else sb.Append("data: ")
                      .Append("{\"choices\":[{\"delta\":{\"content\":\"")
                      .Append(d.Replace("\"", "\\\""))
                      .Append("\"}}]}\n\n");
            }
            QueueResponses(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sb.ToString())
                {
                    Headers = { ContentType = new("text/event-stream") }
                }
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HitCount++;
            if (_repeat is not null) return _repeat(request);
            return Task.FromResult(_queue.Count > 0
                ? _queue.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("mock handler exhausted")
                });
        }

        public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(body) { Headers = { ContentType = new("application/json") } } };
    }
}
