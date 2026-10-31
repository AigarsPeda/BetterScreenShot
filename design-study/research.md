# BetterScreenShot design study

31 October 2026. This study compares four visual directions for the existing Windows desktop app. Open [the interactive examples](index.html) to switch between capture, editor, and preview screens in light and dark themes. Concepts 03 and 04 are the current comparison. These are HTML prototypes, not changes to the WPF application.

## What the current app needs

The 520 × 320 launcher uses two bordered action cards, two colored icon tiles, two chevrons, a shortcut link, and a full-width status badge. The 1100 × 780 editor repeats borders around the toolbar, commands, image area, OCR panel, and text box. Its tool row exposes many similarly weighted actions at once. The screenshot itself has less visual priority than the controls around it. See the current [capture](../docs/screenshots/capture.jpg), [editor](../docs/screenshots/editor.jpg), and [text recognition](../docs/screenshots/text-recognition.jpg) screenshots.

The app already detects the Windows light/dark preference for its launcher and editor. The shortcut dialog reads the theme when it opens. The preview toast remains a fixed dark translucent overlay. Colors and icon paths live in several XAML and C# locations, so a production redesign would need shared semantic color names and a consistent icon source. The current [style guide](../STYLEGUIDE.md) specifies the bordered launcher cards and icon tiles; it should be revised only after a direction is chosen.

## Evidence and design implications

| Source | Relevant guidance | What it means here |
| --- | --- | --- |
| [Apple Human Interface Guidelines: layout](https://developer.apple.com/design/human-interface-guidelines/layout) | Use spacing, alignment, backgrounds, and separators to establish groups and hierarchy. | Remove most enclosing borders. Keep a divider where it helps scan a short list, and let the captured image have the strongest edge. |
| [Apple HIG: buttons](https://developer.apple.com/design/human-interface-guidelines/buttons) and [designing for macOS](https://developer.apple.com/design/human-interface-guidelines/designing-for-macos/) | Make actions recognizable and support desktop interaction conventions. | Keep capture, copy, and save plainly named; preserve shortcuts, focus states, and resizable editor layouts. Apple is a design reference, while Windows conventions still govern this app. |
| [Apple HIG: Dark Mode](https://developer.apple.com/design/human-interface-guidelines/dark-mode) and [color](https://developer.apple.com/design/human-interface-guidelines/color) | Adapt color roles for each appearance rather than simply inverting colors. Check contrast and system accessibility settings. | Design separate light and dark surface, text, muted text, canvas, selection, and accent values. Let Windows theme remain the default. |
| [Apple HIG: materials](https://developer.apple.com/design/human-interface-guidelines/materials) | Reserve prominent materials for navigation and controls; avoid letting effects compete with content. | A solid, calm editor canvas fits better than putting frosted glass on every panel. This is a transfer of the hierarchy principle, not a proposal to imitate macOS materials in WPF. |
| [Microsoft: Windows app visual guidance](https://learn.microsoft.com/en-us/windows/apps/get-started/best-practices), [navigation](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics), and [content spacing](https://learn.microsoft.com/en-us/windows/apps/design/basics/content-basics) | Keep desktop navigation clear, layouts adaptable, and light/dark treatments comfortable. | Prefer simple labeled commands and spacing that survives the editor's 620 × 450 minimum size. Respect Windows focus and keyboard behavior. |
| [Linear's 2026 design refresh](https://linear.app/now/behind-the-latest-design-refresh) | Linear reduced icon size and use, removed colored icon backgrounds, and softened borders while preserving information hierarchy. | The launcher can lose its icon tiles and chevrons without losing its two primary actions. |
| [Lucide accessibility](https://lucide.dev/how-to/accessibility), [icon sizing](https://lucide.dev/guide/react/basics/sizing), and [stroke width](https://lucide.dev/guide/react/basics/stroke-width) | Icons support labels; icon-only controls need accessible names and comfortable targets. The set uses a consistent outline style. | Use a small Lucide subset at 16–20 px in controls, with visible text for less obvious commands. Provide tooltips and accessible names where an icon stands alone. |
| [W3C WCAG 2.2 contrast guidance](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum) | Normal text needs at least 4.5:1 contrast for the AA criterion, with defined exceptions. | Check muted text and shortcut hints in both palettes; do not rely on color alone for selected tools or status. |

### Inspiration, not a usability standard

The requested [Awwwards App Style gallery](https://www.awwwards.com/inspiration_search/App%20Style/) includes app marketing pages and product UI. [Linea Prompt](https://www.awwwards.com/sites/linea-prompt) suggests a restrained command palette, while [Invoko](https://www.awwwards.com/sites/invoko-ai-helper-on-your-mac) is a clean, minimal reference with a pale background and controlled accent. Their typography and restraint are useful moodboard material. They do not settle how a Windows screenshot tool should handle focus, accessibility, or editing density.

Two directly relevant screenshot products are [CleanShot X](https://cleanshot.com/features), whose quick access overlay presents immediate copy/save/annotate actions, and [Shottr](https://shottr.cc/kb/startguide), whose guide describes a preview with copy/save/OCR and a contextual toolbar for a selected annotation. These support keeping the preview decision focused and showing detailed editing options at the moment they are needed.

### Excalidraw follow-up

The live [Excalidraw editor](https://excalidraw.com/) uses a centered floating icon tray, a filled selected-tool state, an open canvas, and separate zoom/undo controls near the bottom. Its style panel appears for drawing or selected elements and clears when the selection does. Tool names and shortcut keys are exposed through labels/tooltips. Its menu offers Light, Dark, and System theme choices. The [official component documentation](https://docs.excalidraw.com/docs/%40excalidraw/excalidraw/api/children-components/sidebar) describes the contextual side panel, and its [project README](https://github.com/excalidraw/excalidraw/blob/master/README.md?plain=1) describes the hand-drawn character and dark mode. These are observations of Excalidraw, not usability rules for this app.

Concept 04 borrows the toolbar and contextual-control patterns. Its screenshot remains a normal captured image. The app's existing Sketch mode is the right place for a hand-drawn cue; altering captured pixels as part of the UI style would misrepresent the screenshot. Copy and Save remain visible because they are frequent end actions in BetterScreenShot.

## The four examples

| Direction | Capture window | Editor and preview | Main cost |
| --- | --- | --- | --- |
| **Quiet utility** | Two equal text-first rows; no card outlines, icon tiles, or chevrons. | Small labeled tool groups and a single active fill. The screenshot gains an unobtrusive shadow. Preview has direct copy/save. | Closest to the current UI and easiest to implement, but less distinctive. |
| **Warm studio** | A typographic heading and short divided action list on warm paper tones. | Words replace most tool icons; the blue accent is reserved for the most important action. | Grouped tools can add a click; density at narrow widths needs attention. |
| **Focus canvas · 03** | Two compact capture tiles with a clear primary action. | A floating dock pairs Lucide-style icons and text labels; the OCR rail can close. | Dock placement needs careful testing at the minimum editor size. |
| **Sketch studio · 04** | Two icon-led actions share one quiet surface. | A centered icon tray, bottom zoom/undo controls, and a contextual stroke panel draw on Excalidraw. Copy, Save, and OCR stay named. | Icon-only tools need excellent tooltips and shortcuts; the style panel needs room. |

### Compare 03 and 04

| Decision | 03 Focus canvas | 04 Sketch studio |
| --- | --- | --- |
| Tool discovery | Icon plus visible label for each frequent tool | Icon tray with tooltips and shortcut letters |
| Annotation options | Hidden until invoked from a tool/menu | Small contextual stroke panel for drawing tools |
| Canvas space | Broad canvas with a bottom dock | Broad canvas with a centered top tray and bottom zoom controls |
| Visual character | Cool, precise screenshot utility | Looser drawing-tool feel with a restrained lilac accent |
| WPF implementation risk | Moderate | Higher: contextual panel placement and icon-only keyboard support |

The user's preferred direction is **03 Focus canvas**. The new **04 Sketch studio** tests whether Excalidraw's tool behavior and more expressive tone improve it. I would retain 03's labels for unfamiliar commands even if 04's icon tray is chosen, since Lucide's own accessibility guidance favors visible text where an icon could be ambiguous.

The WPF editor's minimum window is 620 × 450. Its existing annotation gestures use the image viewport, and the current toolbar tests expect tools to remain reachable at narrow widths. In production, the floating tray should reserve enough vertical space or move at small sizes so it never blocks drawing on the captured image. A contextual style panel should close when selection clears, while the existing tool hotkeys and OCR rail behavior remain available.

## Implementation notes for a later WPF change

1. Put semantic palette resources in one place: application background, raised surface, canvas, primary and secondary text, hairline, accent, selected control, focus, success, and error. Define both themes explicitly and follow Windows by default. Decide separately whether to add a user override.
2. Keep the capture actions equal in size and expose their shortcut hints. Use hover fill and a visible focus treatment. Keep status feedback readable without a permanent full-width pill.
3. Group editor modes under labels. Preserve the existing V/H/P/M/L/A/R/O/T shortcuts and make every tool reachable by keyboard. Test the 620 × 450 minimum window, zoom, OCR open/closed, text selection, and annotation selection.
4. Standardize the small Lucide set. The repo already has Lucide SVG references, but most runtime icons are hand-drawn XAML/C# paths. Convert or recreate the chosen paths consistently in WPF and record the [ISC license](https://lucide.dev/license) in the project's third-party notices when shipping them.
5. Give the toast a corresponding light treatment or deliberately keep a theme-neutral overlay, then verify legibility over both light and dark captured images. Check focus, text contrast, selected states, Windows high contrast, and reduced motion/animation preferences.

The examples contain no capture or editing implementation. They are meant to make the design choice concrete before changing application code.
