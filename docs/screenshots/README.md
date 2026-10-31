# App screenshots

These JPEGs are captures of the actual Windows app windows. The sample document and annotations come from documentation fixtures; no personal desktop content or saved user preferences are included.

Use English and Latvian sample text only.

To refresh a screenshot, build the regression runner and open the corresponding fixture:

```powershell
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --readme-editor
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --readme-capture
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --readme-preview

dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --readme-text-recognition

dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --readme-text-selection
```

Capture just the app window and replace the corresponding image. Hover over the preview to reveal its actions before capturing it. For the OCR fixtures, wait for recognition and the panel animation to finish; the fixture selects the sample passage automatically. Close each fixture window when finished. The fixture uses its own temporary sample images and shortcut store, leaving the installed app's settings intact.

| Image | Fixture | Shows |
| --- | --- | --- |
| `editor.jpg` | `--readme-editor` | Annotation tools, compact color menu, and an editable curved arrow |
| `capture.jpg` | `--readme-capture` | Full-screen and area capture launcher |
| `preview.jpg` | `--readme-preview` | Temporary capture preview and quick actions |
| `text-recognition.jpg` | `--readme-text-recognition` | Local text extraction and synchronized image/panel selection |
| `text-selection.jpg` | `--readme-text-selection` | A selection across two lines, blue handles, and English/Latvian sample text |

The root README uses relative image paths so the screenshots display on GitHub alongside the source code.
