# Issue: Pragmata — same dialog translated 2-3× due to OCR noise between frames

**Date:** 2026-08-08
**Game:** Pragmata (steam)
**Reporter:** user (via diagnostic log)
**Severity:** medium — UX annoyance, not data loss; wastes API quota

## Symptom

User reports the same line of dialog gets re-translated 2-3 times in a row, surfacing as a "koreksi" (correction) on the overlay. Latency is amplified because the translation provider is a reasoning model (qwen3.6-27b) where each call takes 20-30 s.

## Diagnostic capture

Wired `FileLogger` into `TranslatePipeline` (commit `640a780`) and asked user to tail `%APPDATA%\GameSubTranslate\logs\app-YYYY-MM-DD.log` during a real Pragmata run. The first 60 s of capture below is from one short exchange:

```
16:41:46 INFO [OCR] recognize text="| SS Bu as. |
\.. 1 =
A A Diana, aren't you tired? g

AW a!"
16:41:47 INFO [Translate] request (stream) src="| SS Bu as. |
\.. 1 =
A A Diana, aren't you tired? g

AW a!"
16:41:48 INFO [OCR] recognize text="~ Hugh ss & Scere
lug WIZ A

. Dy Diana, aren't you tired? Te"
16:41:48 INFO [Translate] request (stream) src="~ Hugh ss & Scere
lug WIZ A

. Dy Diana, aren't you tired? Te"
16:41:49 INFO [OCR] recognize text="» Hugh De

fl A
Done aren't you tired? me |"
16:41:49 INFO [Translate] request (stream) src="» Hugh De

fl A
Done aren't you tired? me |"
```

(Plus 8 more cycles following the same pattern for the next two lines of dialog.)

## Root causes

### 1. Capture region too tall — captures character nameplate + UI chrome

Pragmata's subtitle box sits at the bottom of the screen, but the dialog exchange above clearly shows the OCR also read:
- A **character name line** above the actual dialog ("| SS Bu as. |", "~ Hugh ss & Scere", "» Hugh De")
- **Box-drawing / decoration glyphs** ("\.. 1 =", "lug WIZ A", "fl A", "AW a!", "Te", "|")
- Speaker tags and punctuation that the speaker's name UI adds every frame

These nameplate lines change visually between frames (fade-in, position, antialiasing), so they re-trigger change detection and pollute the OCR text every cycle.

### 2. OCR noise varies frame-to-frame

Tesseract reads a slightly different string each time the same dialog line is on screen:

| Cycle | OCR text |
|---|---|
| 1 | `"A A Diana, aren't you tired? g\n\nAW a!"` |
| 2 | `". Dy Diana, aren't you tired? Te"` |
| 3 | `"Done aren't you tired? me \|"` |

The actual dialog (`"Diana, aren't you tired?"`) is the same, but the leading decoration and trailing punctuation shift every cycle.

### 3. Pipeline triggers a fresh API call each cycle

`TranslatePipeline.cs:219` compares the raw OCR text against `_lastText`:

```cs
if (!string.IsNullOrWhiteSpace(text) && text != _lastText)
```

Each cycle produces a *different* string, so the condition is true every time and a fresh API call fires. The fuzzy cache at `GetFuzzy(..., similarityThreshold=0.85)` never matches because the noise overhead drops similarity to ~0.5-0.6 — below threshold.

Net effect per dialog line: **3 OCR calls + 3 translation requests** for a single line of dialog, each 20-30 s because of the reasoning model. That's the "koreksi" the user sees.

## Fixes

### Fix 1 — tighten the capture region (user-side, highest impact)

Re-run the Region Selector in Pragmata and crop the box so it only contains the dialog text line(s). The nameplate and any UI chrome above/around the dialog should be outside the box.

This alone should remove the noise glyphs and make most cycles OCR-identical, which makes the existing `_lastText` exact-match gate work.

### Fix 2 — normalize text before cache key + comparison (code)

Add a `NormalizeForCache(string)` helper used both as the cache key AND for `_lastText` comparison. Normalization:
- Strip lines whose alphabetic content is below a threshold (drops pure decoration)
- Collapse whitespace
- Trim punctuation that's likely noise (leading `|`, `~`, `»`, etc.)

Use the normalized string as:
- the key passed to `_cache.Get` / `_cache.Put` / `_cache.GetFuzzy`
- the value compared to `_lastText` in the loop

**Why this helps:** once OCR cycle 1/2/3 collapse to the same normalized form (`"Diana, aren't you tired?"`), the loop's exact-match comparison short-circuits and only one translation fires.

### Fix 3 — lower fuzzy threshold as a safety net (code)

Drop `GetFuzzy` default from `0.85` → `0.80`. With noise overhead removed (Fix 1+2), this is rarely needed, but it covers the case where OCR legitimately reads a different substring of the same dialog (e.g. wraps to 2 lines and we only catch one). (Tuned 0.80 per user request 2026-08-08 — lower than 0.85 to catch the "Done"/"Diana" first-word misread, high enough to avoid junk matches.)

### Fix 4 — drop lines that are mostly non-alphabetic (code, belt + suspenders)

Some cycles produce noise-only output ("Hugh De\nfl A"). Add a check: if after normalization the result has <40 % letters, drop the cycle entirely (treat as "no useful text"). This prevents garbled OCR from polluting the cache with junk translations.

## Out of scope

- Switching OCR engine. The Vision AI OCR fallback (T38) doesn't fix this — the noise comes from the *input image*, not the recognizer.
- Subtitle-frame detection ("only OCR when the subtitle is fully drawn"). Possible but a much larger refactor; not justified by this single game.
- Cache key hashing / dedup. Fix 2 + 3 already collapse identical dialog lines to one API call.

## Status

- **Fix 1 (region):** user-side, belum diterapkan.
- **Fix 2 + 3 + 4 (code):** DITERAPKAN 2026-08-08. Detail implementasi:
  - `NormalizeForCache` ditaruh di `Translation/TextCleaning.cs` (bukan method private di pipeline — file sudah shared + ada test file di sana), public internal-static.
  - Normalisasi 3 layer: drop baris noise (ratio huruf + panjang word + maxword), ambil cuma baris dialog dominan (terpanjang), strip leading short-word noise + trailing fragment.
  - `TranslatePipeline.cs`: loop compare pakai normalized string (`norm != _lastText`), `CaptureOnceAsync` set `_lastText` normalized, `_cache.Put` pakai normalized key.
  - `GetFuzzy` default `0.85` → `0.80` (Fix 3, diturunkan per user request).
  - Test: `tests/GameSubTranslate.Core.Tests/Translation/NormalizeForCacheTests.cs` (4 test, semua pass). Full suite 91/91 pass.
- **Catatan:** "Done aren't you tired?" vs "Diana, aren't you tired?" tidak bisa disamakan oleh normalisasi (salah-OCR huruf pertama, beda kata). Keduanya jadi 2 key beda — tapi sim Levenshtein ~0.9 → `GetFuzzy` (Fix 3) tangkap, cache hit, tanpa API call tambahan. Jadi per dialog line: cycle 1+2 collapse ke 1 key (1 translation), cycle 3 ke key lain tapi fuzzy hit.

## Files touched (actual)

- `src/GameSubTranslate.Core/Translation/TextCleaning.cs` — added `NormalizeForCache` (+ helpers)
- `src/GameSubTranslate.Core/Pipeline/TranslatePipeline.cs` — normalized `_lastText` compare, cache key, garbage-drop
- `src/GameSubTranslate.Core/Cache/TranslationCacheRepository.cs` — `GetFuzzy` default threshold `0.85` → `0.80`
- `tests/GameSubTranslate.Core.Tests/Translation/NormalizeForCacheTests.cs` — new tests

After Fix 1 (region) is applied, the next Pragmata run should show in the log:
- Same `[OCR] recognize` entries but with much shorter, cleaner text
- Several consecutive `[OCR] skip (same as last)` entries between dialog changes
- One `[Translate] request` per dialog line, not 3

If `[OCR] recognize` entries are still noisy after region tightening, ship Fix 2 + 3 + 4 in one batch and re-run.

## Files touched (planned)

- `src/GameSubTranslate.Core/Pipeline/TranslatePipeline.cs` — add `NormalizeForCache`, use for `_lastText` compare + cache key, drop garbage lines
- `src/GameSubTranslate.Core/Cache/TranslationCacheRepository.cs` — change `GetFuzzy` default threshold `0.85` → `0.80`
- `tests/GameSubTranslate.Core.Tests/Pipeline/NormalizeForCacheTests.cs` — new tests for the helper

_(lihat "Files touched (actual)" di atas — lokasi test beda dari rencana: `Translation/` bukan `Pipeline/` karena helper ditaruh di `TextCleaning.cs`.)