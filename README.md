# GameSubTranslate

> Real-time subtitle translation overlay for PC games. Pick a region, get translated text on screen.

Windows-only. C# / .NET 8 / WPF. Capture → OCR (Tesseract / PaddleOCR / Vision AI) → translate (OpenAI-compatible) → click-through overlay.

![Status](https://img.shields.io/badge/status-active-success)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

## Preview

Drop screenshots of the main windows into [`docs/screenshots/`](docs/screenshots/README.md) and they'll show up here.

| Main | Overlay | Region Selector | Settings |
|------|---------|-----------------|----------|
| _tbd_ | _tbd_ | _tbd_ | _tbd_ |

## What it does

- Capture any rectangle of your screen (per-monitor, multi-DPI).
- OCR engine of choice: **Tesseract** (instant, local), **PaddleOCR** (fast + accurate for stylized fonts, GPU-accelerated when CUDA available), or **Vision AI** (cloud fallback for noisy frames).
- Translate via any OpenAI-compatible endpoint (OpenAI, OpenRouter, Groq, Ollama, LM Studio…).
- Render result in a click-through overlay that sits on top of the game.
- Hotkeys to toggle / pause / manually capture without leaving the game.
- Auto-load a profile when a matching game window gets focus.
- Translation cache (exact + fuzzy) — repeated lines cost ~0ms.

## Why

Most RPG / story-heavy games ship with English-only text or no official translation. This tool lets you play them in your language without alt-tabbing, without modifying game files, and without running a game process injection (read-only screen capture only).

## Quickstart

Requires Windows 10 1903+ and the .NET 8 SDK.

```bash
git clone https://github.com/hanif05/gamesub-translate.git
cd gamesub-translate
dotnet build
dotnet run --project src/GameSubTranslate.App
```

On first launch the welcome wizard walks you through:

1. **Provider** — base URL, model, API key (encrypted with DPAPI at rest).
2. **Profile** — name + target executable (e.g. `CODEVEIN.exe`).
3. **Region** — drag a rectangle over the game's subtitle area.

Press <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>T</kbd> to toggle the overlay. <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>P</kbd> to pause. Full hotkey list in Settings → Hotkeys.

## CLI (optional)

A console prototype lives in `src/GameSubTranslate.Prototype/` for headless testing or scripted capture loops.

```bash
export OPENAI_API_KEY="sk-..."
export OPENAI_BASE_URL="https://api.openai.com/v1"
export OPENAI_MODEL="gpt-4o-mini"

dotnet run --project src/GameSubTranslate.Prototype -- \
  --x 0 --y 800 --w 1280 --h 200 --interval 1000
```

## Tech stack

| Layer | Choice |
|-------|--------|
| UI | WPF on `net8.0-windows10.0.19041.0` |
| Capture | `Windows.Graphics.Capture` + Win2D |
| OCR | Tesseract (`Tesseract` by Charlesw) · PaddleOCR (`Sdcb.PaddleOCR`, Apache-2.0, CUDA optional) · Vision AI (OpenAI-compatible image endpoint) |
| Translation | `HttpClient` to OpenAI-compatible `/chat/completions` — provider-agnostic |
| Cache | SQLite + Dapper, exact + Levenshtein-fuzzy lookup |
| Storage | JSON + DPAPI-encrypted secrets (`%APPDATA%/GameSubTranslate/`) |
| Hotkeys | Win32 `RegisterHotKey` |
| Overlay | WPF `WS_EX_LAYERED \| WS_EX_TRANSPARENT` |
| Tray | `Hardcodet.NotifyIcon.Wpf` |

Design system: tokens (`Resources/Tokens.xaml`), Segoe Fluent Icons, 60fps `DispatcherTimer` animations — all bundled in `src/GameSubTranslate.App/`.

## Project layout

```
.
├── src/
│   ├── GameSubTranslate.Prototype/   console end-to-end smoke
│   ├── GameSubTranslate.Core/        capture, OCR, translation, pipeline, config, storage
│   └── GameSubTranslate.App/         WPF windows, hotkeys, tray, design tokens
├── tests/
│   └── GameSubTranslate.Core.Tests/  xUnit (99 tests)
├── assets/tessdata/                  eng.traineddata
├── docs/
│   ├── PRD-Auto-Translate-Subtitle-Game.md
│   ├── game-presets.md
│   ├── screenshots/                  drop PNGs here, README picks them up
│   └── tasks/                        per-fase task logs
├── installer/                        Inno Setup script
├── tools/                            build helpers (icon generator, etc.)
├── GameSubTranslate.sln
├── CLAUDE.md                         AI assistant ruleset
└── README.md
```

## Tests

```bash
dotnet test
```

99 xUnit tests covering the change detector, translation client (retry/timeout/error categorization/failover), streaming, settings store, cache, file logger, all three OCR engines, and the Fase 5 UI helpers.

## Self-checks

Some flows have standalone self-check commands that exit non-zero on failure — useful in CI without pulling in a display server.

```bash
dotnet run --project src/GameSubTranslate.Prototype -- --selfcheck-t3
dotnet run --project src/GameSubTranslate.App         -- --selfcheck-t14
```

## Known limitations

- OCR ships with English only. Drop additional `*.traineddata` into `assets/tessdata/` for Tesseract, or switch to PaddleOCR / Vision AI for other languages (PaddleOCR auto-downloads the English v3 model from `paddleocr-models/` on first use).
- End-to-end latency is dominated by the LLM API. Streaming brings first-token to sub-second; cache hits are ~0ms.
- Free-tier providers (e.g. OpenRouter `:free`) rate-limit aggressively. The client retries with exponential backoff and falls over to a backup provider if configured.
- PaddleOCR GPU mode requires `cudnn64_8.dll` in `PATH` (CUDA Toolkit 12.x or extract from cuDNN zip). Without it the engine falls back to CPU (mkldnn) which still works, just slower on NVIDIA hardware.

## Contributing

PRs welcome for bug fixes, new providers, additional game presets, or UI polish. For new features please open an issue first so we can agree on the shape. See [`CLAUDE.md`](CLAUDE.md) for the AI assistant workflow used in this repo.

## License

[MIT](LICENSE) © hanif05
