# CLAUDE.md — GameSubTranslate

Dokumen ini adalah aturan main untuk Claude Code setiap kali kerja di project ini. Baca file ini di awal setiap sesi sebelum mulai coding.

## Project Overview

Personal tool untuk auto-translate subtitle game PC (fokus RPG/story-heavy game, termasuk game modern AAA seperti The Last of Us). User bisa menentukan custom capture area (karena posisi subtitle beda-beda tiap game), lalu sistem otomatis OCR + translate teks tersebut dan tampilkan sebagai overlay di atas game.

Dibangun untuk kebutuhan personal (bukan produk komersial). Lihat `PRD-Auto-Translate-Subtitle-Game.md` untuk requirement lengkap.

## Tech Stack (Keputusan Final — Windows-only)

- **Bahasa & UI:** C# (.NET 8), WPF
- **Screen Capture:** `Windows.Graphics.Capture` API (Fase 2+). Fase 1 prototype pakai `System.Drawing` (GDI+) sementara.
- **OCR Lokal:** Tesseract (NuGet package `Tesseract` oleh Charlesw)
- **OCR Alternatif:** Vision AI via endpoint OpenAI-compatible (fallback untuk font sulit, next phase)
- **Translation:** `HttpClient` manual ke endpoint OpenAI-compatible (`/chat/completions`), bukan SDK resmi — supaya fleksibel ganti provider (OpenAI, OpenRouter, dsb) cukup ganti `base_url`.
- **Storage:** SQLite (profile, multi-region config, cache) via EF Core atau Dapper
- **Global Hotkey:** Win32 `RegisterHotKey` API
- **Overlay Click-through:** Win32 API (`WS_EX_LAYERED` + `WS_EX_TRANSPARENT`)
- **Target Framework:** `net8.0-windows10.0.19041.0`

**Platform:** Windows-only. Tidak ada rencana cross-platform (Linux/macOS). Jangan tambahkan abstraction layer untuk platform lain.

## Struktur Folder

```
project-root/
├── CLAUDE.md
├── docs/
│   ├── PRD.md
│   └── tasks/
│       ├── TASKS-fase-1-prototype.md
│       ├── TASKS-fase-2-mvp-overlay.md      (dibuat setelah Fase 1 selesai)
│       ├── TASKS-fase-3-optimisasi.md       (dibuat setelah Fase 2 selesai)
│       └── TASKS-fase-4-polish.md           (dibuat setelah Fase 3 selesai)
├── src/
│   ├── GameSubTranslate.Prototype/          ← Fase 1: console app end-to-end
│   ├── GameSubTranslate.Core/               ← shared logic (Capture, Ocr, Translation, Pipeline)
│   └── GameSubTranslate.App/                ← Fase 2+: WPF app dengan overlay
├── assets/
│   └── tessdata/                            ← file .traineddata untuk Tesseract
└── GameSubTranslate.sln
```

Catatan: struktur `Core` project baru dibentuk resmi pas Fase 2 (migrasi dari Prototype). Selama Fase 1, semua kode boleh tetap di `GameSubTranslate.Prototype` — jangan over-engineer folder terlalu awal.

## Convention

- **Commit message:** `T<n>: <short desc>` sesuai nomor task di `TASKS-fase-X-*.md` (contoh: `T3: implement ScreenCapture module`).
- **Namespace:** `GameSubTranslate.<Module>` (contoh: `GameSubTranslate.Ocr`, `GameSubTranslate.Translation`).
- **Async method:** selalu suffix `Async` (contoh: `TranslateAsync`, `CaptureRegionAsync` jika nanti di-async-kan).
- **Interface:** prefix `I` (contoh: `IOcrEngine`, `ITranslationClient`).
- **Config/secrets:** JANGAN pernah hardcode API key di kode. Fase 1 pakai env var (`OPENAI_API_KEY`, `OPENAI_BASE_URL`, `OPENAI_MODEL`). Fase 2+ pindah ke config file terenkripsi.
- **Error handling:** service yang manggil API eksternal (translation) harus punya try-catch + tidak boleh crash aplikasi kalau API gagal — tampilkan status error, bukan throw ke atas tanpa handling.

## Cara Run

```bash
# Fase 1 (Prototype)
dotnet run --project src/GameSubTranslate.Prototype -- --x 0 --y 0 --w 800 --h 100 --interval 1000
```

```bash
# Build seluruh solution
dotnet build
```

## Cara Test

_(Belum ada unit test formal di Fase 1 — verifikasi masih manual sesuai "Done when" di tiap task. Section ini akan diisi begitu testing strategy mulai dipakai, kemungkinan mulai Fase 2/3.)_

## Environment Variables yang Dibutuhkan (Fase 1)

| Variable | Wajib | Keterangan |
|---|---|---|
| `OPENAI_API_KEY` | Ya (kalau mau test translation) | API key untuk endpoint OpenAI-compatible |
| `OPENAI_BASE_URL` | Ya | Contoh: `https://api.openai.com/v1` |
| `OPENAI_MODEL` | Ya | Contoh: `gpt-4o-mini` |

Kalau env var kosong, `TranslationClient` harus skip pemanggilan API (bukan crash) — lihat T7 di TASKS-fase-1-prototype.md.

## Known Gotchas (update terus seiring project jalan)

- **Target framework windows-specific mengubah output path.** Karena target framework `net8.0-windows10.0.19041.0`, output build ada di `bin/Debug/net8.0-windows10.0.19041.0/`, BUKAN `bin/Debug/net8.0/`. Perhatikan ini saat verifikasi manual atau menulis script yang menyentuh path build.
- **`System.Drawing.Common` tidak built-in otomatis.** Sejak .NET 6+, package ini harus ditambahkan eksplisit ke `.csproj` kalau project bukan WinForms/WPF template — dibutuhkan untuk `ScreenCapture` module di Fase 1.
- **Retry & timeout untuk `TranslationClient` belum diimplementasikan di Fase 1** (PRD section 6.5 minta retry dengan exponential backoff + timeout 10 detik). Fase 1 boleh skip ini untuk kecepatan prototyping, tapi WAJIB ditambahkan sebelum Fase 2/3 dianggap selesai.
- **Windows.Graphics.Capture butuh Windows 10 1903+.** Kalau nanti testing di VM/environment lama, capture bisa gagal — pastikan versi Windows sesuai.
- **Anti-cheat:** screen capture di project ini bersifat read-only via API resmi OS, TIDAK melakukan memory injection/hooking ke proses game. Jangan ubah pendekatan ini walau ada opsi "lebih akurat" yang butuh akses ke memory game — itu di luar scope dan berisiko.
- **`UseWindowsForms=true` bikin type ambigu di WPF.** Global using `System.Windows.Forms` otomatis aktif → `TextBox`, `Button`, `ComboBox`, `Brush`, `MessageBox`, `Brushes`, `Cursors`, `KeyEventArgs` collide dengan WPF. Fix: tambah alias eksplisit di file yang kena (lihat `SettingsWindow.xaml.cs` header). Ini gotcha yang sama sudah ada di `App.xaml.cs` (fully-qualified `System.Windows.*`).

## Yang Harus Dibaca Sebelum Mulai Kerja

1. `PRD-Auto-Translate-Subtitle-Game.md` — requirement lengkap & konteks produk
2. `docs/tasks/TASKS-fase-<N>-*.md` — task aktif yang sedang dikerjakan (baca checkbox untuk tahu progress terakhir)
3. Section "Known Gotchas" di atas — supaya tidak mengulang masalah yang sudah pernah ditemukan

## Git Flow Rules
- Selalu cek branch aktif sebelum mulai bekerja.
- Jangan pernah mengerjakan task langsung di `main` atau `develop`.
- Untuk setiap fitur gunakan branch `feature/<nama-fitur>`.
- Untuk bug gunakan branch `fix/<nama-bug>`.
- Jika branch yang diperlukan belum ada, buat dari branch dasar yang sesuai.
- Jangan merge, squash, rebase, atau push ke branch utama kecuali diminta secara eksplisit oleh user.
- Sebelum commit, pastikan `git status` bersih dari file yang tidak berkaitan dengan task.

## Alur Kerja per Task

1. Kerjakan task sesuai urutan dependency di file TASKS-fase-X yang aktif — jangan loncat.
2. Setelah selesai, verifikasi manual sesuai kriteria "Done when" di task tersebut.
3. Update checkbox task tersebut jadi selesai di file TASKS-fase-X.
4. Commit dengan pesan sesuai convention.
5. Kalau ketemu masalah/keputusan teknis yang berbeda dari rencana, catat di section "Known Gotchas" di atas sebelum lanjut.
