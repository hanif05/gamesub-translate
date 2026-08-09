# Game Presets (T53)

Profil siap-pakai untuk 3 game AAA yang punya subtitle dialog. Tiap preset adalah file JSON di
`tests/fixtures/profiles/` yang bisa di-import lewat `ProfileRepository.ImportFromJson`.

> ⚠ **PENTING — koordinat di bawah ini adalah estimasi awal, BUKAN final yang sudah diverifikasi
> di game asli.** Belum dijalankan manual pakai Region Selector di game yang dimaksud. Angka
> bisa meleset karena beda resolusi monitor, beda in-game subtitle setting (font size, posisi,
> background box), beda Windows UI scaling.
>
> **Sebelum pakai preset ini sebagai "final"**: jalankan game, buka dialog aktif, pakai
> Region Selector (T7 Fase 2) untuk mengukur ulang koordinat real, lalu update angka di JSON
> dan tabel di bawah. Preset yang salah koordinat akan capture area kosong / subtitle terpotong.

## Cara pakai

### Import preset ke aplikasi

Saat ini tidak ada UI import — preset dipakai lewat ProfileImportTests (T53). Untuk pakai di
runtime, copy JSON lalu buka Settings → Profiles → New, paste angka manual. Alternatifnya:
tulis satu-shot script CLI yang panggil `ProfileRepository.ImportFromJson(File.ReadAllText(path))`
terhadap `Database` yang sama dengan aplikasi.

### Format JSON (SchemaVersion 1)

```json
{
  "SchemaVersion": 1,
  "Name": "...",
  "ExecutableName": "...",
  "SourceLang": "en",
  "TargetLang": "id",
  "OcrEngine": "Tesseract",
  "CaptureIntervalMs": 800,
  "Regions": [
    {
      "RegionName": "Dialog",
      "X": 0, "Y": 0, "Width": 0, "Height": 0,
      "MonitorIndex": 0,
      "IsActiveDefault": true,
      "SortOrder": 0
    }
  ]
}
```

`API key` TIDAK disimpan di sini — itu domain AppSettings (DPAPI-encrypted). Preset cuma
geometry + language hints.

## Preset: The Last of Us Part I (`tlou.json`)

- **Executable**: `tlou-i.exe`
- **Display mode**: Borderless Windowed 1920×1080 (rekomendasi — lihat T52 doc)
- **Subtitle setting di game**: ON, English voice + English text

### Region setup

| Region | X | Y | Width | Height | Monitor | Default |
|---|---|---|---|---|---|---|
| Dialog | 500 | 950 | 920 | 80 | 0 | ✅ |

### Visual (1920×1080 reference)

```
0                                                              1919
0  ┌──────────────────────────────────────────────────────────┐
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
950 ├──────────────────────────────────────────────────────────┤
   │                  [ DIALOG TEXT HERE ]                    │
1030┤──────────────────────────────────────────────────────────┤
   │                                                          │
1079└──────────────────────────────────────────────────────────┘
```

## Preset: God of War (2018) (`god-of-war.json`)

- **Executable**: `GoW.exe`
- **Display mode**: Borderless Windowed 1920×1080
- **Subtitle setting di game**: ON, English voice + English text. Game ini subtitle
  posisinya agak ke tengah-bawah (bukan full-bottom seperti TLOU).

### Region setup

| Region | X | Y | Width | Height | Monitor | Default |
|---|---|---|---|---|---|---|
| Dialog | 480 | 920 | 960 | 100 | 0 | ✅ |

### Visual

```
0                                                              1919
0  ┌──────────────────────────────────────────────────────────┐
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
920 ├──────────────────────────────────────────────────────────┤
   │            [ Kratos: Boy, wait. ]                       │
1020└──────────────────────────────────────────────────────────┘
```

## Preset: Persona 5 Royal (`persona5r.json`)

- **Executable**: `P5R.exe`
- **Display mode**: Borderless Windowed 1920×1080
- **Subtitle setting di game**: ON. Game ini pakai custom dialog box besar, bukan full-width
  text strip.

### Region setup

| Region | X | Y | Width | Height | Monitor | Default |
|---|---|---|---|---|---|---|
| Dialog Box | 410 | 1000 | 1100 | 80 | 0 | ✅ |

### Visual

```
0                                                              1919
0  ┌──────────────────────────────────────────────────────────┐
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
   │                                                          │
1000├──────────────────────────────────────────────────────────┤
   │       ┌────────────────────────────┐                     │
   │       │  [Ryuji: Let's go, man!]   │                     │
1080│       └────────────────────────────┘                     │
   └──────────────────────────────────────────────────────────┘
```

## Multi-region (opsional, advanced)

Untuk game dengan subtitle yang pindah-pindah (cutscene vs gameplay, dua bahasa,
multiple character), tambahkan beberapa region di array `Regions`. Set `IsActiveDefault=true`
di salah satu (yang paling sering muncul). Switch region lewat tray menu
**Region → …** (T49) atau pakai hotkey `Ctrl+Alt+R` (planned — belum diimplementasi T51).

Contoh struktur multi-region:
```json
"Regions": [
  { "RegionName": "Dialog",   "X": 500, "Y": 950, "Width": 920, "Height": 80,  "IsActiveDefault": true,  "SortOrder": 0 },
  { "RegionName": "Cutscene", "X": 0,   "Y": 880, "Width": 1920, "Height": 120, "IsActiveDefault": false, "SortOrder": 1 }
]
```

## Validasi manual (WAJIB sebelum commit sebagai "final")

1. Jalankan game di **Borderless Windowed 1920×1080**.
2. Buka scene dengan subtitle aktif.
3. Buka Region Selector (T7) — drag rectangle tepat di sekitar baris subtitle.
4. Catat X, Y, Width, Height dari Output overlay Region Selector.
5. Update angka di file JSON preset + tabel di doc ini.
6. Re-run `ProfileImportTests` untuk verifikasi round-trip masih lulus.
7. Commit dengan message `T53: verified {gamename} coordinates`.

## Known limitation (T52)

Kalau game jalan di **Fullscreen Exclusive**, overlay TIDAK visible (lihat T52 doc) tapi
**capture tetap jalan**. Pakai Borderless Windowed sebagai gantinya — semua fitur kerja di mode itu.
