# BetterScreenShot style guide

## Direction

- A compact Windows utility with the calm canvas and drawing-tool character of the Sketch studio concept.
- The captured image is the visual anchor. Controls use one consistent icon language, quiet surfaces, and a purple active state.
- Group with spacing, one shared surface, or a fine divider. Use a border for focus or selection when it communicates state.
- Keep text functional. Do not add decorative headings, slogans, size captions, or explanatory copy to the launcher, editor header, or preview.

## Launcher

- Keep the 520 × 548 default window and the 380 × 518 minimum.
- Show full screen, area, scrolling capture, and area recording immediately. Their rows have equal height and share a single raised surface with dividers.
- Give each action a small outline icon, a clear label, and its shortcut hint. Do not add icon tiles or trailing chevrons.
- Place status and the keyboard-shortcuts link in a quiet footer. Status color can change with meaning, but the area should not become a permanent badge.
- Hover uses a soft surface fill. Keyboard focus has a visible purple outline.
- Place scrolling capture controls in a draggable floating toolbar beside the selected area, using the viewer's theme colors.
- Align the scrolling and recording toolbars with the same three-column layout: title, option label, and dropdown above equal-width action buttons.
- Keep a click-through accent outline around the selected area during scrolling capture, excluded from saved pixels.
- Use the same outline and floating toolbar treatment for area recording. Offer Video and GIF after area selection. Replace the selected format button with an equal-width Stop button so the other controls stay in place. Show a red REC dot only while recording is active.

## Editor

- Keep Extract text, Copy, and Save visible as three separated buttons. Copy and Save use icons with accessible names and shortcut tooltips. Place zoom and undo/redo where they remain reachable at narrow widths.
- Toolbar icon buttons share a 40 × 40 DIP size, including the drawing tools, More, Properties, Copy, Save, zoom, and history. Top-row surfaces align along their top and bottom edges. Preserve square dimensions when the tool dock wraps to a second row.
- Put the ten tools in a compact centered icon dock. The selected tool has a filled purple state without an active border. Keyboard focus uses a separate visible outline. Each icon has a tooltip, accessible name, and existing keyboard shortcut.
- Show color, stroke, text size, Sketch, and delete controls when the active tool or selected annotation needs them. Do not leave a dense full-width options bar visible at rest.
- Use equal-sided 24-DIP color swatches. Tooltips use rounded application surfaces, themed text, and a restrained shadow. More and Properties have distinct button surfaces and a small gap.
- Place the current-color square after the five presets, separated by a small divider. It opens a themed floating picker beside the properties card with a palette, shades, and validated hex input. Leave an 18-DIP gap above the zoom/history group.
- New arrows use a constant visible stroke, arrowhead, and badge size at creation. Placed annotations scale with the image without changing their stored dimensions.
- Unsaved-edit dialogs separate the red Discard button on the left from Keep editing and the primary Save copy action on the right. Pair labels with small outline icons and preserve a clear keyboard focus indicator.
- Align the tool dock and export actions in one row when space permits; move tools to a second row at narrow widths. Put zoom and history controls at the bottom left.
- Float contextual properties over the left side of the image for the available height between the top and bottom controls. Group labeled properties at the top, keep Delete selection at the bottom, and scroll the middle when needed. The canvas fills the window behind every tool surface. Do not reserve blank rows or a sidebar gutter for menus, or move/rescale the image when properties open. Compact layouts open the panel on demand. At 620 × 450, the image and essential commands must remain usable.
- Let canvas tone and a slight image shadow separate the screenshot from the workspace. Avoid an outline around every layer.
- The extracted-text panel uses the same surface and theme roles. Recognition status appears only when needed. Compact layouts replace the canvas with text results and provide Back to image; empty/error states offer Retry.

## Capture preview

- Show the captured thumbnail and immediate Copy and Save actions. Keep edit and discard available with accessible names.
- End the toast below its action buttons with normal outer padding. Do not reserve a text/status row underneath; success feedback belongs inside the buttons.
- Recording previews show Copy, Save, and Discard without an editor action.
- Preserve pending previews when another capture arrives, with previous/next navigation. Show copy/save confirmation inline. Closing a capture notification dismisses it without a confirmation; retain the separate unsaved-editor close guard.
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
- Use small, eased slide/fade transitions for feedback. Copy and save confirmations hold still for three seconds inside fixed-size buttons before restoring the original content. Required dialogs enter gently and stay until the user responds.
- Prompt on editor close only for actual content changes. Untouched captures and undoing back to opening/saved content must close directly.
- Verify launcher status wrapping, toolbar fit at 620/1100/1280 widths, image viewport height, OCR drawer opening, and both theme palettes before release.
