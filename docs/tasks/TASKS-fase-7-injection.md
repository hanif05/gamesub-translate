# TASKS — Breakdown Fase 7 (DXGI Universal Plugin, Track A)

**Status:** 🆕 Draft, ready for review.
**Branch target:** `feature/fase-7-dxgi-injection` (dibuat dari `main` setelah Fase 6 merged).
**Estimasi roadmap:** 2–3 minggu.
**Dependency:** Fase 6 selesai (PaddleOCR + Sdcb swap, semua tests green). Main branch bersih.

## Tujuan fase ini

Tambah **plugin universal in-game menu** yang kompatibel dengan **SEMUA game DX11**, tanpa tergantung engine. Menu OptiScaler-style: status, kontrol translation, log, advanced — semua native di dalam frame game, gak perlu alt-tab.

**DX12 di-scope ke Fase 8+**, bukan fase ini. Hooking DX12 butuh command queue/list, descriptor heap, dan ImGui DX12 backend terpisah — effort +3-4 hari, dan target RPG/story-heavy single-player yang udah di-spike (Persona, Tales of, Nier, Final Fantasy) semuanya DX11. Kalau ternyata ada game DX12 prioritas tinggi nanti, plugin ditambah sebagai task T99+.

**Metode injeksi: DLL proxying (cara ReShade)** — bukan CreateRemoteThread. User rename `GST.Inject.dll` jadi `dxgi.dll` (atau `d3d11.dll`), taro di folder yang sama dengan game `.exe`. Windows DLL search order auto-load DLL dari folder exe duluan, jadi plugin ke-load tanpa `CreateProcess suspended` + `CreateRemoteThread`. Zero process injection, zero AV flag risk, jauh lebih aman untuk distribusi ke user lain.

**Kenapa Track A dulu, bukan Track B (BepInEx/UE4SS dari PRD section 6):**

1. **Effort lebih kecil.** DXGI + ImGui universal = 1 code path untuk semua engine. BepInEx/UE4SS = per-engine code, signature scanning, fork compatibility.
2. **Risk lebih rendah.** DXGI Present() hook battle-tested (Reshade pakai pattern ini), gak perlu reverse-engineer function offset per game.
3. **Instant benefit untuk game existing.** Fase 1–6 game (yang pakai capture+OCR) langsung bisa pakai in-game menu tanpa setup tambahan.
4. **Validasi pipeline injection.** Kalau Track A jalan, baru Track B (engine-specific text interception) layak dieksekusi.

**Track B tetap didefer** ke Fase 8+ (lihat PRD-Injection.md section 6). Update PRD untuk clarify Track A = "DXGI+ImGui menu", Track B = "engine-specific text interception".

---

## Arsitektur singkat

```
Game process                           WPF App
┌──────────────────────┐                ┌──────────────────────┐
│  Game executable     │                │  GameSubTranslate    │
│  ┌────────────────┐  │  named pipe    │  (sudah ada)         │
│  │ GST.Inject.dll │──┼───────────────▶│  - PipeServer        │
│  │ (C++ native)   │  │  Hello/Status  │  - ISubtitleSource   │
│  │                │  │  ConfigUpdate  │  - Translation       │
│  │  - MinHook     │  │                │    pipeline          │
│  │  - ImGui       │  │                │                      │
│  │  - DXGI hook   │  │                │                      │
│  │  - Named pipe  │  │                │                      │
│  └────────────────┘  │                │                      │
└──────────────────────┘                └──────────────────────┘
```

**Injected via:** DLL proxying — user rename `GST.Inject.dll` jadi `dxgi.dll` (atau `d3d11.dll`, lihat T96), drop ke folder yang sama dengan game `.exe`. Windows DLL search order auto-load DLL dari folder exe duluan, jadi plugin ke-load tanpa `CreateProcess suspended` + `CreateRemoteThread`. Gak ada launcher helper di WPF app, gak ada "Launch with Translation" button. User launch game dari Steam/shortcut biasa, plugin auto-load. (Detail mekanisme: T96.)

**Komunikasi:** Named pipe `GameSubTranslate.{gamePid}` (sama dengan schema PRD section 7). Plugin connect on startup, kirim `Hello`, receive `ConfigSnapshot`, mulai heartbeat.

**Menu trigger:** Hotkey Insert atau Ctrl+Shift+T (configurable, default Insert). Opt-in, hidden by default.

---

## Stack & dependencies

| Component | Library | Alasan |
|---|---|---|
| DXGI hook | **MinHook** (Tsuda Kageyu, MIT) | Mature, native x64, battle-tested. Distribusi: static link atau DLL. |
| ImGui backend | **Dear ImGui** + **imgui_impl_dx11** + **imgui_impl_win32** | Standar industri untuk in-game UI. Direct C++ binding, no wrapper overhead. |
| Named pipe | Win32 API native | Sama dengan Core pipe server. |
| Build | **CMake** + MSBuild (Visual Studio 2022) | Standalone C++ project di luar .sln (per konfirmasi). |
| Distribusi | `plugins/inject/` folder di release ZIP | Manual copy untuk MVP, installer di v2. |

**MinHook & Dear ImGui sources** di-vendor sebagai git submodule atau fetched via CMake FetchContent. Submodule lebih reproducible untuk release artifact.

---

## Task list (Track A only)

### T91: Setup C++ project skeleton + build environment

**Deskripsi:** Bikin folder `plugins/inject/` dengan struktur:
```
plugins/inject/
├── CMakeLists.txt
├── src/
│   ├── main.cpp                 (DllMain entry point)
│   ├── hook/
│   │   ├── dxgi_hook.cpp        (IDXGISwapChain::Present hook)
│   │   └── dxgi_hook.h
│   ├── imgui/
│   │   ├── imgui_renderer.cpp   (ImGui context setup + render loop)
│   │   ├── imgui_renderer.h
│   │   └── menu.cpp             (menu UI definition, all sections)
│   ├── pipe/
│   │   ├── pipe_client.cpp      (named pipe client)
│   │   └── pipe_client.h
│   └── config/
│       ├── config.cpp          (load hotkey, pipe name from env/registry)
│       └── config.h
├── vendor/
│   ├── minhook/                 (git submodule)
│   └── imgui/                   (git submodule, includes backends/)
├── build.bat                    (cmake + msbuild wrapper)
└── README.md                    (cara build, dependency, deployment)
```

CMakeLists.txt: import MinHook + ImGui via `add_subdirectory(vendor/minhook)` + `add_subdirectory(vendor/imgui)`, build `GST.Inject.dll` sebagai shared library, target `x64`. Konfigurasi Release: optimization `/O2`, strip symbols, output ke `build/Release/GST.Inject.dll`.

**Done when:**
- `build.bat` jalan tanpa error di Visual Studio 2022 + CMake 3.20+
- Output `GST.Inject.dll` ~200KB, dependensi cuma `d3d11.dll`, `dxgi.dll`, `kernel32.dll`, `user32.dll`
- Manual `rundll32 GST.Inject.dll,Init` gak crash (no-op entry, hook belum dipasang)

**Estimasi:** 0.5 hari.

---

### T92: MinHook integration + DXGI hook infra

**Deskripsi:** Implementasi hook `IDXGISwapChain::Present` pakai MinHook. Pattern sama dengan Reshade:
1. `DllMain` di `DLL_PROCESS_ATTACH`: install hook ke DirectX 11 swap chain
2. Hook dipasang via signature scan di memory atau hook `D3D11CreateDevice` + `IDXGISwapChain::Present` vtable
3. Hook function: panggil original `Present()`, lalu jalankan callback (ImGui render di T93)
4. Cleanup di `DLL_PROCESS_DETACH`

**DX11 ONLY — DX12 di-defer ke Fase 8+.** Hooking DX12 butuh command queue/list interception, descriptor heap management, dan `imgui_impl_dx12` backend terpisah. Effort +3-4 hari kalau ditambah sekarang. Target game RPG/story-heavy single-player yang udah di-identifikasi (Persona 5 Royal, Tales of Arise, Nier Automata, Final Fantasy VII Remake Intergrade) semuanya DX11. Re-scope kalau ada game DX12 prioritas tinggi muncul.

**Pattern approach (pilih salah satu):**
- **A. Vtable hook via D3D11CreateDevice hook:** hook `D3D11CreateDevice`, dapet `ID3D11Device*`, lalu traverse ke `IDXGISwapChain*` (umum di game DX11).
- **B. Vtable hook via signature scan:** scan memory untuk pattern `IDXGISwapChain::Present` vtable, patch langsung. Lebih stealth tapi fragile.
- **Pilih A** (lebih reliable, gak perlu signature maintenance).

**Validasi target:** hook function terpanggil setiap frame, `originalPresent` return normal, no crash, no graphical glitch.

**Done when:**
- Inject ke notepad.exe (test process non-game) → no crash
- Inject ke game DX11 sederhana (e.g. Windows built-in 3D viewer) → game jalan normal, hook callback terpanggil setiap frame (verify via log/debugger)
- DLL unload clean, no memory leak (validate via `_CrtDumpMemoryLeaks` di debug build)

**Estimasi:** 1–1.5 hari.

---

### T93: ImGui context + DX11 render backend

**Deskripsi:** Setup Dear ImGui context + DX11 backend di dalam hook callback. Alur:
1. Setelah `originalPresent()` return, get `ID3D11DeviceContext*` dari swap chain
2. `ImGui_ImplDX11_NewFrame()` + `ImGui_ImplWin32_NewFrame()` + `ImGui::NewFrame()`
3. Render menu (definisi UI di T94)
4. `ImGui::Render()` + `ImGui_ImplDX11_RenderDrawData()`

**Win32 message handling:** ImGui butuh `WndProc` hook untuk input. Pattern: hook `SetWindowLongPtrW` atau simpan original `WndProc` lalu subclass. Bisa pakai `imgui_impl_win32` helper `ImGui_ImplWin32_WndProcHandler`.

**Hotkey handling:** `RegisterHotKey(hwnd, INSERT_ID, 0, VK_INSERT)` (gak conflict dengan `Ctrl+Shift+T`, kasih dua toggle option). Toggle visibility flag di `menu_state.visible`.

**Validasi target:** menu toggle on Insert key, show empty ImGui window "GameSubTranslate" dengan FPS counter di corner. Game tetap interactive (klik game window = input diteruskan ke game, bukan ke ImGui).

**Done when:**
- Inject ke game → menu toggle on Insert, FPS counter tampil
- Klik di area menu → Interact dengan ImGui widgets (button click, slider drag)
- Klik di luar menu → Input diteruskan ke game normal
- FPS di game stabil +/- 2 FPS dari baseline (no significant overhead)

**Estimasi:** 1–1.5 hari.

---

### T94: Menu UI implementation (full PRD scope)

**Deskripsi:** Implementasi semua 6 section menu per PRD section 11:

| Section | Widgets | Persistence |
|---|---|---|
| **Status** | Connection indicator (●/○ warna), engine detected label, source/target lang, last 3 translations list | Read-only |
| **Translation** | Target language dropdown (id, en, ja, ko, dll dari list), provider dropdown (OpenAI, OpenRouter, custom), API endpoint text input | `ui-config.json` |
| **Display** | Overlay position (Bottom/Center/Top dropdown), font size slider, background opacity slider | `ui-config.json` |
| **Advanced** | Skip patterns textarea (multi-line), capture fallback toggle, "Edit Config JSON" button, "Reload Config" button | `game-config.json` |
| **Log** | Last 20 translations: original + translated, timestamp, scrollable list | Read-only |
| **Actions** | "Pause Translation" / "Resume Translation" button, "Unload Plugin" button (kirim `Unload` message) | Toggle states |

**Config persistence:** pakai `SHGetFolderPath` + `FOLDERID_RoamingAppData` → `%APPDATA%\GameSubTranslate\inject-config.json`. File di-write on every setting change (debounced 500ms).

**Edit Config JSON in-game:** pakai `ImGui::InputTextMultiline` dengan flag `ImGuiInputTextFlags_AllowTabInput`. Save button → write to file → trigger reload.

**Done when:**
- Semua 6 section render dengan benar
- Setiap setting change persist ke JSON file
- Reload button baca JSON ulang tanpa perlu restart game
- Pause/Resume toggle mengirim message yang benar ke WPF app
- Unload button: kirim `Unload` message, tunggu ack 1 detik, free hook, spawn thread → `FreeLibraryAndExitThread(moduleHandle, 0)` untuk self-unload

> **Catatan unload:** `return FALSE` dari `DllMain` **bukan** mekanisme self-unload yang valid. `DllMain` return value `FALSE` cuma diproses saat `DLL_PROCESS_ATTACH` (artinya load gagal) — kalau dipanggil setelah attach sukses, return value di-ignore. Buat self-unload yang benar: spawn worker thread, di thread itu panggil `FreeLibraryAndExitThread(handle, 0)`. DLL gak boleh `FreeLibrary` ke dirinya sendiri dari thread yang punya reference ke DLL (deadlock). Ref: [Microsoft Docs — DllMain](https://learn.microsoft.com/en-us/windows/win32/dlls/dllmain), [FreeLibraryAndExitThread](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-freelibraryandexitthread).

**Estimasi:** 2–3 hari (section banyak, layout tuning perlu iterasi).

---

### T95: Named pipe client (Win32 API)

**Deskripsi:** Implementasi client pipe di `pipe_client.cpp` yang connect ke named pipe `GameSubTranslate.{gamePid}`.

**Lifecycle:**
1. `DllMain` on attach: spawn worker thread, connect ke pipe
2. Worker thread: send `Hello` message, wait for `ConfigSnapshot` reply, validate `protocolVersion`
3. Main thread: periodic heartbeat every 2s (status update)
4. Render loop: kirim `TextBatch` kalau ada text intercepted (Track B integration di Fase 8, untuk Track A kirim empty `TextBatch` atau `Status` update saja)
5. Worker thread listen loop: read `Translation`, `ConfigUpdate`, `Pause`/`Resume` messages
6. `DllMain` on detach: send `Unload`, join worker thread, close pipe handle

**Protocol:** newline-delimited JSON per PRD section 7. Schema version constant `"1.0.0"` di `config.h`, reject handshake kalau version mismatch.

**Reconnect logic:** kalau pipe putus (app crash, restart), retry every 3s dengan exponential backoff max 30s. UI status indicator reflect connection state.

**Dependencies on WPF app:** app harus sudah running dan listening di pipe sebelum plugin inject. Kalau pipe gak ada saat startup, plugin jalan offline (menu tetap accessible, status indicator merah "Disconnected").

**Done when:**
- Plugin connect ke WPF app `Hello`/`ConfigSnapshot` roundtrip jalan (validate via WPF app log)
- Heartbeat diterima app setiap 2s
- ConfigUpdate dari app (misal: user ganti target lang di WPF settings) ter-apply di plugin (update menu dropdown state)
- Pipe disconnect → status indicator update, reconnect otomatis saat app restart
- Plugin close → WPF app detect disconnect < 5s, fallback ke capture (kalau game profile ada di capture-fallback)

**Estimasi:** 1.5–2 hari.

---

### T96: Proxy-DLL mechanism (rename + exports forward)

**Deskripsi:** Konversi `GST.Inject.dll` jadi proxy DLL. Pattern: ambil `GST.Inject.dll` build artifact, rename jadi `dxgi.dll` (atau `d3d11.dll` — pilih salah satu), dan tambahkan stub exports yang forward semua fungsi ke system DLL asli via `LoadLibraryW(L"dxgi.dll")` + `GetProcAddress`.

**Cara kerja DLL proxying (Windows DLL search order):**
1. Saat game launch, Windows `LoadLibraryEx("dxgi.dll")`
2. Windows search: folder exe game → system32 → PATH
3. Folder exe game ada `dxgi.dll` (yang sebenernya plugin kita) → Windows load duluan
4. Plugin `DllMain` jalan → install hook → setup ImGui
5. Setiap export `dxgi.dll` di-forward ke system `dxgi.dll` asli yang di-load via `LoadLibraryW` di dalam proxy

**Pemilihan target DLL per game:**
- **`dxgi.dll`**: default choice. Mayoritas game DX11 load `dxgi.dll` duluan (DirectX Graphics Infrastructure).
- **`d3d11.dll`**: alternatif kalau game bundle `dxgi.dll` sendiri di folder (e.g. beberapa UE4 game). Cek dependency pakai `dumpbin /dependents game.exe`.
- **Dua-duanya**: kalau game load keduanya, plugin taro dua file `dxgi.dll` + `d3d11.dll` di folder (satu instance plugin, hooked via whichever loads first).

**Generate proxy header otomatis:**
```python
# scripts/gen_proxy.py (satu-shot script, gak masuk production code)
import pefile
pe = pefile.PE("C:/Windows/System32/dxgi.dll")
for exp in pe.DIRECTORY_ENTRY_EXPORT.symbols:
    print(f"#pragma comment(linker, \"/export:{exp.name}={exp.name},@{exp.ordinal}\")")
```
Generate `.def` file atau `#pragma comment` directives, link ke `GST.Inject.dll` build. Tiap export stub forward ke system DLL via wrapper function:
```cpp
HMODULE g_systemDxgi = nullptr;
void LoadSystemDxgi() {
    char sysPath[MAX_PATH];
    GetSystemDirectoryA(sysPath, MAX_PATH);
    strcat_s(sysPath, "\\dxgi.dll");
    g_systemDxgi = LoadLibraryA(sysPath);
}
extern "C" __declspec(dllexport) HRESULT WINAPI D3D11CreateDevice(...) {
    LoadSystemDxgi();
    auto fn = (HRESULT(WINAPI*)(...))GetProcAddress(g_systemDxgi, "D3D11CreateDevice");
    return fn(...);
}
// ... repeat untuk semua exports
```

**Build setup update:** CMakeLists tambahkan custom command untuk generate proxy stub sources sebelum compile, atau pakai library `libproxy` (tapi lebih simple generate manual sekali + commit hasil ke `vendor/dxgi_stub.cpp`).

**Folder deployment:** T96 tidak butuh "Launch with Translation" di WPF app. User flow:
1. WPF app jalan (background), listening di pipe `GameSubTranslate.*`
2. User drop `dxgi.dll` ke folder game (manual atau via installer di v2)
3. User launch game dari Steam/shortcut biasa
4. Game auto-load `dxgi.dll` proxy → plugin init → connect ke WPF app via pipe

**Done when:**
- Drop `dxgi.dll` (renamed dari `GST.Inject.dll` dengan proxy exports) ke folder game
- Launch game → menu toggle on Insert works
- Semua fungsi DirectX (D3D11CreateDevice, dll) ke-forward correctly → game render normal, no graphical glitch
- Folder `C:/Windows/System32/dxgi.dll` asli **tidak** ter-overwrite (proxy cuma rename file yang ditaro di folder game)
- Edge case: game yang bundle `dxgi.dll` sendiri di folder (rare) → user drop `d3d11.dll` proxy sebagai gantinya
- Edge case: game yang manifest-nya disable DLL search order (proteksi) → documented di known issues, gak fix di fase ini

**Estimasi:** 1–1.5 hari (generate stub ~0.5 hari, integration test ~0.5-1 hari).

**Catatan keamanan:** pendekatan ini **bukan** process injection, murni legitimate DLL loading. Windows Defender dan anti-malware umumnya allow behavior ini karena Reshade, ENB, mod framework lain (ASI Loader, ScriptHookV) pakai pattern yang sama. Risiko AV flag turun drastis dibanding `CreateRemoteThread`.

---

### T96b: WPF app — PluginSource + source switching

**Deskripsi:** Di Core, tambah `PluginSource : ISubtitleSource` concrete implementation. Di App, refactor existing `SourceSelector` (logic PRD section 9) untuk support plugin path.

**SourceSelector logic:**
```
Startup:
  1. App start di capture mode untuk active game profile.
  2. PipeServer start listen di GameSubTranslate.* (wildcard).
  3. Plugin (kalau di-drop ke folder game) connect → kirim Hello dengan gameId.
  4. Match gameId dengan active profile:
     - Match → switch ISubtitleSource ke PluginSource. Capture suspended.
     - Gak match → PluginSource tetap active untuk game yang lagi jalan, capture stay untuk profile lain.
  5. Heartbeat missed >10s → drop PluginSource, revert ke CaptureSource.
```

**PluginSource implementation:**
- Wrap pipe client + parsing JSON message sesuai PRD section 7
- Implement `ISubtitleSource.PullAsync()` → return `Translation` dari queue (di-push dari pipe messages)
- Implement `ISubtitleSource.Pause()/Resume()` → kirim Pause/Resume message ke plugin

**Error handling:**
- Pipe connect timeout (5s) → gak ada plugin = capture mode stay, no error popup (normal case untuk game tanpa plugin)
- Plugin crash mid-game → app detect disconnect <5s, fallback ke capture, kasih notifikasi sekali "Plugin disconnected, fallback ke capture mode"
- Protocol version mismatch → reject handshake, plugin status indicator merah "Protocol version tidak kompatibel"

**Game profile integration:**
- Tambah field `pluginCompatible: bool` di game profile schema
- Kalau `true`, app siap switch ke plugin source
- Kalau `false`, app gak listen di pipe untuk profile ini (gak mungkin plugin connect)

**Done when:**
- Active game profile punya plugin → app detect plugin via pipe `Hello` match, switch otomatis
- Active game profile tanpa plugin → app stay di capture mode
- Plugin crash → app fallback capture, notifikasi sekali, gak spam
- WPF app tetap jalan normal (gak hang/crash) kalau plugin connect-disconnect-connect cepat (race condition test)
- Existing capture flow gak regress (test pakai game profile existing tanpa plugin)

**Estimasi:** 1–1.5 hari (refactor `ISubtitleSource` udah ada di Core, tinggal concrete implementation + SourceSelector update).

---

### T97: End-to-end integration test + manual validation

**Deskripsi:** Manual test skenario full di multiple game + edge cases. Game test matrix:

| Game | Engine | DX version | Anti-cheat | Notes |
|---|---|---|---|---|
| Hollow Knight | Unity (Mono) | DX11 | None | Validate hook + ImGui render stability |
| Celeste | XNA/FNA | DX11 | None | Validate input forward (gameplay gak ke-block) |
| ~~Hades~~ | ~~Unity (IL2CPP)~~ | ~~DX11~~ | ~~EAC~~ | **DROP dari matrix awal** — anti-cheat game di-defer sampai metode proxy-DLL divalidasi aman di game tanpa proteksi. Test nanti kalau ada waktu. |
| Dragon's Dogma: Dark Arisen | Custom (MT Framework) | DX11 | None | Validate existing capture+OCR game juga bisa pakai menu |
| Final Fantasy XV Windows Edition | Custom (Luminous Studio) | DX11 | None | Validate edge case — custom engine dengan DX11 hook |
| Persona 5 Royal (kalau punya) | UE4 | DX11 | None | Validate UE4 specific (Track A only menu, gak expect text interception) |
| Persona 3 Reload (kalau punya) | UE4 | DX11 | None | UE4 juga |

**Test cases per game:**
- [ ] Plugin load tanpa crash
- [ ] Menu toggle on Insert → show, toggle off → hide
- [ ] Game FPS drop < 5% (ukur pakai FRAPS atau in-game benchmark sebelum/sesudah inject)
- [ ] Input di area menu → interact dengan ImGui widget
- [ ] Input di luar menu → diteruskan ke game (character jalan, attack, dialog skip)
- [ ] All 6 menu section accessible, gak ada yang crash
- [ ] Config save → reload game → config masih ke-apply
- [ ] Unload plugin dari menu → hook removed, game jalan normal tanpa plugin
- [ ] Kill game brutal (Task Manager End Task) → WPF app fallback ke capture clean, gak hang

**Edge cases:**
- Game launch tanpa plugin (klik biasa dari Steam) → capture mode jalan seperti biasa
- Multi-monitor: game di monitor 2, menu muncul di posisi yang benar
- Alt+Tab saat menu open → menu state ke-restore saat balik ke game (atau hide sementara)
- DPI scaling tinggi (4K monitor) → ImGui font gak pecah
- **Game dengan manifest disable DLL search order** (rare, e.g. beberapa game dengan proteksi ekstra) → documented sebagai known issue, butuh mod loader framework (Track B) untuk hook

**Done when:**
- All test cases pass di 3+ game (minimum: Hollow Knight, Dragon's Dogma, satu UE4 game). **Skip game ber-anti-cheat sampai T96/T96b divalidasi aman di game tanpa proteksi.**
- FPS impact documented per game (target < 5% drop)
- Known issues list dibuat di `docs/issues/fase-7.md`
- Video demo: screen record menu in-game di 1 game representative

**Estimasi:** 2–3 hari (banyak game, banyak iterasi).

---

### T98: Packaging + release artifact

**Deskripsi:** Bikin release artifact untuk distribusi.

**Struktur ZIP:**
```
GameSubTranslate.Injection-v0.1.zip
├── README.md                          (cara pakai, requirement, troubleshooting)
├── GST.Inject.dll                     (Release build, x64)
├── GST.Inject.pdb                     (opsional, untuk debug)
└── config/
    ├── inject-default-config.json     (default hotkey, pipe prefix)
    └── ui-config.json                 (default UI settings)
```

**Build script:** `build.bat` → CMake configure + build Release → copy DLL + PDB ke `dist/` → zip.

**Installer (deferred ke v2):** v1 manual drop-in. User copy `GST.Inject.dll` ke folder app, WPF app detect di launch. Future: script PowerShell auto-register dengan game profile.

**Documentation:**
- `plugins/inject/README.md` — cara build dari source
- `docs/injection-setup.md` — cara pakai, troubleshooting umum (antivirus block, anti-cheat issue)
- `docs/issues/fase-7.md` — known issues + workarounds

**Done when:**
- `build.bat` produce release artifact tanpa manual intervention
- README menjelaskan 3-step setup: extract → configure game profile → launch via app
- Known issues list di-update dari T97 testing
- Release tag `v0.7.0` (atau `v0.1.0-injection` kalau versioning beda) di git

**Estimasi:** 1 hari.

---

## Total estimasi

| Task | Estimasi |
|---|---|
| T91 Project skeleton | 0.5 hari |
| T92 DXGI hook (DX11 only) | 1–1.5 hari |
| T93 ImGui setup | 1–1.5 hari |
| T94 Menu UI | 2–3 hari |
| T95 Pipe client | 1.5–2 hari |
| T96 Proxy-DLL mechanism | 1–1.5 hari |
| T96b WPF app source switching | 1–1.5 hari |
| T97 E2E test (skip anti-cheat game awal) | 2–3 hari |
| T98 Packaging | 1 hari |
| **Total** | **11.5–16.5 hari (~2.5–3 minggu)** |

Buffer 20% untuk unexpected issue = ~3 minggu actual. Sedikit lebih cepat dari estimasi sebelumnya karena T96 launcher (CreateRemoteThread) diganti proxy-DLL yang lebih simple, plus scope mengecil (gak handle anti-cheat game di test matrix awal).

---

## Track B (deferred ke Fase 8+)

Track B = engine-specific text interception via BepInEx/UE4SS (sesuai PRD section 6). Di-defer karena:
1. Track A lebih rendah risk, validasi injection pipeline
2. Effort Track B 3-5x Track A (per-engine development)
3. Coverage benefit Track A = semua game DX11 (termasuk Track B targets yang juga DX11: Persona, Tales of, Nier, FF7 Remake)
4. Track A menu juga useful buat Track B (user configure di in-game, bukan WPF settings)

Saat Track A stabil, Track B jadi enhancement opsional: tambah text interception layer tanpa ubah menu/pipe/launcher infra.

---

## Reference

- `docs/PRD-Injection.md` — full vision document, section 7 (IPC), section 9 (hybrid app), section 11 (menu scope). Note: PRD sebut Track B (BepInEx/UE4SS engine-specific text interception) detail — Track A (DXGI universal menu) di fase ini adalah subset dari PRD. PRD perlu di-update untuk reflect: Track A = fase ini, Track B = Fase 8+. Update PRD bisa parallel dengan eksekusi T91, atau di-defer ke T98 (packaging).
- `CLAUDE.md` — git flow, conventions, known gotchas
- Reshade source (github.com/crosire/reshade) — referensi MinHook + DXGI hook + DLL proxying pattern, Apache-2.0
- Dear ImGui (github.com/ocornut/imgui) — UI library, MIT
- MinHook (github.com/TsudaKageyu/minhook) — hooking library, MIT
