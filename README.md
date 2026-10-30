# BetterScreenShot

A Windows screenshot app built with C# and WPF.

## Features

- Choose a monitor or drag an area to capture. Press Esc to cancel.
- Per-monitor DPI-aware selection overlays. Area selection is clipped to the monitor where the drag starts.
- Bottom-right preview with Copy, Save, Open Viewer, and Discard actions.
- Save actual PNG, JPEG, or BMP images.
- Screenshot editor with smoothed pen strokes, translucent highlights, underlines, arrows, rectangles, circles, and text.
- Click annotations to move, resize, recolor, or delete them. Double-click text to edit it; undo and redo work across edits.

## Screenshots

Mark up a screenshot with highlights, editable shapes, curved arrows, and text, then copy it directly to another app.

![Screenshot editor with a highlighted heading, blue rectangle, editable curved arrow, and text annotation](docs/screenshots/editor.jpg)

| Capture a screen or area | Preview and quick actions |
| --- | --- |
| <img src="docs/screenshots/capture.jpg" alt="Capture launcher with full-screen and area selection options" width="480"> | <img src="docs/screenshots/preview.jpg" alt="Screenshot preview with Copy, Save, Edit, and Discard actions" width="300"> |

Captures are temporary until you choose Save. Files live in `%TEMP%/BetterScreenShot` while their preview or viewer is open. Replacing or closing a preview removes its file. Opening the viewer transfers ownership to it; closing the viewer removes the temporary file. Copies saved with Save As remain on disk. Cleanup failures are written to the debug output; a crash or a locked file can leave temporary files behind.

## Install on Windows

Build a self-contained package with `./scripts/Publish-Installer.ps1`. Extract `artifacts/BetterScreenShot-win-x64.zip` and run `Install.cmd`. The installer adds the application to your user profile, creates a Start menu shortcut, enables startup at sign-in, and starts it quietly in the tray. It does not require administrator access or a separately installed .NET runtime. Existing capture shortcuts are preserved.

Run `Install.cmd` from File Explorer in your normal Windows desktop. An isolated terminal can redirect registry writes, making an installation entry visible to that terminal while absent from Windows Settings. The normal installation records the version, installation date, size, and an uninstall command that starts the app's dedicated uninstall mode.

Closing the launcher keeps the application running. Use its tray menu to open the launcher, configure keyboard shortcuts, turn sign-in startup on or off, or Exit. Launching another copy opens the existing instance instead of competing for the same shortcuts.

Keyboard shortcuts keep both existing selection modes: click a monitor for full-screen capture or drag an area. The launcher stays hidden before and after selection. The usual side preview appears with Copy, Save, Edit, and Discard, without taking keyboard focus.

Uninstall through Windows Settings → Apps → Installed apps, or run `Uninstall.ps1` from `%LOCALAPPDATA%/Programs/BetterScreenShot`. Uninstalling removes the application and startup entry while retaining saved screenshots and shortcut preferences.

## Portable single executable

Run `./scripts/Publish-Portable.ps1` to create `artifacts/BetterScreenShot-Portable-win-x64/BetterScreenShot.exe`. Copy just that EXE to another Windows x64 PC and double-click it. It includes the .NET runtime and all application assets, so no installer, companion files, or separate .NET installation are needed. For a Windows ARM64 PC, build with `-Runtime win-arm64` instead.

Use `./scripts/Publish-Portable.ps1 -FrameworkDependent` for the smallest executable. Its output is in `artifacts/BetterScreenShot-Portable-win-x64-FrameworkDependent/` and requires the .NET 8 **Windows Desktop Runtime** for x64 on the destination PC. The base .NET Runtime alone is insufficient.

The self-contained portable and installer builds compress their bundled assemblies. This reduces the EXE size at the cost of some startup decompression. WPF does not support trimming, so these builds keep framework code intact. Native runtime libraries are extracted automatically into the user's temporary cache when needed, and shortcut preferences remain in `%LOCALAPPDATA%/BetterScreenShot`. Running the portable EXE does not add it to Installed apps or enable startup automatically. If you enable Start with Windows in the tray menu, keep the EXE at the same location. Exit the installed copy first if you want to run a different portable build on the same PC; the app normally permits one instance per user.

On the development PC, compression reduced the self-contained EXE from 154.43 MiB to about 68.43 MiB. A framework-dependent EXE is about 0.37 MiB. A relocation check copied each EXE into an empty folder and launched it without companion files. The self-contained check used nonexistent .NET runtime search paths and a fresh native-library extraction cache. Median warmed secondary-instance launch times over three runs were 232 ms for the original EXE, 545 ms for the compressed EXE, and 320 ms for the framework-dependent EXE. These measure runtime startup and single-instance dispatch, not time to show the editor or capture a screenshot. First launches also include extraction and operating-system executable loading, so they can take longer.

## To run locally

1. Open `BetterScreenShot.sln` in Visual Studio.
2. Press `F5` to run the application.

You can also run `dotnet run --project BetterScreenShot.csproj`. Building requires the .NET 8 SDK or a newer compatible SDK on Windows.

## Capture shortcuts

Click **Shortcuts…** below the capture buttons. Click the shortcut field for Capture Full Screen or Select Area, press a key combination, and choose Save shortcuts. Each action can have its own shortcut; Clear disables it. Shortcuts start unassigned, work from other applications while BetterScreenShot is running, and are restored on the next launch. Settings are stored in `%LOCALAPPDATA%/BetterScreenShot/shortcuts.json`.

The app reports duplicate shortcuts and combinations Windows cannot register, including shortcuts already used by another app. Cancelling the dialog keeps the previous configuration. A shortcut starts the same monitor or area selection as its launcher button; Esc cancels selection.

While a shortcut field is listening, its recorder intercepts key presses and releases before they reach other applications. It stops listening on capture, Escape, Tab, or loss of focus. This temporary protection does not give saved shortcuts priority over another app or Windows. If Windows rejects a combination, disable that assignment in the other app or choose a different shortcut. Win-key combinations can be reserved by Windows.

## Checks

The regression runner uses the .NET runtime without third-party test packages. Run it on Windows with an interactive desktop:

```powershell
dotnet build BetterScreenShot.sln -c Release
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release
```

The checks cover format decoding, failed overwrites, captured pixel colors, frozen preview images, cached theme icons, temporary-file ownership, real selection cancellation and reuse, overlapping selections, launcher layout, and coordinate mapping at 100%, 125%, 150%, and 200% scaling. Tests briefly create selection overlays and preview/viewer windows and remove their fixtures afterwards.

To compare full-monitor capture plus cropping with direct area capture:

```powershell
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --benchmark
```

The benchmark reads the desktop into memory without saving screenshots. On the development machine, a 1920×1200 monitor and an 800×600 selection averaged 45.06 ms for full-monitor capture plus cropping and 17.52 ms for direct capture over 20 iterations. Capture pixel buffers fell from 11,136,000 to 1,920,000 bytes. These timings exclude PNG encoding and vary with hardware and desktop activity.

The same command also compares preview creation on a synthetic 1920×1200 image. Over 10 iterations, the median time for saving and decoding a PNG was 79.64 ms; copying the pixels directly and saving the PNG took 52.22 ms. This comparison includes saving the temporary PNG and excludes the desktop capture itself. Synthetic fixture files are removed after the run.

Capture uses a compositor flush instead of fixed sleeps, then copies the captured pixels into a frozen WPF image and saves the PNG on a worker thread. Preview, clipboard, and viewer share this image, so the app does not need to read and decode its own PNG. See Microsoft's [DwmFlush reference](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmflush) and [BitmapSource.Create reference](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.bitmapsource.create) for the synchronization and pixel-copy APIs.

## Image editing

Use the markup toolbar or the shortcuts V (select), H (pan), P (pen), M (highlight), L (underline/line), A (arrow), R (rectangle), O (circle/oval), and T (text). Click any annotation to select it, including while another tool is active. Drag it to move it, drag a corner to resize, choose a color or stroke width, or press Delete. Double-click text to edit it. Enter commits text; Shift+Enter adds a line; Esc cancels the current operation.

Toolbar controls share a 32-pixel height, centered icons and swatches, and consistent spacing. Undo, redo, and delete sit together. Groups wrap at narrower window widths while keeping the controls the same height. Light and dark toolbar previews can be rendered with `dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --editor-toolbar-preview`; images are written under `obj/editor-toolbar-preview`.

Hold Shift while drawing for squares, circles, or 45-degree lines. Hold Space to pan or Alt to draw over an existing annotation. Ctrl+Z undoes an operation; Ctrl+Shift+Z or Ctrl+Y redoes it. Scroll to zoom and click the zoom percentage to fit the image.

Selected arrows and lines have circular endpoint handles and a purple midpoint. Drag the midpoint to bend the stroke into a curve, or drag either endpoint to change its direction. Curves remain editable and support undo/redo. The Sketch toggle starts enabled and gives shape outlines a hand-drawn style. Turn it off for precise outlines. The setting applies to the whole image, including exports, without changing annotation positions or colors.

Right-click an annotation for Delete, Duplicate, and Change Color. Bent lines and arrows also offer Straighten, and text offers Edit Text. Right-click selects the annotation under the pointer. Ctrl+D duplicates the selection; these editing actions support undo/redo.

Click Copy or press Ctrl+C with the canvas focused to copy the image with all current annotations at its original pixel dimensions. Paste it into another application with Ctrl+V without saving a file first. Copy finishes any text being entered and keeps the editor open for further changes. While editing text, Ctrl+C copies the selected text as usual.

Save As exports the image and annotations together at the original pixel dimensions to PNG, JPEG, or BMP. The open editor keeps its editable objects after exporting. Compositing and encoding run off the UI thread using an independent snapshot, so editing can continue during export. Closing the editor discards its temporary image and editing session.

Run the synthetic 4K annotation benchmark with:

```powershell
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --editor-benchmark
```

The benchmark measures stroke geometry updates with 300 existing annotations, full-resolution compositing, and PNG encoding separately. It measures CPU work, not display frame rate, and results vary with hardware.

## Runtime assets

The app draws its window icons and interface icons in code or XAML. `assets/BetterScreenShot.ico` supplies the embedded executable icon used by Windows for shortcuts and Installed apps. It includes sizes from 16 to 256 pixels and can be regenerated with `./scripts/Generate-AppIcon.ps1`. `assets/camera.svg`, used for the capture cursor, is embedded in the assembly. No loose image assets are required. The other SVGs, `scan.png`, and `render-icon.html` are design references retained in the repository.

## Manual display checks

Coordinate tests do not replace checks on physical monitors with different scales. Before release:

| Scenario | Expected result |
| --- | --- |
| Capture the monitor containing the launcher | The launcher and the previous preview are absent from the capture. |
| Cancel either capture mode with Esc, then retry | All overlays disappear, both buttons work, and the previous preview returns. |
| Drag in every direction and past the starting monitor's edges | The captured pixels match the clipped selection. |
| Place 100%, 150%, and 200% monitors left, right, or above the primary monitor | The selection matches physical pixels and the preview appears inside the chosen monitor's working area. |
| Change resolution, scaling, or connected monitors between captures | The next selection uses the updated monitor geometry. |
| Capture repeatedly, open a viewer, then close it | Replaced previews are cleaned up; the viewer retains its file until it closes. |
| Save with each format, copy, zoom, and pan | Saved formats match their extensions, copied images retain full resolution, and viewer controls work. |

Windows DPI awareness is declared in the manifest before windows are created. Toast placement uses physical monitor coordinates and the target window's DPI, following Microsoft's [desktop DPI guidance](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows).
