# GameSubTranslate

Personal Windows tool untuk auto-translate subtitle game PC (fokus RPG/story-heavy, termasuk AAA modern). Capture custom region di layar → OCR lokal (Tesseract) → translate via endpoint OpenAI-compatible → tampil di overlay transparan click-through di atas game. Built for personal use, **Windows-only**.

Lihat [`PRD-Auto-Translate-Subtitle-Game.md`](PRD-Auto-Translate-Subtitle-Game.md) untuk requirement lengkap. Aturan kerja + konteks project untuk AI assistant: [`CLAUDE.md`](CLAUDE.md).

## Status

| Fase | Status | Detail |
|---|---|---|
| 1 — Prototype (console, end-to-end) | ✅ Done | `docs/tasks/TASKS-fase-1-prototype.md` |
| 2 — MVP Overlay (WPF click-through) | ✅ Done | `docs/tasks/TASKS-fase-2-mvp-overlay.md` |
| 3 — Optimisasi | ⏳ Next | `docs/tasks/TASKS-fase-3-optimisasi.md` |
| 4 — Polish | ⏳ Pending | |

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
│   ├── GameSubTranslate.Core/        ← classlib: Capture, Ocr, Pipeline, Translation, Config, Storage, Profiles, Cache
│   └── GameSubTranslate.App/         ← WPF: MainWindow, ProfileEdit, RegionSelector, Overlay, Settings, Hotkeys
├── assets/tessdata/                  eng.traineddata
├── docs/
│   ├── PRD-Auto-Translate-Subtitle-Game.md
│   └── tasks/
│       ├── TASKS-fase-1-prototype.md
│       └── TASKS-fase-2-mvp-overlay.md
├── GameSubTranslate.sln
├── CLAUDE.md                         ruleset untuk AI assistant
└── README.md
```

## Setup

### Prasyarat

- Windows 10 1903+ (build 18362) — `Windows.Graphics.Capture` requirement
- .NET 8 SDK
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

- OCR English only (`eng.traineddata`). Bahasa lain = tambah `.traineddata` ke `assets/tessdata/`.
- Capture region manual per game (auto-detect region via AI — Fase 3+).
- Model provider `:free` (OpenRouter) mudah kena rate limit → API call gagal beruntun (retry + overlay error status sudah ditangani).
- Latency end-to-end dominan waktu LLM API (~1.5–5s di verifikasi), cache hit ~0-1ms.

## Self-checks

Verifikasi otomatis per task, jalan tanpa test framework:

```bash
# Core/Prototype
dotnet run --project src/GameSubTranslate.Prototype -- --selfcheck-t3
# App (WPF) — butuh display session
dotnet run --project src/GameSubTranslate.App -- --selfcheck-t14
```

## Contributing

Personal project — tidak menerima external PR. Issues untuk self-tracking.

## License

Personal use only.
