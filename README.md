# GameSubTranslate

Personal Windows tool untuk auto-translate subtitle game PC (fokus RPG/story-heavy, termasuk AAA modern). Capture custom region di layar → OCR lokal (Tesseract) → translate via endpoint OpenAI-compatible → tampil di overlay transparan click-through di atas game. Built for personal use, **Windows-only**.

Lihat [`PRD-Auto-Translate-Subtitle-Game.md`](PRD-Auto-Translate-Subtitle-Game.md) untuk requirement lengkap. Aturan kerja + konteks project untuk AI assistant: [`CLAUDE.md`](CLAUDE.md).

## Status

| Fase | Status | Detail |
|---|---|---|
| 1 — Prototype (console, end-to-end) | ✅ Done | `docs/tasks/TASKS-fase-1-prototype.md` |
| 2 — MVP Overlay (WPF click-through) | ✅ Done | `docs/tasks/TASKS-fase-2-mvp-overlay.md` |
| 3 — Optimisasi (testing, caching, streaming, failover) | ✅ Done | `docs/tasks/TASKS-fase-3-optimisasi.md` |
| 4 — Polish & Packaging (installer, UI polish, presets) | ✅ Done | `docs/tasks/TASKS-fase-4-polish.md` |
| 5 — UI Revamp (design tokens, Segoe Fluent Icons, animations) | ✅ Done | `docs/tasks/TASKS-fase-5-ui-revamp.md` |

## Tech Stack

- **.NET 8** (`net8.0-windows10.0.19041.0`) — C# + WPF
- **Screen capture**: `Windows.Graphics.Capture` (Win2D), per-monitor, crop ke region
- **OCR**: Tesseract (`Tesseract` NuGet oleh Charlesw)
- **Translation**: `HttpClient` manual ke `/chat/completions` OpenAI-compatible (provider-agnostic: OpenAI, OpenRouter, Groq, Ollama, dsb) + retry exponential backoff + cache SQLite
- **Storage**: SQLite + Dapper (`%APPDATA%/GameSubTranslate/profiles.db`) — profile, region, translation cache
- **Config**: JSON + DPAPI-encrypted API key (`%APPDATA%/GameSubTranslate/settings.json`)
- **Global hotkey**: Win32 `RegisterHotKey` (toggle overlay / pause / settings / manual capture)
- **Overlay**: WPF window `WS_EX_LAYERED | WS_EX_TRANSPARENT` (click-through, always-on-top)
- **System tray**: `Hardcodet.NotifyIcon.Wpf`

## Struktur

```
.
├── src/
│   ├── GameSubTranslate.Prototype/   ← console end-to-end (testing CLI tanpa UI)
│   ├── GameSubTranslate.Core/        ← classlib: Capture, Ocr, Pipeline, Translation, Config, Storage, Profiles, Cache, Logging
│   └── GameSubTranslate.App/         ← WPF: MainWindow, ProfileEdit, RegionSelector, Overlay, Settings, Hotkeys, Onboarding
├── tests/
│   └── GameSubTranslate.Core.Tests/  ← xUnit (91 tests: ChangeDetector, TranslationClient, SettingsStore, TranslationCache, streaming, FileLogger, Ocr engines, failover, + Fase 5 UI helpers)
├── assets/tessdata/                  eng.traineddata
├── installer/                        (Fase 4) Inno Setup script + publish output
├── docs/
│   ├── PRD-Auto-Translate-Subtitle-Game.md
│   ├── game-presets.md               (Fase 4) preset region per game populer
│   ├── screenshots/                  (Fase 5) target file list untuk capture manual tiap window
│   └── tasks/
│       ├── TASKS-fase-1-prototype.md
│       ├── TASKS-fase-2-mvp-overlay.md
│       ├── TASKS-fase-3-optimisasi.md
│       ├── TASKS-fase-4-polish.md
│       └── TASKS-fase-5-ui-revamp.md
├── GameSubTranslate.sln
├── CLAUDE.md                         ruleset untuk AI assistant
└── README.md
```

## Setup

### Prasyarat

- Windows 10 1903+ (build 18362) — `Windows.Graphics.Capture` requirement
- .NET 8 SDK (build & dev)
- **Untuk runtime end-user (Fase 4 installer)**: .NET 8 Desktop Runtime — installer akan cek dan prompt download link resmi kalau belum ada
- API key endpoint OpenAI-compatible (OpenAI, OpenRouter, Groq, Ollama, dsb)

### Build

```bash
dotnet build
```

### Run (Fase 2 — app WPF)

```bash
dotnet run --project src/GameSubTranslate.App
```

### Run (Fase 1 — console prototype, untuk testing CLI)

```bash
# Tanam env var untuk translation
export OPENAI_API_KEY="sk-..."
export OPENAI_BASE_URL="https://api.openai.com/v1"
export OPENAI_MODEL="gpt-4o-mini"

# Run: --x --y --w --h = capture region, --interval = ms antar tick
dotnet run --project src/GameSubTranslate.Prototype -- --x 0 --y 800 --w 1280 --h 200 --interval 1000
```

## Cara Pakai (Fase 2)

1. Jalankan app → buka Settings (`Ctrl+Alt+S` atau tray icon) → isi Base URL, Model, API key → Test Connection → Save.
2. **New Profile** → nama game + executable name (untuk auto-load) → Save.
3. **Edit** profile → **Add Region** → drag rectangle di atas area subtitle game → beri nama → Save.
4. **Start** pipeline → overlay menampilkan terjemahan real-time di atas game.
5. Hotkeys (default, bisa diganti di Settings):
   - `Ctrl+Alt+T` — toggle overlay show/hide
   - `Ctrl+Alt+P` — pause/resume capture
   - `Ctrl+Alt+S` — buka Settings
   - `Ctrl+Alt+Space` — manual capture 1x (skip change detection)
6. Auto-load: fokus ke window game yang executable-nya match profile → profile aktif otomatis dipilih dalam <3 detik.
7. Tutup MainWindow → app tetap jalan di system tray. **Exit** (tray) untuk keluar penuh.

## Limitasi

- OCR English only (`eng.traineddata`). Bahasa lain = tambah `.traineddata` ke `assets/tessdata/`. Vision AI OCR (Fase 3) bisa handle font stylized multibahasa tanpa file tessdata.
- Capture region manual per game (auto-detect region via AI — Fase 5+).
- Model provider `:free` (OpenRouter) mudah kena rate limit → API call gagal beruntun. Retry + failover ke fallback provider + categorized overlay error sudah ditangani (Fase 3 T12, T39, T40).
- Latency end-to-end dominan waktu LLM API. Streaming mode (Fase 3 T36) turunkan first-token ke <1s; full response ~1.5–5s. Cache hit (exact + fuzzy) ~0-1ms.

## Self-checks

Verifikasi otomatis per task, jalan tanpa test framework:

```bash
# Core/Prototype
dotnet run --project src/GameSubTranslate.Prototype -- --selfcheck-t3
# App (WPF) — butuh display session
dotnet run --project src/GameSubTranslate.App -- --selfcheck-t14

# Fase 3 integration smoke (T35 long-running profile, T36 streaming, T37 fuzzy, dst)
for t in t35 t36 t37 t38 t39 t40 t41; do
  "/d/Coding/game-sub-translate/src/GameSubTranslate.App/bin/Debug/net8.0-windows10.0.19041.0/GameSubTranslate.App.exe" "--selfcheck-$t"
done
```

## Tests

xUnit suite (91 tests, sejak Fase 3 + Fase 5):

```bash
dotnet test
```

Coverage: `ChangeDetector`, `TranslationClient` (retry/timeout/error kategori/failover), `TranslationStream`, `SettingsStore` (DPAPI + JSON), `TranslationCache` (exact + fuzzy + Levenshtein), `FileLogger` (rotation), `TesseractOcrEngine` + `VisionAiOcrEngine`, `MaskLayer`-style converter helpers (Fase 5).

## UI Design System (Fase 5)

Sejak Fase 5, semua styling WPF pakai design tokens terpusat di `src/GameSubTranslate.App/Resources/Tokens.xaml` (color, font, spacing, radius, shadow). Implicit style untuk `Button`, `TextBox`, `PasswordBox`, `ComboBox`, `CheckBox`, `ListBox`, `TabControl`, `Slider`, `Window`. Named style: `Button.Primary`, `Button.Destructive`, `Slider.Polished`, `Tab.Card`, `Banner.Warn`, `Text.Helper`, `Font.Icon`. Iconography pakai **Segoe Fluent Icons** (built-in Win11), monospace U+E codepoint — no emoji, no font file tambahan.

Animasi pakai `DispatcherTimer` 16ms (60fps): overlay entrance slide (200ms ease-out) + pause-state glow pulse loop.

## Contributing

Personal project — tidak menerima external PR. Issues untuk self-tracking.

## License

Personal use only.
