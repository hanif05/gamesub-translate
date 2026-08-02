# GameSubTranslate

Personal Windows tool untuk auto-translate subtitle game PC (fokus RPG/story-heavy, termasuk AAA modern). Capture custom region di layar → OCR lokal (Tesseract) → translate via endpoint OpenAI-compatible → tampil di overlay. Built for personal use, **Windows-only**.

Lihat [`PRD-Auto-Translate-Subtitle-Game.md`](PRD-Auto-Translate-Subtitle-Game.md) untuk requirement lengkap. Aturan kerja + konteks project untuk AI assistant: [`CLAUDE.md`](CLAUDE.md).

## Status

| Fase | Status | Detail |
|---|---|---|
| 1 — Prototype (console, end-to-end) | ✅ Done | `docs/tasks/TASKS-fase-1-prototype.md` |
| 2 — MVP Overlay (WPF click-through) | ⏳ Next | `docs/tasks/TASKS-fase-2-mvp-overlay.md` (belum dibuat) |
| 3 — Optimisasi | ⏳ Pending | |
| 4 — Polish | ⏳ Pending | |

## Tech Stack

- **.NET 8** (`net8.0-windows10.0.19041.0`) — C# + WPF (Fase 2+)
- **Screen capture**: GDI+ via `System.Drawing.Common` (Fase 1); `Windows.Graphics.Capture` (Fase 2+)
- **OCR**: Tesseract (`Tesseract` NuGet oleh Charlesw)
- **Translation**: `HttpClient` manual ke `/chat/completions` OpenAI-compatible (provider-agnostic)
- **Storage**: SQLite (planned Fase 2+)
- **Hotkey/Overlay**: Win32 API

## Struktur

```
.
├── src/
│   └── GameSubTranslate.Prototype/   ← Fase 1: console end-to-end
│       ├── Capture/                  ScreenCapture.cs (GDI+)
│       ├── Ocr/                      IOcrEngine + TesseractOcrEngine
│       ├── Translation/              TranslationClient (OpenAI-compatible)
│       ├── Pipeline/                 ChangeDetector + TranslatePipeline
│       ├── Config/                   AppConfig (env-var)
│       └── Program.cs                entry point
├── assets/tessdata/                  eng.traineddata
├── docs/
│   ├── PRD-Auto-Translate-Subtitle-Game.md
│   └── tasks/
│       └── TASKS-fase-1-prototype.md
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

### Run (Fase 1 prototype)

```bash
# Tanam env var untuk translation
export OPENAI_API_KEY="sk-..."
export OPENAI_BASE_URL="https://api.openai.com/v1"
export OPENAI_MODEL="gpt-4o-mini"

# Run: --x --y --w --h = capture region, --interval = ms antar tick
dotnet run --project src/GameSubTranslate.Prototype -- --x 0 --y 800 --w 1280 --h 200 --interval 1000
```

Translation opsional: kalau env var kosong, app tetap jalan (OCR + console print), cuma skip call ke API.

## Cara Pakai

1. Jalankan game / buka video dengan subtitle.
2. Tentukan koordinat region subtitle (PoSH: `(Get-Process game).MainWindowHandle` + screenshot untukukur).
3. Run dengan `--x --y --w --h` sesuai region.
4. Tekan Ctrl+C untuk stop.

Console print: `{timestamp} | src: ... | dst: ...` setiap kali teks berubah.

## Limitasi Fase 1

- Capture masih GDI+ (`CopyFromScreen`) — bisa blank/artifact di game dengan protected surface atau full-screen exclusive mode. **Fase 2** pindah ke `Windows.Graphics.Capture`.
- Output console only, belum overlay di atas game. **Fase 2** bikin WPF window click-through.
- OCR English only (`eng.traineddata`). Bahasa lain = tambah `.traineddata` ke `assets/tessdata/`.
- Translation client belum retry/timeout. TODO Fase 2: timeout 10s + exponential backoff 3x.

## Contributing

Personal project — tidak menerima external PR. Issues untuk self-tracking.

## License

Personal use only.
