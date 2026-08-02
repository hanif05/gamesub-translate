# TASKS — Breakdown Fase 2 (MVP Overlay)

**Status:** Draft — siap eksekusi setelah approval.
**Branch target:** `feature/fase-2-mvp-overlay` (dibuat dari `main` saat mulai).
**Estimasi roadmap PRD:** 2–3 minggu.
**Dependency:** Fase 1 selesai (semua kode di `GameSubTranslate.Prototype`, pipeline end-to-end berjalan).

Tujuan fase ini: ganti console output jadi **WPF overlay transparan click-through** + **game profile management** + **global hotkeys** + **settings panel**, sesuai PRD section 15.

## Scope yang Dibawa dari Fase 1 ke Fase 2

Kode `OcrEngine`, `ChangeDetector`, `TranslationClient`, `Config/AppConfig` dari `GameSubTranslate.Prototype` **dimigrasi** ke project baru `GameSubTranslate.Core` (classlib). TIDAK refactor besar — desain sudah stabil. Yang berubah:

- `ScreenCapture`: ganti GDI+ → `Windows.Graphics.Capture`.
- `AppConfig`: tambah `DPAPI` encryption untuk `ApiKey`, pindah dari env-var ke file config.
- `TranslationClient`: implement TODO retry+timeout (10s, 3 attempt exp backoff).

## Yang TIDAK Masuk Fase 2 (deferred ke Fase 3+)

- Auto-detect region via vision AI (PRD 5.2).
- Speech-to-text (PRD 5.2).
- Overlay history / log dialog (PRD 5.2).
- Installer, auto-update, UI polish untuk distribusi (Fase 4).
- Vision AI OCR fallback (PRD 6.4) — Fase 2 cukup Tesseract dulu; Vision AI ditambahkan saat font game stylized terbukti jadi masalah di testing.

## Aturan Eksekusi

- Setiap task = 1 commit (atau 1 PR kecil). Pesan: `T<n>: <short desc>`.
- Tiap task HARUS selesai + verifikasi manual sebelum lanjut ke task yang bergantung.
- Target framework semua project Fase 2: `net8.0-windows10.0.19041.0` (WPF + Windows.Graphics.Capture).
- Dependency NuGet tambahan fase ini:
  - `Microsoft.Data.Sqlite` + `Dapper` (penyimpanan profile/cache; EF Core overkill untuk personal tool).
  - `System.Security.Cryptography.ProtectedData` (DPAPI untuk encrypt API key — built-in, tidak perlu NuGet).
  - `Hardcodet.NotifyIcon.Wpf` (system tray — ringan, popular, MIT license).
- Jangan tambahkan dependency lain kecuali ada justifikasi kuat (lihat CLAUDE.md "stdlib and native first").
- Branch `feature/fase-2-mvp-overlay` WAJIB dibuat sebelum commit pertama. Jangan commit langsung ke `main`.

---

## Urutan Task (by Dependency)

### FASE 2.A — Project Restructure & Storage

#### T1. Restructure solusi: Core + App projects
**Deskripsi**: Tambah 2 project baru ke `GameSubTranslate.sln`:
- `src/GameSubTranslate.Core` — classlib `net8.0-windows10.0.19041.0`. Namespace `GameSubTranslate.<Module>`. Pindah file dari `Prototype/Capture/`, `Prototype/Ocr/`, `Prototype/Pipeline/`, `Prototype/Translation/`, `Prototype/Config/`. Update `using` di `Prototype/Program.cs` kalau perlu.
- `src/GameSubTranslate.App` — WPF project `net8.0-windows10.0.19041.0`. Empty untuk task ini (isi di T2).

Update `.sln`, tambah `<ProjectReference>` dari App → Core, dan dari Prototype → Core kalau Prototype masih mau jalan independen untuk testing.
**Output**: solution build OK dengan 3 project, `Prototype` masih bisa run seperti Fase 1.
**Done when**: `dotnet build` dari root sukses 0 error. `dotnet run --project src/GameSubTranslate.Prototype -- --x 0 --y 0 --w 100 --h 100` masih jalan seperti Fase 1.
**Status**: ✅ DONE (commit 5082aec)
**Depends on**: —

#### T2. WPF App skeleton
**Deskripsi**: Di `GameSubTranslate.App`, bikin `App.xaml` (tanpa StartupUri) + `MainWindow.xaml` (placeholder `<TextBlock Text="GameSubTranslate"/>`). Override `OnStartup` di `App.xaml.cs` untuk show `MainWindow`. Set `ShutdownMode=OnExplicitShutdown` (window lain seperti overlay & region-selector akan handle lifecycle sendiri).
**Output**: `src/GameSubTranslate.App/App.xaml`, `MainWindow.xaml`, window muncul saat run.
**Done when**: `dotnet run --project src/GameSubTranslate.App` launch WPF window dengan text "GameSubTranslate" terlihat.
**Status**: ✅ DONE (T1 commit 5082aec sudah include App skeleton; verified window stays up)
**Depends on**: T1.

#### T3. Settings model + DPAPI-encrypted config file
**Deskripsi**: Di Core, bikin `GameSubTranslate.Config` namespace.
- `AppSettings` (POCO): `ApiKey`, `BaseUrl`, `Model`, `SourceLang` (default `"auto"`), `TargetLang` (default `"id"`), `CaptureIntervalMs` (default 800), `OcrEngine` (enum: `Tesseract`, `VisionAi`), `OverlayFontFamily`, `OverlayFontSize`, `OverlayTextColor`, `OverlayBgColor`, `OverlayOpacity`, `HotkeyToggleOverlay`, `HotkeyPauseCapture`, `HotkeyOpenSettings`, `HotkeyManualCapture`.
- `SettingsStore` dengan method `Load()` / `Save()`: serialize ke JSON di `%APPDATA%/GameSubTranslate/settings.json`. Field `ApiKey` di-encrypt pakai `ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)` (DPAPI) sebelum ditulis; di-decrypt saat load. Field lain plain JSON.
- Hapus dependency env-var dari Fase 1 (atau treat sebagai fallback kalau file belum ada).

**Output**: file `src/Core/Config/AppSettings.cs`, `SettingsStore.cs`.
**Done when**:
- Run app, set API key via `SettingsStore`, restart, key terbaca kembali.
- Buka file `settings.json` manual — field `ApiKey` terlihat sebagai base64 blob, bukan plaintext.
- Kalau file corrupt / hilang → `Load()` return default `AppSettings` baru, bukan crash.
**Status**: ✅ DONE (commit 2a41024, verified --selfcheck-t3)
**Depends on**: T1.

#### T4. SQLite + Dapper: schema + migration
**Deskripsi**: Tambah NuGet `Microsoft.Data.Sqlite` + `Dapper` di Core. Bikin `GameSubTranslate.Storage` namespace.
- File DB di `%APPDATA%/GameSubTranslate/profiles.db`.
- Schema (CREATE TABLE IF NOT EXISTS saat startup):
  - `GameProfile(Id PK, Name, ExecutableName, SourceLang, TargetLang, OcrEngine, CaptureIntervalMs, CreatedAt)`.
  - `CaptureRegion(Id PK, ProfileId FK, RegionName, X, Y, Width, Height, MonitorIndex, IsActiveDefault, SortOrder)`.
  - `TranslationCache(TextHash PK, SourceText, TranslatedText, SourceLang, TargetLang, CreatedAt)` — `TextHash` = SHA-256 hex dari `SourceText + TargetLang`.
- `Database` class: buka connection, jalankan `EnsureSchema()` sekali di startup. Pakai `SqliteConnection` per-query (Dapper style, simple).
**Output**: file `src/Core/Storage/Database.cs`, `Migrations/Schema.sql` (atau inline string di C#).
**Done when**: app startup dengan folder `%APPDATA%/GameSubTranslate/` kosong → file `profiles.db` terbuat dengan 3 tabel. Pakai `sqlite3` CLI (atau DB Browser for SQLite) verify schema.
**Status**: ✅ DONE (commit 09fa7f4, verified --selfcheck-t4)
**Depends on**: T1.

---

### FASE 2.B — Profile & Region

#### T5. GameProfile model + repository
**Deskripsi**: Di Core, namespace `GameSubTranslate.Profiles`.
- `GameProfile` model dengan `List<CaptureRegion> Regions`.
- `ProfileRepository` dengan method `GetAll()`, `GetById(int id)`, `Create(GameProfile)`, `Update(GameProfile)`, `Delete(int id)`. Pakai Dapper, parameterized query.
- Validation minimal: `Name` non-empty, `ExecutableName` optional.
**Output**: file `src/Core/Profiles/GameProfile.cs`, `CaptureRegion.cs`, `ProfileRepository.cs`.
**Done when**: dari quick test di `Program.cs` console, bisa create profile + 2 region, GetAll return list benar, update nama, delete. DB row count sesuai.
**Depends on**: T4.

#### T6. MainWindow: profile list + create/edit/delete
**Deskripsi**: Di App, `MainWindow.xaml` jadi launcher: listbox profile di kiri, tombol "New Profile", "Edit", "Delete", "Duplicate" di kanan. Klik "New Profile" → buka `ProfileEditWindow` (modal) dengan form input name, executable name, source/target lang, ocr engine, capture interval. Save → call `ProfileRepository.Create`.
**Output**: file `src/App/MainWindow.xaml(.cs)`, `ProfileEditWindow.xaml(.cs)`.
**Done when**: create profile via UI, restart app, profile masih ada. Delete bekerja, row hilang dari DB.
**Depends on**: T2, T3, T5.

#### T7. Region Selector: full-screen drag-select
**Deskripsi**: Window baru `RegionSelectorWindow`. Saat dibuka, full-screen ke primary monitor (extend ke multi-monitor di T8), `WindowStyle=None`, `WindowState=Maximized`, `Topmost=true`, `Background=Transparent`, `AllowsTransparency=true`. Mouse cursor jadi crosshair, click+drag gambar rectangle semi-transparan (misal `Rectangle` dengan `Fill="#40FF0000"`), live update `Width/Height`. Tampilkan TextBlock kecil real-time dengan koordinat (x, y, w, h). ESC cancel, Enter/click kedua confirm. Saat confirm, return `CaptureRegion` ke caller via callback / `DialogResult`.
**Output**: file `src/App/Regions/RegionSelectorWindow.xaml(.cs)`.
**Done when**: dari `ProfileEditWindow` klik "Add Region" → RegionSelectorWindow muncul full-screen → drag rectangle di atas Notepad → confirm → koordinat muncul di form. Cancel dengan ESC tutup window tanpa save.
**Depends on**: T6.

#### T8. Multi-monitor support di Region Selector
**Deskripsi**: Sebelum drag, kalau ada >1 monitor, tampilkan list monitor di top-bar RegionSelectorWindow (atau di step terpisah). User pilih monitor dulu, baru drag di monitor tersebut. Pakai `System.Windows.Forms.Screen.AllScreens` (WinForms interop) atau `Display.GetDisplays()` dari `Microsoft.Extensions.Hosting` — tapi yang paling simple: `System.Windows.Forms.Screen.AllScreens` (tambah `<UseWindowsForms>true</UseWindowsForms>` di `.csproj` App). Window RegionSelector diposisikan ke bounds monitor yang dipilih, drag di dalam bounds itu saja.
**Output**: update `RegionSelectorWindow.xaml(.cs)`, simpan `MonitorIndex` di `CaptureRegion`.
**Done when**: dengan 2 monitor, dropdown monitor muncul, pilih monitor 2 → window pindah ke monitor 2 → drag di sana → koordinat yang disimpan benar relatif ke virtual screen.
**Depends on**: T7.

#### T9. Region switcher aktif: switch region dalam 1 profile
**Deskripsi**: Di MainWindow atau tray icon (T24), user bisa pilih region aktif dari dropdown list region di profile yang sedang loaded. Pilih region → simpan di memory (tidak perlu ke DB setiap kali, kecuali user klik "Save" — atau auto-save kalau simple). Hotkey untuk cycle region ditambahkan di T22.
**Output**: dropdown region di MainWindow + method `ProfileService.SetActiveRegion(int regionId)`.
**Done when**: profile dengan 2 region, pilih region A di dropdown → pipeline pakai koordinat A; pilih region B → koordinat B. Persist pilihan saat restart app (simpan di `AppSettings` atau tabel baru `LastActiveState`).
**Depends on**: T6, T7.

---

### FASE 2.C — Capture Engine Upgrade

#### T10. Ganti ScreenCapture ke Windows.Graphics.Capture
**Deskripsi**: Rewrite `ScreenCapture.cs` di Core, ganti dari `System.Drawing` ke `Windows.Graphics.Capture` API. Output: `byte[]` PNG (signature method sama dengan Fase 1 supaya caller tidak berubah). Implementation hint:
- `GraphicsCaptureSession` capture specific window atau monitor.
- Capture per-region (bukan full monitor): pakai `GraphicsCaptureItem` dari monitor + crop di software (compositor), atau capture window langsung kalau bisa.
- Encoding ke PNG via `Windows.Graphics.Imaging.BitmapEncoder` atau fallback `System.Drawing` (yang masih dipakai cuma untuk encode PNG, bukan screenshot).
- Catatan: `Windows.Graphics.Capture` butuh `WindowsCapture.dll` & Windows 10 1903+ — sudah di target framework.

**Output**: rewrite `src/Core/Capture/ScreenCapture.cs`.
**Done when**: panggil `CaptureRegion(x, y, w, h)` dari test program, hasilnya PNG dengan dimensi sesuai region dan konten yang benar (compare dengan screenshot manual).
**Depends on**: T1.

#### T11. Change detection: perceptual hash
**Deskripsi**: Upgrade `ChangeDetector.cs`. Fase 1 pakai byte-compare (kurang robust). Fase 2: perceptual hash (pHash) 64-bit.
- Resize image ke 8x8 grayscale, hitung mean, bandingkan tiap pixel > mean → 64-bit hash.
- `IsChanged(byte[] newPng, byte[] lastPng) -> bool` pakai Hamming distance threshold (default 5 dari 64).
- Test: 2x panggil dengan image identik → false. Image dengan noise kecil (1-2 pixel beda) → false. Image dengan teks berbeda → true.
**Output**: update `src/Core/Pipeline/ChangeDetector.cs`.
**Done when**: test synthetic pass: 100 identik calls semua return false; 100 calls dengan teks berganti 1 kata di tengah return true.
**Depends on**: T10.

---

### FASE 2.D — Translation Hardening

#### T12. TranslationClient: timeout + retry exp backoff
**Deskripsi**: Implement TODO dari Fase 1 (T7). Update `TranslationClient.cs`:
- `HttpClient.Timeout = TimeSpan.FromSeconds(10)`.
- Wrap call dalam retry loop max 3 attempt. Delay: 1s, 2s, 4s.
- Tangani HTTP 429 (rate limit) & 5xx (server error) sebagai retryable. HTTP 4xx lain (400, 401, 403) → langsung throw `TranslationException` non-retryable.
- Kalau gagal setelah 3 attempt: log error + return `null` (jangan throw ke pipeline). UI/overlay tampilkan status error.
- Pakai `Polly` NuGet atau hand-roll retry (hand-roll cukup, 1 method).
**Output**: update `src/Core/Translation/TranslationClient.cs`.
**Done when**:
- Mock `HttpMessageHandler` simulate timeout → 3 attempt lalu return null, total ~7s elapsed.
- Mock simulate 429 di attempt 1, success di attempt 2 → return translated text.
- Mock simulate 401 → throw `TranslationException` setelah 1 attempt, no retry.
**Depends on**: T1.

#### T13. Translation cache
**Deskripsi**: Di Core, namespace `GameSubTranslate.Cache`. `TranslationCacheRepository`:
- `Get(string sourceText, string targetLang) -> string?` — lookup by hash.
- `Put(string sourceText, string translatedText, string targetLang)`.
- Hash: SHA-256(`sourceText + "|" + targetLang`) → hex string.
- Hook ke pipeline: sebelum panggil `TranslationClient.TranslateAsync`, cek cache dulu. Setelah dapat hasil dari API, simpan ke cache.
**Output**: file `src/Core/Cache/TranslationCacheRepository.cs`, integrasi di `TranslatePipeline`.
**Done when**: translate "Hello" 2x → API call hanya 1x, kedua call dapat hasil yang sama dari cache. Verify dengan log HTTP request.
**Depends on**: T4, T12.

---

### FASE 2.E — Overlay Renderer

#### T14. Overlay Window: transparent + always-on-top + click-through
**Deskripsi**: Window baru `OverlayWindow.xaml`. WPF window dengan:
- `WindowStyle=None`, `WindowStartupLocation=Manual`, `ShowInTaskbar=false`.
- `AllowsTransparency=true`, `Background=Transparent`.
- `Topmost=true`.
- Click-through: pakai `Win32` interop. `SetWindowLong(hwnd, GWL_EXSTYLE, currentStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT)` di `SourceInitialized`.
- Default size 800x150, posisi di-set manual dari `AppSettings` (atau center bottom saat pertama run).
**Output**: file `src/App/Overlay/OverlayWindow.xaml(.cs)`, helper `Win32.cs` dengan `SetWindowLong` P/Invoke.
**Done when**: app launch → overlay window muncul transparan (hanya background kosong kelihatan) di atas desktop, mouse click tembus ke window di belakangnya (test dengan Notepad di belakang overlay).
**Depends on**: T2.

#### T15. Overlay text rendering + style settings
**Deskripsi**: Di `OverlayWindow`, tambah `TextBlock` (atau `ContentControl` dengan `ScrollViewer` untuk teks panjang) yang bind ke `OverlayViewModel.Text`. Style:
- `FontFamily`, `FontSize`, `Foreground`, `Background` (semi-transparent box belakang teks) dari `AppSettings`.
- `Opacity` window-level.
- Text wrap, max width.
- Update binding via `INotifyPropertyChanged` di ViewModel.

**Output**: `src/App/Overlay/OverlayViewModel.cs`, update `OverlayWindow.xaml`.
**Done when**: panggil `overlay.ShowText("Hello translated world")` dari test code → teks muncul styled sesuai settings. Ganti setting → restart app → style baru aktif.
**Depends on**: T14, T3.

---

### FASE 2.F — Pipeline Integration

#### T16. Pipeline service (background, headless)
**Deskripsi**: Refactor `TranslatePipeline` dari Fase 1 jadi service yang bisa di-start/stop dari WPF. Constructor inject `ScreenCapture`, `OcrEngine`, `ChangeDetector`, `TranslationClient`, `TranslationCacheRepository`, dan `Action<string> onTranslated` callback. Method `Start()` spawn background `Task` (atau `Timer`), loop capture→detect→ocr→translate→cache→callback. Method `Stop()` cancel via `CancellationToken`.
**Output**: update `src/Core/Pipeline/TranslatePipeline.cs`.
**Done when**: dari `MainWindow` klik "Start Pipeline" → loop berjalan di background, callback dipanggil tiap ada teks baru. Klik "Stop" → loop cancel bersih tanpa exception.
**Depends on**: T10, T11, T12, T13.

#### T17. Pipeline pause/resume flag
**Deskripsi**: Tambah `Pause()` / `Resume()` method di `TranslatePipeline`. Saat paused, loop tetap jalan tapi skip OCR+translate (capture tetap jalan supaya saat resume, frame terbaru langsung diproses — tidak ada backlog). Bind ke `AppSettings.HotkeyPauseCapture` di T20.
**Output**: update `TranslatePipeline.cs`.
**Done when**: start pipeline, panggil `Pause()` dari test code → API call berhenti muncul di log. Panggil `Resume()` → API call jalan lagi. Capture loop tetap aktif selama paused (verify dengan log internal).
**Depends on**: T16.

---

### FASE 2.G — Hotkeys

#### T18. Global hotkey manager
**Deskripsi**: Di Core (atau App), namespace `GameSubTranslate.Hotkeys`. Class `GlobalHotkeyManager`:
- P/Invoke `RegisterHotKey` / `UnregisterHotKey` dari `user32.dll`.
- Hook `WM_HOTKEY` message via `HwndSource.AddHook` di MainWindow (atau hidden message window).
- `Register(ModifierKeys mods, Key key, string id, Action callback)` → return bool.
- `Unregister(string id)`, `UnregisterAll()`.
- IDisposable, cleanup di app exit.
**Output**: file `src/Core/Hotkeys/GlobalHotkeyManager.cs`, `Win32.cs` (extend).
**Done when**: register `Ctrl+Alt+T` → callback dipanggil saat tekan hotkey, meskipun MainWindow tidak focused. Unregister saat dispose → callback tidak dipanggil lagi.
**Depends on**: T2.

#### T19. Hotkey: toggle overlay show/hide
**Deskripsi**: Bind hotkey `AppSettings.HotkeyToggleOverlay` (default `Ctrl+Alt+T`) → `overlay.Show() / overlay.Hide()`. State overlay disimpan di memory, hide = `Visibility.Hidden` (bukan close, supaya state teks tetap).
**Output**: wiring di `App.OnStartup`.
**Done when**: tekan `Ctrl+Alt+T` saat main game window aktif → overlay muncul/hilang. State teks tidak reset.
**Depends on**: T14, T15, T18.

#### T20. Hotkey: pause/resume capture
**Deskripsi**: Bind `AppSettings.HotkeyPauseCapture` (default `Ctrl+Alt+P`) → `pipeline.Pause() / pipeline.Resume()`. Visual indicator kecil (icon di tray, lihat T24) update.
**Output**: wiring di `App.OnStartup`.
**Done when**: tekan `Ctrl+Alt+P` → pipeline pause (tidak ada log OCR/translate baru). Tekan lagi → resume.
**Depends on**: T17, T18.

#### T21. Hotkey: open settings panel
**Deskripsi**: Bind `AppSettings.HotkeyOpenSettings` (default `Ctrl+Alt+S`) → show `SettingsWindow` (lihat T23) atau focus ke MainWindow.
**Output**: wiring di `App.OnStartup`.
**Done when**: tekan `Ctrl+Alt+S` saat game aktif → settings window muncul di foreground. ESC atau close → hilang.
**Depends on**: T18, T23 (minimal placeholder settings window).

#### T22. Hotkey: manual screenshot trigger
**Deskripsi**: Bind `AppSettings.HotkeyManualCapture` (default `Ctrl+Alt+Space`) → trigger 1x capture pipeline (capture → ocr → translate → tampil di overlay) tanpa change detection. Method `Pipeline.CaptureOnce()` return `Task<string?>`.
**Output**: update `TranslatePipeline.cs`, wiring di `App.OnStartup`.
**Done when**: dengan pipeline paused atau capture area kosong, tekan `Ctrl+Alt+Space` → 1 translate result muncul di overlay. Tidak loop otomatis.
**Depends on**: T16, T18.

---

### FASE 2.H — Settings Panel & UX

#### T23. Settings Panel UI
**Deskripsi**: Window `SettingsWindow.xaml` dengan TabControl:
- **API & Model**: TextBox `BaseUrl`, ComboBox `Model` (free text + recent list), PasswordBox `ApiKey` (encrypt saat save), tombol "Test Connection" → panggil `TranslationClient` dengan text dummy, show success/fail.
- **Language**: ComboBox `SourceLang` (with "Auto" option), ComboBox `TargetLang` (default `id`).
- **Capture**: NumericUpDown `CaptureIntervalMs`, ComboBox `OcrEngine` (Tesseract / VisionAi — VisionAi placeholder disabled di fase ini).
- **Overlay**: TextBox `FontFamily`, Slider `FontSize`, ColorPicker `TextColor` & `BgColor`, Slider `Opacity` (0.0-1.0), tombol "Pick Position" → buka mini drag-reposition UI.
- **Hotkeys**: 4 text field readonly + tombol "Change" → capture next keypress → simpan ke `AppSettings`.
- **Profiles**: list profile + tombol Edit / Delete / Duplicate (link ke `MainWindow` functionality, atau duplicate di sini).

Save → `SettingsStore.Save()`. Cancel → discard.
**Output**: `src/App/Settings/SettingsWindow.xaml(.cs)`, `SettingsViewModel.cs`.
**Done when**: buka settings, ganti API key, font, hotkey, language → restart app → semua setting persisten. Test Connection panggil API real dan return success kalau key valid.
**Depends on**: T3, T5, T6, T18.

#### T24. System tray icon
**Deskripsi**: Pakai `Hardcodet.NotifyIcon.Wpf` (NuGet). `TaskbarIcon` di App, context menu:
- "Show / Hide Overlay" → toggle overlay.
- "Pause / Resume" → toggle pipeline.
- "Settings" → open SettingsWindow.
- "Exit" → close app (cleanup hotkeys, save state).

Double-click icon → show MainWindow.
**Output**: update `App.xaml`, `App.xaml.cs`.
**Done when**: app jalan, icon muncul di system tray. Right-click → context menu → semua aksi bekerja. Close MainWindow (X) tidak keluar app, hanya hide. Pilih Exit → app fully shutdown.
**Depends on**: T19, T20, T21, T23.

#### T25. Auto-load profile by foreground executable
**Deskripsi**: Pakai `Win32` `GetForegroundWindow` + `GetWindowThreadProcessId` + `QueryFullProcessImageName` untuk deteksi exe name foreground window. Background `Timer` di App cek tiap 2 detik. Kalau foreground exe match `ExecutableName` di salah satu profile → auto-load profile itu (set active region ke `IsActiveDefault`). Frontend window ke MainWindow atau settings window TIDAK trigger switch.
**Output**: `src/Core/Profiles/ForegroundWatcher.cs`, wiring di `App.OnStartup`.
**Done when**: launch game dengan executable `game.exe` yang ada di profile → app auto-load profile tersebut dalam <3 detik. Switch ke window lain (browser, explorer) → profile tetap (tidak auto-unload). Switch balik ke game → profile masih loaded.
**Depends on**: T5, T9, T16.

---

### FASE 2.I — Verification

#### T26. End-to-end manual verification
**Deskripsi**: Jalankan app full pipeline:
1. Settings → isi API key valid, base URL, model.
2. New Profile → nama "Test", drag region di atas subtitle game (atau video YouTube dengan subtitle ON).
3. Verify overlay muncul dengan teks terjemahan.
4. Test hotkeys: toggle overlay, pause, settings, manual trigger.
5. Test multi-region: tambah region kedua, switch via dropdown / hotkey.
6. Test cache: trigger translate teks yang sama 2x → hanya 1 API call.
7. Test auto-load: launch game dengan exe di profile → auto switch.
8. Ukur latency dari perubahan subtitle di game sampai overlay update.
9. Test change detection: biarkan subtitle diam 30 detik → tidak ada API call.
10. Test retry/timeout: pakai API key invalid → overlay tampilkan error status, app tidak crash.

Catat hasil di section "Hasil Verifikasi" di bawah file ini.
**Output**: section "Hasil Verifikasi" + screenshot/log terlampir.
**Done when**: semua 10 skenario di atas lulus + latency tercatat.
**Depends on**: T1-T25.

---

## Dependency Graph (Ringkas)

```
T1 → T2 → T6 → T9
T1 → T3 → T6, T15
T1 → T4 → T5 → T6
T1 → T10 → T11
T1 → T12 → T13 → T16
T2 → T7 → T8 → T9
T2 → T14 → T15 → T19
T6 → T23
T16 → T17 → T20
T18 → T19, T20, T21, T22
T19,T20,T21 → T24
T5,T9,T16 → T25
T1-T25 → T26
```

Critical path: T1 → T2 → T6 → T16 → T25 → T26 (atau T16 → T17 → T26).

## Estimasi Kasar (untuk planning sprint)

| Task Group | Estimasi |
|---|---|
| T1-T4 (Restructure + Storage) | 2-3 hari |
| T5-T9 (Profile + Region) | 3-4 hari |
| T10-T11 (Capture upgrade) | 1-2 hari |
| T12-T13 (Translation hardening) | 1-2 hari |
| T14-T15 (Overlay) | 2-3 hari |
| T16-T17 (Pipeline service) | 1-2 hari |
| T18-T22 (Hotkeys) | 1-2 hari |
| T23-T25 (Settings + UX) | 3-4 hari |
| T26 (Verification) | 1 hari |
| **Total** | **15-23 hari** (≈3-4 minggu dengan buffer) |

Sesuai estimasi roadmap PRD 2-3 minggu, on track kalau tidak ada blocker tak terduga.

## Hasil Verifikasi (diisi saat T26 selesai)

_(kosong — akan diisi di akhir fase)_

---

## Catatan

- **Struktur folder akhir Fase 2:**
  ```
  src/
  ├── GameSubTranslate.Prototype/    (masih ada, untuk testing CLI tanpa UI)
  ├── GameSubTranslate.Core/         (classlib: Capture, Ocr, Pipeline, Translation, Config, Storage, Profiles, Cache, Hotkeys)
  └── GameSubTranslate.App/          (WPF: MainWindow, ProfileEdit, RegionSelector, Overlay, Settings)
  ```

- **Konvensi tetap:** namespace `GameSubTranslate.<Module>`, async method suffix `Async`, interface prefix `I`, error handling di translation client tidak boleh crash app.

- **Jangan over-engineer:** vision AI OCR, speech-to-text, installer, auto-update — semua Fase 3+. Cukup Tesseract + WPF overlay + SQLite + DPAPI + 4 hotkey + settings tabs.

- **Test strategy:** Fase 2 masih verifikasi manual per-task (sama dengan Fase 1). Unit test formal untuk `ChangeDetector`, `TranslationClient` retry logic, `SettingsStore` DPAPI round-trip — **penting** ditambahkan minimal di Fase 3 untuk stabilitas. Untuk Fase 2 cukup verify happy path + 1-2 edge case per task.
