const concepts = {
  quiet: {
    number: '01 / 04',
    title: 'Quiet utility',
    description: 'A restrained Windows utility. Two clear capture actions lead the compact window; the editor groups related tools and lets the image set the visual weight.',
    points: [
      'Action rows use spacing and hover fill in place of outlines.',
      'Editor tools have labels and one selected state.',
      'Image shadow provides the only strong edge.'
    ],
    tradeoff: 'The safest path from the current WPF UI, though less visually distinct.'
  },
  studio: {
    number: '02 / 04',
    title: 'Warm studio',
    description: 'A more editorial take on capture. Warm surfaces, restrained type, and one blue accent give the app a recognizable character without filling it with icons.',
    points: [
      'Capture choices read as a short list with fine dividers.',
      'Typography carries hierarchy; tool groups use plain words.',
      'Warm light and dark palettes keep the canvas neutral.'
    ],
    tradeoff: 'Grouped editing tools may take an extra click. Keyboard shortcuts remain important.'
  },
  canvas: {
    number: '03 / 04',
    title: 'Focus canvas',
    description: 'The screenshot gets most of the editor. A compact dock pairs familiar icons with labels, while details and text extraction come forward only when needed.',
    points: [
      'Two compact capture tiles remove the row chrome.',
      'A small dock pairs icons with labels for frequent tools.',
      'The OCR rail is easy to close when space matters.'
    ],
    tradeoff: 'The dock must avoid covering small captures and needs strong keyboard and tooltip support.'
  },
  sketch: {
    number: '04 / 04',
    title: 'Sketch studio',
    description: 'An Excalidraw-inspired variation on the canvas-first idea. A centered icon tray and contextual stroke controls bring a drawing-tool feel to screenshot markup.',
    points: [
      'Six familiar annotation icons sit in one floating tray.',
      'Stroke settings appear for drawing tools and clear with Select.',
      'Copy, Save, and text extraction stay explicit in the header.'
    ],
    tradeoff: 'Icon-only tools need excellent tooltips and shortcuts. The properties tray needs room at narrow editor sizes.'
  }
};

const windowEl = document.querySelector('#app-window');
const stage = document.querySelector('#stage');
const panel = document.querySelector('#concept-panel');
const editor = document.querySelector('.editor-view');
const toast = document.querySelector('#demo-toast');
let toastTimer;

document.querySelectorAll('.icon').forEach(icon => icon.setAttribute('aria-hidden', 'true'));

function setConcept(name) {
  if (!concepts[name]) return;
  windowEl.dataset.concept = name;
  const details = concepts[name];
  document.querySelector('#note-number').textContent = details.number;
  document.querySelector('#note-title').textContent = details.title;
  document.querySelector('#note-description').textContent = details.description;
  document.querySelector('#note-tradeoff').textContent = details.tradeoff;
  document.querySelector('#note-points').replaceChildren(...details.points.map(point => {
    const item = document.createElement('li');
    item.textContent = point;
    return item;
  }));
  document.querySelector('.launcher-heading h2').textContent = name === 'sketch' ? 'Capture, then draw.' : 'What would you like to capture?';
  setOcrOpen(name !== 'sketch');
  if (name === 'sketch') setSketchTool('arrow');
  document.querySelectorAll('[data-concept].concept-tab').forEach(button => {
    const active = button.dataset.concept === name;
    button.classList.toggle('is-active', active);
    button.setAttribute('aria-selected', String(active));
    button.tabIndex = active ? 0 : -1;
    if (active) panel.setAttribute('aria-labelledby', button.id);
  });
}

function setScreen(name) {
  windowEl.dataset.screen = name;
  document.querySelectorAll('.segmented button[data-screen]').forEach(button => {
    const active = button.dataset.screen === name;
    button.classList.toggle('is-active', active);
    button.setAttribute('aria-pressed', String(active));
  });
}

function setTheme(name) {
  windowEl.dataset.theme = name;
  stage.classList.toggle('is-dark', name === 'dark');
  document.querySelectorAll('.segmented button[data-theme]').forEach(button => {
    const active = button.dataset.theme === name;
    button.classList.toggle('is-active', active);
    button.setAttribute('aria-pressed', String(active));
  });
}

function showToast(message) {
  toast.textContent = message;
  toast.classList.add('is-visible');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toast.classList.remove('is-visible'), 2200);
}

document.querySelectorAll('.concept-tab').forEach(button => {
  button.addEventListener('click', () => setConcept(button.dataset.concept));
  button.addEventListener('keydown', event => {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const tabs = [...document.querySelectorAll('.concept-tab')];
    const current = tabs.indexOf(button);
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (current + (event.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length;
    setConcept(tabs[next].dataset.concept);
    tabs[next].focus();
  });
});

document.querySelectorAll('.segmented [data-screen]').forEach(button => button.addEventListener('click', () => setScreen(button.dataset.screen)));
document.querySelectorAll('.segmented [data-theme]').forEach(button => button.addEventListener('click', () => setTheme(button.dataset.theme)));

const ocrToggle = document.querySelector('#ocr-toggle');
const headerOcrToggle = document.querySelector('#header-ocr-toggle');
function setOcrOpen(open) {
  editor.classList.toggle('ocr-closed', !open);
  ocrToggle.setAttribute('aria-expanded', String(open));
  headerOcrToggle.setAttribute('aria-expanded', String(open));
}
ocrToggle.addEventListener('click', () => setOcrOpen(editor.classList.contains('ocr-closed')));
headerOcrToggle.addEventListener('click', () => setOcrOpen(editor.classList.contains('ocr-closed')));
document.querySelector('#ocr-close').addEventListener('click', () => setOcrOpen(false));

function setSketchTool(name) {
  document.querySelectorAll('.sketch-tool').forEach(button => {
    const active = button.dataset.sketchTool === name;
    button.classList.toggle('is-selected', active);
    button.setAttribute('aria-pressed', String(active));
  });
  editor.classList.toggle('has-style-tool', !['select', 'text'].includes(name));
  document.querySelector('#sketch-style-title').textContent = `${name[0].toUpperCase()}${name.slice(1)} style`;
}

document.querySelectorAll('.sketch-tool').forEach(button => button.addEventListener('click', () => setSketchTool(button.dataset.sketchTool)));
document.querySelectorAll('.color-options button, .width-options button').forEach(button => button.addEventListener('click', () => {
  const group = button.parentElement;
  group.querySelectorAll('button').forEach(option => {
    option.classList.remove('is-selected');
    option.setAttribute('aria-pressed', 'false');
  });
  button.classList.add('is-selected');
  button.setAttribute('aria-pressed', 'true');
  if (group.classList.contains('color-options')) document.querySelector('.annotation').style.stroke = button.style.getPropertyValue('--swatch');
}));

document.querySelectorAll('.tool').forEach(button => button.addEventListener('click', () => {
  document.querySelectorAll('.tool').forEach(tool => {
    tool.classList.remove('is-selected');
    tool.setAttribute('aria-pressed', 'false');
  });
  button.classList.add('is-selected');
  button.setAttribute('aria-pressed', 'true');
}));

document.querySelectorAll('[data-demo-action]').forEach(button => button.addEventListener('click', () => {
  const action = button.dataset.demoAction;
  if (action === 'Capture Full Screen' || action === 'Select Area') {
    setScreen('preview');
    showToast('Capture preview concept');
  } else if (action === 'Open editor') {
    setScreen('editor');
  } else if (!button.classList.contains('tool')) {
    showToast(`${action} · visual example`);
  }
}));

setConcept('canvas');
