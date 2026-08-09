# TASKS — Breakdown Fase 5 (UI Revamp)

**Status:** Draft — menunggu approval.
**Branch target:** `feature/fase-5-ui-revamp` (dibuat dari `main` setelah `feature/fase-4-polish` merged).
**Estimasi roadmap:** 1–2 minggu.
**Dependency:** Fase 4 selesai (T43–T54 merged, 82+ tests green).

Tujuan fase ini: bikin UI tidak lagi "polos default WPF". Tetap **no new NuGet UI library** — cukup XAML rework, color system terpusat, spacing/typography konsisten, animasi halus. Tujuannya supaya aplikasi terasa intentional dan cohesive, bukan kelihatan template kosong.

Prinsip desain:
- **Hierarchy lewat size + weight**, bukan border tebal atau warna ramai.
- **Spacing konsisten** — pakai 4/8/12/16/24 grid, bukan 5/7/13.
- **Color palette terpusat** di `App.xaml` lewat `SolidColorBrush` resource key — bukan hardcode hex di tiap XAML.
- **Tipografi**: Segoe UI Variable (atau fallback Segoe UI) untuk semua kontrol. Subtitle overlay tetap pakai font user setting.
- **Micro-interactions**: hover/press state pada semua button; cross-fade sudah ada (T47), tinggal apply ke transisi window/menu.

---

## Scope yang Dibawa dari Fase 4

Fase 4 punya fondasi UI tapi masih default-template look:
- Welcome wizard 3-step (T45) — window plain, button default WPF.
- Main window — ListBox profil polos, status text abu-abu, button tanpa hierarchy.
- Settings window — 7 tab dengan unicode emoji header, font size slider, color picker, palette popup.
- Overlay window — sudah ada drop shadow + fade animation (T46, T47).
- Tray icon — drawn runtime bitmap "GS" (T49).
- ProfileEditWindow / ProviderEditWindow — form polos Grid 2-kolom.
- RegionSelectorWindow — semi-transparent overlay untuk drag-select.

Fase 5 memoles semua ini. **TIDAK** refactor arsitektur — hanya presentational layer.

## Yang TIDAK Masuk Fase 5 (deferred)

- Theme switching dark/light (single theme cukup untuk personal tool).
- Custom icon set bitmap (emoji unicode + drawn runtime cukup).
- Animasi page transition (TabControl default fade cukup).
- High-DPI perfectionism (sudah auto-scale via WPF; kalau ada glitch minor → fix).
- Localization UI (multi-bahasa string) — di-defer ke Fase 6+.
- Custom window chrome (no title bar) — tidak perlu, default window chrome konsisten.
- Plugin/theming extensibility — over-engineering untuk personal tool.

---

## Aturan Eksekusi

- Setiap task = 1 commit (atau 1 PR kecil). Pesan: `T<n>: <short desc>` (lanjut nomor dari Fase 4, mulai T55).
- Branch `feature/fase-5-ui-revamp` WAJIB dibuat sebelum commit pertama.
- TIDAK tambah UI library baru (Material Design, ModernWpf, dll) — polish cukup via XAML rework + `App.xaml` resources.
- TIDAK ubah behavior; hanya presentational layer. Pipeline, hotkey, capture logic TIDAK boleh berubah signature public.
- Regression: `dotnet test` harus tetap exit 0. Manual smoke test tiap window setelah polish.
- Naming: tambah `ponytail:` comment di code yang sengaja disederhanakan (sesuai CLAUDE.md).
- Reference task ID di tiap file XAML yang di-rework: `<!-- F55: ... -->` singkat di atas block.

---

## Design Token Plan (di-apply di T55)

Sebelum mulai per-window, tetapkan **design tokens** di `App.xaml`:

```xml
<!-- F55: Color tokens. Single source of truth for the whole UI. -->
<SolidColorBrush x:Key="Brush.Bg.Base"      Color="#FF1A1A1F"/>  <!-- window bg -->
<SolidColorBrush x:Key="Brush.Bg.Surface"   Color="#FF24242B"/>  <!-- card/tab bg -->
<SolidColorBrush x:Key="Brush.Bg.Surface2"  Color="#FF2E2E36"/>  <!-- hover/active -->
<SolidColorBrush x:Key="Brush.Border"       Color="#FF3A3A45"/>
<SolidColorBrush x:Key="Brush.Text.Primary" Color="#FFF1F1F4"/>
<SolidColorBrush x:Key="Brush.Text.Muted"   Color="#FF9A9AA8"/>
<SolidColorBrush x:Key="Brush.Accent"       Color="#FF7C8CFF"/>  <!-- primary action -->
<SolidColorBrush x:Key="Brush.Accent.Hover" Color="#FF8E9DFF"/>
<SolidColorBrush x:Key="Brush.Success"      Color="#FF5BB97A"/>
<SolidColorBrush x:Key="Brush.Warn"         Color="#FFE0A040"/>
<SolidColorBrush x:Key="Brush.Error"        Color="#FFE06060"/>

<!-- F55: Typography. Segoe UI Variable (Win11) → fallback Segoe UI. -->
<FontFamily x:Key="Font.Ui">Segoe UI Variable, Segoe UI</FontFamily>
<sys:Double x:Key="Font.H1">22</sys:Double>
<sys:Double x:Key="Font.H2">16</sys:Double>
<sys:Double x:Key="Font.Body">13</sys:Double>
<sys:Double x:Key="Font.Caption">11</sys:Double>

<!-- F55: Spacing scale (use as Margin/Padding). -->
<Thickness x:Key="Pad.Sm">4</Thickness>
<Thickness x:Key="Pad.Md">8</Thickness>
<Thickness x:Key="Pad.Lg">12</Thickness>
<Thickness x:Key="Pad.Xl">16</Thickness>
```

Window background default → `#1A1A1F` (dark, konsisten dengan tray icon "OK" green yang dipakai sebagai status indicator).

---

## Urutan Task (by Dependency)

### FASE 5.A — Foundation

#### T55. Design token + global style (App.xaml)
**Status**: ✅ done (commit `ccdb9c8`).
**Deskripsi**: Introduce `App.xaml` resource dictionary yang berisi color brush, font family, spacing, dan implicit `Style` untuk `Button`, `TextBox`, `ComboBox`, `TabItem`, `TabControl`, `ListBox`, `Slider`, `PasswordBox`, `CheckBox`. Setiap window pakai tokens via `{StaticResource Brush.X}` — tidak ada hardcode hex.
- File terpusat: `src/GameSubTranslate.App/App.xaml` + split jadi `src/GameSubTranslate.App/Resources/Tokens.xaml` kalau mulai panjang.
- Implicit style override WPF default — semua kontrol di semua window otomatis dapat look baru.
- Hover state pakai trigger `IsMouseOver` → swap ke `Brush.Accent.Hover`.
- Press state pakai trigger `IsPressed` → slight darker.
- Disabled state pakai `Opacity=0.5` (tidak abu-abu total — readable).
- Border radius 6 untuk `Button`, `4` untuk `TextBox`/`ComboBox`.
- Padding konsisten: `8,6` untuk button, `6,4` untuk text input.
- Font default untuk semua kontrol = `Font.Ui`.

**Output**: update `src/GameSubTranslate.App/App.xaml` (atau split resource dictionary).
**Done when**:
- `App.xaml` (atau file baru) define semua token + implicit style.
- Build sukses, tidak ada referensi hardcode yang putus.
- Tidak ada perubahan functionality — hanya styling.
**Depends on**: — (foundation, semua task lain pakai tokens).

#### T56. Window chrome polish (title bar + corner radius)
**Status**: ✅ done (commit `f067d8c`).
**Deskripsi**: Semua window utama (MainWindow, SettingsWindow, WelcomeWindow, ProfileEditWindow, ProviderEditWindow) dapat:
- `Window.Background = Brush.Bg.Base` (atau `Transparent` untuk overlay-only).
- `WindowChrome` opsional: kalau mau flat title bar, set `WindowStyle="SingleBorderWindow"` (default) — TIDAK pakai custom chrome.
- `Border` di dalam window dengan `CornerRadius="8"` kalau set `WindowStyle="None"` — skip dulu, default chrome sudah cukup konsisten.
- Icon window: pakai tray icon resource sebagai `Icon=""` reference (satu resource shared).
- Min width + min height supaya tidak bisa di-resize jadi bentuk aneh.

**Output**: minor edit di tiap `XAML` window.
**Done when**: setiap window punya background konsisten, tidak ada window yang masih default white bg.
**Depends on**: T55 (tokens).

#### T57. Iconography & emoji polish
**Status**: ✅ done (commit `c8e7d95`).
**Deskripsi**: Tab header emoji saat ini (T48) menggunakan unicode emoji color (Windows Color Emoji). Beberapa user merasa ini tidak cocok dengan tema dark. Pilihan:
- Pakai monochrome `Segoe Fluent Icons` font (built-in Windows 11) untuk konsistensi visual — codepoint: `� = E713`, `🌐 = E909`, `📷 = E8B8`, `🎨 = E8B1`, `⌨ = E765`, `🎮 = E7FC`, `ℹ = E946`.
- Tulis converter kecil `IconToTextConverter` atau pakai langsung `<Run FontFamily="Segoe Fluent Icons">` inline.
- Welcome wizard step indicator: pakai filled/empty circle (● / ○) bukan angka "1."/"2."/"3.".

**Output**: update `SettingsWindow.xaml`, `WelcomeWindow.xaml`, optional `MainWindow.xaml`.
**Done when**: semua emoji jadi monochrome (atau tetap color — pick satu, konsisten).
**Depends on**: T55.

---

### FASE 5.B — Main Window Revamp

#### T58. Main window layout overhaul
**Status**: ✅ done (commit `eb2ac17`).
**Deskripsi**: `MainWindow.xaml` saat ini layout-nya raw DockPanel. Revamp jadi:
- **Sidebar kiri** (240px): profile list dengan icon per profile + count badge.
- **Main panel** (kanan): region combo, status pill, control button (Start/Pause/Stop) — pakai accent color untuk primary action.
- **Bottom strip** (40px): status text + tray indicator mini.
- Header atas: app title "GameSubTranslate" + version (read dari `version.txt`).
- Pakai `Grid` dengan column definition, bukan raw DockPanel — lebih predictable.
- Button Start: `Background=Brush.Accent`, Foreground white, prominent.
- Button Stop: outlined (no fill), warn-red border.
- Button Pause: outlined neutral.
- Status: pill-shaped `Border` dengan `CornerRadius=12`, background warn/ok sesuai state.
- Empty state untuk profile list: centered text "No profiles yet — click + to add" dengan icon.

**Output**: rewrite `src/GameSubTranslate.App/MainWindow.xaml` + minor `MainWindow.xaml.cs` kalau ada binding baru.
**Done when**:
- Window terasa "designed" bukan "default WPF template".
- Start button prominent (accent color), Stop secondary.
- Status pill warna dinamis (idle gray, running green, paused yellow, error red).
- Tidak ada regression functional (semua event handler tetap wired).
**Depends on**: T55, T56.
**Catatan**: T58 tidak butuh T57 (mono icon font) — tab icon di MainWindow opsional, pakai saat T57 selesai kalau sempat.

#### T59. Profile list visual treatment
**Status**: ✅ done (commit `eb2ac17`).
**Deskripsi**: `ListBox` profile jadi custom item template:
- Avatar/icon kiri (huruf pertama profile name dalam circle 32×32 dengan bg `Brush.Bg.Surface2`).
- Nama profile (bold) + executable name (caption muted) di tengah.
- Active indicator: vertical accent strip 3px di kiri item.
- Region count badge kanan: small pill "3 regions".
- Hover: bg swap ke `Brush.Bg.Surface2`.
- Selected: bg `Brush.Bg.Surface2` + accent strip + text color swap ke `Brush.Text.Primary`.

**Output**: `DataTemplate` di `MainWindow.xaml.Resources`.
**Done when**:
- Profile list terasa seperti "card per profile" bukan raw list string.
- Active profile visually distinct.
**Depends on**: T58.

---

### FASE 5.C — Settings Window Revamp

#### T60. Settings tab iconography + spacing
**Status**: ✅ done (commit `070dff3`).
**Deskripsi**: `SettingsWindow.xaml` saat ini 7 tab pakai emoji color unicode. Apply T57 (mono icon font) + tambah spacing breathing room:
- Tab header: icon + label, gap 6px.
- TabControl `Background=Transparent`, content area `Brush.Bg.Surface`, `CornerRadius=8` content area.
- Selected tab indicator: 2px `Brush.Accent` di bawah header.
- Hover tab header: bg `Brush.Bg.Surface2`.
- Content per tab: `Margin=16` (lebih lega dari current 10).

**Output**: update `SettingsWindow.xaml`.
**Done when**: tab visibly distinct, content area punya card feel.
**Depends on**: T57, T56.

#### T61. API & Model tab redesign
**Status**: ✅ done (commit `070dff3`).
**Deskripsi**: Sub-form paling penting karena user wajib isi di sini. Revamp:
- Label di-atas input dengan caption kecil "Get one from platform.openai.com" (link styled).
- API key field: toggle visibility eye icon (G/F) di kanan.
- Test Connection button jadi primary (accent).
- Fallback providers: drag-drop reorder pakai `AllowDrop` + handler simple (instead of ▲▼ buttons) — kalau drag-drop terlalu kompleks, tetap ▲▼ tapi styling better (icon button, bukan text unicode).
- Inline help text dengan icon ⓘ di kanan field.
- Validation error: red border + caption di bawah field (bukan `TextBlock` terpisah di samping).

**Output**: update `SettingsWindow.xaml` API tab section + `SettingsWindow.xaml.cs` untuk show/hide password.
**Done when**: API tab terasa "form designed" bukan "stack of TextBox + label".
**Depends on**: T60.

#### T62. Overlay tab — slider polish
**Status**: ✅ done (commit `070dff3`).
**Deskripsi**: Slider FontSize & Opacity pakai style baru:
- Track lebih tipis (4px), thumb lebih besar (16px circle).
- Value tooltip muncul saat drag (floating label).
- Color picker: pakai `Popup` dengan palette yang lebih besar (16 → 32 swatch, organized by hue).
- Preview card: lebih besar (min height 80), padding lebih lega, font preview pakai text contoh dinamis "The quick brown fox jumps over the lazy dog." bukan "Sample subtitle text".
- Live preview tetap real-time (T48) — hanya styling update.

**Output**: update `SettingsWindow.xaml` Overlay tab + style override untuk Slider.
**Done when**: slider drag smooth, palette lebih kaya, preview card prominent.
**Depends on**: T60, T55.

#### T63. Hotkey capture polish
**Status**: ✅ done (commit `070dff3`).
**Deskripsi**: Hotkey "Change" button saat ini plain. Revamp:
- Button pakai icon font "pencil/edit" codepoint + label.
- Saat capture aktif: hint area jadi banner full-width dengan bg `Brush.Warn` semi-transparent + label "Press the new keys… ESC to cancel".
- Conflict warning pakai `Brush.Error` red.
- Layout lebih lapang, label + hotkey sejajar dengan min-width supaya konsisten.

**Output**: minor edit `SettingsWindow.xaml` Hotkey tab + style untuk banner.
**Done when**: hotkey capture flow terasa guided.
**Depends on**: T60.

#### T64. About tab polish
**Status**: ✅ done (commit `070dff3`).
**Deskripsi**: About tab saat ini informative tapi flat. Revamp:
- App name H1 (22px) + version caption (11px muted).
- Description dengan max-width supaya tidak full-justify.
- "Known limitation" banner: warna konsisten dengan status (warn amber), icon ⓘ di kiri.
- Links: "Open Logs Folder", "Documentation", "Check for updates" (disabled jika tidak applicable) — style jadi secondary button.
- Credits section kecil: "Built with .NET 8 + WPF + Tesseract" muted caption.
- Reset to Defaults button jadi destructive-styled (red outline, di kanan bawah).

**Output**: minor edit `SettingsWindow.xaml` About tab.
**Done when**: About tab terasa "informative landing", bukan "list of plain TextBlock".
**Depends on**: T60, T55.

---

### FASE 5.D — Welcome Wizard Revamp

#### T65. Welcome window hero treatment
**Status**: ✅ done (commit `0b906a7`).
**Deskripsi**: Wizard 3-step jadi terasa "onboarding" bukan "form dump":
- **Header strip** (60px): app name + tagline "Real-time subtitle translation for your games".
- **Step indicator**: 3 dot horizontal di atas konten — filled = current/done, outlined = upcoming. Transition slide content 150ms cross-fade.
- **Step 1 (API)**: ilustrasi icon besar "🔑 → 🧠" (atau Segoe Fluent Icons equivalent), input group, "Why we need this" expandable accordion (collapsed by default).
- **Step 2 (Language)**: card-style language picker, big flag/icon per bahasa + label. (Skip flag icon kalau tidak ada — pakai huruf 2 pertama uppercase.)
- **Step 3 (Tour)**: 3 feature card sejajar (atau stacked dengan icon besar), masing-masing card punya icon 32px + heading + body.
- Button "Skip" jadi text-only di kanan (less prominent), "Back/Next" jadi primary.
- Background gradient halus dari `#1A1A1F` ke `#24242B` top-to-bottom.

**Output**: rewrite `src/GameSubTranslate.App/Onboarding/WelcomeWindow.xaml` + adjust `.xaml.cs` untuk step transition.
**Done when**:
- Wizard terasa modern, bukan "WPF form default".
- Step transition smooth (cross-fade 150ms).
**Depends on**: T55, T57.

---

### FASE 5.E — Overlay & Tray Polish

#### T66. Overlay subtle entrance animation
**Status**: ✅ done (commit `0d1bc4e`).
**Deskripsi**: Overlay sudah punya fade in/out (T47). Tambah:
- Subtitle baru muncul dengan slide-up animation 8px (200ms ease-out) — combine dengan fade in.
- Saat text berubah (cross-fade), text baru slide-up, text lama slide-down 4px — feels "subtle scroll" bukan pop.
- Pause/resume state: overlay border glow tipis dengan accent color (animated opacity 0.3 → 0.6 → 0.3 loop 2 detik) supaya user tahu pipeline aktif tapi tidak distraksi.

**Output**: update `OverlayWindow.xaml.cs` animation hooks (extend `TickFade` atau add separate `SlideTimer`).
**Done when**:
- Subtitle masuk dengan slide halus, bukan pop.
- Pause state visibly distinct (glow).
**Depends on**: T47 (Fase 4), T55 (untuk glow color).

#### T67. Tray menu polish
**Status**: ✅ done (commit `e218bf4`).
**Deskripsi**: Tray menu (`ContextMenu`) saat ini pakai WPF default. Revamp:
- Submenu region/target lang: pakai item template dengan icon kecil (globe untuk language, region shape untuk region).
- Spacing per item lebih lega (Padding 8,4) supaya tidak cramped.
- Separator pakai 1px line `Brush.Border`.
- Hover state pakai `Brush.Bg.Surface2`.
- Status indicator (T49) tampil sebagai item non-clickable di paling atas: "● Running on primary" / "● Degraded: <provider>" (bullet color = status).
- Double-click behavior (T49) tetap.

**Output**: update `App.xaml.cs:InitTray` + tambah item template di `App.xaml`.
**Done when**:
- Tray menu terasa "designed" bukan "WPF default ContextMenu".
- Status visible di menu.
**Depends on**: T55, T49 (Fase 4).

#### T68. Profile edit + provider edit dialog polish
**Status**: ✅ done (commit `39a49c6`).
**Deskripsi**: `ProfileEditWindow` dan `ProviderEditWindow` saat ini form Grid polos. Apply consistent form styling:
- Header strip dengan nama window + icon.
- Form fields pakai label + input dengan caption helper text di bawah.
- Required field indicator: asterisk merah di label.
- Save jadi primary (accent), Cancel jadi secondary (outline).
- Validation error muncul inline di bawah field, bukan `MessageBox`.
- Untuk ProfileEdit: regions list di kanan dengan card per region (name + coordinate) — bukan raw list.

**Output**: rewrite `ProfileEditWindow.xaml`, `ProviderEditWindow.xaml` + minor `.xaml.cs` untuk inline validation.
**Done when**: dialog form terasa cohesive dengan MainWindow + Settings.
**Depends on**: T55, T58.

---

### FASE 5.F — Misc & Verification

#### T69. Region selector polish
**Status**: ⬜.
**Deskripsi**: `RegionSelectorWindow` (Fase 2 T7) semi-transparent crosshair overlay untuk drag-select capture region. Polish:
- Crosshair lebih halus (1px line + 8px circle center).
- Selection rectangle: dashed border `Brush.Accent`, 2px width.
- Coordinate readout di pojok kiri atas: `(X, Y) — WxH` dengan monospace font.
- Hint text "Drag to select subtitle region — ESC to cancel" center-bottom muted.
- Setelah select, fade out 200ms sebelum close.

**Output**: update `src/GameSubTranslate.App/Regions/RegionSelectorWindow.xaml` + `.xaml.cs`.
**Done when**: region selector terasa "tool" bukan "debug rectangle".
**Depends on**: T55.

#### T70. Final visual QA pass
**Status**: ⬜.
**Deskripsi**: Buka tiap window dalam sequence:
1. First-run wizard (hapus settings.json dulu).
2. MainWindow dengan profile baru.
3. Settings tiap tab.
4. ProfileEditWindow + ProviderEditWindow.
5. RegionSelectorWindow (kalau game jalan).
6. Overlay (pakai selfcheck atau dummy).
7. Tray menu right-click.
Cek: spacing konsisten, color konsisten, tidak ada hardcode hex leftover, font konsisten, hover/press state responsive. Tweak minor kalau ada glitch.

**Output**: laporan singkat di section "Fase 5 QA Report" bawah file ini.
**Done when**: tiap window pass visual review.
**Depends on**: T55–T69 (implisit transitive via graph; ditulis ringkas di sini untuk dokumentasi).

#### T71. Update screenshots di `docs/`
**Status**: ⬜.
**Deskripsi**: Tambah section "Screenshots" di `README.md` (atau doc baru `docs/ui-preview.md`):
- Capture tiap window pakai Print Screen atau render XAML to bitmap.
- Annotate dengan arrow + caption.
- File di `docs/screenshots/` (PNG, ~80% quality, max 200KB each).

**Output**: `docs/screenshots/*.png` + section di README atau doc baru.
**Done when**: minimal 5 screenshot tersedia (welcome, main, settings overlay tab, profile edit, tray menu).
**Depends on**: T70.

---

## Dependency Graph (Ringkas)

```
T55 → T56, T57, T58, T60, T62, T64, T65, T66, T67, T68, T69
T56 → T58, T60
T57 → T60, T65
T58 → T59, T68
T60 → T61, T62, T63, T64
T70 → T71
```

Critical path: `T55 → T58 → T59` atau `T55 → T60 → T61/62/63/64`.

Catatan fase luar & implisit:
- T66 butuh T47 (Fase 4: fade timer + cross-fade di `OverlayWindow.xaml.cs`).
- T67 butuh T49 (Fase 4: tray init di `App.xaml.cs`).
- T70 (final QA) implisit transitive ke semua task; tidak di-edge di sini untuk hindari clutter.
- Semua task depend on T55 karena pakai design tokens; di-edge sekali di paling atas.

## Estimasi Kasar

| Task Group | Estimasi |
|---|---|
| T55–T57 (Foundation: tokens + chrome + icons) | 1–2 hari |
| T58–T59 (Main window + profile list) | 1–2 hari |
| T60–T64 (Settings tabs) | 2–3 hari |
| T65 (Welcome wizard) | 1 hari |
| T66–T67 (Overlay + tray) | 1 hari |
| T68–T69 (Dialog polish) | 1 hari |
| T70–T71 (QA + docs) | 1 hari |
| **Total** | **8–11 hari** (≈2 minggu dengan buffer) |

Sesuai estimasi roadmap 1–2 minggu, on track.

## Hasil Verifikasi (diisi saat T70–T71 selesai)

_(Diisi setelah eksekusi. Format: tiap window + checklist item + status + catatan.)_

## Catatan

- **Konvensi tetap:** namespace `GameSubTranslate.<Module>`, async method suffix `Async`, interface prefix `I`, error handling di translation client tidak boleh crash app. Test naming: `MethodName_StateUnderTest_ExpectedBehavior` (standard xUnit).
- **Performance budget:** setiap perubahan visual harus dicek tidak menambah FPS impact > 1% (overlay animation sudah diukur di T50). Animasi window pakai `DispatcherTimer` atau `Storyboard` saja — tidak ada custom render loop.
- **Accessibility:** color contrast ratio minimum 4.5:1 untuk body text. Pakai `Brush.Text.Primary` (#F1F1F4) di atas `Brush.Bg.Surface` (#24242B) = 11.6:1, jauh di atas standar. Icon-only button harus ada `ToolTip` (semua sudah).
- **Test strategy:** tidak ada automated UI test untuk Fase 5 (deferred ke Fase 6+ kalau pakai FlaUI). Verifikasi manual + screenshot.
- **Jangan over-engineer:** theme switching, custom chrome, animasi berlebihan, library baru semua explicit out-of-scope. Polish cukup XAML rework + tokens terpusat.
- **Revert safety:** setiap task berdiri sendiri (commit kecil). Kalau ada task yang hasilnya tidak disukai, `git revert <commit>` clean rollback tanpa affect task lain.
