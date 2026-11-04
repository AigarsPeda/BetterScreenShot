# App screenshots

These PNGs render the current WPF window content with sample images and annotations. The documentation renderer adds a matching title bar to the launcher, editor, and recording viewer, and leaves pixels outside each rounded window transparent. The previews are transparent cutouts of the actual toast. Recording screenshots set sample timer and playback values without capturing the desktop. Each screen has a light and dark version. The fixtures use no personal desktop content and do not change saved theme or shortcut preferences.

Regenerate all eighteen images with:

```powershell
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --readme-generate
```

| Pair | Shows |
| --- | --- |
| `launcher-{light,dark}.png` | Full-screen capture, area capture, opening an image, and the sun/moon theme switch |
| `preview-{light,dark}.png` | Screenshot preview and its Copy, Save, Edit, and Discard actions |
| `record-options-{light,dark}.png` | Floating video and GIF choices with the video quality picker |
| `recording-active-{light,dark}.png` | Red recording indicator, elapsed time, and Stop button |
| `recording-preview-{light,dark}.png` | Recording preview with View, Copy, Save, and Discard actions |
| `recording-viewer-{light,dark}.png` | Read-only video viewer with playback controls, Copy, and Save |
| `editor-{light,dark}.png` | Floating annotation toolbar, contextual controls, and editable marks |
| `text-recognition-{light,dark}.png` | Extracted text panel and matching image-word selection |
| `text-selection-{light,dark}.png` | A selected passage spanning two lines, with matching panel text |

The root README embeds these files with relative paths so they display on GitHub. Windows may draw the live title bar differently depending on the user's version and settings.
