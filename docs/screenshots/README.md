# App screenshots

These PNGs render the current WPF window content with sample images and annotations. Each screen has a light and dark version. The fixtures use no personal desktop content and do not change saved theme or shortcut preferences.

Regenerate all ten images with:

```powershell
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --readme-generate
```

| Pair | Shows |
| --- | --- |
| `launcher-{light,dark}.png` | Full-screen capture, area capture, opening an image, and the sun/moon theme switch |
| `preview-{light,dark}.png` | Screenshot preview and its Copy, Save, Edit, and Discard actions |
| `editor-{light,dark}.png` | Floating annotation toolbar, contextual controls, and editable marks |
| `text-recognition-{light,dark}.png` | Extracted text panel and matching image-word selection |
| `text-selection-{light,dark}.png` | A selected passage spanning two lines, with matching panel text |

The root README embeds these files with relative paths so they display on GitHub. The screenshots show the app content without the Windows title bar; title bar appearance depends on the user's Windows theme and version.
