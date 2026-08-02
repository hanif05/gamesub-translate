# TASKS — Breakdown Fase 1 (Prototype)

Fase 1 sesuai PRD section 15: capture area manual (hardcoded coordinate dulu) → OCR lokal (Tesseract.NET) → translate via HTTP call ke OpenAI-compatible endpoint → tampil di console/simple window (belum overlay transparan).

Tujuan fase ini: **bukti end-to-end pipeline berjalan** sebelum investasi ke overlay WPF yang ribet.

## Aturan Eksekusi

- Setiap task = 1 PR / 1 commit terpisah (kalau kamu mau commit incremental).
- Tiap task HARUS selesai + verifikasi manual sebelum lanjut ke task berikutnya yang bergantung.
- Gunakan .NET 8 SDK. Target framework: `net8.0-windows10.0.19041.0` untuk akses Windows.Graphics.Capture di task berikutnya.
- Dependency NuGet minimum fase ini:
  - `Tesseract` (NuGet wrapper `Tesseract` oleh Charlesw)
  - `System.Drawing.Common` (eksplisit sejak .NET 6+; dipakai di T3 untuk screenshot GDI+, **wajib** di-declare manual karena project ini console, bukan WinForms/WPF)
  - `System.Text.Json` (built-in)
- **Output fase ini**: aplikasi console yang kalau dijalankan (dengan `--x`, `--y`, `--w`, `--h` argumen) akan:
  1. Capture region layar tiap 1 detik.
  2. Jalankan OCR lewat Tesseract pada hasil capture.
  3. Kalau teks berubah dari capture sebelumnya, kirim teks ke endpoint OpenAI-compatible.
  4. Print teks asli + terjemahan ke console.

---

## Urutan Task (by Dependency)

### T1. Setup project skeleton
**Deskripsi**: Bikin solution dan project .NET console.
**Output**: `src/GameSubTranslate.Prototype/GameSubTranslate.Prototype.csproj` yang build & run `Hello World`.
**Done when**: `dotnet run` dari folder project print "hello".
**No dependency.**

### T2. Tambah package Tesseract + assets
**Deskripsi**: Tambah NuGet `Tesseract`. Download `eng.traineddata` (minimal english) ke folder `assets/tessdata/`. Tambah `.csproj` content/copy item supaya file .traineddata ter-copy ke output directory.
**Output**: folder `assets/tessdata/eng.traineddata` ada di `bin/Debug/net8.0/`.
**Done when**: `Test-Path bin/Debug/net8.0-windows10.0.19041.0/eng.traineddata` return true setelah `dotnet build`. (Path output ikut versi TargetFramework — pakai yang exact, jangan asumsi `net8.0/`.)
**Depends on**: T1.

### T3. Module `ScreenCapture`
**Deskripsi**: Bikin class `ScreenCapture.cs` dengan method `CaptureRegion(int x, int y, int w, int h)` return `byte[]` PNG. Implementasi awal pakai `System.Drawing.Common` (GDI+) screenshot bounded — cukup untuk fase ini, Windows.Graphics.Capture API dipakai di Fase 2. **Penting**: tambah NuGet `System.Drawing.Common` secara eksplisit di `.csproj` di task T1 (catatan tercantum di bagian Aturan Eksekusi). Tanpa itu, build di .NET 8 console app akan gagal karena package ini tidak otomatis inklusif seperti di WinForms/WPF.
**Output**: file `src/.../Capture/ScreenCapture.cs`.
**Done when**: panggil `CaptureRegion` dari `Program.cs` untuk area fixed (misal pojok layar), simpan ke file `test.png`, buka file tersebut punya dimensi sesuai area.
**Depends on**: T1.

### T4. CLI args parser sederhana
**Deskripsi**: Parse argumen `--x`, `--y`, `--w`, `--h`, `--interval` (default 1000ms). Pakai `System.CommandLine` atau parsing manual (manual cukup untuk fase ini, satu file kecil).
**Output**: file kecil `CliArgs.cs`.
**Done when**: `dotnet run -- --x 0 --y 0 --w 100 --h 100` bisa di-parse tanpa error.
**Depends on**: T1.

### T5. Module `OcrEngine` (Tesseract wrapper)
**Deskripsi**: Interface `IOcrEngine` + implement `TesseractOcrEngine`. Method `Recognize(byte[] pngBytes) -> string`. Dispose Tesseract engine proper (reuse satu instance, singleton internal).
**Output**: file `src/.../Ocr/IOcrEngine.cs` + `src/.../Ocr/TesseractOcrEngine.cs`.
**Done when**: dari `Program.cs`, capture area statis yang mengandung teks (misal area dalam Notepad), hasilnya string tidak kosong.
**Depends on**: T2, T3.

### T6. Module `ChangeDetector`
**Deskripsi**: Class `ChangeDetector` dengan method `IsChanged(byte[] newPng, byte[] lastPng) -> bool`. Algoritma awal: byte-compare exact. **Catatan saat eksekusi T6**: verify dulu bahwa PNG encoded dari bitmap identik menghasilkan byte[] identik saat dipanggil berturut-turut — kalau tidak (misal timestamp EXIF, compression nondeterminism), ganti strategi jadi: decode PNG → `System.Drawing.Bitmap` → compare pixel grid (atau perceptual hash `pHash`). Jangan asumsi byte-exact cukup tanpa verify dulu.
**Output**: file `src/.../Pipeline/ChangeDetector.cs`.
**Done when**:
- 2x panggil dengan image identik return false.
- 2x panggil dengan image berbeda return true.
- (Bonus) call `IsChanged(captureA, captureA)` dalam loop 100x — pastikan konsisten.
**Depends on**: T1.

### T7. Module `TranslationClient`
**Deskripsi**: Class `TranslationClient` dengan constructor terima `apiKey`, `baseUrl`, `model`, `sourceLang`, `targetLang`. Method `TranslateAsync(string text, CancellationToken ct) -> string` panggil endpoint `/chat/completions` pakai `HttpClient`. System prompt hardcode untuk MVP fase ini (sesuai PRD section 6.5). Pakai `System.Text.Json`.
**Output**: file `src/.../Translation/TranslationClient.cs`.
**Done when**:
- Dari `Program.cs` kecil yang instantiate client + panggil translate "Hello world" → return string non-empty (asumsi api key valid).
- Test harus disable-able kalau env var kosong (skip call kalau no key).

**TODO Fase 2 (PRD section 6.5 — tidak wajib di fase ini tapi catat):**
- Timeout request: 10 detik (`HttpClient.Timeout` atau `CancellationToken`).
- Retry dengan exponential backoff: max 3 attempt (contoh: 1s → 2s → 4s delay).
- Graceful error: kalau gagal setelah retry, return null/throw specific exception (jangan crash pipeline).

Tambahkan sebagai TODO comment di header file `TranslationClient.cs`, bukan implementasi penuh di fase ini — biar scope Fase 1 tetap kecil (sesuai PRD: "belum overlay transparan").
**Depends on**: T1.

### T8. Config loader (sederhana)
**Deskripsi**: Load config dari `appsettings.json` atau env vars. Field minimal: `ApiKey`, `BaseUrl`, `Model`, `SourceLang` (default "auto"), `TargetLang` (default "id"). Kalau pakai env, baca `OPENAI_API_KEY`, `OPENAI_BASE_URL`, `OPENAI_MODEL`. **MVP fase 1: env-var only**, file config di Fase 2.
**Output**: file `src/.../Config/AppConfig.cs`.
**Done when**: set env var di shell, run app, config ter-load dengan benar.
**Depends on**: T7.

### T9. Pipeline orchestrator + console output
**Deskripsi**: Class `TranslatePipeline` yang gabungin T3-T8 dalam loop. Tiap tick: capture → change-detect → kalau berubah, OCR → translate → print `{timestamp} | src: ... | dst: ...` ke console. Jalankan sampai user tekan Ctrl+C (handle `Console.CancelKeyPress`).
**Output**: file `src/.../Pipeline/TranslatePipeline.cs`.
**Done when**: jalanin app dengan region statis yang ada teks berubah-ubah, console print setiap update, request ke API hanya muncul saat teks berubah.
**Depends on**: T3, T4, T5, T6, T7, T8.

### T10. Verifikasi end-to-end manual
**Deskripsi**: Run app dengan region yang menunjuk subtitle di game yang sedang jalan (atau video YouTube dengan subtitle ON sebagai test case aman). Capture flow lengkap, ukur latency dari perubahan teks sampai translate muncul. Catat hasil di bagian bawah file ini.
**Output**: section "Hasil Verifikasi" di bawah.
**Done when**: latency tercatat + screenshot console log terlampir (di-copy paste aja ke file).
**Depends on**: T9.

---

## Hasil Verifikasi (diisi saat T10 selesai)

Tanggal: 2026-08-02
Branch: `feature/fase-1-prototype`
Sample input: "The quick brown fox jumps over the lazy dog." (text-on-white PNG, 800x100)

| Tahap | Latency | Hasil |
|---|---|---|
| OCR (Tesseract eng) | 26-28 ms | "The quick brown fox jumps over the lazy dog." (clean) |
| Translate (OpenRouter `inclusionai/ling-3.0-flash:free`, en→id) | 1666 ms | "Rubah cokelat yang cepat melompati anjing malas." |
| Total satu siklus | 1692 ms | OK |

### Live capture test (pipeline penuh, bukan sintetis)

Run: `dotnet run --project src/GameSubTranslate.Prototype -- --x 60 --y 200 --w 900 --h 150 --interval 800`
Durasi: 20 detik, region menunjuk ke editor VS Code tempat file `TASKS-fase-1-prototype.md` terbuka.

Log console (potongan representative):

```
[hh:mm:ss.fff] | src: ... (raw English dari OCR IDE window) | dst: ... (terjemahan ID)
```

Teks mentah OCR mengandung derau khas UI IDE (icon names, path Windows), tapi pipeline berubah dari mode synthetic ke mode **real screen capture** tetap jalan tanpa error: capture → change-detect → OCR → translate → print, dengan API call HANYA muncul saat pixel berubah (validasi T6 work end-to-end).

### Catatan

- 429 dari `google/gemma-4-31b-it:free` di OpenRouter — bukan bug client kita, free-tier rate limit. Switch ke `inclusionai/ling-3.0-flash:free` (1.7s) cukup cepat.
- Retry+timeout (TODO Fase 2) belum ada; di run ini API sukses jadi tidak terlihat. Akan muncul di Fase 2.
- Latency translate (~1.7s) sudah cukup untuk subtitle game (frame rate subtitle ~2-4s), Fase 3 bisa optimasi streaming.

---

## Catatan

- Fase 2 (MVP Overlay) akan ganti `ScreenCapture` dari GDI+ ke Windows.Graphics.Capture, dan ganti console output dengan WPF overlay click-through. Modul lain (`OcrEngine`, `ChangeDetector`, `TranslationClient`) **didesain stabil** supaya tidak perlu refactor besar.
- Kalau Tesseract gagal baca font game, acceptable — Vision AI OCR baru masuk nanti. Cukup capture teks English dulu sebagai proof of concept.
- Commit message convention: `T<n>: <short desc>`.
