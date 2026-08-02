# PRD: Auto-Translate Subtitle untuk Game PC

**Nama Produk (sementara):** GameSubTranslate
**Versi Dokumen:** 1.3
**Tanggal:** 2 Agustus 2026
**Author:** Hanif
**Status:** Draft — siap untuk breakdown teknis & eksekusi
**Tujuan Proyek:** Personal tool untuk kebutuhan sendiri (main game RPG/story-heavy tanpa subtitle bahasa yang dipahami). Monetisasi bukan prioritas, hanya dicatat sebagai opsi masa depan kalau suatu saat relevan.

---

## 1. Latar Belakang & Problem Statement

Banyak game PC (terutama game Jepang, Korea, Eropa, atau indie) tidak punya subtitle Bahasa Indonesia atau bahkan Bahasa Inggris. Pemain yang ingin memahami cerita/dialog harus:

- Manual translate pakai Google Lens / screenshot ke Google Translate (ribet, keluar dari game / alt-tab).
- Bergantung pada fan-translation yang jarang up to date.
- Melewatkan konteks cerita karena tidak paham bahasa aslinya.

Setiap game punya **posisi subtitle yang berbeda-beda** (bawah tengah, bawah kiri, dialog box custom, dsb), sehingga solusi generic overlay OCR (seperti fitur built-in beberapa OS) sering gagal capture area yang tepat atau ikut men-capture elemen UI lain yang tidak relevan.

**Problem inti:** Tidak ada tool ringan, cepat, dan fleksibel yang bisa:
1. Membiarkan user **menentukan sendiri area capture** subtitle per game (karena tiap game beda posisi).
2. Melakukan **OCR** teks dari area tersebut secara real-time/near real-time.
3. **Translate otomatis** hasil OCR menggunakan AI (LLM) dengan API key & base URL yang bisa dikonfigurasi sendiri (OpenAI-compatible, sehingga bisa pakai OpenAI, Azure OpenAI, atau provider compatible lain seperti OpenRouter, DeepSeek, dsb).
4. Menampilkan hasil translate sebagai **overlay** di atas game tanpa mengganggu gameplay.

---

## 2. Goals & Objectives

### 2.1 Goals
- User bisa menonton dialog/subtitle game dalam bahasa apapun dan langsung mendapat terjemahan real-time di layar, tanpa alt-tab.
- Setup per-game cepat (< 1 menit): drag area capture, simpan sebagai profile game.
- Latency translate cukup rendah agar tidak terasa lag terlalu jauh dari dialog aslinya (target awal: < 3 detik dari perubahan teks di layar sampai muncul hasil translate).
- User bebas pakai LLM provider apa saja selama compatible dengan OpenAI Chat Completions / Responses API (bring your own API key).

### 2.2 Non-Goals (di luar scope v1)
- Translate suara/voice (speech-to-text real-time dari audio game) — jadi future enhancement, bukan MVP.
- Auto-detect posisi subtitle tanpa setup manual (AI vision auto-detect) — future enhancement.
- Mobile / console support — hanya PC.
- **Cross-platform (Linux/macOS) — di luar scope, keputusan final: Windows-only.** Alasan: overlay transparan + click-through dan screen capture jauh lebih kompleks di Linux (khususnya Wayland yang membatasi capture & always-on-top demi security), sementara mayoritas game PC (termasuk target utama seperti RPG/story-heavy AAA titles) dimainkan di Windows.
- Distribusi/monetisasi sebagai produk komersial — **bukan tujuan proyek ini**, murni personal tool. Hanya dicatat singkat di bagian akhir sebagai opsi kalau suatu saat mau dikembangkan lebih lanjut.

---

## 3. Target User

- **Primary user: Hanif sendiri** — pemain RPG/game story-heavy (JRPG, western RPG, game AAA modern seperti The Last of Us, God of War, dsb) yang tidak selalu paham bahasa asli dialog/subtitle game tersebut.
- Tool ini dibangun untuk kebutuhan personal, bukan produk yang akan didistribusikan ke publik di tahap awal — jadi requirement bisa lebih fleksibel & iteratif (gak perlu polish UI/UX seperti produk komersial dulu).
- User teknikal (developer), jadi tidak masalah memasukkan API key sendiri, edit config file, atau melakukan sedikit setup manual.

---

## 4. User Stories

1. **Sebagai pemain game**, saya ingin menggambar (drag) kotak area capture di atas jendela game, supaya saya bisa menentukan tepat di mana posisi subtitle muncul.
2. **Sebagai pemain game**, saya ingin area capture tersebut tersimpan sebagai "profile" per game, supaya saat saya main game yang sama lagi, saya tidak perlu setting ulang.
3. **Sebagai pemain game**, saya ingin teks yang ter-capture otomatis di-OCR dan diterjemahkan tanpa saya harus klik tombol setiap kali dialog berganti.
4. **Sebagai pemain game**, saya ingin hasil terjemahan muncul sebagai overlay transparan di atas game (bukan jendela terpisah yang harus saya lirik ke taskbar).
5. **Sebagai user**, saya ingin memasukkan API key dan base URL saya sendiri di setting, supaya saya bisa pakai provider AI pilihan saya (OpenAI, OpenRouter, provider self-hosted, dsb).
6. **Sebagai user**, saya ingin memilih bahasa sumber (atau auto-detect) dan bahasa target terjemahan.
7. **Sebagai user**, saya ingin bisa pause/resume proses auto-translate dengan hotkey global, tanpa keluar dari game.
8. **Sebagai user**, saya ingin bisa atur ukuran font, posisi overlay, dan opacity supaya tidak mengganggu visual game.
9. **Sebagai user**, saya ingin sistem cukup pintar untuk tidak mengirim request translate berulang-ulang kalau teks di layar tidak berubah (hemat biaya API & rate limit).

---

## 5. Scope & Fitur (MVP vs Next Phase)

### 5.1 MVP (Versi 1.0)

| # | Fitur | Deskripsi |
|---|-------|-----------|
| 1 | **Custom Capture Region Selector** | User drag kotak di atas layar/jendela game untuk menentukan area subtitle. Support multi-monitor. |
| 2 | **Game Profile Management** | Simpan/load capture region + setting lain per game (nama game, executable, region, bahasa). |
| 2b | **Multi-Region per Profile** | Satu game profile bisa punya lebih dari satu named region (misal "Dialog", "Battle", "Menu/Quest Log") karena banyak RPG punya posisi teks berbeda tergantung konteks (cutscene vs battle vs UI). User bisa switch region aktif manual lewat hotkey/tray icon. |
| 3 | **Screen Capture Engine** | Ambil screenshot area terpilih secara periodik (polling interval configurable, default ±500ms–1s) atau event-based (deteksi perubahan pixel). |
| 4 | **Change Detection** | Bandingkan frame capture terakhir vs sekarang (image diff / hashing) untuk menghindari OCR & translate yang tidak perlu saat teks tidak berubah. |
| 5 | **OCR Engine** | Ekstrak teks dari gambar area capture. Bisa pakai OCR lokal (Tesseract) atau vision model dari LLM provider yang sama. |
| 6 | **Translation via OpenAI-Compatible SDK** | Kirim teks hasil OCR ke LLM (Chat Completions) dengan prompt translation, terima hasil terjemahan. User isi sendiri `api_key` dan `base_url`. |
| 7 | **Overlay Renderer** | Jendela overlay transparan, always-on-top, click-through, menampilkan hasil terjemahan di posisi yang bisa diatur user. |
| 8 | **Global Hotkeys** | Toggle overlay show/hide, pause/resume capture, buka setting cepat. |
| 9 | **Settings Panel** | Konfigurasi API key, base URL, model name, bahasa sumber/target, interval capture, font & style overlay. |
| 10 | **Local Caching** | Cache pasangan (teks asli → terjemahan) supaya teks yang sama tidak translate ulang → hemat cost & lebih cepat. |
| 11 | **Manual Screenshot Trigger** | Selain mode auto-capture, tersedia hotkey untuk trigger screenshot terjemahan on-demand (capture sekali → OCR → translate → tampil di overlay). Berguna untuk kasus di mana auto-detection sulit atau user ingin translate dialog tertentu saja. Default hotkey: `Ctrl+Alt+Space`. |

### 5.2 Next Phase (Post-MVP)

- Auto-detect area subtitle menggunakan vision model (tanpa perlu drag manual).
- Speech-to-text untuk voice dialog (opsional pakai Whisper API atau lokal).
- Overlay history / log semua dialog yang sudah diterjemahkan (bisa dibuka seperti "riwayat chat").
- Auto-switch region otomatis berdasarkan deteksi context (misal deteksi UI battle vs cutscene), bukan manual hotkey.
- Preset komunitas untuk game-game populer (share region config antar user).

---

## 6. Functional Requirements (Detail)

### 6.1 Capture Region Selector
- Saat user klik "Add New Game Profile" atau "Set Capture Area", layar menampilkan overlay semi-transparan full-screen untuk drag-select kotak area.
- Menampilkan koordinat & ukuran (x, y, width, height) real-time saat drag.
- Support pilih monitor (jika multi-monitor) sebelum drag select.
- Bisa edit ulang area kapan saja dari profile game yang tersimpan.
- Preview live thumbnail area yang dipilih untuk verifikasi sebelum save.

### 6.2 Game Profile
- Field profile: `game_name`, `executable_name` (opsional, untuk auto-detect game aktif), `source_language`, `target_language`, `ocr_engine`, `capture_interval_ms`.
- Field per region (satu profile bisa punya banyak region): `region_name` (misal "Dialog", "Battle", "Menu"), `x, y, width, height, monitor_id`, `is_active_default` (region mana yang aktif saat profile pertama kali di-load).
- Switch antar region dalam satu profile lewat hotkey (misal `Ctrl+Alt+1/2/3`) atau lewat tray icon menu — berguna untuk RPG yang punya posisi teks beda antara cutscene, battle, dan menu/quest log.
- Auto-switch profile: jika `executable_name` diisi dan tool mendeteksi game tsb sedang foreground/aktif, otomatis load profile terkait (region tetap dipilih manual oleh user).
- Data disimpan lokal (misal JSON/SQLite), tidak perlu cloud sync di MVP.

### 6.3 Capture & Change Detection
- Ambil screenshot region tiap interval tertentu (default 500–1000ms, configurable).
- Sebelum kirim ke OCR, bandingkan dengan capture sebelumnya menggunakan perceptual hash / pixel diff dengan threshold tertentu.
- Jika diff di bawah threshold (dianggap tidak berubah) → skip OCR & translate.
- Jika di atas threshold → lanjut proses OCR.

### 6.4 OCR
- Ambil gambar hasil capture → ekstrak teks.
- Opsi engine:
  - **Lokal (offline):** Tesseract OCR — gratis, tanpa API call, cocok untuk hemat biaya & privasi.
  - **Vision LLM (online):** kirim gambar langsung ke model vision (misal lewat OpenAI-compatible endpoint yang support image input) — lebih akurat untuk font game yang stylized, tapi kena biaya API tambahan.
- User bisa pilih mode di setting (Local OCR vs Vision AI OCR).
- Hasil OCR dibersihkan (trim whitespace, hapus noise karakter aneh) sebelum masuk ke tahap translate.

### 6.5 Translation Engine (OpenAI SDK Compatible)
- Konfigurasi user:
  - `api_key` (disimpan terenkripsi di local storage, **tidak pernah dikirim ke server lain selain base_url yang ditentukan user**).
  - `base_url` (default `https://api.openai.com/v1`, tapi bisa diganti ke provider lain yang compatible).
  - `model` (misal `gpt-4o-mini`, atau model lain sesuai provider).
- Request menggunakan endpoint Chat Completions (atau Responses API), dengan system prompt khusus translation, contoh:
  - "Kamu adalah mesin penerjemah subtitle game. Terjemahkan teks berikut dari {source_lang} ke {target_lang}. Jawab HANYA dengan hasil terjemahan, tanpa penjelasan tambahan, tanpa tanda kutip."
- Response di-parse sebagai plain text hasil terjemahan.
- Retry mechanism dengan exponential backoff jika request gagal (timeout, rate limit, dsb).
- Timeout request default 10 detik, dengan indikator loading di overlay jika translate belum selesai.

### 6.6 Overlay Renderer
- Window terpisah, transparan, always-on-top, tanpa border/title bar.
- Mode **click-through** (mouse events tembus ke game di belakangnya) supaya tidak mengganggu kontrol game.
- Posisi & ukuran overlay bisa disesuaikan manual oleh user (drag reposisi saat mode edit aktif), independen dari area capture asli.
- Styling: font size, font family, warna teks, warna background box, opacity, bisa dikustomisasi dari Settings Panel.
- Animasi fade in/out saat teks berganti (opsional, untuk UX lebih halus).

### 6.7 Hotkeys (Global)
- Default (bisa di-remap):
  - `Ctrl+Alt+T` — Toggle overlay visible/hidden.
  - `Ctrl+Alt+P` — Pause/resume auto capture.
  - `Ctrl+Alt+S` — Buka Settings Panel cepat.
- Hotkey harus tetap berfungsi meskipun game dalam mode fullscreen exclusive (perlu dites khusus, karena capture & overlay sering bermasalah di mode ini dibanding borderless windowed).

### 6.8 Settings Panel
- Tab **API & Model**: api_key, base_url, model name, test connection button.
- Tab **Language**: source language (dengan opsi "Auto-detect"), target language.
- Tab **Capture**: interval, OCR engine pilihan, sensitivity change detection.
- Tab **Overlay**: font, warna, posisi default, opacity.
- Tab **Hotkeys**: remap semua hotkey.
- Tab **Game Profiles**: list semua profile tersimpan, edit/hapus/duplicate.

### 6.9 Caching
- Simpan pasangan teks asli → hasil translate di local cache (per profile game atau global) selama sesi berjalan (atau persist ke disk).
- Jika teks OCR baru match persis (atau mirip dengan fuzzy match tertentu) dengan cache, langsung tampilkan hasil cache tanpa call API lagi.

---

## 7. Non-Functional Requirements

| Kategori | Requirement |
|---|---|
| **Performance** | Capture + OCR + translate end-to-end idealnya < 3 detik. Overlay rendering tidak boleh menyebabkan FPS drop signifikan pada game (target: dampak < 5% FPS). |
| **Resource Usage** | Aplikasi harus ringan di background (target idle RAM usage < 200MB, CPU usage minimal saat tidak ada perubahan teks). |
| **Security & Privacy** | API key disimpan terenkripsi secara lokal. Tidak ada data capture/teks yang dikirim ke server pihak Anthropic/developer tool ini — hanya ke `base_url` yang user tentukan sendiri. |
| **Reliability** | Jika API call gagal, overlay menampilkan status error singkat, bukan crash aplikasi. |
| **Compatibility** | Wajib support Windows 10/11 (prioritas #1, karena mayoritas gamer PC). Harus bekerja baik di mode Fullscreen Borderless & Windowed; Fullscreen Exclusive sebagai best-effort. |
| **Usability** | Setup game baru (drag area + save profile) harus bisa diselesaikan dalam < 1 menit oleh user non-teknikal. |
| **Cost Control** | Fitur change-detection & caching wajib ada di MVP untuk meminimalkan jumlah API call (karena user bayar sendiri per-token). |

---

## 8. Tech Stack — Keputusan Final (Windows-only)

Karena scope sudah dikunci **Windows-only**, kelebihan utama Opsi B (Tauri/Electron, yaitu portabilitas cross-platform) jadi kurang relevan. Native Windows stack lebih optimal untuk performa capture & overlay, jadi ini keputusan final:

### Stack Final
- **Bahasa & Framework:** C# (.NET 8) dengan WPF untuk overlay window transparan + click-through.
- **Screen Capture:** `Windows.Graphics.Capture` API (Windows 10 versi 1903+) — API resmi Microsoft, efisien, read-only (aman dari deteksi anti-cheat).
- **Overlay Transparan + Click-through:** Win32 API (`SetWindowLong` dengan flag `WS_EX_LAYERED` + `WS_EX_TRANSPARENT`) dikombinasikan dengan WPF window.
- **OCR Lokal:** Tesseract (via `Tesseract.NET` wrapper) sebagai default engine.
- **OCR Alternatif:** Vision AI (kirim gambar ke model vision lewat endpoint OpenAI-compatible) sebagai fallback untuk game dengan font sulit.
- **Translation:** `HttpClient` langsung ke endpoint OpenAI-compatible (`/chat/completions` atau `/responses`) — tidak perlu SDK resmi OpenAI, cukup REST call biasa supaya lebih ringan dan fleksibel ganti provider.
- **Storage:** SQLite (profile, multi-region config, cache) via EF Core atau Dapper.
- **Global Hotkey:** Win32 `RegisterHotKey` API.

### Kenapa stack ini
- Performa capture & overlay paling smooth karena native ke Windows, tidak ada overhead abstraction layer.
- Windows.Graphics.Capture API dirancang khusus untuk capture per-window/per-region dengan efisien, cocok untuk kebutuhan capture area custom yang sering (polling tiap beberapa ratus ms).
- Win32 overlay API (`WS_EX_LAYERED` + `WS_EX_TRANSPARENT`) adalah cara paling stabil & terbukti untuk bikin overlay click-through di Windows (dipakai juga oleh tool sejenis seperti RTSS, Discord overlay, dsb).
- SQLite ringan, cukup untuk kebutuhan personal tool tanpa perlu server database terpisah.

### Catatan alternatif (didrop, hanya untuk referensi)
- **Python (PyQt + mss + pytesseract):** tetap opsi valid kalau mau prototyping super cepat sebelum masuk ke C#, tapi karena target akhirnya production-ready personal tool (bukan sekadar validasi ide), langsung ke C# native lebih efisien secara waktu total.
- **Tauri/Electron:** didrop karena keunggulan cross-platform-nya tidak relevan lagi setelah keputusan Windows-only, sementara overhead-nya (butuh native binding tambahan untuk capture & overlay) tetap ada.

---

## 9. High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        GameSubTranslate                       │
│                                                                 │
│  ┌───────────────┐    ┌──────────────┐    ┌────────────────┐ │
│  │ Region         │    │ Game Profile │    │ Settings Store  │ │
│  │ Selector UI    │───▶│ Manager      │◀──▶│ (API key, dsb)  │ │
│  └───────────────┘    └──────┬───────┘    └────────────────┘ │
│                               │                                │
│                               ▼                                │
│                       ┌───────────────┐                        │
│                       │ Capture Loop  │  (interval timer)      │
│                       │ (screen grab) │                        │
│                       └───────┬───────┘                        │
│                               ▼                                │
│                    ┌─────────────────────┐                     │
│                    │ Change Detection     │  (skip jika sama)  │
│                    └──────────┬──────────┘                     │
│                               ▼ (jika berubah)                 │
│                    ┌─────────────────────┐                     │
│                    │ OCR Engine           │                     │
│                    │ (Local / Vision AI)  │                     │
│                    └──────────┬──────────┘                     │
│                               ▼                                │
│                    ┌─────────────────────┐        ┌──────────┐│
│                    │ Cache Lookup         │───────▶│  Cache   ││
│                    └──────────┬──────────┘  hit    │ (local)  ││
│                               │ miss                └──────────┘│
│                               ▼                                │
│                    ┌─────────────────────┐                     │
│                    │ Translation Service   │──▶ OpenAI-compatible│
│                    │ (OpenAI SDK)          │    API (user's key) │
│                    └──────────┬──────────┘                     │
│                               ▼                                │
│                    ┌─────────────────────┐                     │
│                    │ Overlay Renderer      │                     │
│                    │ (transparent window)  │                     │
│                    └─────────────────────┘                     │
└─────────────────────────────────────────────────────────────┘
```

---

## 10. UX Flow Singkat

1. User install & buka aplikasi pertama kali → diarahkan ke Settings untuk isi `api_key` & `base_url`.
2. User klik "New Game Profile" → drag area subtitle di game yang sedang berjalan → preview & save.
3. User main game seperti biasa → aplikasi otomatis capture, OCR, translate di background.
4. Overlay muncul di posisi yang ditentukan, update tiap kali dialog berganti.
5. User bisa toggle overlay atau pause kapan saja dengan hotkey.

---

## 11. Success Metrics (untuk versi produk, bukan sekadar prototipe)

- **Setup time:** Rata-rata waktu user menyelesaikan setup profile game baru < 1 menit.
- **Latency:** P95 waktu dari perubahan teks di layar sampai overlay update < 3 detik.
- **API cost efficiency:** Rasio cache-hit vs total capture change > 30% (menunjukkan caching efektif menghemat biaya).
- **Stability:** Tidak ada crash aplikasi selama sesi gaming > 2 jam pada testing internal.
- **Overlay non-intrusive:** FPS impact terukur < 5% dibanding tanpa overlay aktif (diuji di beberapa game berbeda).

---

## 12. Risks & Assumptions

| Risk / Assumption | Mitigasi |
|---|---|
| Screen capture API berbeda-beda perilakunya di fullscreen exclusive mode | Fokus dukung Borderless/Windowed dulu di MVP, exclusive fullscreen sebagai best-effort/dokumentasi limitation. |
| Biaya API bisa membengkak jika user main game dengan dialog sangat cepat/banyak | Change detection + caching sebagai mitigasi wajib, bukan opsional. |
| OCR font game yang stylized (fantasy font, handwritten style) sulit dibaca Tesseract | Sediakan opsi Vision AI OCR sebagai fallback untuk kasus font sulit. |
| Anti-cheat software di beberapa game mendeteksi overlay/injection sebagai mencurigakan | Pastikan capture bersifat **read-only** (screen capture API resmi OS, bukan memory injection/hooking ke proses game) supaya aman dari deteksi anti-cheat. |
| Overlay click-through tidak konsisten antar OS/driver GPU | Testing menyeluruh di berbagai kombinasi GPU (NVIDIA/AMD/Intel) sebelum rilis. |

---

## 13. Tips Setup untuk Game Modern (AAA Story-Heavy)

Untuk maksimalin akurasi OCR di game modern seperti The Last of Us, God of War, RDR2, dsb, sebelum mulai capture:

1. **Aktifkan subtitle background/box** di menu Accessibility/Subtitle Settings — kebanyakan game modern punya opsi ini. Ini bikin kontras teks vs background tinggi, drastis mengurangi salah baca OCR terutama di adegan terang/putih.
2. **Perbesar ukuran font subtitle** kalau ada opsi — makin besar teks, makin akurat OCR-nya.
3. **Jalankan game di mode Borderless Windowed**, bukan Fullscreen Exclusive — supaya screen capture API bekerja stabil tanpa risiko freeze/gagal capture.
4. **Cek warna teks vs opsi kontras tinggi (high contrast subtitle)** kalau game menyediakan — beberapa game AAA modern punya opsi ini khusus untuk aksesibilitas, kebetulan sangat membantu OCR juga.
5. **Set capture region setelah masuk ke scene dialog pertama**, supaya posisi yang di-drag benar-benar pas dengan tempat subtitle muncul (bukan tebak-tebak dari menu utama).
6. Kalau game punya konteks UI berbeda (dialog vs battle vs menu), manfaatkan fitur **multi-region per profile** (lihat bagian 6.2) supaya tiap konteks punya area capture sendiri.

---

## 14. Catatan Monetisasi (Opsional, Bukan Prioritas)

Proyek ini murni dibangun untuk kebutuhan personal. Kalau suatu saat kepikiran untuk dikembangkan lebih jauh, beberapa arah yang bisa dipertimbangkan (tidak perlu dipikirkan sekarang):
- Freemium dengan bring-your-own API key vs versi terkelola.
- One-time purchase untuk niche gaming tools.
- Preset marketplace untuk share region config antar user.

*(Bagian ini sepenuhnya opsional dan tidak mempengaruhi scope teknis MVP.)*

---

## 15. Roadmap Ringkas

| Fase | Fokus | Estimasi |
|---|---|---|
| **Fase 1 — Prototype** | Capture area manual (hardcoded coordinate dulu) → OCR lokal (Tesseract.NET) → translate via HTTP call ke OpenAI-compatible endpoint → tampil di console/simple window (belum overlay transparan) | 1–2 minggu |
| **Fase 2 — MVP Overlay** | Overlay transparan click-through + game profile + hotkeys + settings panel | 2–3 minggu |
| **Fase 3 — Optimisasi** | Change detection, caching, error handling, performance tuning | 1–2 minggu |
| **Fase 4 — Polish & Packaging** | Installer, auto-update, UI polish, testing di berbagai game | 1–2 minggu |
| **Fase 5+ — Next Phase Features** | Auto-detect region via vision AI, voice translate, dsb | TBD |

---

## 16. Open Questions (Sisa yang Belum Diputuskan)

_Kosong di v1.3 — semua open question v1.2 sudah diputuskan._

**Sudah diputuskan (v1.3, update 2 Agustus 2026):**
- ✅ Platform: **Windows-only**, tidak ada rencana cross-platform.
- ✅ Tech stack final: **C# (.NET 8) + WPF + Windows.Graphics.Capture + Tesseract.NET + SQLite**.
- ✅ Multi-region per game profile: **termasuk di MVP**, bukan next phase.
- ✅ **Manual screenshot trigger** termasuk MVP (fitur #11). Hotkey default `Ctrl+Alt+Space`. Capture sekali → OCR → translate → tampil di overlay. Berguna saat auto-detection sulit atau untuk translate dialog tertentu saja.
- ✅ **OCR engine pluggable**, default **Tesseract (lokal)**, Vision AI jadi opsi kedua. User bisa switch di settings per profile. Tesseract cukup untuk kebanyakan game AAA modern dengan font relatif bersih; Vision AI fallback untuk font stylized.
- ✅ **Multi target language** didukung di settings, dengan **Bahasa Indonesia sebagai default dan prioritas utama**. User bisa ganti target language kapan saja.

---

*Dokumen ini adalah starting point. Detail teknis (skema database, API contract internal, struktur folder project) bisa dikembangkan lebih lanjut saat masuk fase implementasi.*
