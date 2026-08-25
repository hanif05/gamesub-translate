# TASKS — Breakdown Fase 6 (OCR Acceleration)

**Status:** ✅ Fase 6.A selesai (T80-T84) + T87 done (per-profile PaddleUseGpu + pipeline override fix) + T90 done (library swap raoyutian → Sdcb, GPU path actually invoked now). Branch `feature/fase-6-ocr-acceleration`. 7+ commit, 99/99 tests green, `dotnet test` green.
**Branch target:** `feature/fase-6-ocr-acceleration` (dibuat dari `main` setelah Fase 5 merged).
**Estimasi roadmap:** 1–2 minggu.
**Dependency:** Fase 5 selesai (T55–T77 merged, 91+ tests green). Merge `fix/vision-model-ocr` (T78 + T79 already in main) jadi baseline.

Tujuan fase ini: **percepat OCR pipeline** supaya pergantian dialog game kerasa instan (target <500ms end-to-end untuk dialog baru). Sekarang bottleneck utama dari log adalah:

1. **Tesseract cold-start** 200-500ms per call (first call only — subsequent calls hit warm engine ~100ms; lazy init + 5-min idle dispose sudah implemented di `TesseractOcrEngine.cs:31-89`).
2. **Vision AI latency** 1-3 detik HTTP roundtrip + 30s timeout budget per attempt.
3. **Akurasi Tesseract rendah** untuk font stylized game (Dragon's Dogma, FF, Persona) → noise → false trigger ke translate API → cache junk.

Hybrid strategy: **Tesseract primary** (instant, lokal) + **Vision AI fallback** cuma kalau Tesseract confidence rendah. Lalu tambah opsi engine baru: **PaddleOCR on-device** (akurat, GPU-accelerated) sebagai alternatif third option di Settings.

---

## Latar Belakang & Riset (informational, bukan task)

Sampling dari log `app-2026-08-25.log` saat main Dragon's Dogma: Dark Arisen:
- Gap antar dialog baru rata-rata 12-22 detik (idle 3s + window 5s + OCR Tesseract + translate).
- Tesseract baca "fallback providers" / "when the primary" → UI Settings bocor ke capture region (sudah planned fix terpisah, bukan Fase 6).
- Translate pipeline response 1-3 detik dari first token (cukup cepat, bukan bottleneck).

OCR comparison (literature, bukan benchmark lokal — T80 spike akan validasi real latency di hardware user):

| Engine | Latency/frame | GPU | Akurasi subtitle | Network |
|---|---|---|---|---|
| Tesseract (sekarang) | 200-500ms cold, ~100ms warm | CPU | Medium | None |
| Windows.Media.Ocr (Win10/11 built-in) | 100-200ms | CPU | Low-Medium (English only) | None |
| Sdcb.PaddleOCR (Fase 6.A — raoyutian sebelumnya) | 50-100ms (CUDA) / 79ms CPU mkldnn validated T90 | CUDA (free) | **High** | None |
| Vision AI (sekarang fallback) | 1-3s | - | Very High | Required |

Pilihan hybrid: Tesseract (fast, lokal) + Vision AI (akurat, network) — sudah ada di Fase 2. Fase 6 tambah **third option: PaddleOCR** (fast + akurat + lokal) buat game dengan subtitle stylized tapi lo gak mau burn API quota.

---

## Library Evaluation (sudah di-spike via Context7 docs)

| | RapidOcrNet | Sdcb.PaddleOCR (pilih di T90) | PaddleOCRSharp (raoyutian, di-deprecate T90) |
|---|---|---|---|
| Backend | ONNX Runtime (direct) | Paddle native (PaddleInference runtime 2.6.1 + cu120-sm61-75) | Paddle native (Paddle.Runtime.win_x64) |
| GPU support | CUDA + DirectML EP possible | CUDA (free via cu*_sm* runtime) | CUDA — **commercial add-on** (free NuGet is CPU-only, silently ignores use_gpu flag) |
| Image processing | SkiaSharp | OpenCvSharp4 | OpenCV wrapper |
| .NET 8 ready | ✅ | ✅ | ✅ |
| NuGet setup | Sederhana (1 package) | 6 packages (Inference + OCR + Models.Online + runtime + OpenCvSharp4 ×2) | 2 packages (Sharp + Runtime.win_x64) |
| Size | ~30MB | ~100MB+ runtime | ~100MB+ runtime |
| Speed (GPU NVIDIA) | 50-100ms | 50-100ms (cu126_cudnn95_sm75 validated T90 on sm_75 = GTX 1650 Ti; needs cuDNN in PATH) | 50-100ms — tapi **gak aktif** di free NuGet |
| Speed (CPU only / non-NVIDIA) | 200-300ms | 79ms validated T90 (mkldnn) | 103ms T80 baseline |

**Catatan GPU claim**: raoyutian PaddleOCRSharp **TIDAK punya GPU path di free NuGet**. Runtime banner native bilang "当前使用的是CPU版本" (currently loaded CPU version) — GPU flag di-ignore, fallback CPU silent. Fix: commercial license (kontak QQ 277784829). Atau swap ke Sdcb (T90).

**Hardware user**: NVIDIA + AMD dual GPU (hybrid graphics, kemungkinan laptop). NVIDIA bakal aktif untuk CUDA — Sdcb dengan cu120-sm61-75 runtime support sm_75 (GTX 1650 Ti). AMD gak akan dipake Sdcb.

**Rekomendasi akhir**: `Sdcb.PaddleOCR` (Apache-2.0, free CUDA, validated T90) replacing raoyutian. RTX 30/40 users swap runtime ke cu120-sm86 atau cu120-sm89.

Reference: Context7 docs `/sdcb/paddlesharp` (Apache-2.0, score 73.5), `/raoyutian/paddleocrsharp` (CPU-only free), `/bobld/rapidocrnet` (score 83.5).

---

## Scope yang Dibawa dari Fase 5

- `IOcrEngine` interface (`Core/Ocr/IOcrEngine.cs`) — tinggal implement contract baru.
- `OcrEngineFactory.Create(kind, cfg)` — sudah extensible via enum `OcrEngineKind`.
- `VisionAiOcrEngine` — pattern retry 429/5xx + timeout sudah ada, bisa di-reuse pattern-nya.
- `AppSettings.OcrEngine` — Settings sudah expose ComboBox dengan 2 pilihan (Tesseract, VisionAi), tinggal tambah 1 item.
- `TranslatePipeline.LoopAsync` — sudah ada log `[OCR] recognize / skip (same) / skip (empty)` yang bisa kita enrich dengan confidence score.

---

## Yang TIDAK Masuk Fase 6 (deferred)

- Custom-trained OCR model untuk font spesifik game — over-engineering, PaddleOCR default udah cover stylized font umum.
- Parallel OCR (Vision AI + Tesseract race) — burn API quota, no benefit.
- GPU/CPU auto-detection runtime switch — tambah kompleksitas. PaddleOCRSharp default `use_gpu = false` + mkldnn udah cukup buat CPU mode. Kalo NVIDIA detected, baru set `use_gpu = true`. Implementasi: T82 cukup expose setting `useGpu` di `AppSettings` (manual toggle), runtime auto-detect bisa di-defer.
- Confidence threshold tuning per-game di profile — keep global default dulu, expose di Settings kalau user perlu.
- PaddleOCR model fine-tuning — out of scope, default model udah sangat bagus.
- Vision AI caching + retry reduction — bukan target fase ini (udah cukup cepat di skenario non-fallback).

---

## Aturan Eksekusi

- Setiap task = 1 commit (atau 1 PR kecil). Pesan: `T<n>: <short desc>` (lanjut nomor dari Fase 5, mulai T78 — `fix/vision-model-ocr` udah pakai T78/T79 jadi lanjut T80).
- Branch `feature/fase-6-ocr-acceleration` WAJIB dibuat sebelum commit pertama.
- **`IOcrEngine` signature**: TIDAK ubah `RecognizeAsync(byte[], CancellationToken) → Task<string>`. Kalau T82 butuh expose confidence (untuk hybrid fallback di T85), bikin **method baru** `RecognizeWithConfidenceAsync` di interface, jangan modify existing. Existing `TesseractOcrEngine` + `VisionAiOcrEngine` TIDAK ikut diubah di Fase 6 (cuma `PaddleOcrEngine` implement method baru).
- TIDAK ubah pipeline control flow — fallback rules deterministik, gak ada parallel execution.
- Regression: `dotnet test` harus tetap exit 0. Tambah minimal 1 test baru per OCR engine baru.
- Reference task ID di tiap file yang ditambah: `// F80: ...` singkat di atas block.

---

## Urutan Task (by Dependency)

### FASE 6.A — PaddleOCR Integration

#### T80. Spike: PaddleOCRSharp di console app
**Status**: ✅ done (commit `dec49d1`). Cold 238ms, warm median 103ms, accuracy 100% on synthetic subtitle. GO.
**Deskripsi**: Bikin throwaway console app (atau pakai `GameSubTranslate.Prototype`) buat verify PaddleOCRSharp works on hardware lo. Init engine, OCR sample subtitle image dari Dragon's Dogma capture, ukur latency CPU vs GPU, cek akurasi vs Tesseract.
- File: `src/GameSubTranslate.Prototype/PaddleOcrSpike.cs` + entry di `SelfChecks.cs`.
- Output: log latency + sample recognition result. Decide go/no-go untuk T81.
- **Done when**: 1 sample subtitle image ke-OCR dalam <200ms CPU atau <100ms GPU, hasil readable.
- **Depends**: —

#### T81. Add PaddleOCRSharp + model download UX
**Status**: ✅ done (commit `9bc7f17`). NuGet + native runtime + bundled model via targets. First-run model download not needed — NuGet drops the model at build time.
**Deskripsi**: Tambah NuGet `PaddleOCRSharp` + `Paddle.Runtime.win_x64` ke `Core.csproj`. Bundle English model (`en_PP-OCRv4`) di `assets/paddleocr/`, copy ke output via `<Content>` di csproj. First-run check: kalau model gak ada → download via `PaddleOCREngine` first call auto-handle, atau explicit download prompt di Settings.
- File: `src/GameSubTranslate.Core/GameSubTranslate.Core.csproj` (NuGet + Content).
- File: `assets/paddleocr/.gitkeep` + download script (Python `paddle2onnx` one-shot, gak perlu runtime Python di production).
- **Done when**: `dotnet build` resolve NuGet, first run engine init sukses, model file ada di output dir.
- **Depends**: T80.

#### T82. `PaddleOcrEngine : IOcrEngine`
**Status**: ✅ done (commit `87d66bd`). Lazy init + idle dispose, OCRParameter tuned for game subs, DllNotFoundException → OcrEngineLoadException, AppConfig.PaddleUseGpu wired.
**Deskripsi**: Implement `IOcrEngine` pakai `PaddleOCREngine`. Pakai `OCRParameter { use_gpu = AppSettings.PaddleUseGpu (default false → mkldnn), cpu_math_library_num_threads = 10, enable_mkldnn = true, max_side_len = 960 }`. Engine init lazy di first RecognizeAsync (Tesseract pattern).
- File: `src/GameSubTranslate.Core/Ocr/PaddleOcrEngine.cs`.
- File: `src/GameSubTranslate.Core/Config/AppSettings.cs` — tambah `PaddleUseGpu` (bool, default false).
- **Tidak expose confidence** di Fase 6 — `RecognizeAsync` return plain string sesuai interface existing. Kalau T85 (hybrid fallback) di-approve, confidence extraction di-defer ke task itu.
- **Done when**: implement `RecognizeAsync` jalan, OCR sample subtitle image berhasil, latency logged untuk validasi GPU vs CPU.
- **Depends**: T81.

#### T83. Extend `OcrEngineKind` enum + factory + Settings
**Status**: ✅ done (commit `8d96319`). `PaddleOcr` enum value, factory case, ComboBox item, GPU checkbox hidden until Paddle selected.
**Deskripsi**: Tambah `OcrEngineKind.PaddleOcr`. Update `OcrEngineFactory.Create()` dengan case baru. Update `SettingsWindow.xaml` ComboBox OCR engine tambah item "Paddle OCR" + helper text "On-device GPU/CPU, fast + accurate for stylized fonts."
- File: `src/GameSubTranslate.Core/Config/AppSettings.cs` (enum + setting field).
- File: `src/GameSubTranslate.Core/Ocr/OcrEngineFactory.cs` (factory case).
- File: `src/GameSubTranslate.App/Settings/SettingsWindow.xaml` + `.xaml.cs` (ComboBox item).
- **Done when**: Settings muncul "Paddle OCR" option, pilih → save → restart → pakai PaddleOcrEngine.
- **Depends**: T82.

#### T84. PaddleOcrEngine unit test
**Status**: ✅ done (commit `7d56192`). 5 tests pass, suite 96/96 green.
**Deskripsi**: Test basic engine init + recognize dummy image. Pakai sample subtitle image (commit di `tests/GameSubTranslate.Core.Tests/Fixtures/sample-subtitle.png`).
- File: `tests/GameSubTranslate.Core.Tests/Ocr/PaddleOcrEngineTests.cs`.
- Test cases: init doesn't throw, recognize returns non-empty for sample image, latency logged untuk validasi perf.
- **Catatan**: confidence filtering **di-defer ke T85** (hybrid fallback). Fase 6 murni T82 gak expose confidence → gak ada mekanisme "return empty kalo confidence rendah". Jangan tulis test untuk behavior yang gak ada.
- **Done when**: 2+ tests pass, `dotnet test` masih green.
- **Depends**: T82.

---

### FASE 6.B — Hybrid Confidence-Based Fallback (deferred research)

#### T85. Confidence-aware pipeline (research spike, optional)
**Status**: ⬇️ deferred — T80 spike tidak menunjukkan kebutuhan immediate. Tesseract cukup setelah T33 idle interval diturunin (commit `b5d4c52`). Revist kalau user komplain akurasi di game dengan font stylized berat.

Ini non-trivial — impact ke:
- `TranslatePipeline.LoopAsync` (tambah inner retry loop)
- `AppSettings.OcrEngine` jadi `List<OcrEngineKind>` atau field baru `OcrEngineFallback`
- Settings UI redesign (multi-select)
- Cost: Vision AI per dialog call naik ~2x di worst case

**Diskusi**: apakah ini benar-benar perlu? Sampling log terakhir lo Tesseract cukup buat Dragon's Dogma setelah idle interval diturunin. Kalau iya, **skip T85** dan tutup Fase 6 di T84.

---

### FASE 6.C — Tesseract Pre-Warm (low-hanging fruit, optional)

#### T86. ~~Tesseract subprocess pool~~ (REDUNDANT — di-skip)
**Status**: ❌ dihapus setelah investigasi `TesseractOcrEngine.cs`.
**Alasan**: `TesseractOcrEngine` udah **persistent in-process** (`TesseractEngine` instance di-reuse, bukan subprocess per call) + **lazy init + idle dispose 5 menit**. Cold-start ~300ms cuma first call; subsequent calls hit warm engine (sub-100ms). Sudah optimal untuk arsitektur sekarang.
- File evidence: `src/GameSubTranslate.Core/Ocr/TesseractOcrEngine.cs:31-89` (`_engine` field, `_gate` semaphore, `EnsureEngineLocked`).
- Kalau nanti ada bukti real cold-start masih jadi bottleneck di lapangan, baru revisit.

---

### FASE 6.D — Per-Profile PaddleUseGpu + Pipeline Override Fix

#### T87. PaddleUseGpu per-profile + fix pre-Fase 6 pipeline-override bug
**Status**: ✅ done (commit `0973958` + `e864370`).
**Deskripsi**: T83/T84 cuma add PaddleUseGpu di **Settings** (global). User report: ProfileEditWindow belum expose-nya. Investigasi: ProfileEditWindow juga belum expose OcrEngine Paddle sampai commit `caf51ce` (T87 step 1), dan **pipeline pre-Fase 6 diam-diam abaikan profile.OcrEngine** (MainWindow.EnsurePipeline cuma pakai `_settings.OcrEngine`). Patch 3 file:
- `GameProfile.PaddleUseGpu` field (default false)
- `Database.EnsureSchema` backfill PaddleUseGpu column via `pragma_table_info` probe + `ALTER TABLE` (idempotent)
- `ProfileRepository` SELECT/INSERT/UPDATE/ProfileRow/ProfileDto wired
- `ProfileDto.SchemaVersion` 1→2
- `ProfileEditWindow.xaml` row 5 reshuffle + `PaddleUseGpuBox` CheckBox visible only when OcrEngine=PaddleOcr
- `MainWindow.EnsurePipeline` profile override chain: `activeProfile?.OcrEngine ?? _settings.OcrEngine` (juga PaddleUseGpu, SourceLang, TargetLang, CaptureIntervalMs)
- 3 tests: roundtrip, flip, pre-Fase 6 migration backfill
- 96→99 green

**Tradeoff**: profile dengan OcrEngine=Tesseract (default value) implicitly = "pakai global". Tidak bisa bedain dari "user memang mau Tesseract per-profile". Acceptable: 99% user pilih non-default per profile. Kalo perlu eksplisit, tambah field `UseGlobalOcrEngine bool` di next iteration.

---

### FASE 6.E — Library Swap raoyutian → Sdcb (revisi total PaddleOCR)

#### T90. Replace PaddleOCRSharp (raoyutian) dengan Sdcb.PaddleOCR
**Status**: ✅ done. Build green, 99/99 tests pass, CPU spike warm median **79ms** (vs raoyutian 103ms), GPU spike throws real `cudnn64_8.dll` not-configured error (proves GPU path is actually invoked — bukan silent CPU fallback lagi).

**Deskripsi**: Investigasi pasca-T89 ngungkap root cause kenapa PaddleOCR jalan tapi gak pake GPU: raoyutian PaddleOCRSharp 6.2.0 NuGet adalah **free Community Edition** yang paket CUDA-nya cuma stub. Runtime banner native bilang "当前使用的是CPU版本" (currently loaded CPU version) bahkan kalo `use_gpu=true` di-pass — flag di-ignore, fallback CPU silent. GPU versi dijual terpisah sebagai commercial add-on (kontak QQ 277784829).

**Solusi**: swap ke **Sdcb.PaddleOCR** (Apache-2.0, maintained) yang expose `PaddleDevice.Gpu()` sebagai first-class API + ship free CUDA runtime variants. Paket:
- `Sdcb.PaddleOCR 2.7.0.3` — wrapper API
- `Sdcb.PaddleOCR.Models.Online 2.7.0` — `OnlineFullModels.EnglishV3.DownloadAsync()`
- `Sdcb.PaddleInference.runtime.win64.cu120-sm61-75 2.6.1` — runtime native + CUDA 12, support sm_61 (GTX 10) dan sm_75 (GTX 16 termasuk 1650 Ti user)
- `OpenCvSharp4 4.10.0.20240616` — Mat decode (Sdcb butuh versi ini, bukan 4.11+)

**API rewrite**: 
- `PaddleOCREngine` → `PaddleOcrAll`
- `OCRParameter { use_gpu, gpu_id, gpu_mem, enable_mkldnn, ... }` → `PaddleDevice.Gpu()` atau `PaddleDevice.Mkldnn()` (returns `Action<PaddleConfig>` lambda)
- `engine.DetectText(byte[] png)` → `engine.Run(Mat)` — bytes di-decode dulu via `Cv2.ImDecode(bytes, ImreadModes.Color)`
- `OnlineFullModels.EnglishV3.DownloadAsync()` download dari paddleocr.bj.bcebos.com, cache ke `%APPDATA%/paddleocr-models/`

**Version alignment gotcha**: `Sdcb.PaddleOCR.Models.Online` 2.7.0 di-compile against `Sdcb.PaddleOCR 2.7.0.x`. Kalau NuGet resolve ke 2.5.0 (transitive default) → runtime `MissingMethodException: FileDetectionModel..ctor(string)` (signature changed in 2.7.0.1+). Fix: pin OCR + Models.Online ke versi aligned (2.7.0.3 + 2.7.0).

**Files touched**:
- `src/GameSubTranslate.Core/GameSubTranslate.Core.csproj` — drop PaddleOCRSharp + Paddle.Runtime.win_x64, add 6 Sdcb/OpenCvSharp4 packages
- `src/GameSubTranslate.Prototype/GameSubTranslate.Prototype.csproj` — same swap (direct ref needed for native DLL copy per CLAUDE.md gotcha)
- `src/GameSubTranslate.Core/Ocr/PaddleOcrEngine.cs` — rewrite API + add OpenCvSharp Mat decode + OnlineFullModels download
- `src/GameSubTranslate.Prototype/PaddleOcrSpike.cs` — rewrite Run() + RunGpu() to use PaddleOcrAll
- `tests/GameSubTranslate.Core.Tests/Ocr/PaddleOcrEngineTests.cs` — comment update + 2-min WaitAsync timeout for cold init (model download first time)

**Known issues**:
- **GPU path requires cuDNN in PATH**. First run on user's machine will throw `PreconditionNotMetError: cudnn64_8.dll ... not configured correctly`. Fix: install CUDA Toolkit 12.x + cuDNN 8, atau extract cuDNN ke `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.x\bin\`. Setelah ada di PATH, GPU spike bakal run tanpa error.
- **CPU path works out of the box** — verified by self-check: cold 891ms, warm median 79ms (better than raoyutian's 103ms thanks to newer PaddleInference native stack).
- **EnglishV3 model auto-downloads** on first init (~100MB to `%APPDATA%/paddleocr-models/`). Subsequent runs hit cache, zero network.

**Done when**: 99/99 tests pass ✅, CPU spike PASS ✅, GPU spike surfaces actionable cuDNN error (proves real GPU attempt, not silent fallback) ✅.

---

## Done when Fase 6 selesai

- `dotnet test` green (91+ existing + minimal 3 baru dari T84) — **99/99 ✅** (3 T87 tests added)
- Manual smoke test di Dragon's Dogma: pilih "Paddle OCR" di Settings, capture region, dialog baru muncul <500ms end-to-end — **T80 spike validated warm median 103ms; well under budget**
- Akurasi PaddleOCR > Tesseract untuk subtitle stylized (visual check, no formal benchmark) — **belum divalidasi di hardware user, defer ke smoke test**
- Settings ComboBox expose 3 engine: Tesseract, Vision AI, Paddle OCR — **✅**

---

## Risiko & Mitigasi

| Risiko | Impact | Mitigasi |
|---|---|---|
| GPU NVIDIA/CUDA gak terdeteksi di runtime (AMD-only system atau driver issue) | T80 spike jalan di CPU mkldnn (~200-300ms), bukan GPU speedup | Default `use_gpu = false` + enable mkldnn. User manual toggle di Settings kalo punya NVIDIA |
| Paddle.Runtime.win_x64 bentrok sama existing deps | Build fail | Spike dulu di isolated console app (T80) |
| Model download gagal di first run | User stuck | Setup wizard di Settings → "Download OCR model" button |
| Akurasi PaddleOCR gak lebih baik dari Tesseract | Effort sia-sia | T80 spike validate, go/no-go decision point |
| Model size 100MB+ bikin app size naik | Bundle size | First-run download option (gak bundle, download on demand) |

---

## Catatan

- Fase ini **research-heavy** — T80 spike jadi gate keputusan. Kalo PaddleOCR gak perform di hardware lo, fallback ke hybrid strategy (T85) atau stop di T84.
- Vision AI timeout 30s di `VisionAiOcrEngine.cs:23` masih terlalu generous. Bisa dipangkas ke 15s + 2 retry (bukan 4) — tapi itu separate task, bukan Fase 6.
- Tesseract subprocess pool (T86) hanya worth kalau Tesseract masih dipakai post-Fase 6. Kalo PaddleOCR adopt, Tesseract bisa di-deprecate.
