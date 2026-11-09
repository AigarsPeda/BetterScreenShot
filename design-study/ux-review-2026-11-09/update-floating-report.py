from pathlib import Path

base = Path(__file__).parent
report = base / 'index.html'
text = report.read_text(encoding='utf-8-sig')
changes = {
    'Proposal · three review passes': 'Earlier proposal · three review passes',
    'The final proposal scores <strong>98.05/100</strong> on the rubric below.': 'The earlier proposal scored <strong>98.05/100</strong> on the rubric below. User review then identified excessive space occupied by three stacked rows; that assessment was too optimistic about density. The current floating-controls revision supersedes that layout. Its checks are documented below; it has not received a new usability score.',
    'Move a named Extract text button into the command row.': 'Keep a named Extract text button in the upper-right action group.',
    "Give commands and tools a protected strip above the image. Hide irrelevant properties for Select and Pan. This intentionally revises the style guide's floating-dock rule. Validate visible canvas space before accepting the change.": 'Use a compact floating tool dock at the top and a contextual properties card on the left. Fit the image beside the visible card at wide sizes. At 760 DIP and below, collapse properties behind a button; the temporary popover may cover part of the image and must be easy to dismiss. Hide properties for Select and Pan.',
    'evidence/proposal-editor-light.png': 'evidence/floating-editor-light.png',
    'Proposed editor with named actions and tools above a protected image workspace': 'Proposed editor with floating tool dock, left properties card, and corner controls',
    'Proposal. Common actions stay named, the tool strip stays outside image coordinates, and status appears below the workspace.': 'Revised proposal. A floating tool dock and left properties card replace the three permanent rows. Export actions remain named in the upper-right corner.',
    'The editor has three clear levels: document actions, annotation tools, and contextual properties.': 'Following the requested <a href="https://excalidraw.com/">Excalidraw</a> arrangement, the editor separates controls into small floating groups: drawing tools above, contextual properties on the left, export actions at the upper right, and zoom with undo/redo at the lower left. Properties expose existing capabilities only: stroke color, width, Sketch, and text size. Select and Pan do not show irrelevant fields.',
    'Replace the floating-tool requirement with a protected image workspace.': 'Retain floating tools, add a left contextual properties card, and define image-fit bounds that avoid the card at wide sizes. Compact properties open temporarily from a button and close with Escape or their close control, restoring focus.',
    'Archived final-pass source': 'Archived third-pass source (layout superseded)',
    'The proposal reaches the requested 98-point review threshold.': 'The earlier assessment reached the requested 98-point threshold, but user feedback exposed a density issue that this rubric missed. Treat these values as historical judgments, not validation of the new layout.',
    'Move tools into reserved space and move zoom to the status row. Recalculate the image viewport rather than overlaying controls on it.': 'Use a floating top tool dock, left properties card, and lower-left zoom and undo groups. Recalculate image-fit bounds around the visible wide-layout card. Compact properties must toggle without changing annotation state; Escape closes the popover and returns focus.',
    'The final prototype passed 106 layout/state combinations and 14 interaction checks with no detected horizontal overflow or script errors. At 620 pixels wide, image mode measured 441 pixels high and text mode measured 415, both within the 450-pixel target.': 'The revised prototype passed 106 layout/state combinations and 14 existing interaction checks with no detected horizontal overflow or script errors. The floating-controls suite also passed 36 width/theme/state cases and seven interaction checks, including property selection, contextual visibility, compact disclosure, and focus restoration. Both image and text mode measure 620 × 450 pixels at the native minimum-size target. The first floating-layout check caught image overflow and a properties/footer collision; both were corrected and the suite rerun. See <a href="evidence/floating-checks.json">floating-checks.json</a> and <a href="verify-floating.cjs">verify-floating.cjs</a>.',
}
for old, new in changes.items():
    if old not in text:
        raise ValueError(f'Missing expected text: {old}')
    text = text.replace(old, new)
marker = '<section id="score">'
revision = '''<section><h2>Revision 4 · floating controls</h2><p>The user’s reference revealed a problem with the earlier proposal: three full-width control rows consumed too much vertical space. This revision replaces them with independent floating groups. It preserves named export actions and the earlier recovery flows.</p><div class="pair"><figure><img src="evidence/floating-editor-dark.png" alt="Dark editor with properties floating on the left"><figcaption>Wide layout: properties remain visible beside the image.</figcaption></figure><figure><img src="evidence/floating-properties-compact.png" alt="Compact editor with properties opened temporarily"><figcaption>Compact layout: properties are hidden by default and open on request. The card can temporarily cover image content.</figcaption></figure></div><p><a href="iterations/v4.html">Archived floating-controls source</a>. Visual inspection and geometry checks cover light and dark themes at widths from 320 to 1024 pixels. Native WPF implementation, accessibility testing, and real task performance remain unverified.</p></section>
'''
assert marker in text
text = text.replace(marker, revision + marker)
report.write_text(text, encoding='utf-8')
