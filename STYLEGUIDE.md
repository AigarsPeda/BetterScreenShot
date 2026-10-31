# BetterScreenShot style guide

## Direction

- A compact Windows utility with the calm canvas and drawing-tool character of the Sketch studio concept.
- The captured image is the visual anchor. Controls use one consistent icon language, quiet surfaces, and a purple active state.
- Group with spacing, one shared surface, or a fine divider. Use a border for focus or selection when it communicates state.
- Keep text functional. Do not add decorative headings, slogans, size captions, or explanatory copy to the launcher, editor header, or preview.

## Launcher

- Keep the 520 × 320 default window and the 380 × 290 minimum.
- Show the two primary capture actions immediately. Their rows have equal height and share a single raised surface with one divider.
- Give each action a small outline icon, a clear label, and its shortcut hint. Do not add icon tiles or trailing chevrons.
- Place status and the keyboard-shortcuts link in a quiet footer. Status color can change with meaning, but the area should not become a permanent badge.
- Hover uses a soft surface fill. Keyboard focus has a visible purple outline.

## Editor

- Keep Copy, Save, and Extract text visible and named. Place zoom and undo/redo where they remain reachable at narrow widths.
- Put the nine annotation tools in a compact centered icon dock. The selected tool has a filled purple state. Each icon has a tooltip, accessible name, and existing keyboard shortcut.
- Show color, stroke, text size, Sketch, and delete controls when the active tool or selected annotation needs them. Do not leave a dense full-width options bar visible at rest.
- Float the dock and contextual controls over the upper image viewport. They must not consume a layout row. Keep hit testing within the controls so drawing gestures work elsewhere on the image. At 620 × 450, the image and essential commands must remain usable.
- Let canvas tone and a slight image shadow separate the screenshot from the workspace. Avoid an outline around every layer.
- The extracted-text panel uses the same surface and theme roles. Recognition status appears only when needed.

## Capture preview

- Show the captured thumbnail and immediate Copy and Save actions. Keep edit and discard available with accessible names.
- Do not add a title, timestamp, dimensions, or a “screenshot ready” caption.
- Use a readable light or dark surface based on the Windows app theme. Preview actions must remain legible over light and dark images.

## Color and icons

- Light: paper `#F7F7F3`, white raised surface, ink `#252735`, purple `#625FC6`.
- Dark: near-black `#171922`, raised surface `#252833`, light ink, lavender `#B9B5FF`.
- Use semantic brush resources for text, canvas, surface, focus, accent, selection, status, and errors. Do not invert the light palette mechanically.
- Use a small consistent Lucide-style outline set. Keep common actions labeled; an icon-only tool must have a tooltip and accessible name.
- Check text and selected-state contrast in both themes. Do not rely on purple alone to communicate which tool is active.

## Interaction and verification

- Preserve capture, edit, OCR, copy, save, shortcut, and annotation behavior while changing visual layout.
- Honor Windows light/dark changes while windows are open. Respect keyboard focus and reduced-motion settings.
- Verify launcher status wrapping, toolbar fit at 620/1100/1280 widths, image viewport height, OCR drawer opening, and both theme palettes before release.
