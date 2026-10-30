# App screenshots

These JPEGs are captures of the actual Windows app windows. The sample document and annotations come from documentation fixtures; no personal desktop content or saved user preferences are included.

To refresh a screenshot, build the regression runner and open the corresponding fixture:

```powershell
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --readme-editor
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --readme-capture
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --readme-preview
```

Capture just the app window and replace `editor.jpg`, `capture.jpg`, or `preview.jpg`. Hover over the preview to reveal its actions before capturing it. Close each fixture window when finished. The fixture uses its own temporary sample images and shortcut store, leaving the installed app's settings intact.

The root README uses relative image paths so the screenshots display on GitHub alongside the source code.
