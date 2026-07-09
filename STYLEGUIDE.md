# Better ScreenShot Style Guide

## Design Direction
- Compact Windows desktop utility.
- Fast to scan, low-friction, and visually calm.
- Two equal primary actions should be the main focus of the launcher screen.
- Avoid decorative chrome that steals space from the actions.

## Main Window
- The launcher window should be compact and task-first.
- Do not show a large in-window title on the launcher screen.
- The standard window title can be empty when the UI itself is self-explanatory.
- The two primary actions should appear immediately without extra introductory copy.

## Layout
- Outer page margin: 16 px.
- Action cards should be stacked vertically.
- Both primary action cards must always have the same height.
- Status bar should sit below the actions with a small top gap.
- Prefer real responsive layout over scaling the whole UI with a Viewbox.
- Text and icons must remain visible at common window sizes and display scales.

## Action Card Sizing
- Action card height: 64 px.
- Card corner radius: 18 px.
- Card inner padding: 10 px.
- Icon tile size: 36 x 36 px.
- Icon tile corner radius: 10 px.
- Gap between icon and label: 10 px.
- Right arrow column: 16 px.
- Vertical gap between cards: 10 px.

## Typography
- Action labels: 15 px, semi-bold.
- Status text: 13 px, semi-bold.
- Use short, direct labels.
- Prefer one-line labels when possible.

## Color
- Page background: soft cool gray-blue.
- Card surface: white.
- Card border: light cool gray.
- Full screen action accent: blue.
- Select area action accent: green.
- Status colors:
  - Neutral for idle.
  - Blue for guidance and temporary state.
  - Green for success.
  - Red for errors.

## Iconography
- Use simple outline icons with rounded caps and joins.
- Icons should communicate quickly at small sizes.
- Use a monitor metaphor for full-screen capture.
- Use a scan/corner metaphor for area capture.
- Avoid overly detailed icons in primary launcher actions.
- Keep icon treatment visually balanced with the text, not larger than the text block demands.

## Responsiveness
- Use standard WPF layout containers first.
- Avoid scaling the entire launcher UI as a single visual block.
- Do not let icons or labels clip at smaller heights.
- If space becomes tight, reduce padding before reducing readability.
- Ellipsis is acceptable for long text, but clipping is not.

## Interaction
- Action cards should highlight lightly on hover.
- Pressed state should darken subtly.
- Cursor should always indicate clickability on action cards.
- Full-screen capture mode should use a distinct capture cursor.

## Status Messaging
- Keep status messages short and action-oriented.
- Preferred examples:
  - Ready to capture.
  - Choose a monitor to capture.
  - Drag to select the area you want.
  - Screenshot captured. Use the popup to save, copy, or discard it.
- Avoid technical implementation details in launcher messages.

## Future Screens
- Reuse the same radius, spacing, and border rhythm.
- Popup and viewer windows should feel like the same product family.
- New primary actions should match the same action card height and spacing unless there is a strong reason not to.
