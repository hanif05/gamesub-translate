# TASKS — Breakdown Fase 4 (Polish & Packaging)

**Status:** Draft — siap eksekusi setelah approval.
**Branch target:** `feature/fase-4-polish` (dibuat dari `main` setelah `feature/fase-3-optimisasi` merged).
**Estimasi roadmap PRD:** 1–2 minggu.
**Dependency:** Fase 3 selesai (T28–T42 merged, 79 tests green, selfcheck mode T35–T41 verified).

Tujuan fase ini: poles UX yang masih kasar dari Fase 2, isi celah UI/UX yang di-defer, distribusi sebagai installer Windows yang bisa dipakai harian, dan validasi terhadap beberapa game populer (PRD 13). Perubahan bersifat user-facing (installer, UI) + keandalan (FPS impact, fullscreen compatibility).

## Scope yang Dibawa dari Fase 2 / 3

Fase 2+3 sudah punya fondasi:
- Overlay transparan click-through (T14, Win32 `WS_EX_LAYERED` + `WS_EX_TRANSPARENT`).
- Multi-region per profile + auto-load foreground game (T25).
- Settings panel lengkap: API, language, capture, overlay style, hotkey, profile, provider failover (T23 + T40).
- Change detection, fuzzy cache, streaming translation, vision AI OCR fallback, error categorisasi, persistent log (T11, T13, T36, T38, T39, T41).
- 79 xUnit tests + 6 selfcheck mode.

Fase 4 **memoles, mengemas, dan memvalidasi**. TIDAK refactor besar — desain stabil, hanya improvement.

## Yang TIDAK Masuk Fase 4 (deferred ke Fase 5+)

- Auto-detect region via vision AI (PRD 5.2) — Fase 5+.
- Speech-to-text / voice (PRD 5.2) — Fase 5+.
- Auto-switch region by context (PRD 5.2) — Fase 5+.
- Overlay history / log dialog (PRD 5.2) — Fase 5+ atau cukup ditambahkan sebagai "Recent Translations" tab sederhana di Fase 4 jika scope memungkinkan.
- Preset komunitas (PRD 5.2) — out of scope personal tool.
- Auto-update in-app (download patch dari server) — Fase 5+ (butuh hosting update feed, di luar personal-tool scope).
- Multi-target language quick switch UI (PRD catatan Fase 3) — **masuk Fase 4** sebagai T51.

## Aturan Eksekusi

- Setiap task = 1 commit (atau 1 PR kecil). Pesan: `T<n>: <short desc>` (lanjut nomor dari Fase 3, mulai T43).
- Tiap task HARUS selesai + verifikasi sebelum lanjut ke task yang bergantung.
- Target framework tetap `net8.0-windows10.0.19041.0`.
- Dependency NuGet tambahan fase ini (tambah hanya kalau ada justifikasi kuat):
  - `Microsoft.WindowsAppSDK` / `WiX` untuk installer — atau pakai hand-rolled Inno Setup script (lebih ringan, no NuGet). Default: Inno Setup portable.
  - TIDAK tambahkan UI library baru (Material Design, dll) — polish cukup via XAML rework + color scheme.
  - TIDAK tambahkan benchmark library — cukup stopwatch + log.
- Branch `feature/fase-4-polish` WAJIB dibuat sebelum commit pertama. Jangan commit ke `main`.
- Unit test dijalankan via `dotnet test` dari root, harus exit code 0.
- FPS impact verification (T50) butuh GPU monitoring (`PresentMon` / `MSI Afterburner`) di dua game berbeda — manual verification, bukan automated.
- Game compatibility test (T53) butuh instalasi game yang sesuai — list game ada di "Done when" tiap subtask.

---

## Urutan Task (by Dependency)

### FASE 4.A — Installer & Distribution

#### T43. Inno Setup installer script
**Status**: ✅ done (commit ea2309c).
**Deskripsi**: Hand-rolled installer pakai Inno Setup (free, portable, no NuGet). Output `GameSubTranslate-Setup-1.0.0.exe` di folder `installer/`.
- Include semua output build: `GameSubTranslate.App.exe`, `GameSubTranslate.Core.dll`, dependencies (`Tesseract.dll`, `assets/tessdata/`, SQLite native jika ada).
- Pilihan install path default: `%ProgramFiles%\GameSubTranslate\`.
- Start Menu shortcut + optional Desktop shortcut + optional auto-start dengan Windows.
- Registry: tulis `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` jika user pilih auto-start.
- Uninstall entry di Add/Remove Programs dengan proper display name + version.
- Pure installer script (`.iss` file), bukan pakai WiX/MSI — cukup untuk personal tool.
- **Prerequisite check (PENTING — app framework-dependent, jadi runtime wajib ada):**
  - Pakai Inno Setup `[Code]` function `IsRequiredNetDesktopRuntimeInstalled: Boolean;` yang baca registry `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App` untuk versi 8.0.x.
  - Kalau return `False` → `MsgBox(...)` informatif dengan tombol "Download" buka `https://dotnet.microsoft.com/download/dotnet/8.0` (link langsung ke runtime Windows x64) + tombol "Cancel" abort instalasi.
  - Pesan jelas: "GameSubTranslate requires .NET 8 Desktop Runtime. Click Download to get it from the official site, then run this installer again."
  - Reasoning: tanpa check ini, installer "sukses" tapi app crash silent saat di-launch kalau runtime belum ada — jebakan kecil kalau user install ulang Windows atau di PC lain.

**Output**: `installer/GameSubTranslate.iss`, `installer/build-installer.cmd` (run `ISCC.exe GameSubTranslate.iss`), file installer ter-build.
**Done when**:
- `build-installer.cmd` menghasilkan `GameSubTranslate-Setup-1.0.0.exe` (~30–50MB).
- Install di Windows 11 tanpa .NET 8 Desktop Runtime → prompt jelas + link download, instalasi abort sebelum copy file.
- Install di Windows 11 dengan .NET 8 Desktop Runtime sudah ada → app bisa di-launch dari Start Menu, jalan normal.
- Uninstall via Add/Remove Programs → semua file + registry entry bersih.
**Depends on**: — (independent, T44 butuh installer sudah ada).

#### T44. Release build configuration
**Status**: ✅ done (commit c7750e2).
**Deskripsi**: Tambah konfigurasi `Release` ke solution + tweak `GameSubTranslate.App.csproj`:
- `dotnet publish -c Release` menghasilkan single-folder output yang siap dikemas installer.
- Enable optimizations (`<Optimize>true</Optimize>`, `<PublishReadyToRun>true</PublishReadyToRun>`).
- Trimmed? — JANGAN pakai `<PublishTrimmed>true</PublishTrimmed>` (Tesseract reflection & SQLite native breaks; trimming butuh extra config). Pakai R2R cukup.
- Set `AssemblyVersion` + `FileVersion` + `Product` + `Company` di `.csproj`.
- Generate `version.txt` di output untuk About tab pakai itu.
- Self-contained? JANGAN — pakai framework-dependent publish biar installer kecil (~30MB vs ~150MB). User harus sudah install .NET 8 Desktop Runtime.

**Output**: update `src/GameSubTranslate.App/GameSubTranslate.App.csproj`, `dotnet publish` script di `installer/publish.cmd`.
**Done when**:
- `publish.cmd` menghasilkan folder output siap-installer di `installer/publish-output/`.
- Installer built from this output jalan tanpa missing dependency.
- About tab di app menampilkan version dari `version.txt`.
**Depends on**: T43 (installer butuh output publish).

#### T45. First-run welcome + setup wizard
**Status**: ✅ done (commit 136d913).
**Deskripsi**: Saat app pertama kali jalan (`%APPDATA%\GameSubTranslate\settings.json` belum ada), tampilkan wizard 3 langkah:
1. **API Setup**: jelaskan butuh API key + base URL + model, link ke "How to get API key" (OpenAI / OpenRouter doc), tombol "Skip — pakai nanti".
2. **Target language**: pilih dari dropdown (default id).
3. **Quick tour**: 3 slide singkat (auto-load profile saat game foreground; hotkey toggle overlay; tray icon untuk pause).
Tombol "Finish" → simpan settings default + langsung buka Settings Panel untuk diisi API key kalau di-skip.
- Settings UI tambah field "Setup completed: true/false" — wizard hanya muncul kalau false.

**Output**: `src/GameSubTranslate.App/Onboarding/WelcomeWindow.xaml` + `WelcomeWindow.xaml.cs`, wire di `App.OnStartup`.
**Done when**:
- Hapus `%APPDATA%\GameSubTranslate\settings.json` → run app → wizard muncul.
- Isi wizard → settings tersimpan, SettingsPanel terbuka untuk lanjut API key.
- Settings sudah ada → wizard skip langsung ke main window.
**Depends on**: T23 (Fase 2 settings sudah ada).

---

### FASE 4.B — UI Polish

#### T46. Overlay text wrapping + max-width + margin
**Status**: ✅ done (commit 4d4adda).
**Deskripsi**: Saat ini overlay text satu line panjang bisa overflow ke luar monitor. Fase 4:
- `OverlayWindow` width jadi configurable (default 800px), `MaxWidth` jadi 80% screen width.
- TextBlock wrap dengan `TextWrapping=Wrap` + padding 8px atas/bawah + 16px kiri/kanan.
- Background card auto-grow vertikal sesuai text (sudah, tinggal verify).
- Shadow tipis di belakang text untuk readability di atas background apapun (drop shadow effect).
- Multi-line subtitle (max 3 baris, ellipsis setelahnya) untuk game dengan dialog panjang.

**Output**: update `src/GameSubTranslate.App/Overlay/OverlayWindow.xaml` (+ code-behind kalau perlu).
**Done when**:
- Subtitle 200 karakter panjang → wrapped ke 3 baris, tidak overflow.
- Overlay di game dengan background putih → text tetap readable (drop shadow).
- Resize overlay window → text reflow dengan mulus.
**Depends on**: T14 (Fase 2 overlay sudah ada).

#### T47. Overlay fade in/out animation
**Status**: ✅ done (commit 97ba11b).
**Deskripsi**: PRD 6.6 minta animasi halus. Pakai WPF `DoubleAnimation` di `Opacity`:
- ShowText: fade in 0 → 1 dalam 200ms.
- New text menggantikan old: cross-fade 150ms (old fade out, new fade in).
- HideOverlay: fade out 1 → 0 dalam 300ms sebelum `Hide()`.
- Tidak block pipeline — animasi berjalan paralel dengan translation.

**Output**: update `src/GameSubTranslate.App/Overlay/OverlayWindow.xaml.cs` + tambah `Storyboard` resource di XAML.
**Done when**:
- Subtitle baru muncul: fade in halus, bukan pop-in tiba-tiba.
- Hide via hotkey: fade out dulu baru hilang.
- FPS impact dari animasi < 1% (verify di T50).
**Depends on**: T46 (overlay layout fix).

#### T48. Settings panel polish
**Status**: ⬜ pending.
**Deskripsi**: Saat ini `SettingsWindow` functional tapi plain. Fase 4:
- **Tab icons**: tambah icon sederhana (unicode `⚙ API`, `🌐 Language`, `📷 Capture`, `🎨 Overlay`, `⌨ Hotkeys`, `🎮 Profiles`, `ℹ About`) di header tab.
- **Field validation real-time**: interval < 100ms → warning merah di samping field (T23 validasi hanya di Save click).
- **Font preview**: di tab Overlay, tambah TextBlock live preview "Sample subtitle text" yang update saat font/size/color/opacity diubah (live preview, bukan Save → reopen).
- **Reset to Defaults** tombol di About tab — konfirmasi dialog → reset semua setting ke factory default (API key kosong, interval 800ms, font Segoe UI 20, opacity 1.0, hotkey default).
- **Provider list reorder**: drag-drop atau tombol "Up/Down" untuk ubah urutan fallback (T40 list order = failover order, tapi belum ada UI reorder).

**Output**: update `src/GameSubTranslate.App/Settings/SettingsWindow.xaml` + `SettingsWindow.xaml.cs`.
**Done when**:
- Live preview font di-update real-time tanpa click Save.
- Reset to Defaults → semua field balik ke nilai awal, konfirmasi dialog muncul.
- Provider Up/Down button berfungsi (provider order berubah di `_settings.Providers`).
**Depends on**: T23 (Fase 2 settings).

#### T49. Tray icon + menu polish
**Status**: ⬜ pending.
**Deskripsi**: T24 sudah punya tray icon basic. Fase 4:
- **Active profile indicator**: tray icon tooltip menampilkan "GameSubTranslate — Active: The Last of Us Part I" atau "GameSubTranslate — No active profile".
- **Quick region switch** submenu: saat profile aktif punya >1 region, submenu "Region → Dialog / Battle / Menu" untuk switch langsung dari tray tanpa buka main window.
- **Status indicator**: icon berubah warna kalau degraded (provider failover) atau error (kuning/merah). Default ijo = OK.
- **Double-click behavior**: saat ini double-click buka MainWindow. Fase 4: kalau app minimized, double-click buka MainWindow; kalau tidak, double-click toggle overlay visibility (shortcut yang sering).

**Output**: update `src/GameSubTranslate.App/App.xaml.cs` (`InitTray`).
**Done when**:
- Active profile berubah → tooltip update real-time.
- Region submenu muncul dengan jumlah region yang sesuai (1 region = tidak ada submenu).
- Failover ke fallback → icon warna kuning (verify manual atau log).
**Depends on**: T24 (Fase 2 tray), T25 (Fase 2 auto-load profile).

#### T50. FPS impact verification (overlay rendering)
**Status**: ⬜ pending.
**Deskripsi**: PRD 7 target FPS impact < 5%. Verifikasi dengan `PresentMon` (Intel tool, free) di dua game:
- Test 1: **The Last of Us Part I** (target utama PRD 13) di Borderless Windowed 1080p.
- Test 2: **Stardew Valley** (game ringan baseline) di Windowed 1080p.
Metode: jalankan game 60 detik tanpa overlay (baseline FPS), 60 detik dengan overlay aktif (text diam), 60 detik dengan overlay aktif + streaming translation aktif. Catat P95 + average FPS. Target: delta average FPS < 5%.

**Output**: section "FPS Impact Report" di bawah file ini (tabel per-game + skenario).
**Done when**:
- TLOU P95 FPS delta < 5% antara baseline dan overlay aktif.
- Stardew P95 FPS delta < 5%.
- Tidak ada micro-stutter saat overlay text update (visual verification, capture 5s footage).
**Depends on**: T47 (fade animation), T46 (overlay layout).

#### T51. Multi-target language quick switch
**Status**: ⬜ pending.
**Deskripsi**: PRD 16 sudah punya multi-target language support, tapi tidak ada UI quick switch. Fase 4:
- Tambah submenu "Target language →" di tray menu (T49 sudah polish tray). Isi: daftar bahasa yang aktif di TargetLangBox (id, en, ja, ko, zh, fr, de, es). Click → set `_settings.TargetLang` + simpan + rebuild pipeline kalau jalan.
- Tambah hotkey `Ctrl+Alt+L` (configurable) → cycle ke bahasa berikutnya.

**Output**: update `src/GameSubTranslate.App/App.xaml.cs` (tray menu + hotkey).
**Done when**:
- Click submenu bahasa Indonesia → pipeline berikutnya pakai `targetLang="id"`.
- Hotkey cycle → bahasa berubah sesuai urutan.
- Persisted ke settings.json setelah switch.
**Depends on**: T49 (tray polish), T22 (manual capture hotkey precedent).

---

### FASE 4.C — Game Compatibility

#### T52. Fullscreen exclusive compatibility test
**Status**: ⬜ pending.
**Deskripsi**: PRD 12 catat fullscreen exclusive bisa bermasalah. Verifikasi di TLOU + satu game lain:
- Test di Fullscreen Exclusive: capture tetap jalan? Overlay tetap visible? Hotkey tetap responsive?
- Kalau gagal: dokumentasikan di About tab "Known limitations" + rekomendasi pakai Borderless Windowed.
- Test di Borderless Windowed (mode utama yang direkomendasikan PRD 13): pasti OK.

**Output**: section "Fullscreen Compatibility Report" + update About tab text kalau ada limitation.
**Done when**:
- Borderless Windowed → semua fitur OK.
- Fullscreen Exclusive → minimal capture OK; kalau overlay/hotkey gagal, dokumentasi jelas.
**Depends on**: — (independent verification).

#### T53. Game-specific profiles & preset docs
**Status**: ⬜ pending.
**Deskripsi**: PRD 13 list game AAA dengan subtitle. Bikin profile + dokumen preset untuk 3 game:
- **The Last of Us Part I** — region tipikal: 1920x1080 di y=950, width=920, height=80. Font: game default, capture interval 800ms.
- **God of War (2018)** — region tipikal: 1920x1080 di y=920, width=960, height=100. Bahasa: English (default game), target: id.
- **Persona 5 Royal** — region tipikal: 1920x1080 di y=1000, width=1100, height=80 (dialog box custom).

> **⚠ CATATAN PENTING — koordinat di atas adalah estimasi awal, BUKAN final.** Belum divalidasi manual terhadap game beneran. Koordinat bisa meleset karena beda resolusi monitor, beda in-game subtitle setting (font size, posisi, background box), beda Windows UI scaling. **WAJIB verifikasi manual pakai Region Selector (T7 Fase 2) di game yang dimaksud sebelum commit angka ke `docs/game-presets.md`** — kalau meleset, ukur ulang dan update angka. Preset JSON di `tests/fixtures/profiles/` juga harus pakai koordinat yang sudah verified, bukan tebakan.

Buat 3 profile di SQLite (`tests/fixtures/profiles/`), export ke JSON via ProfileRepository, tulis `docs/game-presets.md` yang list region coordinate + setting recommendation untuk 3 game tsb.

**Output**: `docs/game-presets.md`, `tests/fixtures/profiles/tlou.json`, `god-of-war.json`, `persona5r.json`.
**Done when**:
- 3 file JSON bisa di-import via ProfileRepository (test `ProfileImportTests` di T28 suite).
- `game-presets.md` punya step-by-step setup + screenshot ASCII region coordinate.
- **Setiap koordinat di preset WAJIB sudah diverifikasi manual di game aslinya** (jalankan game, pakai Region Selector, capture subtitle aktif, catat koordinat real). Kalau belum verified → jangan commit preset sebagai "final".
**Depends on**: — (dokumen + fixtures saja, no code).

---

### FASE 4.D — Verification

#### T54. End-to-end verification Fase 4
**Status**: ⬜ pending.

**Deskripsi**: Jalankan installer + semua fitur Fase 4, extended dari T42:
1. **Install clean** → uninstall → install lagi di test machine, verify tidak ada leftover.
2. **Welcome wizard** muncul di first run, skip di run kedua.
3. **Release build** jalan tanpa debug-mode log spam.
4. **Overlay wrap + fade**: subtitle panjang di-wrap 3 baris, fade in/out halus.
5. **Settings live preview**: ubah font size → preview update real-time, no Save click needed.
6. **Tray region switch**: profile dengan 2 region → submenu muncul, click switch region.
7. **Multi-language quick switch**: tray click "ja" → pipeline berikutnya pakai target `ja`.
8. **FPS impact**: jalankan T50 report (P95 delta < 5%).
9. **Game presets**: import 3 JSON preset → capture di game yang dimaksud → translation muncul.
10. **Reset to Defaults**: klik tombol → konfirmasi → settings balik ke factory default.

Catat hasil di section "Hasil Verifikasi".
**Output**: section "Hasil Verifikasi" + tabel hasil.
**Done when**: semua 10 skenario di atas lulus + regression T42 (Fase 3) + T26 (Fase 2) tetap pass.
**Depends on**: T43–T53.

---

## Dependency Graph (Ringkas)

```
T43 → T44
T44 → T54
T45 → T54
T46 → T47, T50
T47 → T50, T54
T48 → T54
T49 → T51, T54
T50 → T54
T51 → T54
T52 → T54
T53 → T54
```

Critical path: T43 → T44 → T54 (atau T46 → T47 → T50 → T54).

## Estimasi Kasar (untuk planning sprint)

| Task Group | Estimasi |
|---|---|
| T43–T45 (Installer & distribution) | 3–4 hari |
| T46–T51 (UI polish) | 4–5 hari |
| T52–T53 (Game compatibility) | 1–2 hari |
| T54 (Verification) | 1 hari |
| **Total** | **9–12 hari** (≈2 minggu dengan buffer) |

Sesuai estimasi roadmap PRD 1–2 minggu, on track kalau tidak ada blocker tak terduga.

## Hasil Verifikasi (diisi saat T54 selesai)

_(Diisi setelah eksekusi. Format mengikuti T42: tabel skenario + metode + hasil + catatan.)_

### FPS Impact Report (T50)

**Metodologi (PRD 7 target: average FPS delta < 5%)**

Alat: [PresentMon](https://github.com/intel/pcm) (Intel, free) — `PresentMon.exe -process_name <exe> -output_file out.csv -terminate_after_seconds 60`.

Protokol per game:
1. **Baseline** — game berjalan 60 detik tanpa `GameSubTranslate.App.exe` aktif. Capture fullscreen gameplay (no idle menu).
2. **Overlay idle** — start overlay (transparan, no text). Pilih profile game-nya tapi JANGAN klik Start (pipeline off). Biarkan 60 detik.
3. **Streaming** — klik Start, biarkan subtitle translation stream aktif dengan text yang berubah. 60 detik.

Skenario di-reset setiap ganti game. Display mode = **Borderless Windowed 1080p** (rekomendasi PRD 13). GPU = apa pun yang user pakai.

**Results** _(diisi setelah eksekusi user)_

| Game | Baseline avg/P95 | Overlay idle avg/P95 | Streaming avg/P95 | Δ avg | Δ P95 |
|---|---|---|---|---|---|
| The Last of Us Part I | _pending_ | _pending_ | _pending_ | _pending_ | _pending_ |
| Stardew Valley | _pending_ | _pending_ | _pending_ | _pending_ | _pending_ |

**Catatan visual** _(diisi setelah eksekusi user)_: micro-stutter saat text update / fade in-out: _pending_

**Status**: ⬜ template siap — angka diisi manual setelah user menjalankan test di mesin masing-masing. FPS counter tergantung GPU + driver + game build yang user punya — tidak ada angka default yang bisa diisi di sini.

### Fullscreen Compatibility Report (T52)

_(Diisi setelah T52 selesai. Status per-game di Borderless vs Fullscreen Exclusive.)_

## Catatan

- **Struktur folder akhir Fase 4:**
  ```
  tests/
  └── GameSubTranslate.Core.Tests/    (+ ProfileImportTests di T53)
  src/
  ├── GameSubTranslate.Prototype/    (Fase 1, masih ada)
  ├── GameSubTranslate.Core/         (Fase 3 final state)
  └── GameSubTranslate.App/          (+ WelcomeWindow, polished tray, animated overlay)
  installer/
  ├── GameSubTranslate.iss           (T43)
  ├── build-installer.cmd            (T43)
  ├── publish.cmd                    (T44)
  └── publish-output/                (Release build, gitignored)
  docs/
  ├── game-presets.md                (T53)
  └── tasks/TASKS-fase-4-polish.md   (file ini)
  ```

- **Konvensi tetap:** namespace `GameSubTranslate.<Module>`, async method suffix `Async`, interface prefix `I`, error handling di translation client tidak boleh crash app. Test naming: `MethodName_StateUnderTest_ExpectedBehavior` (standard xUnit).

- **Performance budget (PRD 7) tambahan Fase 4:**
  - FPS impact < 5% saat overlay aktif.
  - Installer size < 50MB.
  - First-run wizard completion < 30 detik.

- **Jangan over-engineer:** installer pakai Inno Setup (bukan WiX/MSI) cukup. UI polish cukup XAML rework + animation primitive, tidak perlu library baru. Welcome wizard cukup 3 step inline, tidak perlu framework onboarding.

- **Out of scope reminder:** auto-detect region, voice translate, history log, auto-update TIDAK masuk Fase 4. Kalau ada godaan untuk expand scope, push ke Fase 5+.

- **Test strategy shift lanjutan:** mulai Fase 4, integration test (full WPF pipeline) mulai masuk xUnit via WPF automation library (FlaUI atau White) — di-defer ke Fase 5 kecuali terbukti critical. Manual verification + selfcheck cukup untuk Fase 4.
