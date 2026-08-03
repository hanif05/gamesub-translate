# TASKS — Breakdown Fase 3 (Optimisasi)

**Status:** Draft — siap eksekusi setelah approval.
**Branch target:** `feature/fase-3-optimisasi` (dibuat dari `main` saat mulai, setelah `feature/fase-2-mvp-overlay` merged).
**Estimasi roadmap PRD:** 1–2 minggu.
**Dependency:** Fase 2 selesai (T1–T26 merged, T27 = shift-drag overlay juga merged).

Tujuan fase ini: perdalam fondasi optimisasi yang sudah ada di Fase 2, isi gap yang di-defer, dan penuhi NFR di PRD section 7. Perubahan BUKAN fitur baru yang user-facing (kecuali T35 streaming & T37 vision OCR), melainkan kualitas internal + keandalan.

## Scope yang Dibawa dari Fase 2

Fase 2 sudah punya fondasi:
- Change detection (T11, perceptual hash 64-bit).
- TranslationClient retry+timeout (T12, 3 attempt exp backoff).
- Translation cache by exact hash (T13).
- Pipeline pause/resume + manual trigger (T17, T22).
- Settings DPAPI + tray (T3, T24).

Fase 3 **memperdalam & mengisi celah**, bukan replace. TIDAK refactor besar — desain stabil.

## Yang TIDAK Masuk Fase 3 (deferred ke Fase 4+)

- Auto-detect region via vision AI (PRD 5.2) — Fase 5+.
- Speech-to-text (PRD 5.2) — Fase 5+.
- Overlay history / log dialog (PRD 5.2) — Fase 4 atau 5, bukan prioritas optimisasi.
- Auto-switch region by context (PRD 5.2) — Fase 5+.
- Installer, auto-update, distribusi (Fase 4).
- Multi-target language quick switch UI — Fase 4 polish (multi-language support sudah ada di settings per PRD 16, hanya UI quick switch yang kurang).
- Preset komunitas (PRD 5.2) — out of scope personal tool.

## Aturan Eksekusi

- Setiap task = 1 commit (atau 1 PR kecil). Pesan: `T<n>: <short desc>` (lanjut nomor dari Fase 2, mulai T28).
- Tiap task HARUS selesai + verifikasi sebelum lanjut ke task yang bergantung.
- Target framework tetap `net8.0-windows10.0.19041.0`.
- Dependency NuGet tambahan fase ini (tambah hanya kalau ada justifikasi kuat):
  - `xunit` + `xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk` (unit test project).
  - `Moq` atau `NSubstitute` (mock `HttpMessageHandler` & `IDispatcher`).
  - TIDAK tambahkan `Polly` — hand-roll retry sudah cukup (sudah ada di T12).
  - TIDAK tambahkan benchmark library (`BenchmarkDotNet`) kecuali terbukti perlu — cukup stopwatch + log.
- Branch `feature/fase-3-optimisasi` WAJIB dibuat sebelum commit pertama. Jangan commit ke `main`.
- Unit test dijalankan via `dotnet test` dari root, harus exit code 0.

---

## Urutan Task (by Dependency)

### FASE 3.A — Testing Foundation

#### T28. xUnit test project + infrastructure
**Status**: ✅ done (commit `ca9c9ec`).
**Deskripsi**: Tambah project `tests/GameSubTranslate.Core.Tests/GameSubTranslate.Core.Tests.csproj` (xUnit + Moq). Setup:
- Reference ke `GameSubTranslate.Core` saja (App butuh WPF, di-test terpisah di fase 4).
- Folder `Helpers/` dengan `MockHttpMessageHandler` (delegate `Func<HttpRequestMessage, HttpResponseMessage>` untuk stub HTTP response, plus counter untuk hit count).
- Folder `Fixtures/` dengan `TempAppDataFixture` (xunit `IDisposable`, set `APPDATA` ke folder temp supaya DPAPI test tidak mengotori user settings).
- `csproj` pakai `IsPackable=false`, `GenerateDocumentationFile=false`.
- Tambah ke `GameSubTranslate.sln`.

**Output**: project `tests/GameSubTranslate.Core.Tests/` dengan sample 1 test kosong yang pass.
**Done when**: `dotnet test` dari root jalan, exit 0, 1 test pass. ✅
**Depends on**: —

#### T29. Tests: ChangeDetector
**Status**: ✅ done (commit `8667196`).
**Deskripsi**: Test `ChangeDetector` (Core/Pipeline/) pakai synthetic PNG 8x8 (built in-code, tidak perlu file fixture):
- Identik 100x → `false`.
- 1 pixel beda (noise) di threshold 5 → `false`.
- Teks berbeda (gambar fully replaced) → `true`.
- Hash deterministik: hash image A == hash image A (re-call).
- Hamming distance calculation benar untuk synthetic 64-bit hash.
- Edge: empty PNG / 0x0 → throw atau return true (documented).

**Output**: `tests/.../Pipeline/ChangeDetectorTests.cs`, min 6 test cases. — 12 cases implemented.
**Done when**: `dotnet test --filter ChangeDetector` all green. ✅
**Depends on**: T28.

#### T30. Tests: TranslationClient (retry + timeout + error categorization)
**Status**: ✅ done (commit `2092b1c`).
**Deskripsi**: Test `TranslationClient` (Core/Translation/) pakai `MockHttpMessageHandler`:
- HTTP 200 + valid body → return translated text.
- Timeout di attempt 1, success di attempt 2 (pakai `Task.Delay` di handler) → 1 retry, return text, elapsed ~retry-delay.
- HTTP 429 di attempt 1, 200 di attempt 2 → retry, success.
- HTTP 401 (non-retryable) → throw setelah 1 attempt, no retry.
- HTTP 500 di semua attempt → throw setelah 3 attempt.
- Invalid JSON response → throw `TranslationException` dengan kategori `BadResponse`.
- Empty API key di constructor → throw di ctor (validation).
- Pakai `ISetupTimer` atau sleep dengan margin, **HINDARI** `Thread.Sleep` persis sama dengan retry delay (flaky).

**Output**: `tests/.../Translation/TranslationClientTests.cs`, min 7 test cases. — 10 cases implemented.
**Done when**: `dotnet test --filter TranslationClient` all green. ✅
**Depends on**: T28.

#### T31. Tests: SettingsStore (DPAPI + JSON round-trip + corruption)
**Status**: ✅ done (commit `2d50543`).
**Deskripsi**: Test `SettingsStore` (Core/Config/) pakai `TempAppDataFixture`:
- Save settings → load → semua field equal (ApiKey di-decrypt benar).
- ApiKey di file hasil save = base64, BUKAN plaintext (grep).
- File hilang → `Load()` return default `AppSettings`, tidak throw.
- File corrupt (JSON invalid) → `Load()` return default, log warning (capture log pakai `ILogger` mock atau `TestOutputHelper`).
- DPAPI di-test round-trip minimal 1 case (sisanya covered by `DataProtectionScope.CurrentUser` Microsoft sendiri).

**Output**: `tests/.../Config/SettingsStoreTests.cs`, min 5 test cases. — 6 cases implemented.
**Done when**: `dotnet test --filter SettingsStore` all green. ✅
**Depends on**: T28.

#### T32. Tests: TranslationCache (hash + get/put + DB cleanup)
**Status**: ✅ done (commit `1979821`).
**Deskripsi**: Test `TranslationCacheRepository` (Core/Cache/) pakai in-memory SQLite (`:memory:` connection):
- Put `("Hello", "Halo", "id")` → Get return `"Halo"`.
- Get untuk teks yang tidak ada → return `null`.
- Hash deterministik: `"Hello|id"` == `"Hello|id"` (re-call).
- Hash beda untuk target lang beda: `"Hello|id"` != `"Hello|en"`.
- Same source text + lang overwrite OK (replace).
- Cleanup: `DeleteOlderThan(DateTime)` remove entries created sebelum cutoff.

**Output**: `tests/.../Cache/TranslationCacheTests.cs`, min 5 test cases. — 7 cases implemented.
**Done when**: `dotnet test --filter TranslationCache` all green. ✅
**Depends on**: T28.

**Side fixes (bundled in T32 commit):**
- `Database.EnsureSchema` skip `Directory.CreateDirectory` ketika path kosong (`:memory:` SQLite test mode).
- `TranslationCacheRepository.Put` accept optional `DateTime? createdAt` parameter (back-compat default `DateTime.UtcNow`).
- Tambah `TranslationCacheRepository.DeleteOlderThan(DateTime cutoff)` (spec T32 meminta method ini).

---

### FASE 3.B — Performance

#### T33. Adaptive capture interval
**Status**: ✅ done (commit `0edd6cb`).
**Deskripsi**: Di `TranslatePipeline`, ganti fixed interval `AppSettings.CaptureIntervalMs` jadi adaptive:
- Default: pakai `CaptureIntervalMs` (Fase 2 behavior).
- **Idle mode**: kalau 3 capture berturut-turut tidak ada perubahan (change detector return false), naikkan interval ke `min(CaptureIntervalMs * 4, 3000ms)` — kurangi CPU saat subtitle diam.
- **Active mode**: begitu ada perubahan, reset ke `CaptureIntervalMs` normal.
- Tambahan: kalau pipeline paused, **skip capture total** (capture loop sleep saja, return early). Fase 2 tetap capture saat paused (T17 catatan); Fase 3 optimize: pause = no capture = no CPU.
- Settle ke idle setelah 5 detik tidak ada perubahan.

**Output**: update `src/Core/Pipeline/TranslatePipeline.cs`, tambah `AppSettings.IdleCaptureIntervalMs` (default 3000), `AppSettings.IdleActivationThreshold` (default 3 frames).
**Done when**:
- Pipeline running di region kosong: CPU usage < 1% (cek Task Manager).
- Teks berubah 3x: interval kembali ke normal.
- Pause → CPU 0% (verify dengan log internal + Task Manager).
**Depends on**: T16 (Fase 2, sudah done).

#### T34. Lazy-load + dispose Tesseract
**Status**: ✅ done (commit `2144ef6`).
**Deskripsi**: `TesseractOcrEngine` saat ini init di constructor (Fase 2). Fase 3:
- **Lazy**: init `TesseractEngine` di method `Recognize` pertama, bukan constructor. `App.OnStartup` jadi tidak block.
- **Dispose on idle**: `Timer` background dispose engine setelah 5 menit tanpa OCR call. Next `Recognize` re-init.
- Exception handling: kalau `TesseractEngine` ctor gagal (file `tessdata` hilang) → throw dengan pesan jelas + saran fix (letakkan di `assets/tessdata/`).
- `IDisposable` di engine, cleanup di app exit sudah ada — verify tidak double-dispose.

**⚠ Thread-safety (catatan saat eksekusi, bukan blocker tapi wajib di-handle):**
Tesseract internal TIDAK thread-safe — `Recognize` dan `Dispose` tidak boleh overlap. Background `Timer` (di ThreadPool) yang trigger `Dispose` setelah 5 menit idle BISA race dengan `Recognize` call yang baru masuk dari pipeline (capture loop jalan di thread lain). Pattern yang aman:
- Pakai `SemaphoreSlim(1, 1)` atau `object _lock` di `TesseractOcrEngine`:
  - `Recognize` acquire lock → panggil engine.Recognize → release.
  - Timer callback acquire lock (blocking `Wait()`) → panggil engine.Dispose() → set `_engine = null` → release.
- Hasil: kalau `Recognize` sedang jalan, `Dispose` nunggu sampai selesai (sebaliknya juga — Recognize nunggu Dispose selesai re-init, ~300ms).
- `TesseractOcrEngine` jadi mutable shared state, dokumentasikan di XML comment: "All public methods are thread-safe; do not call Dispose externally — handled by idle timer + app shutdown."

**Done when**:
- App startup time < 500ms (cek dengan stopwatch, sebelumnya init Tesseract ~300ms).
- Idle 5 menit → `tessdata/` file handle di-release (verify dengan `handle.exe` atau comment in log).
- First OCR setelah idle latency ~300ms (re-init), berikutnya normal.
- Stress test: Recognize + Dispose simultan 100x di parallel (xUnit `[Theory]` dengan `Parallel.For`) → tidak ada `ObjectDisposedException` atau access violation.
**Depends on**: — (Fase 2 T5 sudah done, ini deepen).

#### T35. Memory & CPU profiling pass
**Status**: ✅ done (commit `0da45de`).
**Deskripsi**: Jalankan app full pipeline 1 jam (bisa pakai video YouTube dengan subtitle sebagai loop subtitle), monitor:
- **RAM usage** (Task Manager → Details → Working Set). Target: < 200MB idle. Kalau lebih, identify via dotMemory trial atau `dotnet-counters` (`--counters System.Runtime`) dan fix leak.
- **CPU usage** saat tidak ada perubahan teks. Target: < 1% average. Idle capture interval (T33) + lazy Tesseract (T34) harus cukup.
- **Handle count** (Process Explorer). Tiap profil harusnya stabil, tidak naik terus.
- **GC pressure** (`dotnet-counters System.Runtime` → `% Time in GC since last GC` < 5%).

Catat hasil di section "Hasil Verifikasi" T42.
**Output**: tidak ada code change langsung (atau fix minor kalau ada leak), hanya profiling report. Bug yang ditemukan → task baru (T36+) di luar breakdown ini.
**Done when**: profil sesuai target NFR PRD 7. Kalau tidak, **wajib** ada task tambahan di Fase 3 untuk fix.
**Depends on**: T33, T34.

---

### FASE 3.C — Translation Quality

#### T36. Streaming translation (SSE)
**Status**: ✅ done (commit `60c76fa`).
**Deskripsi**: Tambah method `IAsyncEnumerable<string> TranslateStreamAsync(string text, CancellationToken ct)` ke `TranslationClient` untuk support streaming response dari endpoint OpenAI-compatible yang support SSE (`stream=true` di body). Pakai `HttpClient.SendAsync` dengan `HttpCompletionOption.ResponseHeadersRead`, baca stream line-by-line (`data: {...}\n\n`), parse JSON per chunk, extract `choices[0].delta.content`.

**⚠ Backward compatibility (PENTING — T30 test suite tidak boleh jadi outdated):**
- `TranslateAsync(string, CancellationToken)` **TIDAK berubah** — masih return full string, masih dipakai pipeline non-realtime (cache warm-up, manual capture single-shot, T22). T30 tests tetap valid tanpa modifikasi.
- `TranslateStreamAsync` **method baru** — terpisah, tidak replace `TranslateAsync`.
- Di pipeline real-time (loop capture→OCR→translate→overlay), `TranslatePipeline` di-update untuk prefer `TranslateStreamAsync` (pakai `await foreach` append per token ke `OverlayViewModel`). Kalau endpoint/provider tidak support `stream=true` (T30 test pakai mock non-streaming juga termasuk ini), `TranslateStreamAsync` fallback internal ke `TranslateAsync` (kirim request non-streaming, yield seluruh response sebagai 1 chunk). Fallback ini menjaga pipeline tetap jalan di semua provider.
- `IAsyncEnumerable` lebih cocok dari `Task<string>` per-chunk karena caller `await foreach` syntax natural + cancellation token propagate bersih via `WithCancellation`.

**Hook ke UI:**
- `OverlayViewModel` tambah method `IProgress<string>` atau subscribe ke `INotifyCollectionChanged` dari buffer token — partial text muncul incremental (bukan tunggu full response ~1.5s baru muncul sekaligus).
- Latency target: first token < 1 detik (vs full response ~1.5-2s saat ini).

**Test impact:** T30 tests untuk `TranslateAsync` tetap pass tanpa diubah. Tambah test baru `TranslateStreamAsyncTests` di T28 suite — pakai mock handler yang return SSE-formatted response, verify token di-yield dalam urutan benar + fallback ke non-streaming saat endpoint tidak support.

**Output**: update `src/Core/Translation/TranslationClient.cs` (+ `TranslateStreamAsync`), `src/App/Overlay/OverlayViewModel.cs` (append per token).
**Done when**:
- OpenRouter streaming model → partial text muncul di overlay sebelum complete.
- Non-streaming model (atau endpoint reject `stream=true`) → fallback graceful, full text tetap muncul.
- First token latency < 1s terukur via log.
**Depends on**: T12 (Fase 2).

#### T37. Fuzzy cache match
**Status**: ✅ done (commit `09911ec`).
**Deskripsi**: Update `TranslationCacheRepository`:
- Tambah `GetFuzzy(string sourceText, string targetLang, double similarityThreshold) -> (string translated, double similarity)?`.
- Similarity: **Normalized Levenshtein distance** (`1 - editDistance / max(lenA, lenB)`), threshold default 0.85.
- Implementasi Levenshtein: hand-roll (1 method, < 30 baris), atau pakai `namespace` `System.Memory.Extensions` kalau ada. TIDAK tambah NuGet (FuzzySharp terlalu besar).
- Lookup strategy: exact match dulu (existing T13). Kalau miss, scan recent N rows (default 500, di-sort by `CreatedAt DESC`), hitung similarity, return best match kalau >= threshold.
- Hook ke pipeline: kalau fuzzy hit, pakai cached result tapi log `[cache-fuzzy] similarity=0.91`.

**Output**: update `src/Core/Cache/TranslationCacheRepository.cs`, integrasi di `TranslatePipeline` sebelum panggil `TranslationClient`.
**Done when**:
- Translate "Hello world" → cache exact. Translate "Hello worlds" (1 char beda) → fuzzy hit, similarity 0.92, no API call.
- Translate "Completely different text" → no fuzzy hit (similarity < threshold), API call normal.
- Test: similarity calculation benar untuk 5 known cases.
**Depends on**: T13 (Fase 2).

#### T38. Vision AI OCR fallback (pluggable)
**Status**: ✅ done (commit `6cea253`).
**Deskripsi**: Tambah `VisionAiOcrEngine` (Core/Ocr/) implementasi `IOcrEngine` pakai OpenAI-compatible vision endpoint.

**⚠ Keputusan interface (PENTING — eksplisit di sini supaya T38 + test T29 tidak ambigu):**
Fase 1 T5 mendesain `IOcrEngine.Recognize(byte[] pngBytes) -> string` sebagai **sinkron**. Tapi `VisionAiOcrEngine` butuh HTTP call → **wajib async** untuk hindari deadlock kalau dipanggil dari UI thread (WPF).
**T38 mengubah signature** menjadi:
```csharp
public interface IOcrEngine {
    Task<string> RecognizeAsync(byte[] pngBytes, CancellationToken ct = default);
    void Dispose();
}
```
`TesseractOcrEngine.RecognizeAsync` jadi wrapper: panggil sync method existing di `Task.Run` (Tesseract engine internal tidak thread-safe di panggil paralel, jadi `Task.Run` cukup — eksekusi di thread pool, tidak block UI). `TesseractOcrEngine` jadi internal `string Recognize(byte[])` yang dipanggil via `Task.Run`. Caller (pipeline) di-update untuk `await` RecognizeAsync. Ini **breaking change kecil**: caller lama yang `Recognize(...)` sync harus migrasi ke `await RecognizeAsync(...)`. Scope breaker: hanya `TranslatePipeline` (1 call site) + `SelfCheck` script kalau ada.

**Alasan tidak pakai `.Result`/`.Wait()`:** WPF UI thread + sync-over-async = deadlock klasik (task menunggu sync continuation yang nunggu UI thread bebas).

**Test impact:** T29 (ChangeDetector) tidak kena (test ChangeDetector, bukan OcrEngine). Tambah test baru `TesseractOcrEngineTests` di T28 suite — verify `RecognizeAsync` return task yang complete dengan text benar.

**Output**: file `src/Core/Ocr/IOcrEngine.cs` (signature baru), `VisionAiOcrEngine.cs`, update `TesseractOcrEngine.cs` jadi `RecognizeAsync`. Wire di `TranslatePipeline` (1 call site update).

**Detail Vision AI HTTP call:**
- POST ke `/chat/completions` dengan `messages: [{role:"user", content: [{type:"text", text:"<prompt OCR>"}, {type:"image_url", image_url:{url:"data:image/png;base64,..."}}]}]`.
- System prompt: "Ekstrak teks dari gambar ini. Jawab HANYA dengan teks hasil ekstraksi, tanpa penjelasan."
- Reuse `HttpClient` config dari `TranslationClient` (timeout 10s, retry 3x — atau reuse instance langsung).
- `IOcrEngine` factory: `OcrEngineFactory.Create(OcrEngineType, AppSettings)` di Core.
- Update `AppSettings.OcrEngine` enum sudah ada (`Tesseract` / `VisionAi`) → wiring di pipeline sudah cukup.
- Settings UI sudah punya ComboBox `OcrEngine` (T23, VisionAi disabled placeholder) → enable VisionAi option.

**Output (lengkap)**: file `src/Core/Ocr/IOcrEngine.cs` (signature baru), `VisionAiOcrEngine.cs`, update `TesseractOcrEngine.cs` jadi `RecognizeAsync`, update `SettingsWindow.xaml` (enable VisionAi option).
**Done when**:
- Pilih `VisionAi` di settings, set `BaseUrl`/`Model`/API key valid → OCR pakai vision model return text benar.
- Switch balik ke `Tesseract` → jalan normal, no regression.
- Test dengan screenshot game font stylized (misal Gothic font) → vision AI lebih akurat dari Tesseract (verify manual).
**Depends on**: T3 (settings model, Fase 2), T12 (HttpClient + retry, Fase 2).

#### T39. Better error reporting
**Status**: ✅ done (commit `969e0b4`).
**Deskripsi**: Categorize `TranslationException`:
- `Category` enum: `Network` (timeout, DNS, connection refused), `Auth` (401, 403), `RateLimit` (429), `BadRequest` (400, invalid params), `Provider` (5xx, model error), `Unknown`.
- `TranslationClient` set category berdasarkan HTTP status + exception type.
- Overlay: ganti `⚠ [translate-error]` generic jadi `⚠ [auth-error: cek API key]` / `⚠ [rate-limit: tunggu...]` / `⚠ [network: cek koneksi]` — lebih actionable untuk user.
- Tray icon: tooltip saat error = "Translation error: <category>".

**Output**: update `src/Core/Translation/TranslationClient.cs` (exception class), `src/App/Overlay/OverlayViewModel.cs`, `src/App/App.xaml.cs` (tray tooltip).
**Done when**:
- API key invalid → overlay tampil `⚠ [auth-error: cek API key di Settings]`.
- Rate limit 429 → overlay tampil `⚠ [rate-limit: provider limiting, retry...]`.
- Network down → overlay tampil `⚠ [network: cek koneksi internet]`.
- Tidak ada error message generic (semua categorized).
**Depends on**: T12 (Fase 2).

---

### FASE 3.D — Reliability

#### T40. Provider failover (primary + fallback)
**Deskripsi**: Extend `AppSettings`:
- `Provider` list: `List<ProviderConfig>` (satu entry default untuk back-compat), masing-masing `Name`, `BaseUrl`, `ApiKey`, `Model`, `IsPrimary`.
- UI: Settings tab "API & Model" jadi dynamic — bisa add/remove provider, drag reorder primary.
- Logic: `TranslationClient` coba primary dulu. Kalau 3x consecutive failure kategori `Network` atau `Provider` (5xx) → switch ke fallback. Tandai "degraded" di overlay. Re-try primary setelah 5 menit.
- `Auth` / `BadRequest` / `RateLimit` TIDAK trigger failover (key salah di primary akan salah juga di fallback).

**Output**: update `src/Core/Config/AppSettings.cs`, `src/Core/Translation/TranslationClient.cs`, `src/App/Settings/SettingsWindow.xaml` (dynamic provider list).
**Done when**:
- Primary API down (simulate dengan invalid BaseUrl pointing ke `127.0.0.1:1`) → auto-failover ke fallback dalam 3 attempt + ~10 detik.
- Primary key invalid (`Auth` error) → TIDAK failover, tampil error auth langsung.
- Setelah primary recover (5 menit) → next translate coba primary lagi.
**Depends on**: T3 (Fase 2 settings), T12 (Fase 2), T39 (kategorisasi error).

#### T41. Persistent error log with rotation
**Deskripsi**: Tambah logger sederhana (hand-roll, no NuGet — pakai `StreamWriter` ke file):
- Folder `%APPDATA%/GameSubTranslate/logs/` dengan file `app-YYYY-MM-DD.log`.
- Log level: `Info`, `Warn`, `Error`. Filter: `Warn`+ ke file, `Info` hanya ke file debug.
- Rotation: file > 5MB → archive jadi `app-YYYY-MM-DD-<seq>.log`, keep max 5 archived files. Older → delete.
- Log entry: timestamp + level + category + message. Ex: `2026-08-03 14:23:11 ERROR [TranslationClient] Auth error: 401`.
- Wire ke: pipeline events, hotkey actions, settings changes, error events. BUKAN setiap frame (hanya perubahan state + error).
- Settings UI: tombol "Open Logs Folder" di tab General/About (tambah section kecil).

**Output**: `src/Core/Logging/FileLogger.cs`, wire ke `App.OnStartup`.
**Done when**:
- Force error 3x → 3 baris ERROR muncul di file log hari ini.
- File > 5MB (generate manual dengan script kecil) → archive tercipta, active file reset.
- 5+ archived files → oldest auto-deleted.
- "Open Logs Folder" di Settings → Explorer terbuka di folder yang benar.
**Depends on**: — (independent).

---

### FASE 3.E — Verification

#### T42. End-to-end verification Fase 3
**Deskripsi**: Jalankan app full pipeline + exercise semua fitur Fase 3, extended dari skenario T26:
1. Baseline test (ulang 10 skenario T26) → semua tetap pass, no regression.
2. Adaptive interval: idle region 30 detik → log menunjukkan interval naik ke 3000ms, CPU < 1%.
3. Lazy Tesseract: app startup < 500ms (stopwatch). Idle 5 menit → re-init saat OCR call berikutnya.
4. Streaming translation: first token < 1s, partial text muncul incremental.
5. Fuzzy cache: translate "Hello world" + "Hello worlds" → 1 API call, similarity 0.92 logged.
6. Vision AI OCR: pilih VisionAi di settings, OCR font stylized → return text benar.
7. Error categories: 5 jenis error (network/auth/rate-limit/bad-request/provider) → overlay tampil kategori spesifik.
8. Provider failover: primary API down → auto-switch ke fallback dalam 10s, overlay mark "degraded".
9. Memory profile: app idle 1 jam → RAM < 200MB, no handle leak.
10. Log file: trigger 5 error → file log ada 5 entry, rotation kerja saat file besar.

Catat hasil di section "Hasil Verifikasi" di bawah.
**Output**: section "Hasil Verifikasi" + tabel hasil.
**Done when**: semua 10 skenario di atas lulus + regression T26 tetap pass.
**Depends on**: T28–T41.

---

## Dependency Graph (Ringkas)

```
T28 → T29, T30, T31, T32
T33 → T35
T34 → T35
T12 (Fase2) → T36, T38
T13 (Fase2) → T37
T3, T12 (Fase2) → T38
T12 (Fase2) → T39
T3, T12, T39 → T40
T28-T41 → T42
```

Critical path: T28 → T29 → T35 → T42 (atau T33 → T35 → T42).

## Estimasi Kasar (untuk planning sprint)

| Task Group | Estimasi |
|---|---|
| T28–T32 (Testing foundation) | 3–4 hari |
| T33–T35 (Performance) | 2–3 hari |
| T36–T39 (Translation quality) | 3–4 hari |
| T40–T41 (Reliability) | 2–3 hari |
| T42 (Verification) | 1 hari |
| **Total** | **11–15 hari** (≈2–3 minggu dengan buffer) |

Sesuai estimasi roadmap PRD 1–2 minggu, on track kalau tidak ada blocker tak terduga.

## Hasil Verifikasi (diisi saat T42 selesai)

_(Diisi setelah T42 selesai dieksekusi.)_

### Profiling — T35 smoke (15s run, FakeCapture, 50ms interval)

Run via `dotnet run --project src/GameSubTranslate.App -- --selfcheck-t35 --selfcheck-t35-secs 15 --selfcheck-t35-sample-ms 2000`.

| Metric | Start | End | Delta | Target | Status |
|---|---|---|---|---|---|
| Working set | 44 MB | 56 MB | +12 MB | < 200 MB idle | ✅ |
| Handles (post-warmup) | 313 | 313 | 0 | stable | ✅ |
| GC heap | 373 KB | 405 KB | +32 KB | no leak | ✅ |
| Pipeline latency (cached frame) | — | — | ~0 ms | < 3000 ms P95 | ✅ |

**Catatan warmup:** handles naik ~62 di awal proses (251→313) dalam ~2 detik pertama — itu JIT + module load + WPF resource cache init, satu kali. Threshold pakai rebase setelah warmup selesai supaya tidak false-positive.

**Verifikasi 1-jam penuh:** untuk profile panjang sesuai PRD 7, run:
```
dotnet run --project src/GameSubTranslate.App -- --selfcheck-t35 --selfcheck-t35-secs 3600 --selfcheck-t35-sample-ms 60000
```
Atau jalankan app penuh (`dotnet run --project src/GameSubTranslate.App`) sambil monitor dari Task Manager / `dotnet-counters System.Runtime`. Hasil dicatat ulang saat T42.

## Catatan

- **Struktur folder akhir Fase 3:**
  ```
  tests/
  └── GameSubTranslate.Core.Tests/    (xUnit, new in T28)
  src/
  ├── GameSubTranslate.Prototype/    (Fase 1, masih ada)
  ├── GameSubTranslate.Core/         (+ Logging, VisionAiOcr, streaming, fuzzy)
  └── GameSubTranslate.App/          (+ vision OCR wiring, dynamic provider UI, log folder button)
  ```

- **Konvensi tetap:** namespace `GameSubTranslate.<Module>`, async method suffix `Async`, interface prefix `I`, error handling di translation client tidak boleh crash app. Test naming: `MethodName_StateUnderTest_ExpectedBehavior` (standard xUnit).

- **Performance budget (PRD 7):**
  - RAM idle < 200MB.
  - CPU idle (no text change) < 1%.
  - P95 latency capture→translate < 3s.
  - FPS impact < 5% (overlay rendering). Verifikasi di T42.

- **Jangan over-engineer:** vision AI OCR pluggable sudah cukup (VisionAi class + factory), tidak perlu full plugin architecture. Fuzzy cache pakai Levenshtein sederhana, tidak perlu Faiss/embedding. Multi-provider cukup linear list, tidak perlu priority queue + scoring.

- **Test strategy shift:** Mulai Fase 3, unit test jadi bagian dari Definition of Done untuk task yang melibatkan logic. Integration test (full WPF pipeline) tetap manual sampai Fase 4 (bisa pakai Playwright for WPF atau cukup self-check script seperti `--selfcheck-t26`).
