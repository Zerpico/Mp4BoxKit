const fileInput = document.getElementById('file');
const dropZone = document.getElementById('drop-zone');
const fileRow = document.getElementById('file-row');
const fileName = document.getElementById('file-name');
const fileSize = document.getElementById('file-size');
const inspectButton = document.getElementById('inspect');
const status = document.getElementById('status');
const resultPanel = document.getElementById('result');
const summary = document.getElementById('summary');
const warnings = document.getElementById('warnings');
const tabs = document.getElementById('tabs');
const sampleTrack = document.getElementById('sample-track');
const sampleNote = document.getElementById('sample-note');
const boxSearch = document.getElementById('box-search');
const boxTree = document.getElementById('box-tree');
const boxProperties = document.getElementById('box-properties');
const boxPath = document.getElementById('box-path');
const boxCount = document.getElementById('box-count');
const runtimeBadge = document.getElementById('runtime-badge');
const rawJson = document.getElementById('raw-json');
const runtime = globalThis.Mp4BoxKitRuntime;

const state = {
  file: null,
  result: null,
  runtimeReady: false,
  boxRows: [],
  boxMap: new Map(),
  expandedBoxes: new Set(),
  selectedBoxId: null
};

initTheme();

if (runtime) {
  runtimeBadge.textContent = runtime.label;
  try {
    await runtime.ready;
    state.runtimeReady = true;
    status.textContent = `${runtime.label} module ready. Open an MP4 file.`;
  } catch (error) {
    status.textContent = `Runtime failed to load: ${error.message}`;
    status.classList.add('error');
  }
} else {
  status.textContent = 'WASM runtime adapter did not initialize.';
  status.classList.add('error');
}

fileInput.addEventListener('change', () => {
  const file = fileInput.files?.[0] ?? null;
  setFile(file);
});

document.getElementById('change-file').addEventListener('click', () => {
  fileInput.click();
});

dropZone.addEventListener('dragover', (event) => {
  event.preventDefault();
  dropZone.classList.add('dragging');
});

dropZone.addEventListener('dragleave', () => {
  dropZone.classList.remove('dragging');
});

dropZone.addEventListener('drop', (event) => {
  event.preventDefault();
  dropZone.classList.remove('dragging');
  const file = event.dataTransfer?.files?.[0];
  if (file) {
    setFile(file);
  }
});

inspectButton.addEventListener('click', async () => {
  if (!state.file || !state.runtimeReady) {
    return;
  }

  const started = performance.now();
  inspectButton.disabled = true;
  status.textContent = 'Reading file…';
  status.classList.remove('error');

  try {
    const bytes = new Uint8Array(await state.file.arrayBuffer());
    const options = {
      includeBoxes: true,
      includeSamples: document.getElementById('include-samples').checked,
      maxSamples: readSampleLimit(),
      includeSegments: document.getElementById('include-segments').checked,
      maxSegments: 1000,
      maxDepth: 12
    };

    status.textContent = 'Inspecting…';
    const result = await runtime.inspectBytes(bytes, options);
    result.fileName = state.file.name;
    state.result = result;
    render(result);
    status.textContent = `Inspected ${formatBytes(result.size)} in ${formatMilliseconds(performance.now() - started)}.`;
    status.classList.remove('error');
  } catch (error) {
    status.textContent = `Inspection failed: ${error.message}`;
    status.classList.add('error');
  } finally {
    inspectButton.disabled = false;
  }
});

tabs.addEventListener('click', (event) => {
  const button = event.target.closest('button[data-tab]');
  if (!button) {
    return;
  }

  for (const item of tabs.querySelectorAll('button')) {
    item.classList.toggle('active', item === button);
  }

  for (const pane of document.querySelectorAll('.tab-pane')) {
    pane.hidden = pane.id !== `tab-${button.dataset.tab}`;
  }
});

sampleTrack.addEventListener('change', () => {
  renderSamples();
});

boxSearch.addEventListener('input', () => {
  renderBoxes();
});

boxTree.addEventListener('click', (event) => {
  const row = event.target.closest('.box-row');
  if (!row) {
    return;
  }

  if (event.target.closest('.box-toggle')) {
    toggleBox(row.dataset.id);
    return;
  }

  selectBox(row.dataset.id);
});

boxTree.addEventListener('keydown', (event) => {
  if (event.key !== 'Enter' && event.key !== ' ') {
    return;
  }

  const row = event.target.closest('.box-row');
  if (!row) {
    return;
  }

  event.preventDefault();
  selectBox(row.dataset.id);
});

document.getElementById('expand-boxes').addEventListener('click', () => {
  for (const row of state.boxRows) {
    if (row.hasChildren) {
      state.expandedBoxes.add(row.id);
    }
  }

  renderBoxes();
});

document.getElementById('collapse-boxes').addEventListener('click', () => {
  state.expandedBoxes.clear();
  renderBoxes();
});

document.getElementById('download-json').addEventListener('click', () => {
  if (!state.result) {
    return;
  }

  const blob = new Blob([JSON.stringify(state.result, null, 2)], {
    type: 'application/json'
  });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `${state.file?.name ?? 'input.mp4'}.inspection.json`;
  link.click();
  URL.revokeObjectURL(url);
});

document.getElementById('copy-json').addEventListener('click', async () => {
  if (!state.result) {
    return;
  }

  try {
    await navigator.clipboard.writeText(JSON.stringify(state.result, null, 2));
    status.textContent = 'Inspection JSON copied to clipboard.';
  } catch (error) {
    status.textContent = `Could not copy JSON: ${error.message}`;
    status.classList.add('error');
  }
});

document.getElementById('theme-toggle').addEventListener('click', () => {
  const next = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
  setTheme(next);
});

function setFile(file) {
  state.file = file;
  if (!file) {
    fileRow.hidden = true;
    dropZone.hidden = false;
    inspectButton.disabled = true;
    return;
  }

  fileName.textContent = file.name;
  fileSize.textContent = formatBytes(file.size);
  fileRow.hidden = false;
  dropZone.hidden = true;
  inspectButton.disabled = !state.runtimeReady;
}

function render(result) {
  resultPanel.hidden = false;
  document.getElementById('result-file').textContent = result.fileName;
  document.getElementById('result-title').textContent =
    result.movie.isFragmented ? 'Fragmented MP4' : 'Classic MP4';

  summary.innerHTML = [
    summaryCard('Size', formatBytes(result.size)),
    summaryCard('Brand', result.movie.majorBrand || '—'),
    summaryCard('Tracks', result.movie.trackCount),
    summaryCard('Duration', `${formatNumber(result.movie.durationSeconds)} s`),
    summaryCard('Boxes', result.boxes.length),
    summaryCard('Segments', result.segments.length)
  ].join('');

  warnings.hidden = result.warnings.length === 0;
  warnings.innerHTML = result.warnings.length === 0
    ? ''
    : `<strong>Warnings</strong><ul>${result.warnings
      .map((warning) => `<li>${escapeHtml(warning)}</li>`)
      .join('')}</ul>`;

  state.selectedBoxId = null;
  state.expandedBoxes.clear();
  state.boxRows = flattenBoxes(result.boxes);
  state.boxMap = new Map(state.boxRows.map((row) => [row.id, row]));
  boxSearch.value = '';

  for (const row of state.boxRows) {
    if (row.depth < 2 && row.hasChildren) {
      state.expandedBoxes.add(row.id);
    }
  }

  state.selectedBoxId = state.boxRows[0]?.id ?? null;
  renderOverview(result);
  renderBoxes();
  renderSampleTracks(result);
  renderSamples();
  renderSegments(result);
  rawJson.textContent = JSON.stringify(result, null, 2);
}

function renderOverview(result) {
  const movie = result.movie;
  const details = [
    ['File', result.fileName],
    ['Size', formatBytes(result.size)],
    ['Major brand', movie.majorBrand || '—'],
    ['Minor version', movie.minorVersion],
    ['Layout', movie.isFragmented ? 'Fragmented MP4' : 'Classic MP4'],
    ['Movie timescale', movie.timescale],
    ['Movie duration', `${formatNumber(movie.durationSeconds)} s`],
    ['Tracks', movie.trackCount]
  ];

  const tracks = result.tracks.length === 0
    ? '<div class="empty-state">No readable tracks.</div>'
    : `<div class="track-list">${result.tracks.map((track) => `
        <div class="track-card">
          <div class="track-index">${track.id}</div>
          <div class="track-main">
            <strong>${escapeHtml(track.type)} · ${escapeHtml(track.codec)}</strong>
            <small>${escapeHtml(trackDetails(track))}</small>
          </div>
          <div class="track-meta">
            ${track.timeScale} ticks/s<br>
            ${formatNumber(track.durationSeconds)} s
          </div>
        </div>`).join('')}</div>`;

  document.getElementById('tab-overview').innerHTML = `
    <div class="overview-grid">
      <div class="info-card">
        <h3>File</h3>
        <dl class="info-list">
          ${details.map(([label, value]) => `
            <div><dt>${escapeHtml(label)}</dt><dd>${escapeHtml(value)}</dd></div>`).join('')}
        </dl>
      </div>
      <div class="info-card">
        <h3>Tracks</h3>
        ${tracks}
      </div>
    </div>`;
}

function flattenBoxes(boxes, parentId = '', depth = 0, path = [], target = []) {
  boxes.forEach((box, index) => {
    const id = `${parentId}/${index}`;
    const boxPath = [...path, box.type];
    target.push({
      id,
      parentId,
      depth,
      path: boxPath,
      box,
      hasChildren: box.children.length > 0
    });
    flattenBoxes(box.children, id, depth + 1, boxPath, target);
  });

  return target;
}

function renderBoxes() {
  if (!state.result) {
    return;
  }

  const query = boxSearch.value.trim().toLowerCase();
  const rows = state.boxRows;
  const boxMap = state.boxMap;
  let visibleIds = null;

  if (query.length > 0) {
    visibleIds = new Set();
    for (const row of rows) {
      if (boxSearchText(row.box).includes(query)) {
        let id = row.id;
        while (id) {
          visibleIds.add(id);
          id = boxMap.get(id)?.parentId ?? '';
        }
      }
    }

    for (const id of visibleIds) {
      if (boxMap.get(id)?.hasChildren) {
        state.expandedBoxes.add(id);
      }
    }
  }

  const visibleRows = rows.filter((row) => {
    if (visibleIds && !visibleIds.has(row.id)) {
      return false;
    }

    let parentId = row.parentId;
    while (parentId) {
      if (!state.expandedBoxes.has(parentId)) {
        return false;
      }

      parentId = boxMap.get(parentId)?.parentId ?? '';
    }

    return true;
  });

  boxCount.textContent = visibleIds
    ? `${visibleRows.length} / ${rows.length}`
    : `${rows.length} boxes`;
  boxTree.innerHTML = visibleRows.length === 0
    ? '<div class="empty-state">No boxes matched.</div>'
    : visibleRows.map(renderBoxRow).join('');
  renderBoxProperties();
}

function renderBoxRow(row) {
  const expanded = state.expandedBoxes.has(row.id);
  const selected = state.selectedBoxId === row.id;
  const toggle = row.hasChildren
    ? `<button class="box-toggle" type="button" aria-label="${expanded ? 'Collapse' : 'Expand'} ${escapeHtml(row.box.type)}">${expanded ? '▾' : '▸'}</button>`
    : '<span class="box-toggle-spacer" aria-hidden="true"></span>';

  return `<div class="box-row${selected ? ' selected' : ''}"
      data-id="${escapeHtml(row.id)}"
      role="treeitem"
      tabindex="0"
      aria-expanded="${row.hasChildren ? String(expanded) : undefined}"
      style="--depth:${row.depth}">
    ${toggle}
    <span class="box-type">${escapeHtml(row.box.type)}</span>
    <span class="box-name">(${escapeHtml(boxClassName(row.box.type))})</span>
    <span class="box-meta">@${row.box.offset} · ${formatBytes(row.box.size)}</span>
  </div>`;
}

function renderBoxProperties() {
  const row = state.boxMap?.get(state.selectedBoxId);
  if (!row) {
    boxPath.textContent = '';
    boxProperties.innerHTML =
      '<div class="empty-state">Select a box in the tree to inspect its properties.</div>';
    return;
  }

  const box = row.box;
  const properties = [
    ['type', box.type],
    ['box_name', boxClassName(box.type)],
    ['start', box.offset],
    ['size', box.size],
    ['header_size', box.headerSize],
    ['payload_size', box.size - box.headerSize],
    ['version', box.version],
    ['flags', box.flags]
  ];

  for (const field of box.fields) {
    properties.push([field.name, field.value]);
  }

  if (box.truncated) {
    properties.push(['truncated', 'true']);
  }

  boxPath.textContent = row.path.join(' / ');
  boxProperties.innerHTML = `
    <table class="property-table">
      <thead><tr><th>Property name</th><th>Property value</th></tr></thead>
      <tbody>${properties.map(([name, value]) => `
        <tr><td>${escapeHtml(name)}</td><td>${escapeHtml(value)}</td></tr>`).join('')}
      </tbody>
    </table>`;
}

function toggleBox(id) {
  if (state.expandedBoxes.has(id)) {
    state.expandedBoxes.delete(id);
  } else {
    state.expandedBoxes.add(id);
  }

  renderBoxes();
}

function selectBox(id) {
  state.selectedBoxId = id;
  renderBoxes();
}

function boxSearchText(box) {
  return `${box.type} ${boxClassName(box.type)} ${box.fields
    .map((field) => `${field.name}=${field.value}`)
    .join(' ')}`.toLowerCase();
}

function boxClassName(type) {
  return BOX_CLASS_NAMES[type] ?? 'Box';
}

const BOX_CLASS_NAMES = {
  ftyp: 'FileTypeBox',
  styp: 'SegmentTypeBox',
  moov: 'MovieBox',
  mvhd: 'MovieHeaderBox',
  trak: 'TrackBox',
  tkhd: 'TrackHeaderBox',
  edts: 'EditBox',
  elst: 'EditListBox',
  mdia: 'MediaBox',
  mdhd: 'MediaHeaderBox',
  hdlr: 'HandlerBox',
  minf: 'MediaInformationBox',
  vmhd: 'VideoMediaHeaderBox',
  smhd: 'SoundMediaHeaderBox',
  nmhd: 'NullMediaHeaderBox',
  dinf: 'DataInformationBox',
  dref: 'DataReferenceBox',
  'url ': 'DataEntryUrlBox',
  stbl: 'SampleTableBox',
  stsd: 'SampleDescriptionBox',
  avc1: 'VisualSampleEntry',
  hvc1: 'VisualSampleEntry',
  mp4a: 'AudioSampleEntry',
  avcC: 'AVCConfigurationBox',
  hvcC: 'HEVCConfigurationBox',
  esds: 'ESDescriptorBox',
  stts: 'TimeToSampleBox',
  ctts: 'CompositionOffsetBox',
  stsc: 'SampleToChunkBox',
  stsz: 'SampleSizeBox',
  stco: 'ChunkOffsetBox',
  co64: 'ChunkOffset64Box',
  stss: 'SyncSampleBox',
  mvex: 'MovieExtendsBox',
  trex: 'TrackExtendsBox',
  moof: 'MovieFragmentBox',
  mfhd: 'MovieFragmentHeaderBox',
  traf: 'TrackFragmentBox',
  tfhd: 'TrackFragmentHeaderBox',
  tfdt: 'TrackFragmentDecodeTimeBox',
  trun: 'TrackRunBox',
  sidx: 'SegmentIndexBox',
  mfra: 'MovieFragmentRandomAccessBox',
  tfra: 'TrackFragmentRandomAccessBox',
  mfro: 'MovieFragmentRandomAccessOffsetBox',
  mdat: 'MediaDataBox',
  free: 'FreeSpaceBox'
};

function renderSampleTracks(result) {
  const current = sampleTrack.value;
  sampleTrack.innerHTML = [
    '<option value="">All tracks</option>',
    ...result.tracks.map((track) =>
      `<option value="${track.id}">Track ${track.id} · ${escapeHtml(track.type)} · ${escapeHtml(track.codec)}</option>`)
  ].join('');
  sampleTrack.value = current;
}

function renderSamples() {
  if (!state.result) {
    return;
  }

  const track = sampleTrack.value;
  const samples = track === ''
    ? state.result.samples
    : state.result.samples.filter((sample) => String(sample.track) === track);

  sampleNote.textContent = `${samples.length} shown / ${state.result.samples.length} read`;
  if (samples.length === 0) {
    document.getElementById('sample-table').innerHTML =
      '<div class="empty-state">Samples were not requested or none were found. Enable “Read sample metadata” before inspecting.</div>';
    return;
  }

  document.getElementById('sample-table').innerHTML = `
    <table>
      <thead><tr>
        <th>Track</th><th>PTS</th><th>DTS</th><th>Duration</th><th>Size</th><th>Key</th>
      </tr></thead>
      <tbody>${samples.map((sample) => `<tr>
        <td>${sample.track}</td>
        <td class="mono">${sample.pts}</td>
        <td class="mono">${sample.dts}</td>
        <td class="mono">${sample.duration}</td>
        <td class="mono">${formatBytes(sample.size)}</td>
        <td>${sample.keyFrame
          ? '<span class="badge success">key</span>'
          : '<span class="badge muted">delta</span>'}</td>
      </tr>`).join('')}</tbody>
    </table>`;
}

function renderSegments(result) {
  if (result.segments.length === 0) {
    document.getElementById('segment-table').innerHTML =
      '<div class="empty-state">No moof+mdat segments were found.</div>';
    return;
  }

  document.getElementById('segment-table').innerHTML = `
    <table>
      <thead><tr>
        <th>Seq</th><th>Offset</th><th>Size</th><th>MOOF</th><th>MDAT</th>
        <th>Samples</th><th>Duration</th><th>Tracks</th>
      </tr></thead>
      <tbody>${result.segments.map((segment) => `<tr>
        <td>${segment.sequenceNumber}</td>
        <td class="mono">${segment.offset}</td>
        <td class="mono">${formatBytes(segment.size)}</td>
        <td class="mono">${segment.moofOffset}</td>
        <td class="mono">${segment.mdatOffset}</td>
        <td>${segment.sampleCount}</td>
        <td class="mono">${segment.duration}</td>
        <td>${segment.tracks.length === 0
          ? '—'
          : segment.tracks.map((track) => `<span class="badge">${track}</span>`).join(' ')}</td>
      </tr>`).join('')}</tbody>
    </table>`;
}

function summaryCard(label, value) {
  return `<div class="summary-card"><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`;
}

function trackDetails(track) {
  if (track.width || track.height) {
    return `${track.width}×${track.height} · ${track.language || 'und'}`;
  }

  if (track.channels || track.sampleRate) {
    return `${track.channels} ch · ${track.sampleRate} Hz · ${track.language || 'und'}`;
  }

  return track.language || 'und';
}

function readSampleLimit() {
  const value = Number(document.getElementById('max-samples').value);
  return Number.isFinite(value) && value > 0 ? Math.floor(value) : 1000;
}

function formatBytes(value) {
  if (value < 1024) {
    return `${value} B`;
  }

  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KiB`;
  }

  return `${(value / (1024 * 1024)).toFixed(2)} MiB`;
}

function formatNumber(value) {
  return Number(value ?? 0).toFixed(3).replace(/\.?0+$/, '');
}

function formatMilliseconds(value) {
  return value < 1000
    ? `${Math.round(value)} ms`
    : `${(value / 1000).toFixed(2)} s`;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function initTheme() {
  let stored = null;
  try {
    stored = localStorage.getItem('mp4boxkit-theme');
  } catch {
    stored = null;
  }

  const preferred = globalThis.matchMedia?.('(prefers-color-scheme: dark)').matches
    ? 'dark'
    : 'light';
  setTheme(stored ?? preferred);
}

function setTheme(theme) {
  document.documentElement.dataset.theme = theme;
  try {
    localStorage.setItem('mp4boxkit-theme', theme);
  } catch {
    // Theme persistence is optional.
  }

  document.getElementById('theme-icon').textContent = theme === 'dark' ? '☀' : '◐';
}
