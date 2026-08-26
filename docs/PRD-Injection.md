# PRD: In-Game Plugin Injection for GameSubTranslate

**Document version:** 0.1 (draft)
**Date:** 25 August 2026
**Author:** Hanif
**Status:** Vision document — pre-implementation planning
**Parent PRD:** `PRD-Auto-Translate-Subtitle-Game.md` (capture-based flow, v1.3)
**Purpose:** Document the architectural direction for extending GameSubTranslate with an in-game plugin that hooks the game's text rendering pipeline. This is a planning artifact, not a task list. Concrete task breakdown (T-numbers, estimates, dependencies) will be produced in `docs/tasks/TASKS-fase-7-injection.md` once this PRD is approved and the architecture is validated.

---

## 1. Context

The current implementation (Fase 1–6) uses a **screen-capture source**: the WPF app grabs a user-defined region of the desktop, runs OCR on the captured pixels, and translates the result. This works for any game regardless of engine and is read-only at the OS layer, so it never conflicts with anti-cheat or violates terms of service. The trade-off is OCR accuracy on stylized fonts, ~100–500ms latency overhead, and per-game region calibration.

A second, complementary approach is to inject a plugin into the game process and hook the engine's text-rendering functions directly. The plugin intercepts the original text string before it is rasterized, sends it to the translation pipeline, and receives the translated text back for display. The trade-off is per-engine development effort and the need to update the plugin when games patch their function signatures, but the benefits are large: zero OCR error, sub-50ms interception latency, and the ability to render the translation as a native in-game element (full control over font, position, and styling).

This PRD covers the second approach as a **hybrid extension** of the existing app. Capture remains a first-class fallback for games that cannot be hooked (proprietary engines, anti-cheat-protected online games, emulators).

## 2. Goals

1. **Universal coverage across major engines.** A single plugin project produces DLLs that hook Unreal Engine 4/5, Unity Mono, and Unity IL2CPP games via existing mature mod frameworks (UE4SS, BepInEx, MelonLoader).
2. **Seamless fallback.** When a supported game launches with the plugin installed, the WPF app automatically switches from capture mode to injection mode. When the plugin is absent or fails to connect, the app falls back to capture without user intervention.
3. **Zero-cost text acquisition.** Hook the engine's text-rendering function so the plugin receives the original (pre-rasterization) string in the source language. No OCR is involved on the injection path.
4. **In-game configuration UI.** Provide an OptiScaler-style in-game menu (ImGui) for status, settings, live translation log, and per-game config editing without alt-tabbing.
5. **Community-extensible per-game configs.** A verified config JSON per game, with auto-detection by executable hash or process name. Community-contributed configs are accepted via pull request without recompilation.
6. **Reuse the existing translation pipeline.** The Core project (`ITranslationClient`, `ITranslatedSubtitleSink`, OCR engines, overlay renderer) is consumed unchanged by both capture and injection paths.

## 3. Non-Goals (v1 of injection feature)

- **Voice / audio translation.** Out of scope. Same as capture PRD non-goal.
- **Online / multiplayer game support.** Plugins will not be tested against games with active anti-cheat. Single-player only for v1.
- **Linux / macOS.** Windows-only, same as the existing app.
- **Per-game visual translation of fonts or text styling.** The plugin extracts the string, not the rendered glyph. Font/style preservation is the responsibility of the in-game renderer.
- **Auto-generation of game configs.** v1 ships with hand-verified configs. Heuristic auto-detection is a future enhancement.
- **Plugin distribution via a public package manager.** v1 ships as a manual drop-in. Auto-update is opt-in for v1, off by default.
- **Hooking proprietary engines** (Naughty Dog, Capcom RE Engine, RGG Studio). These have no public signature surface and require manual reverse engineering per game, which is out of scope.

## 4. Target User

Same as capture PRD: Hanif (primary) and technically-inclined users comfortable with dropping a DLL into a mod framework folder. The in-game menu is designed for non-developer users; the JSON config layer is for advanced users who want to add unsupported games.

## 5. Architecture Overview

```
┌──────────────────────┐         named pipe (JSON)         ┌──────────────────────┐
│   Game process       │  ───────────────────────────▶     │   WPF App            │
│  ┌────────────────┐  │   OriginalText (source lang)      │  ┌────────────────┐  │
│  │ BepInEx /      │  │                                   │  │ PipeServer     │  │
│  │ UE4SS /        │  │   TranslatedText (target lang)    │  │  (existing)    │  │
│  │ MelonLoader    │  │  ◀───────────────────────────     │  └────────┬───────┘  │
│  │  ┌──────────┐  │  │                                   │           │          │
│  │  │ GST      │  │  │   Status / heartbeat / settings   │           ▼          │
│  │  │ Plugin   │◀─┼──┼───────────────────────────────────│  ┌────────────────┐  │
│  │  └──────────┘  │  │                                   │  │ Translation    │  │
│  │  ┌──────────┐  │  │                                   │  │ Pipeline       │  │
│  │  │ ImGui    │  │  │                                   │  │ (Core)         │  │
│  │  │ Menu     │  │  │                                   │  └────────────────┘  │
│  │  └──────────┘  │  │                                   │           │          │
│  └────────────────┘  │                                   │           ▼          │
└──────────────────────┘                                   │  ┌────────────────┐  │
                                                            │  │ Overlay        │  │
                                                            │  │ (existing)     │  │
                                                            │  └────────────────┘  │
                                                            └──────────────────────┘
```

The plugin runs inside the game process. The WPF app runs in the background. Communication is a Windows named pipe carrying JSON messages. The pipe is the same channel regardless of engine.

## 6. Plugin Target Matrix

| Engine | Mod framework | Build target | Hook surface | Coverage examples |
|---|---|---|---|---|
| UE4 (4.20–4.27) | UE4SS | `net6.0` | `FTextLayout::SetText`, `FCanvasTextItem::Draw` in `SlateCore.dll` / `Engine.dll` | Persona 3 Reload, Persona 5 Royal, Tales of Arise, Final Fantasy VII Remake Intergrade, Nier Automata, Kena |
| UE5 (5.0+) | UE4SS (UE5 branch) | `net6.0` | `FSlateApplication`/`STextBlock` in `SlateCore.dll` | UE5-built ports, upcoming Persona / Final Fantasy titles |
| Unity Mono | BepInEx | `netstandard2.0` | `TMP_Text.SetText`, `LocalizationManager.GetLocalizedString` via Harmony patch | RPG Maker MV/MZ (Mono), Trails in the Sky / Crossbell / Decisive ports, indie Unity RPGs |
| Unity IL2CPP | MelonLoader (or BepInEx.IL2CPP) | `net6.0` | Same as Mono, via `Il2CppInterop` wrapper | Most modern Unity ports, Hollow Knight Silksong, mobile ports |

**One project, two output DLLs.** A single csproj with `<TargetFrameworks>netstandard2.0;net6.0</TargetFrameworks>` produces both BepInEx-Mono-compatible and UE4SS/IL2CPP-compatible binaries. Engine-specific code is isolated behind an adapter interface to keep `#if` blocks minimal.

## 7. Inter-Process Communication

**Transport:** Windows named pipe. Pipe name is fixed (`GameSubTranslate.{processId}`) so multiple game instances can run concurrently with the app. PipeOptions.Asynchronous, message mode.

**Protocol:** Newline-delimited JSON. Each frame is a single UTF-8 JSON object terminated by `\n`. Schema versioned via a `protocolVersion` field in every message; plugin and app reject mismatched versions.

**Message types (plugin → app):**

| Type | Fields | Purpose |
|---|---|---|
| `Hello` | `protocolVersion`, `gameId`, `engine`, `executable`, `processId` | Initial handshake, app uses this to identify the source |
| `TextBatch` | `sessionId`, `entries[]` (`{text, timestampMs}`) | Bulk text intercepted in a frame |
| `Heartbeat` | `sessionId`, `uptimeMs`, `translationsSent` | Keep-alive every 2s, app can detect plugin crash |
| `Status` | `sessionId`, `state` (`idle\|translating\|error`), `lastError?` | Optional status updates |
| `Unload` | `sessionId`, `reason` | Plugin shutting down cleanly |

**Message types (app → plugin):**

| Type | Fields | Purpose |
|---|---|---|
| `ConfigSnapshot` | `gameConfig`, `uiConfig` | Initial config delivery on `Hello` |
| `Translation` | `sessionId`, `entryId`, `translatedText`, `engine` | Translated text for a specific intercepted entry |
| `ConfigUpdate` | `gameConfig` | Live config change (target lang, skip patterns, overlay position) |
| `Pause` / `Resume` | `sessionId` | Translation control |
| `Shutdown` | `reason` | App closing, plugin should disconnect gracefully |

**Backpressure:** The plugin throttles TextBatch to one frame per game tick (or every 16ms, whichever is larger). The app can drop batches if a translation queue is full, with a logged warning. No acknowledgement at the entry level; only the session-level Heartbeat.

## 8. Plugin Lifecycle

1. **Game launch.** Mod framework loads `GameSubTranslate.UniversalPlugin.dll`.
2. **Initialization.** Plugin reads its own embedded config defaults, then opens a connection to the app's pipe.
3. **Game detection.** Plugin reads `Process.GetCurrentProcess().ProcessName` and main module hash, then attempts to load a matching game config from `%APPDATA%\GameSubTranslate\plugin-configs\verified\{gameId}.json`. Falls back to the bundled default config if no match.
4. **Handshake.** Plugin sends `Hello`. App replies with `ConfigSnapshot`. Plugin validates protocol version.
5. **Hook installation.** Engine-specific adapter is selected at runtime (Mono / IL2CPP / UE4SS). Harmony patch or signature scan installs the text-rendering hook.
6. **ImGui registration.** Menu callback registered with the mod framework's ImGui integration (BepInEx.ImGui, UE4SS `OnImGuiDraw`).
7. **Steady state.** Plugin intercepts text → sends `TextBatch` → receives `Translation` → renders the translated text via the same overlay path the WPF app uses (or via the engine's own canvas if a richer in-game renderer is chosen).
8. **Config hot-reload.** `FileSystemWatcher` on the config dir triggers a debounced reload. App can also push `ConfigUpdate` over the pipe.
9. **Unload.** On game exit, plugin sends `Unload` and closes the pipe cleanly. App detects disconnect within 5s and falls back to capture if the user has the same game profile loaded.

## 9. Hybrid App Model (Capture Fallback)

The WPF app runs a `SourceSelector` that holds a `ISubtitleSource` and falls back automatically:

```
Startup:
  1. App starts in capture mode for the currently selected game profile.
  2. PipeServer starts listening on GameSubTranslate pipe.

Source selection (per game profile):
  - If the user has a game profile with a known plugin-compatible gameId,
    App waits for a Hello from the plugin on that pipe for up to 5 seconds.
  - If Hello arrives → switch ISubtitleSource to PluginSource. Capture is suspended.
  - If Hello does not arrive within 5s → stay on CaptureSource.

Runtime:
  - If Heartbeat is missed for >10s, App drops PluginSource and reverts to CaptureSource.
  - User can force source via settings: "Always capture" / "Plugin only" / "Auto".
  - Switching source mid-game is supported; in-flight translations are dropped, not retried.
```

This is the only behavioral change to the existing app surface. The existing `CaptureSource`, OCR engines, `ITranslationClient`, and overlay renderer are reused without modification. The new `PluginSource` implements `ISubtitleSource` alongside `CaptureSource` and is selected by the same factory.

## 10. Per-Game Configuration

**Schema (JSON, draft-07, validated against `config/schema/game-config.schema.json`):**

```json
{
  "schemaVersion": "1.0.0",
  "gameId": "persona5royal",
  "verified": true,
  "engine": "UE4",
  "executable": "P5R.exe",
  "processNames": ["P5R"],
  "exeHashes": ["a1b2c3..."],
  "sourceLang": "ja",
  "targetLang": "id",
  "hookPoints": [
    {
      "module": "SlateCore.dll",
      "pattern": "48 8B C4 48 89 58 ?? 48 89 68",
      "name": "FTextLayout::SetText",
      "extractArgs": [2]
    }
  ],
  "skipPatterns": ["^Lv\\.\\s*\\d+", "^\\d+円"],
  "uiFilter": {
    "minLength": 2,
    "maxLength": 500,
    "ignoreIfAllDigits": true
  },
  "captureFallback": {
    "enabled": true,
    "region": { "x": 0, "y": 0, "w": 800, "h": 100 }
  }
}
```

**Storage locations:**

- **Bundled defaults** (ship with the plugin binary): minimal config for engine-only use.
- **Verified configs** (`%APPDATA%\GameSubTranslate\plugin-configs\verified\`): hand-tested by the project owner, git-tracked in `config/verified/`.
- **Community configs** (`%APPDATA%\GameSubTranslate\plugin-configs\community\`): user-contributed via PR to a separate repository or a `community-configs` branch, downloaded opt-in by the app.

**Detection order on plugin startup:** process name → exe hash → bundled default.

**Skip patterns** are regex applied to the source string before sending to the translation API. This avoids API cost on UI noise (HP/MP numbers, level indicators, currency).

**Capture fallback region** in the config lets the app know where to fall back to if the plugin crashes mid-game.

## 11. In-Game Menu (ImGui)

The in-game menu is a separate visual layer from the translation overlay itself. The overlay is what shows the translated dialog on screen; the menu is the configuration surface.

**Toggle key:** configurable per game, default `Ctrl+Shift+T`.

**Sections:**

| Section | Controls | Notes |
|---|---|---|
| Status | Connection indicator, engine detected, source/target lang, last 3 translations | Read-only diagnostic |
| Translation | Target language dropdown, provider dropdown, API endpoint text field | Persisted to `ui-config.json` |
| Display | Overlay position, font size, background opacity | Applied live via `ConfigUpdate` message |
| Advanced | Skip-pattern editor, capture fallback toggle, "Edit config JSON", "Reload config" | Reload triggers `FileSystemWatcher` debounce |
| Log | Last N translation entries with original + translated text | Read-only, useful for debugging |
| Actions | Pause/Resume translation, Unload plugin | Unload sends `Unload` message and removes the hook |

**Implementation:** `BepInEx.ImGui` for Unity targets, UE4SS's built-in ImGui for UE targets. The plugin uses `ImGuiNET` so the same code path renders the menu in both engines. The toggle key is registered via the mod framework's input system.

**File edits inside the menu** use `ImGui.InputTextMultiline` for the JSON config, with a "Save" button that writes the file and triggers a reload. The user does not need to alt-tab to a text editor.

## 12. Distribution & Installation

- **Plugin DLL:** shipped in the GitHub release under `plugins/`. One ZIP per supported engine, each containing the appropriate `GameSubTranslate.UniversalPlugin.dll` plus a README.
- **Verified configs:** shipped alongside the DLL, copied to `%APPDATA%` on first run.
- **Installer:** a small PowerShell or C# tool that detects the game's mod framework, copies the DLL to the correct folder, and downloads the matching verified config. Out of scope for v1 — manual install is fine.
- **Auto-update (opt-in):** a setting in the WPF app that pulls the latest plugin and verified configs from GitHub releases. Off by default.

## 13. Security & Anti-Cheat Considerations

- The plugin modifies the game process via the mod framework's documented API. This is the same approach BepInEx and UE4SS plugins use and is not flagged by anti-cheat in single-player offline mode.
- For online / anti-cheat-protected games (EAC, BattlEye, Vanguard), the plugin will crash on load or trigger a ban. The plugin's `Hello` message includes an explicit "this is a single-player profile" flag so the app can warn the user. v1 does not attempt to bypass anti-cheat.
- The named pipe is local-only (named pipe ACL restricted to current user SID). No data leaves the machine except the translation API call, which is the same surface as the existing capture flow.

## 14. Open Questions (to resolve before task breakdown)

1. **Hook signature stability across UE4 versions.** Persona 5 Royal runs UE 4.27; Persona 3 Reload runs UE 4.27 also. Some UE games use a heavily-modified engine fork (e.g. Final Fantasy VII Remake). A signature pattern that works for stock UE4 may not match a fork — per-game signature overrides are needed in the config schema. **Resolve: confirm via UE4SS testing in v0.1 spike.**
2. **Translation source fidelity.** Engine text functions sometimes return already-localized strings (e.g. UI strings in the player's chosen language). The plugin needs a heuristic to distinguish source-language dialog from already-localized UI. **Resolve: prototype on one UE game and one Unity game before finalising the skip-pattern schema.**
3. **Overlay rendering on the plugin side vs the app side.** The plugin can render translations as a native engine element (full styling control, no overlay window) or send them back to the app for the existing WPF overlay. v1 uses the app-side overlay (simpler, reuses existing code). **Resolve: revisit if a use case demands engine-native rendering.**
4. **Multi-game session handling.** If the user launches two games that both use GameSubTranslate, the pipe name includes processId so concurrent sessions work, but the WPF app only renders one overlay at a time. v1 supports the most-recently-connected plugin. **Resolve: confirm scope with the user — multi-monitor handling is a v2 feature.**
5. **BepInEx vs MelonLoader for Unity IL2CPP.** BepInEx has an IL2CPP fork; MelonLoader is more popular for IL2CPP games specifically. v1 supports BepInEx Mono + MelonLoader IL2CPP. UE4SS handles UE. **Resolve: validate MelonLoader API compatibility with .NET 6 plugin code in v0.1 spike.**

## 15. Out-of-Scope for This PRD

- Plugin-side translation cache (e.g. remembering "Potion" → "Ramuan" across sessions). The Core pipeline already has a translation cache; plugin just passes through.
- Voice / audio capture. Same as capture PRD non-goal.
- Mobile / console ports of Unity games. The plugin only runs on Windows desktop processes.
- Cloud sync of plugin configs. Local file storage for v1.

## 16. Success Criteria (Vision, Not Measurable Here)

When this PRD is implemented, success looks like:

- A new user can install GameSubTranslate, drop the plugin DLL into a supported game folder, launch the game, see the in-game menu confirm "connected," and read translated dialog in their target language with under 200ms perceived latency.
- When the user launches an unsupported game, the WPF app continues to work in capture mode with no intervention.
- Adding a new game is a 30-line JSON config and a pull request; no recompilation of the plugin binary.
- The plugin survives a game patch (function signature change) in 95% of cases by relying on signature patterns rather than absolute addresses, with clear diagnostic output for the 5% that need a config update.

## 17. Related Documents

- `PRD-Auto-Translate-Subtitle-Game.md` — parent PRD, capture flow.
- `CLAUDE.md` — project conventions, git flow, known gotchas.
- `docs/tasks/TASKS-fase-1-prototype.md` … `TASKS-fase-6-*.md` — completed phase task lists.
- Future: `docs/tasks/TASKS-fase-7-injection.md` — concrete task breakdown derived from this PRD.

## 18. Revision History

| Version | Date | Author | Notes |
|---|---|---|---|
| 0.1 | 2026-08-25 | Hanif | Initial draft based on architecture discussion |

---

**Approval needed before:**
1. Starting a v0.1 spike (one engine, one game, end-to-end validation of hook → pipe → translate → overlay).
2. Creating `docs/tasks/TASKS-fase-7-injection.md` from this PRD.
3. Modifying the Core project to add `ISubtitleSource` abstraction and `PipeServer`.
