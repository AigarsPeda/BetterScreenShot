# Project handoff

Last reviewed: 2026-10-30. Source baseline: `d21d66c`.

This is the starting point for another developer or coding agent. Keep it in the repository and update it when architecture, release procedures, or known limitations change. Check `git status` and recent commits before relying on the status recorded here.

## Start here

- [README](README.md) covers installation, portable builds, local development, shortcuts, editing, and test commands.
- [Style guide](STYLEGUIDE.md) describes the desktop UI direction. Use the current XAML as the reference for implemented dimensions and behavior.
- [Screenshot guide](docs/screenshots/README.md) explains how to refresh the public screenshots using demo content.

## Development workflow

The app targets .NET 8 on Windows and uses both WPF and Windows Forms. Build with a compatible .NET SDK on Windows. The regression runner needs an interactive desktop because some checks create native windows and selection overlays.

Use the commands in [local development](README.md#to-run-locally) and [checks](README.md#checks). There are no third-party test packages.

Exit the resident installed app from its tray menu before testing a different development or portable build. A second normal launch opens the existing instance; a second background launch exits quietly. Otherwise, it is easy to mistake the installed version for the version just built.

Demo windows run through the regression executable with isolated sample data. Close them before rebuilding. A lingering regression process can lock `Tests/bin/.../BetterScreenShot.dll`; identify the specific test process rather than stopping all screenshot app processes.

## Code map

| Area | Main files and responsibilities |
| --- | --- |
| App lifetime | [App.xaml.cs](App.xaml.cs) handles startup and command-line modes. [BackgroundInstance.cs](BackgroundInstance.cs) coordinates one instance per Windows user through a named mutex and show/exit events. |
| Background operation | [MainWindow.Background.cs](MainWindow.Background.cs), [TrayController.cs](TrayController.cs), and [StartupRegistration.cs](StartupRegistration.cs) manage the hidden launcher, tray menu, and sign-in startup. |
| Capture flow | [MainWindow.xaml.cs](MainWindow.xaml.cs) orchestrates selection, capture, and preview. [ScreenshotCaptureService.cs](ScreenshotCaptureService.cs) copies pixels and creates the frozen preview image. |
| Selection overlays | [MonitorSelectionOverlayForm.cs](MonitorSelectionOverlayForm.cs) and its partial files handle monitor and area selection. [CaptureSelectionSession.cs](CaptureSelectionSession.cs) controls completion and cleanup. [CaptureGeometry.cs](CaptureGeometry.cs), [MonitorGeometry.cs](MonitorGeometry.cs), and [DesktopWindowService.cs](DesktopWindowService.cs) handle coordinates, DPI, and desktop placement. |
| Capture shortcuts | [GlobalCaptureShortcuts.cs](GlobalCaptureShortcuts.cs) owns native registrations. [CaptureShortcutSettings.cs](CaptureShortcutSettings.cs) validates and stores bindings. [MainWindow.Shortcuts.cs](MainWindow.Shortcuts.cs) connects them to the launcher and dialog. |
| Shortcut recording | [CaptureShortcutsWindow.xaml.cs](CaptureShortcutsWindow.xaml.cs) and [ShortcutRecordingHook.cs](ShortcutRecordingHook.cs) temporarily intercept input while a field is listening. |
| Preview and files | [ScreenshotToastWindow.xaml.cs](ScreenshotToastWindow.xaml.cs) manages preview actions and transfers temporary-file ownership to the editor. [TemporaryScreenshot.cs](TemporaryScreenshot.cs) and [ScreenshotFileService.cs](ScreenshotFileService.cs) handle cleanup and exports. |
| Editor | [ScreenshotViewerWindow.xaml](ScreenshotViewerWindow.xaml), its code-behind, and the Editor/ContextMenu partial files implement tools, inline text, copy, save, and quick actions. |
| Annotations | [ImageAnnotation.cs](ImageAnnotation.cs) contains the annotation model and document history/rendering. [AnnotationSurface.cs](AnnotationSurface.cs) handles selection and gestures. [SketchGeometry.cs](SketchGeometry.cs) creates deterministic sketch outlines. |
| Text extraction | [TextExtractionService.cs](TextExtractionService.cs) calls Tesseract's C API on a worker thread. [ScreenshotViewerWindow.TextExtraction.cs](ScreenshotViewerWindow.TextExtraction.cs) owns the results panel, cancellation, language cache, and text clipboard action. |
| Themes and assets | [ThemeIconService.cs](ThemeIconService.cs) supplies theme resources and window icons. The executable icon and camera cursor are embedded through [BetterScreenShot.csproj](BetterScreenShot.csproj). |
| Verification | [Tests/Program.cs](Tests/Program.cs) runs regression checks and dispatches demo/benchmark modes. [Tests/ReadmeScreenshots.cs](Tests/ReadmeScreenshots.cs) opens the documentation fixtures. |

## Behavior to preserve

- Shortcut capture keeps the launcher hidden before and after selection. Full-screen capture still lets the user choose a monitor; area capture still requires a drag. Both finish with the usual side preview. Do not replace this flow with automatic saving or copying.
- First-use overlays paint while transparent, then apply their dimming opacity. Preserve that ordering and cached-overlay reuse when changing capture initialization. Regressions can blank or flash both monitors.
- Annotations remain editable in the open editor. Clicking a mark selects it across drawing and pan tools. Curved arrows and lines have editable endpoints and a midpoint. Sketch style starts enabled, and exports use the same geometry as the editor.
- Copy composites the entire image with its current annotations at original pixel dimensions. It finishes inline text, leaves the editor open, and writes no saved export. Ctrl+C in a text field keeps its normal text-copy behavior. Background rendering uses an independent snapshot.
- A capture's temporary file belongs to the preview until ownership transfers to the editor. Closing its owner removes it. Saved exports remain on disk. See the README for the full lifetime rules.
- Toolbar controls share a 32-DIP height and wrap in groups. Color presets and custom color are in the Color button's menu; its swatch tracks the active annotation color. Check both themes and narrow windows when changing control templates or spacing.
- Recording protection lasts only while a shortcut field listens, with intercepted releases drained afterward. It does not give a saved shortcut priority over Windows or another app. Native registration must still report conflicts. Fn is not a reliable Windows modifier.

## Installation and releases

Follow [installation](README.md#install-on-windows) and [portable publishing](README.md#portable-single-executable) for commands and output paths. The release entry points are [Publish-Installer.ps1](scripts/Publish-Installer.ps1) and [Publish-Portable.ps1](scripts/Publish-Portable.ps1). Generated `artifacts`, `bin`, and `obj` directories are ignored by Git and must be rebuilt after source changes.

The installed app lives at `%LOCALAPPDATA%/Programs/BetterScreenShot`. [Install.ps1](scripts/Install.ps1) creates its Start menu shortcut, sign-in startup entry, and per-user Installed apps entry. It enables startup when installing or updating. Preferences live separately at `%LOCALAPPDATA%/BetterScreenShot/shortcuts.json`.

Application author, company, and installer publisher are A. Peda. The seven-language x64 self-contained build measures 85.14 MiB, compared with 74.41 MiB for the English/Latvian build before the five additional models.

The uninstall entry points to the installed EXE with `--uninstall`. That mode launches the adjacent [Uninstall.ps1](scripts/Uninstall.ps1) and exits, allowing the script to remove the app files. The uninstaller removes the startup and installation entries while retaining preferences and saved screenshots. The installed package needs this script beside the EXE; the portable EXE needs no companion files for normal operation.

### Installation verification matters

During development, an isolated execution environment could read an uninstall registry entry that a command launched from the normal Windows desktop could not see. Settings and Programs and Features also omitted the app. Running the installer from the normal desktop resolved the missing entry, and the user confirmed it appeared in Installed apps.

Run `Install.cmd` from File Explorer for a real user installation. Do not treat a successful registry query inside a build or agent environment as proof that Windows sees the installation. Verify the actual Installed apps view, including name, icon, version, and size. Check that the installed executable matches the published build and that the background process restarts.

An update exits the resident app. Preserve any open screenshots or unsaved editor work before applying it. The `--exit` switch signals the current user's running instance, so avoid using it casually in benchmarks or process probes.

### Packaging constraints

Self-contained builds bundle .NET and compress managed assemblies. Framework-dependent builds are smaller but require the matching .NET 8 Windows Desktop Runtime. WPF trimming is unsupported; do not enable trimming simply to reduce size. The camera cursor must remain embedded rather than loaded from a loose asset file.

The scripts accept `win-x64` and `win-arm64`. The recorded packaging checks used x64 on the development PC. Portable execution does not create an Installed apps entry or automatically enable startup. Copying the EXE to another computer does not transfer that user's shortcut preferences.

## Validation and current status

The OCR performance work completed with **85 passed, 0 failed**, including small multilingual samples and cancellation before a queued request starts native work. Auto now uses one English pass to choose the working size, reducing small-text work from fourteen passes to eight, then uses at most two independent engines on machines with at least four logical processors. Short unscaled snippets stay sequential. A shared request gate bounds active work across editors and panel toggles. `--ocr-benchmark` records phase timings and CPU/wall time; `--ocr-ui-benchmark` records dispatcher gaps and cached reopening dispatch. Three-call medians on the development PC reduced Auto file-list time from 6386 to 2209 ms and 1080p text from 13371 to 4877 ms. A separate UI run had a 57 ms maximum heartbeat gap and 0.8 ms cached reopening dispatch. Short snippets and manual recognition showed little benefit. See the README for all cases, limits, and commands. The x64 package was rebuilt and installed, its executable SHA256 matched the published build, and the updated background process was running.

The small-filename OCR fix completed with **83 passed, 0 failed** and process exit code 0, using workspace-local TEMP/TMP. Both manual and Auto recognition retry text smaller than 20 pixels at 2x for captures up to four million pixels, then map word bounds back to the original image. Layout selection now uses confidence-weighted alphanumeric coverage instead of mean confidence so a dates-only result does not outrank a complete file list. `Tests/OcrLayoutTests.cs` covers six small filenames beside folder icons and date/type columns, offsets, source coordinates, and image/panel selection. Live double-clicks selected both `BetterScreenShot-win-x64` and its `.zip` filename in the new fixture. Run `--ocr-file-list-demo` to repeat that check. The original capture behind the user's editor screenshot was unavailable; diagnostics used the supplied editor screenshot and a synthetic Explorer-style fixture. The x64 installer package was rebuilt and installed, and the updated installed launcher was opened for testing.

The readability and performance review added incremental annotation rendering. `AnnotationDocument.Changed` reports the change kind and index, and `AnnotationSurface` updates the affected visual while keeping preview and selection visuals on top. Style changes redraw all annotation visuals. Gesture cancellation restores only the edited visual, and editor controls synchronize once per document notification. OCR assets are verified and copied through streams instead of full-resource memory buffers. The expanded suite completed with **80 passed, 0 failed**, including pixel comparisons through history/style changes and cache reuse, corruption repair, and failed replacement cleanup. The run used a workspace-local TEMP/TMP directory because the default temporary OCR cache produced access errors in the isolated environment. One runner process stayed alive after reporting all checks passed and was stopped; a confirmation run passed all 80 checks and exited with code 0. The cause of that intermittent shutdown hang remains unverified. The Release build succeeded; NuGet vulnerability checks could not reach nuget.org. A follow-up check of the current working tree also passed all 80 checks and exited with code 0. Its 300-annotation benchmark measured 0.052 ms and 13.7 KiB per edit, and 0.064 ms for undo plus redo. These are synthetic CPU measurements. A normal Windows-temp OCR smoke check also passed outside the isolated environment, and the fresh installer publish completed without warnings. See the README for the earlier before/after comparison.

The Tesseract addition has 76 passing regression checks, including recognition in English, Latvian, Lithuanian, Estonian, Swedish, Finnish, and Russian, dark backgrounds, blank images, cancellation, language selection, caching, word bounds/offsets, word and multiline selection, interrupted panel transitions, scrollbar paging/dragging in both themes, and editor lifetime. With the Select tool active, double-clicking an original image word starts recognition if needed, highlights the word, and selects it in the focused text panel for Ctrl+C. Selecting a passage in the panel highlights all overlapping image words and reveals selections outside the viewport. Highlights share the image transforms and do not enter exports. ScreenshotViewerWindow.TextSelection.cs owns draggable start/end handles, word snapping across lines, crossing, Escape cancellation, and zoom-independent handle sizing. Range drags update the focused text panel for Ctrl+C; closing the panel or changing tools clears the gesture and handles. TextSelectionTests.cs checks multiline expansion/shrinking, crossing/cancellation, handle sizing during zoom/pan, export exclusion, and panel cleanup. The panel slides open/closed over 200 ms, respecting Windows animation preferences, and has a themed 8-DIP scrollbar. Live double-click selection was also checked before extraction, in the panel, and after zooming. Editable annotations retain their existing input behavior. New editors default to Auto, which compares seven per-language passes and merges word candidates by confidence, dictionary hints, and overlapping image bounds. Manual language choices are in the initially collapsed Advanced options section. The Extract text button toggles the panel even during recognition and reuses cached results on reopen. The close button uses a centered vector X. Persistent selection instructions, privacy captions, bottom tool hints, and the annotation count are removed; recognition progress, empty results, and errors remain contextual. OCR supports x64 processes and requires the Visual C++ x64 runtime. The pinned NuGet package supplies only native libraries; the app does not reference its managed wrapper. Models and native libraries are embedded, then verified and extracted into a build-specific temporary cache on first use. See [text extraction](README.md#text-extraction) and [third-party notices](THIRD-PARTY-NOTICES.md).

Before the OCR addition, the source baseline completed with **60 passed, 0 failed**. It covered capture geometry and overlays, background operation, shortcut registration/recording, annotation history and rendering, copy behavior, exports, and toolbar layout. Documentation fixtures were subsequently built and captured successfully; all three committed screenshot files decoded and their README paths resolved.

Portable x64 builds were launched after copying only the EXE into empty folders. The self-contained probe used nonexistent .NET search paths and a fresh extraction cache. Those launch timings measured startup and secondary-instance dispatch, not full editor readiness or screenshot latency. Sizes and measurements are recorded in the README.

Physical mixed-DPI monitor checks and a transfer to a separate PC remain manual release checks. ARM64 execution has not been validated here. The uninstall launch mode was exercised with a harmless fixture script; a destructive full uninstall was not performed as part of that verification.

There is no unfinished feature request recorded at this baseline. Check the working tree and current user request before planning further work. Do not infer a request to commit, push, reinstall, or uninstall from this document alone.

## Suggested skills

These are optional agent workflows, not application dependencies:

- `karpathy-guidelines` for focused changes with clear verification criteria.
- `computer-use` for observing and testing native Windows behavior. Follow the current tool instructions and stop app input when the user interrupts it.
- `code-simplifier` for scoped cleanup that preserves behavior.
- `unslop` for plain documentation and release notes.
- `handoff` when updating the project context for a later session. Keep this project guide linked to the detailed documentation rather than duplicating it.
