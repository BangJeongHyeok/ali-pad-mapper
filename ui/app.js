'use strict';

/*
 * MacroPad 편집기 화면.
 * 상태(설정, 패드 기록, 연결 여부)는 전부 C#(EditorSession)이 갖고 있고,
 * 여기서는 그 상태를 그리기만 함. 바꿀 때는 C# 에 메시지를 보내고 답으로 온 새 상태로 다시 그림.
 *   - 앱 안(WebView2): window.chrome.webview.postMessage
 *   - 미리보기(tools/UiPreview): fetch('/api')
 */

// ─────────────────────────── 아이콘 ───────────────────────────

const ICONS = {
  grid: '<rect x="3.5" y="3.5" width="7" height="7" rx="1.5"/><rect x="13.5" y="3.5" width="7" height="7" rx="1.5"/><rect x="3.5" y="13.5" width="7" height="7" rx="1.5"/><rect x="13.5" y="13.5" width="7" height="7" rx="1.5"/>',
  sliders: '<path d="M4 7h9M17 7h3M4 12h3M11 12h9M4 17h11M19 17h1"/><circle cx="15" cy="7" r="2"/><circle cx="9" cy="12" r="2"/><circle cx="17" cy="17" r="2"/>',
  check: '<path d="M5 12.5l4.5 4.5L19 7.5"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>',
  x: '<path d="M6 6l12 12M18 6L6 18"/>',
  search: '<circle cx="11" cy="11" r="6.5"/><path d="M20 20l-4.2-4.2"/>',
  volUp: '<path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z"/><path d="M15.5 9a4 4 0 0 1 0 6M18.5 6.5a7.5 7.5 0 0 1 0 11"/>',
  volDown: '<path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z"/><path d="M15.5 9a4 4 0 0 1 0 6"/>',
  mute: '<path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z"/><path d="M16 9.5l5 5M21 9.5l-5 5"/>',
  playPause: '<path d="M4.5 6v12l7.5-6z"/><path d="M15.5 6v12M19.5 6v12"/>',
  next: '<path d="M5 6v12l8.5-6z"/><path d="M18 6v12"/>',
  prev: '<path d="M19 6v12l-8.5-6z"/><path d="M6 6v12"/>',
  stop: '<rect x="6.5" y="6.5" width="11" height="11" rx="1.5"/>',
  sun: '<circle cx="12" cy="12" r="3.5"/><path d="M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6L7 7M17 17l1.4 1.4M5.6 18.4L7 17M17 7l1.4-1.4"/>',
  sunDim: '<circle cx="12" cy="12" r="3.5"/><path d="M12 5.5v.5M12 18v.5M5.5 12h.5M18 12h.5M7.4 7.4l.3.3M16.3 16.3l.3.3M7.4 16.6l.3-.3M16.3 7.7l.3-.3"/>',
  keyboard: '<rect x="2.5" y="6" width="19" height="12" rx="2"/><path d="M6 10h.01M9.5 10h.01M13 10h.01M16.5 10h.01M8 14h8"/>',
  app: '<rect x="3" y="4.5" width="18" height="15" rx="2"/><path d="M3 8.5h18M6.5 6.5h.01M9 6.5h.01"/>',
  globe: '<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18"/>',
  type: '<path d="M5 7.5V5h14v2.5M12 5v14M9 19h6"/>',
  command: '<rect x="2.5" y="8" width="8" height="8" rx="1.8"/><rect x="13.5" y="8" width="8" height="8" rx="1.8"/><path d="M6.5 11v2M17.5 11v2"/>',
  slash: '<circle cx="12" cy="12" r="8.5"/><path d="M6 6l12 12"/>',
  plus: '<path d="M12 6v12M6 12h12"/>',
  alert: '<path d="M12 4l9 16H3z"/><path d="M12 10v4M12 17h.01"/>',
  folder: '<path d="M3.5 7A1.5 1.5 0 0 1 5 5.5h4l2 2h8A1.5 1.5 0 0 1 20.5 9v8.5A1.5 1.5 0 0 1 19 19H5a1.5 1.5 0 0 1-1.5-1.5z"/>',
  file: '<path d="M6 3.5h8l4 4v13H6z"/><path d="M14 3.5v4h4"/>',
  play: '<path d="M8 5.5v13l10-6.5z"/>',
  upload: '<path d="M12 15V4M7.5 8.5L12 4l4.5 4.5"/><path d="M4 15v3.5A1.5 1.5 0 0 0 5.5 20h13a1.5 1.5 0 0 0 1.5-1.5V15"/>',
  ccw: '<path d="M4 4.5v5h5"/><path d="M4.6 9.5A8 8 0 1 1 6 16.6"/>',
  cw: '<path d="M20 4.5v5h-5"/><path d="M19.4 9.5A8 8 0 1 0 18 16.6"/>',
  press: '<circle cx="12" cy="12" r="3"/><circle cx="12" cy="12" r="8"/>',
  plug: '<path d="M9 3v5M15 3v5M6.5 8h11v3a5.5 5.5 0 0 1-11 0zM12 16.5V21"/>',
  external: '<path d="M14 4h6v6M20 4l-9 9"/><path d="M18 14v4.5a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 4 18.5v-11A1.5 1.5 0 0 1 5.5 6H10"/>',
  refresh: '<path d="M20 5v5h-5"/><path d="M19.4 10A8 8 0 1 0 18 15.5"/>',
  copy: '<rect x="8.5" y="8.5" width="11.5" height="11.5" rx="2"/><path d="M15.5 8.5V5.5A1.5 1.5 0 0 0 14 4H5.5A1.5 1.5 0 0 0 4 5.5V14a1.5 1.5 0 0 0 1.5 1.5h3"/>',
  layers: '<path d="M12 4l9 4.5-9 4.5-9-4.5z"/><path d="M3 13l9 4.5 9-4.5"/>',
};

const icon = (name) =>
  `<span class="icon"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">${ICONS[name] || ''}</svg></span>`;

function hydrateIcons(root = document) {
  root.querySelectorAll('[data-icon]').forEach((el) => {
    el.outerHTML = icon(el.dataset.icon);
  });
}

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

// ─────────────────────────── 키 이름 ───────────────────────────

const MOD_LABEL = { ctrl: 'Ctrl', control: 'Ctrl', lctrl: 'Ctrl', shift: 'Shift', lshift: 'Shift', alt: 'Alt', lalt: 'Alt', win: 'Win', lwin: 'Win', windows: 'Win', rctrl: 'RCtrl', rshift: 'RShift', ralt: 'RAlt', rwin: 'RWin' };
const KEY_LABEL = {
  esc: 'Esc', escape: 'Esc', enter: 'Enter', return: 'Enter', backspace: 'Backspace', tab: 'Tab', space: 'Space',
  minus: '-', equal: '=', equals: '=', leftbracket: '[', rightbracket: ']', backslash: '\\', semicolon: ';', quote: "'",
  grave: '`', comma: ',', dot: '.', period: '.', slash: '/', capslock: 'CapsLock', printscreen: 'PrtSc', prtsc: 'PrtSc',
  scrolllock: 'ScrollLock', pause: 'Pause', insert: 'Ins', home: 'Home', pageup: 'PgUp', pgup: 'PgUp', delete: 'Del', del: 'Del',
  end: 'End', pagedown: 'PgDn', pgdn: 'PgDn', right: '→', left: '←', down: '↓', up: '↑', numlock: 'NumLock',
  numpadslash: 'Num /', numpadasterisk: 'Num *', numpadminus: 'Num -', numpadplus: 'Num +', numpadenter: 'Num Enter', numpaddot: 'Num .',
  menu: 'Menu', hangul: '한/영', hanja: '한자',
  volumeup: '볼륨 +', volumedown: '볼륨 -', mute: '음소거', play: '재생/정지', next: '다음 곡', prev: '이전 곡', stop: '정지',
};

function keyLabel(name) {
  const n = name.trim().toLowerCase();
  if (MOD_LABEL[n]) return MOD_LABEL[n];
  if (KEY_LABEL[n]) return KEY_LABEL[n];
  if (/^numpad\d$/.test(n)) return 'Num ' + n.slice(6);
  return n.charAt(0).toUpperCase() + n.slice(1);
}

const prettyChord = (chord) => chord.split('+').map(keyLabel).join(' + ');
const prettySeq = (text) => text.split(',').map((s) => s.trim()).filter(Boolean).map(prettyChord).join(', ');
const kbd = (text) => text.split(',').map((s) => s.trim()).filter(Boolean)
  .map((c) => `<span class="kbd">${esc(prettyChord(c))}</span>`).join('<span class="sep">,</span>');
const norm = (text) => (text || '').toLowerCase().replace(/\s+/g, '');

// 브라우저 키 코드(e.code) → 설정 파일 키 이름. 한글 입력 상태에서도 물리 키 기준이라 정확함
const CODE_KEY = {
  Enter: 'enter', NumpadEnter: 'numpadenter', Escape: 'esc', Backspace: 'backspace', Tab: 'tab', Space: 'space',
  Minus: 'minus', Equal: 'equal', BracketLeft: 'leftbracket', BracketRight: 'rightbracket', Backslash: 'backslash',
  Semicolon: 'semicolon', Quote: 'quote', Backquote: 'grave', Comma: 'comma', Period: 'dot', Slash: 'slash',
  CapsLock: 'capslock', PrintScreen: 'printscreen', ScrollLock: 'scrolllock', Pause: 'pause', Insert: 'insert',
  Home: 'home', PageUp: 'pageup', Delete: 'delete', End: 'end', PageDown: 'pagedown',
  ArrowRight: 'right', ArrowLeft: 'left', ArrowDown: 'down', ArrowUp: 'up', NumLock: 'numlock',
  NumpadDivide: 'numpadslash', NumpadMultiply: 'numpadasterisk', NumpadSubtract: 'numpadminus', NumpadAdd: 'numpadplus',
  NumpadDecimal: 'numpaddot', ContextMenu: 'menu', Lang1: 'hangul', HangulMode: 'hangul', Lang2: 'hanja', Hanja: 'hanja',
  AudioVolumeUp: 'volumeup', AudioVolumeDown: 'volumedown', AudioVolumeMute: 'mute',
  MediaPlayPause: 'play', MediaTrackNext: 'next', MediaTrackPrevious: 'prev', MediaStop: 'stop',
};
const MODIFIER_CODES = new Set(['ControlLeft', 'ControlRight', 'ShiftLeft', 'ShiftRight', 'AltLeft', 'AltRight', 'MetaLeft', 'MetaRight', 'OSLeft', 'OSRight']);

function chordFromEvent(e) {
  if (MODIFIER_CODES.has(e.code)) return null;
  let key = CODE_KEY[e.code];
  if (!key) {
    let m;
    if ((m = /^Key([A-Z])$/.exec(e.code))) key = m[1].toLowerCase();
    else if ((m = /^Digit(\d)$/.exec(e.code))) key = m[1];
    else if ((m = /^Numpad(\d)$/.exec(e.code))) key = 'numpad' + m[1];
    else if ((m = /^F(\d{1,2})$/.exec(e.code))) key = 'f' + m[1];
  }
  if (!key) return { error: `'${e.code || e.key}' 키는 지원하지 않아요.` };
  const mods = [];
  if (e.ctrlKey) mods.push('ctrl');
  if (e.shiftKey) mods.push('shift');
  if (e.altKey) mods.push('alt');
  if (e.metaKey) mods.push('win');
  return { chord: [...mods, key].join('+') };
}

// ─────────────────────────── 고를 수 있는 동작 ───────────────────────────

const MEDIA_INFO = {
  volumeup: { label: '볼륨 높이기', icon: 'volUp', cap: '볼륨 +' },
  volumedown: { label: '볼륨 낮추기', icon: 'volDown', cap: '볼륨 -' },
  mute: { label: '음소거', icon: 'mute', cap: '음소거' },
  play: { label: '재생 / 일시정지', icon: 'playPause', cap: '재생/정지' },
  next: { label: '다음 곡', icon: 'next', cap: '다음 곡' },
  prev: { label: '이전 곡', icon: 'prev', cap: '이전 곡' },
  stop: { label: '정지', icon: 'stop', cap: '정지' },
  brightnessup: { label: '화면 밝게', icon: 'sun', cap: '밝게', desc: '노트북 화면 등 일부에서만 동작' },
  brightnessdown: { label: '화면 어둡게', icon: 'sunDim', cap: '어둡게', desc: '노트북 화면 등 일부에서만 동작' },
};

const PRESETS = [
  { label: '복사', text: 'ctrl+c' },
  { label: '붙여넣기', text: 'ctrl+v' },
  { label: '잘라내기', text: 'ctrl+x' },
  { label: '실행 취소', text: 'ctrl+z' },
  { label: '다시 실행', text: 'ctrl+y' },
  { label: '모두 선택', text: 'ctrl+a' },
  { label: '저장', text: 'ctrl+s' },
  { label: '찾기', text: 'ctrl+f' },
  { label: '화면 캡처 (영역)', text: 'win+shift+s' },
  { label: '클립보드 기록', text: 'win+v' },
  { label: '바탕 화면 보기', text: 'win+d' },
  { label: '작업 관리자', text: 'ctrl+shift+esc' },
  { label: '창 전환', text: 'alt+tab' },
  { label: '새 탭', text: 'ctrl+t' },
  { label: '탭 닫기', text: 'ctrl+w' },
  { label: '닫은 탭 다시 열기', text: 'ctrl+shift+t' },
  { label: '새로 고침', text: 'f5' },
  { label: '확대', text: 'ctrl+equal' },
  { label: '축소', text: 'ctrl+minus' },
  { label: '원래 크기', text: 'ctrl+0' },
];
const PRESET_BY_NORM = new Map(PRESETS.map((p) => [norm(p.text), p]));

// ─────────────────────────── C# 와 통신 ───────────────────────────

const bridge = (() => {
  const wv = window.chrome && window.chrome.webview;
  let seq = 0;
  const waiting = new Map();
  let onPush = () => {};

  if (wv) {
    wv.addEventListener('message', (e) => {
      const msg = e.data;
      if (msg && msg.id != null && waiting.has(msg.id)) {
        waiting.get(msg.id)(msg);
        waiting.delete(msg.id);
      } else if (msg && msg.type === 'state') {
        onPush(msg.state);
      }
    });
  }

  async function call(type, payload = {}) {
    const id = ++seq;
    const msg = { id, type, ...payload };
    let reply;
    if (wv) {
      reply = await new Promise((resolve) => { waiting.set(id, resolve); wv.postMessage(msg); });
    } else {
      const res = await fetch('/api', { method: 'POST', body: JSON.stringify(msg) });
      reply = await res.json();
    }
    if (reply.state) setState(reply.state);
    return reply;
  }

  return { call, inApp: !!wv, onPush: (fn) => { onPush = fn; } };
})();

// ─────────────────────────── 상태 ───────────────────────────

let S = null; // C# 에서 온 상태
const ui = {
  page: 'mapping',
  layer: 0,
  selected: null,   // 선택한 칸 id
  expanded: null,   // 펼친 '직접 지정' 항목 id
  search: '',
  saving: 0,
};

const $ = (sel) => document.querySelector(sel);
const slotById = (id) => S && S.slots.find((s) => s.id === id);

function setState(state) {
  S = state;
  renderAll();
}

// ─────────────────────────── 칸 표시 ───────────────────────────

function capInfo(slot) {
  switch (slot.type) {
    case 'empty': return { icon: 'plus', text: '할당 안 함', cls: 'empty' };
    case 'media': {
      const m = MEDIA_INFO[slot.media] || {};
      return { icon: m.icon || 'volUp', text: m.cap || slot.label, cls: 'assigned' };
    }
    case 'keys': {
      const preset = PRESET_BY_NORM.get(norm(slot.padText));
      return { icon: 'keyboard', text: preset ? preset.label : prettySeq(slot.padText), cls: 'assigned' };
    }
    case 'pc': {
      const pc = slot.pc;
      if (pc.mode === 'app') return { icon: 'app', text: pc.runName || pc.run, cls: 'assigned pc' };
      if (pc.mode === 'web') return { icon: 'globe', text: pc.runName || pc.run, cls: 'assigned pc' };
      if (pc.mode === 'pckeys') return { icon: 'command', text: prettySeq((pc.keys || []).join(', ')), cls: 'assigned pc' };
      const t = (pc.text || '').replace(/\s+/g, ' ');
      return { icon: 'type', text: `“${t.length > 14 ? t.slice(0, 13) + '…' : t}”`, cls: 'assigned pc' };
    }
    default: return { icon: 'alert', text: slot.padText || '오류', cls: 'invalid' };
  }
}

function kindText(slot) {
  switch (slot.type) {
    case 'empty': return '할당 안 함';
    case 'media': return '미디어';
    case 'keys': return '키보드 단축키';
    case 'pc': return { app: '프로그램 열기', web: '웹사이트 열기', text: '문장 입력', pckeys: '단축키 묶음 (PC)' }[slot.pc.mode];
    default: return '잘못된 값';
  }
}

// ─────────────────────────── 그리기 ───────────────────────────

function renderAll() {
  if (!S) return;
  renderSidebar();
  renderLayerTabs();
  renderPad();
  renderApplyBar();
  if (ui.page === 'settings') renderSettings();
  if (ui.selected) {
    renderPanelHead();
    updatePanelMarks();
  }
}

function renderSidebar() {
  const c = $('#connBadge');
  c.className = 'conn ' + (S.device.connected ? 'on' : 'off');
  c.querySelector('.conn-text').textContent = S.device.connected ? '연결됨' : '연결 안 됨';
  $('#deviceSub').textContent = `${S.layout.buttonCount}키 · 노브 ${S.layout.knobCount}개`;
  $('#fileName').textContent = S.config.fileName;
  $('#fileName').title = S.config.path;
  const save = $('#saveState');
  save.classList.toggle('saving', ui.saving > 0);
  save.lastElementChild.textContent = ui.saving > 0 ? '저장 중…' : '자동 저장됨';
}

function renderLayerTabs() {
  const tabs = $('#layerTabs');
  const n = S.layout.layerCount;
  if (tabs.children.length !== n) {
    tabs.innerHTML = Array.from({ length: n }, (_, i) => `<button role="tab" data-layer="${i}">레이어 ${i + 1}</button>`).join('');
  }
  [...tabs.children].forEach((b, i) => {
    b.classList.toggle('active', i === ui.layer);
    const pending = S.slots.some((s) => s.layer === i && s.pending);
    b.title = pending ? '패드에 아직 적용 안 한 변경이 있어요' : '';
    b.innerHTML = `레이어 ${i + 1}${pending ? ' <span class="count">●</span>' : ''}`;
  });
}

function renderPad() {
  const cols = S.layout.columns;
  const keys = $('#keys');
  keys.style.gridTemplateColumns = `repeat(${cols}, var(--cap))`;

  const layerSlots = S.slots.filter((s) => s.layer === ui.layer);
  const buttons = layerSlots.filter((s) => s.control === 'button');
  keys.innerHTML = buttons.map((s) => {
    const info = capInfo(s);
    return `<button class="cap ${info.cls}${s.id === ui.selected ? ' selected' : ''}" data-slot="${s.id}"
              title="${esc(s.name)} · ${esc(s.position)}&#10;${esc(kindText(s))}: ${esc(s.label || '할당 안 함')}">
        <span class="cap-num">${s.index + 1}</span>
        ${s.pending ? '<span class="pending-dot"></span>' : ''}
        ${icon(info.icon)}
        <span class="cap-label">${esc(info.text)}</span>
      </button>`;
  }).join('');

  const knobs = $('#knobs');
  const callouts = $('#callouts');
  const knobCount = S.layout.knobCount;
  let knobHtml = '';
  let calloutHtml = '';
  for (let k = 0; k < knobCount; k++) {
    const parts = ['ccw', 'press', 'cw'].map((a) => layerSlots.find((s) => s.control === 'knob' && s.index === k && s.knobAction === a));
    const selected = parts.some((p) => p && p.id === ui.selected);
    const pending = parts.some((p) => p && p.pending);
    const press = parts[1];
    knobHtml += `<button class="knob${selected ? ' selected' : ''}" data-slot="${press.id}" title="${esc(press.name)}">
        ${pending ? '<span class="pending-dot"></span>' : ''}</button>`;

    calloutHtml += `<div class="callout-group"><div class="callout-title">${esc(press.name)}</div>`;
    for (const p of parts) {
      const info = capInfo(p);
      const dirIcon = { ccw: 'ccw', press: 'press', cw: 'cw' }[p.knobAction];
      calloutHtml += `<button class="callout ${info.cls}${p.id === ui.selected ? ' selected' : ''}" data-slot="${p.id}">
          <span class="dir">${icon(dirIcon)}</span>
          <span class="callout-text"><span class="callout-kind">${esc(p.position)}</span>
            <span class="callout-label" style="display:block">${esc(info.text)}</span></span>
          ${p.pending ? '<span class="pending-dot" title="패드에 아직 적용 안 됨"></span>' : ''}
        </button>`;
    }
    calloutHtml += '</div>';
  }
  knobs.innerHTML = knobHtml;
  callouts.innerHTML = calloutHtml;
}

function renderApplyBar() {
  const up = S.upload;
  const status = $('#applyStatus');
  let cls, ic, title, sub;
  if (!S.device.connected) {
    cls = 'unknown'; ic = 'plug'; title = '패드가 연결돼 있지 않아요';
    sub = up.pending > 0 ? `패드에 적용할 변경 ${up.pending}개가 기다리고 있어요. USB를 연결해 주세요.` : 'USB를 연결하면 패드에 적용할 수 있어요.';
  } else if (!up.hasRecord) {
    cls = 'unknown'; ic = 'info'; title = '이 PC에서 패드에 적용한 기록이 없어요';
    sub = '처음 한 번은 전체를 적용해 두면, 다음부터는 바뀐 칸만 빠르게 적용돼요.';
  } else if (up.pending > 0) {
    cls = 'pending'; ic = 'upload'; title = `패드에 적용할 변경 ${up.pending}개`;
    sub = 'PC 동작은 이미 반영됐어요. 패드에 저장되는 칸만 적용하면 돼요.';
  } else {
    cls = 'synced'; ic = 'check'; title = '패드와 설정이 같아요';
    sub = up.lastWrittenAt ? `마지막 적용 ${up.lastWrittenAt}` : '';
  }
  status.className = 'apply-status ' + cls;
  status.innerHTML = `<span class="badge-icon">${icon(ic)}</span>
    <div><div class="title">${esc(title)}</div><div class="sub">${esc(sub)}</div></div>`;

  const apply = $('#applyBtn');
  const writeAll = $('#writeAllBtn');
  const n = up.hasRecord ? up.pending : S.slots.filter((s) => s.type !== 'empty' && s.type !== 'invalid').length;
  apply.innerHTML = `${icon('upload')}<span>${up.hasRecord ? '패드에 적용' : '전체 적용'}</span>${n > 0 ? `<span class="count">${n}</span>` : ''}`;
  apply.disabled = !S.device.connected || n === 0 || up.busy;
  apply.title = !S.device.connected ? '패드를 연결해 주세요' : n === 0 ? '적용할 변경이 없어요' : '';
  writeAll.hidden = !up.hasRecord;
  writeAll.disabled = !S.device.connected || up.busy;
}

// ─────────────────────────── 오른쪽 패널 ───────────────────────────

function selectSlot(id) {
  if (ui.selected === id) return;
  ui.selected = id;
  ui.search = '';
  $('#search').value = '';
  const slot = slotById(id);
  ui.expanded = slot && isCustomOption(currentOptionId(slot)) ? currentOptionId(slot) : null;
  $('#app').classList.add('panel-open');
  $('#panel').setAttribute('aria-hidden', 'false');
  renderPad();
  renderPanelHead();
  buildPanelBody();
  // 지금 지정된 동작이 보이게 스크롤
  const sel = document.querySelector('#panelBody .opt.selected');
  const body = $('#panelBody');
  body.scrollTop = 0;
  if (sel && sel.offsetTop > body.clientHeight * 0.6) body.scrollTop = sel.offsetTop - 120;
}

function closePanel() {
  ui.selected = null;
  ui.expanded = null;
  $('#app').classList.remove('panel-open');
  $('#panel').setAttribute('aria-hidden', 'true');
  renderPad();
}

function currentOptionId(slot) {
  switch (slot.type) {
    case 'empty': return 'empty';
    case 'media': return 'media:' + slot.media;
    case 'keys': return PRESET_BY_NORM.has(norm(slot.padText)) ? 'preset:' + norm(slot.padText) : 'custom:keys';
    case 'pc': return 'custom:' + slot.pc.mode;
    default: return 'custom:keys';
  }
}
const isCustomOption = (id) => id.startsWith('custom:');

function renderPanelHead() {
  const slot = slotById(ui.selected);
  if (!slot) return closePanel();
  $('#panelKicker').textContent = `레이어 ${slot.layer + 1} · ${slot.control === 'button' ? slot.position : slot.name}`;
  $('#panelTitle').textContent = slot.control === 'button' ? slot.name : slot.position;

  const info = capInfo(slot);
  const where = slot.type === 'pc'
    ? `<span class="tag pc" title="MacroPad 앱이 켜져 있을 때 동작해요">PC 동작 · ${esc(slot.pc.trigger.toUpperCase())}</span>`
    : slot.type === 'empty' ? '' : '<span class="tag pad" title="패드에 저장돼서 PC 앱 없이도 동작해요">패드에 저장</span>';
  const pending = slot.pending
    ? '<span class="tag warn">패드에 적용 필요</span>'
    : slot.type !== 'empty' && slot.type !== 'invalid' ? '<span class="tag ok">적용됨</span>' : '';
  let detail = kindText(slot);
  if (slot.type === 'keys') detail += ' · ' + prettySeq(slot.padText);
  if (slot.type === 'pc' && (slot.pc.mode === 'app' || slot.pc.mode === 'web')) detail = slot.pc.run;
  if (slot.type === 'empty' && slot.padRecord) detail = `패드에 남아 있는 값: ${slot.padRecord.source}`;
  if (slot.type === 'invalid') detail = slot.error;

  const cur = $('#current');
  cur.className = 'current ' + (slot.type === 'pc' ? 'pc' : slot.type === 'empty' ? 'empty' : slot.type === 'invalid' ? 'invalid' : '');
  cur.innerHTML = `<span class="big-icon">${icon(info.icon)}</span>
    <div class="current-main">
      <div class="current-label">${esc(slot.type === 'empty' ? '할당 안 함' : info.text)}</div>
      <div class="current-detail" title="${esc(detail)}">${esc(detail)}</div>
      ${where || pending ? `<div class="tags">${where}${pending}</div>` : ''}
    </div>`;
}

function optionGroups() {
  const media = S.mediaKeys.map((m) => {
    const info = MEDIA_INFO[m.name] || { label: m.label, icon: 'volUp' };
    return { id: 'media:' + m.name, label: info.label, desc: info.desc, icon: info.icon, value: { type: 'pad', text: m.name } };
  });
  return [
    { title: null, items: [{ id: 'empty', label: '할당 안 함', desc: '패드에 원래 있던 값을 그대로 둬요', icon: 'slash', value: { type: 'empty' } }] },
    { title: '미디어', items: media },
    {
      title: '자주 쓰는 단축키',
      items: PRESETS.map((p) => ({ id: 'preset:' + norm(p.text), label: p.label, hint: kbd(p.text), keywords: p.text, value: { type: 'pad', text: p.text } })),
    },
    {
      title: '직접 지정',
      items: [
        { id: 'custom:keys', label: '키보드 단축키', desc: '키 하나, 조합, 연속 입력', icon: 'keyboard', tag: 'pad' },
        { id: 'custom:app', label: '프로그램 · 파일 · 폴더 열기', icon: 'app', tag: 'pc' },
        { id: 'custom:web', label: '웹사이트 열기', icon: 'globe', tag: 'pc' },
        { id: 'custom:text', label: '문장 입력', desc: '자주 쓰는 문구를 한 번에 (한글 가능)', icon: 'type', tag: 'pc' },
        { id: 'custom:pckeys', label: '단축키 묶음 (PC)', desc: '패드에 못 넣는 키나 긴 묶음', icon: 'command', tag: 'pc' },
      ],
    },
  ];
}

const TAG_HTML = {
  pad: '<span class="tag pad" title="패드에 저장돼서 PC 앱 없이도 동작해요">패드</span>',
  pc: '<span class="tag pc" title="MacroPad 앱이 켜져 있을 때 동작해요">PC</span>',
};

function buildPanelBody() {
  const slot = slotById(ui.selected);
  if (!slot) return;
  const q = norm(ui.search);
  const match = (it) => !q || norm(it.label + ' ' + (it.desc || '') + ' ' + (it.keywords || '')).includes(q);

  let html = '';
  for (const g of optionGroups()) {
    const items = g.items.filter(match);
    if (!items.length) continue;
    if (g.title) html += `<div class="group-title">${esc(g.title)}</div>`;
    for (const it of items) {
      html += `<div class="opt" data-opt="${esc(it.id)}">
        <button class="opt-row">
          <span class="radio"></span>
          ${it.icon ? `<span class="opt-icon">${icon(it.icon)}</span>` : ''}
          <span class="opt-text"><span class="opt-label">${esc(it.label)}</span>${it.desc ? `<div class="opt-desc">${esc(it.desc)}</div>` : ''}</span>
          <span class="opt-hint">${it.hint || ''}${it.tag ? TAG_HTML[it.tag] : ''}</span>
        </button>
        ${isCustomOption(it.id) ? `<div class="opt-body">${customBody(it.id, slot)}</div>` : ''}
      </div>`;
    }
  }
  if (!html) html = `<div class="empty-search">'${esc(ui.search)}'에 맞는 동작이 없어요.</div>`;

  const body = $('#panelBody');
  body.innerHTML = html;
  body.querySelectorAll('.opt').forEach((el) => {
    const id = el.dataset.opt;
    const item = optionGroups().flatMap((g) => g.items).find((i) => i.id === id);
    el.querySelector('.opt-row').addEventListener('click', () => onOptionClick(item, el));
    if (isCustomOption(id)) bindCustom(id, el.querySelector('.opt-body'), slot);
  });
  updatePanelMarks();
}

function updatePanelMarks() {
  const slot = slotById(ui.selected);
  if (!slot) return;
  const current = currentOptionId(slot);
  document.querySelectorAll('#panelBody .opt').forEach((el) => {
    el.classList.toggle('selected', el.dataset.opt === current);
    el.classList.toggle('expanded', el.dataset.opt === ui.expanded);
  });
}

async function onOptionClick(item, el) {
  if (item.value) {
    ui.expanded = null;
    const ok = await setSlot(item.value);
    if (ok) updatePanelMarks();
    return;
  }
  // '직접 지정' 항목: 펼치기만 (값을 넣어야 적용)
  ui.expanded = ui.expanded === item.id ? null : item.id;
  updatePanelMarks();
  if (ui.expanded) {
    const first = el.querySelector('.recorder, input, textarea');
    if (first) setTimeout(() => first.focus(), 30);
  }
}

async function setSlot(value, errorEl) {
  ui.saving++;
  renderSidebar();
  try {
    const reply = await bridge.call('setSlot', { slot: ui.selected, value });
    if (errorEl) errorEl.innerHTML = reply.ok ? '' : `${icon('alert')}<span>${esc(reply.error)}</span>`;
    if (!reply.ok && !errorEl) toast(reply.error, 'err');
    if (reply.ok && reply.result && reply.result.backup) {
      toast(`기존 설정 파일은 ${reply.result.backup.split(/[\\/]/).pop()} 으로 백업했어요.`, 'ok', 5000);
    }
    return reply.ok;
  } finally {
    ui.saving--;
    renderSidebar();
  }
}

// ─────────────── '직접 지정' 입력 칸 ───────────────

function customBody(id, slot) {
  const pc = slot.type === 'pc' ? slot.pc : null;
  const same = (mode) => pc && pc.mode === mode;
  const err = '<div class="error"></div>';
  switch (id) {
    case 'custom:keys':
      return `<div class="field"><div class="recorder" tabindex="0"></div>
        <div class="recorder-tools"><span class="help">패드가 이 키를 대신 눌러요. 여러 번 누르면 차례대로 입력돼요.</span>
          <button class="linkish" data-act="clear">지우기</button></div></div>
        <div class="field"><label>또는 직접 입력</label>
          <input class="input mono" data-role="text" placeholder="예: ctrl+shift+esc, ctrl+a, ctrl+c" value="${esc(slot.type === 'keys' || slot.type === 'invalid' ? slot.padText : '')}" spellcheck="false"></div>
        ${err}`;
    case 'custom:app':
      return `<div class="field"><label>열 대상</label>
          <div class="field-row"><input class="input mono" data-role="run" placeholder="C:\\Program Files\\…\\app.exe" value="${esc(same('app') ? pc.run : '')}" spellcheck="false">
          <button class="btn small" data-act="file" title="파일 고르기">${icon('file')}</button>
          <button class="btn small" data-act="folder" title="폴더 고르기">${icon('folder')}</button></div></div>
        <div class="field"><label>실행 인자 (선택)</label>
          <input class="input mono" data-role="args" placeholder="--new-window" value="${esc(same('app') ? pc.args || '' : '')}" spellcheck="false"></div>
        <div class="field"><button class="btn small" data-act="test">${icon('play')}<span>지금 열어 보기</span></button></div>
        <div class="help">프로그램(.exe), 바로가기, 문서, 폴더 모두 돼요. %USERPROFILE% 같은 환경 변수도 쓸 수 있어요.</div>
        ${err}`;
    case 'custom:web':
      return `<div class="field"><label>주소</label>
          <input class="input mono" data-role="url" placeholder="https://www.google.com" value="${esc(same('web') ? pc.run : '')}" spellcheck="false"></div>
        <div class="field"><button class="btn small" data-act="test">${icon('external')}<span>지금 열어 보기</span></button></div>
        <div class="help">기본 브라우저로 열려요.</div>
        ${err}`;
    case 'custom:text':
      return `<div class="field"><label>입력할 문장</label>
          <textarea class="textarea" data-role="text" placeholder="확인했습니다. 감사합니다!">${esc(same('text') ? pc.text : '')}</textarea></div>
        <div class="help">한/영 상태와 상관없이 그대로 입력돼요. 줄바꿈은 Enter로 입력돼요.</div>
        ${err}`;
    case 'custom:pckeys':
      return `<div class="field"><div class="recorder" tabindex="0"></div>
        <div class="recorder-tools"><span class="help">PC가 차례대로 눌러요. 개수 제한이 없어요.</span>
          <button class="linkish" data-act="clear">지우기</button></div></div>
        ${err}`;
    default:
      return '';
  }
}

function bindCustom(id, body, slot) {
  const errorEl = body.querySelector('.error');
  const pc = slot.type === 'pc' ? slot.pc : null;

  if (id === 'custom:keys' || id === 'custom:pckeys') {
    const initial = id === 'custom:keys'
      ? (slot.type === 'keys' || slot.type === 'invalid' ? slot.padText.split(',').map((s) => s.trim()).filter(Boolean) : [])
      : (pc && pc.mode === 'pckeys' ? pc.keys.flatMap((k) => k.split(',').map((s) => s.trim())).filter(Boolean) : []);
    const textInput = body.querySelector('[data-role="text"]');
    const apply = (chords) => {
      if (!chords.length) return;
      if (id === 'custom:keys') {
        if (textInput) textInput.value = chords.join(', ');
        setSlot({ type: 'pad', text: chords.join(', ') }, errorEl);
      } else {
        setSlot({ type: 'pc', mode: 'pckeys', keys: chords }, errorEl);
      }
    };
    const rec = makeRecorder(body.querySelector('.recorder'), initial, debounce(apply, 450), id === 'custom:keys' ? 18 : 64);
    body.querySelector('[data-act="clear"]').addEventListener('click', () => rec.clear());
    if (textInput) {
      onCommit(textInput, () => {
        const v = textInput.value.trim();
        if (!v) return;
        rec.set(v.split(',').map((s) => s.trim()).filter(Boolean));
        setSlot({ type: 'pad', text: v }, errorEl);
      });
    }
    return;
  }

  if (id === 'custom:app') {
    const run = body.querySelector('[data-role="run"]');
    const args = body.querySelector('[data-role="args"]');
    const apply = () => run.value.trim() && setSlot({ type: 'pc', mode: 'app', run: run.value.trim(), args: args.value }, errorEl);
    onCommit(run, apply);
    onCommit(args, apply);
    body.querySelector('[data-act="file"]').addEventListener('click', async () => {
      const r = await bridge.call('browseFile');
      if (r.ok && r.result && r.result.path) { run.value = r.result.path; apply(); }
    });
    body.querySelector('[data-act="folder"]').addEventListener('click', async () => {
      const r = await bridge.call('browseFolder');
      if (r.ok && r.result && r.result.path) { run.value = r.result.path; apply(); }
    });
    bindTest(body, errorEl);
    return;
  }

  if (id === 'custom:web') {
    const url = body.querySelector('[data-role="url"]');
    onCommit(url, () => url.value.trim() && setSlot({ type: 'pc', mode: 'web', run: url.value.trim() }, errorEl));
    bindTest(body, errorEl);
    return;
  }

  if (id === 'custom:text') {
    const text = body.querySelector('[data-role="text"]');
    const apply = () => text.value && setSlot({ type: 'pc', mode: 'text', text: text.value }, errorEl);
    text.addEventListener('input', debounce(apply, 700));
    text.addEventListener('blur', apply);
  }
}

function bindTest(body, errorEl) {
  body.querySelector('[data-act="test"]').addEventListener('click', async () => {
    const r = await bridge.call('testAction', { slot: ui.selected });
    if (!r.ok) errorEl.innerHTML = `${icon('alert')}<span>${esc(r.error)}</span>`;
  });
}

/** Enter 또는 포커스를 벗어날 때 적용 (값이 바뀐 경우만) */
function onCommit(input, fn) {
  let last = input.value;
  const commit = () => { if (input.value !== last) { last = input.value; fn(); } };
  input.addEventListener('blur', commit);
  input.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); commit(); } });
}

function debounce(fn, ms) {
  let t;
  return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); };
}

/**
 * 키 입력 받는 칸. 포커스를 받은 뒤 처음 누른 키는 기존 내용을 대신하고, 이어서 누르면 뒤에 붙음.
 */
function makeRecorder(el, initial, onChange, max) {
  let chords = [...initial];
  let fresh = true;

  const render = () => {
    el.innerHTML = chords.length
      ? chords.map((c) => `<span class="chord"><span class="kbd">${esc(prettyChord(c))}</span></span>`).join('<span class="sep">→</span>')
      : '<span class="placeholder">여기를 누르고 키를 입력하세요</span>';
  };
  render();

  el.addEventListener('focus', () => { fresh = true; });
  el.addEventListener('keydown', (e) => {
    if (e.key === 'Tab' && !e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey && chords.length && !fresh) return; // Tab 으로 빠져나가기
    e.preventDefault();
    e.stopPropagation();
    if (e.repeat) return;
    const r = chordFromEvent(e);
    if (!r) return;
    if (r.error) { toast(r.error, 'err'); return; }
    if (fresh) { chords = []; fresh = false; }
    if (chords.length >= max) { toast(`최대 ${max}개까지 넣을 수 있어요.`, 'err'); return; }
    chords.push(r.chord);
    render();
    onChange(chords);
  });

  return {
    clear() { chords = []; fresh = false; render(); el.focus(); },
    set(list) { chords = [...list]; render(); },
  };
}

// ─────────────────────────── 패드에 적용 ───────────────────────────

async function startApply(onlyChanged) {
  const preview = await bridge.call('previewUpload', { onlyChanged });
  if (!preview.ok) return showModal('적용할 수 없어요', `<p>${esc(preview.error)}</p>`, [{ label: '닫기' }]);
  const items = preview.result.items;
  if (!items.length) return toast('패드에 적용할 변경이 없어요.', 'ok');

  const list = items.slice(0, 40).map((i) => {
    const s = slotById(i.slot);
    const info = s ? capInfo(s) : { icon: 'keyboard', text: i.label };
    return `<li><span class="where">레이어 ${i.layer} · ${esc(slotWhere(i.slot))}</span><span class="what">${icon(info.icon)}${esc(info.text)}</span></li>`;
  }).join('');
  const more = items.length > 40 ? `<p style="margin-top:8px">외 ${items.length - 40}개</p>` : '';
  const skipped = preview.result.skipped ? ` 패드와 같은 ${preview.result.skipped}개는 건너뛰어요.` : '';
  const firstTime = !S.upload.hasRecord;
  showModal(onlyChanged || firstTime ? `패드에 ${items.length}개 적용` : `전체 ${items.length}개 다시 쓰기`,
    `<p>아래 칸을 패드에 저장해요.${skipped} 몇 초면 끝나요.</p><ul class="change-list">${list}</ul>${more}`,
    [{ label: '취소' }, { label: '적용', primary: true, onClick: () => runApply(onlyChanged, items.length) }]);
}

function slotWhere(id) {
  const s = slotById(id);
  if (!s) return id;
  return s.control === 'button' ? s.name : `${s.name} ${s.position.replace('으로 돌리기', '').replace('누르기', '누름')}`;
}

async function runApply(onlyChanged, count) {
  showModal('패드에 쓰는 중', `<div class="progress"><span class="spinner"></span><span>${count}개 칸을 쓰고 있어요. USB를 뽑지 마세요.</span></div>`, []);
  const r = await bridge.call('upload', { onlyChanged });
  hideModal();
  if (r.ok) toast(`패드에 ${r.result.written}개를 적용했어요. 바로 눌러서 확인해 보세요.`, 'ok', 4500);
  else showModal('패드에 쓰지 못했어요', `<p>${esc(r.error)}</p><p style="margin-top:8px">USB 연결을 확인하고 다시 시도해 주세요. 계속 안 되면 설정 › 로그 열기에서 원인을 볼 수 있어요.</p>`, [{ label: '닫기' }]);
}

// ─────────────────────────── 설정 화면 ───────────────────────────

function renderSettings() {
  const a = S.advanced;
  const unref = S.unreferencedActions.length
    ? `<div class="card-row"><div><h3>쓰이지 않는 PC 동작 ${S.unreferencedActions.length}개</h3><p>어느 칸에도 연결되지 않은 동작이 설정 파일에 남아 있어요: ${esc(S.unreferencedActions.join(', '))}</p></div></div>`
    : '';
  $('#settings').innerHTML = `
    <section class="card">
      <div class="card-row">
        <div><h3>Windows 시작 시 실행</h3><p>로그인하면 트레이에 조용히 떠서 PC 동작을 처리해요.</p></div>
        <label class="switch"><input type="checkbox" id="startupToggle" ${S.startup ? 'checked' : ''}><span></span></label>
      </div>
    </section>

    <section class="card">
      <h3>설정 파일</h3>
      <p>편집기에서 바꾸면 자동으로 저장돼요. 직접 고쳐도 되고, 고치면 편집기에 바로 반영돼요.</p>
      <div class="path">${esc(S.config.path)}</div>
      <div class="btns">
        <button class="btn small" data-act="openConfig">${icon('file')}<span>파일 열기</span></button>
        <button class="btn small" data-act="revealConfig">${icon('folder')}<span>폴더에서 보기</span></button>
        <button class="btn small" data-act="reload">${icon('refresh')}<span>다시 불러오기</span></button>
      </div>
    </section>

    <section class="card">
      <div class="card-row">
        <div><h3>패드</h3><p>${S.device.connected ? '연결돼 있어요.' : '연결돼 있지 않아요.'}</p></div>
        <span class="conn ${S.device.connected ? 'on' : 'off'}"><span class="dot"></span>${S.device.connected ? '연결됨' : '연결 안 됨'}</span>
      </div>
      <dl class="kv">
        <dt>장치 ID</dt><dd>VID ${esc(S.device.vendorId)} · PID ${esc(S.device.productId)}</dd>
        <dt>구성</dt><dd>버튼 ${S.layout.buttonCount}개 · 노브 ${S.layout.knobCount}개 · 레이어 ${S.layout.layerCount}개</dd>
        <dt>마지막 적용</dt><dd>${esc(S.upload.lastWrittenAt || '기록 없음')}</dd>
        <dt>고급 값</dt><dd>노브 시작 슬롯 ${a.knobSlotBase} · 리포트 ID ${esc(a.reportIdMode)} · 미디어 바이트 ${a.mediaModeByte} · 키 간격 ${a.keyDelay}</dd>
      </dl>
      <p style="margin-top:10px;font-size:12px">고급 값은 처음 연결이 안 될 때만 설정 파일의 advanced 에서 바꿔요.</p>
    </section>

    <section class="card">
      <div class="card-row">
        <div><h3>문제 해결</h3><p>트리거 키 처리, 패드 쓰기 기록이 남아 있어요.</p></div>
        <button class="btn small" data-act="openLog">${icon('external')}<span>로그 열기</span></button>
      </div>
      ${unref}
    </section>`;

  $('#startupToggle').addEventListener('change', async (e) => {
    const r = await bridge.call('setStartup', { enabled: e.target.checked });
    toast(r.ok ? (e.target.checked ? 'Windows 시작 시 실행할게요.' : '자동 실행을 껐어요.') : r.error, r.ok ? 'ok' : 'err');
  });
  $('#settings').querySelectorAll('[data-act]').forEach((b) => b.addEventListener('click', async () => {
    const r = await bridge.call(b.dataset.act);
    if (!r.ok) toast(r.error, 'err');
    else if (b.dataset.act === 'reload') toast('설정 파일을 다시 불러왔어요.', 'ok');
  }));
}

// ─────────────────────────── 대화상자 / 알림 ───────────────────────────

function showModal(title, bodyHtml, actions) {
  $('#modalTitle').textContent = title;
  $('#modalBody').innerHTML = bodyHtml;
  const box = $('#modalActions');
  box.innerHTML = '';
  for (const a of actions) {
    const b = document.createElement('button');
    b.className = 'btn' + (a.primary ? ' primary' : '');
    b.textContent = a.label;
    b.addEventListener('click', () => { hideModal(); if (a.onClick) a.onClick(); });
    box.appendChild(b);
  }
  $('#modal').hidden = false;
  const primary = box.querySelector('.primary') || box.querySelector('button');
  if (primary) primary.focus();
}

function hideModal() { $('#modal').hidden = true; }
const modalOpen = () => !$('#modal').hidden;

let toastTimer;
function toast(message, kind = 'ok', ms = 3200) {
  const t = $('#toast');
  t.className = 'toast show ' + kind;
  t.innerHTML = `${icon(kind === 'err' ? 'alert' : 'check')}<span>${esc(message)}</span>`;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.remove('show'), ms);
}

// ─────────────────────────── 이벤트 ───────────────────────────

function setPage(page) {
  ui.page = page;
  $('#app').dataset.page = page;
  document.querySelectorAll('.nav-item').forEach((b) => b.classList.toggle('active', b.dataset.nav === page));
  if (page === 'settings') renderSettings();
}

function wire() {
  hydrateIcons();

  document.querySelectorAll('.nav-item').forEach((b) => b.addEventListener('click', () => setPage(b.dataset.nav)));

  $('#layerTabs').addEventListener('click', (e) => {
    const b = e.target.closest('[data-layer]');
    if (!b) return;
    ui.layer = Number(b.dataset.layer);
    const sel = ui.selected && slotById(ui.selected);
    renderLayerTabs();
    if (sel) {
      // 같은 위치의 칸을 다른 레이어에서 계속 보기
      const same = S.slots.find((s) => s.layer === ui.layer && s.control === sel.control && s.index === sel.index && s.knobAction === sel.knobAction);
      ui.selected = null;
      if (same) selectSlot(same.id); else renderPad();
    } else {
      renderPad();
    }
  });

  $('#layerHelp').addEventListener('click', () => showModal('레이어',
    `<p>패드에는 설정을 3벌(레이어) 저장할 수 있어요. 다만 이 모델에서 레이어를 바꾸는 방법은 아직 확인하지 못했어요.</p>
     <p style="margin-top:8px">보통은 <b>레이어 1</b>만 쓰면 돼요. 레이어 2·3에 넣은 값도 패드에 저장은 돼요.</p>`,
    [{ label: '확인', primary: true }]));

  $('#stage').addEventListener('click', (e) => {
    const target = e.target.closest('[data-slot]');
    if (target) selectSlot(target.dataset.slot);
    else if (e.target === e.currentTarget || e.target.classList.contains('stage-inner')) closePanel();
  });

  $('#panelClose').addEventListener('click', closePanel);
  $('#search').addEventListener('input', (e) => { ui.search = e.target.value; buildPanelBody(); });
  $('#applyBtn').addEventListener('click', () => startApply(S.upload.hasRecord));
  $('#writeAllBtn').addEventListener('click', () => startApply(false));
  $('#modal').addEventListener('click', (e) => {
    if (e.target === e.currentTarget && $('#modalActions').children.length) hideModal();
  });

  document.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    if (e.target.closest && e.target.closest('.recorder')) return;
    if (modalOpen()) { if ($('#modalActions').children.length) hideModal(); }
    else if (ui.selected) closePanel();
  });

  // 다른 창에서 돌아오면 최신 상태 (패드 연결, 직접 고친 설정 파일)
  window.addEventListener('focus', () => bridge.call('getState'));
  bridge.onPush((state) => setState(state));
  if (!bridge.inApp) setInterval(() => document.hasFocus() && bridge.call('getState'), 4000);
}

wire();
bridge.call('getState');
