// Simulation test for OfflineVehiclesService core logic.
// Pure Node (no Angular, no RxJS) — duplicates the filter/sort/remove algorithm
// from offline-vehicles.service.ts and verifies behavior against fixtures.
//
// Run: node src/services/offline-vehicles.service.sim.mjs

function filterAndSort(vehicles) {
  const offline = (vehicles || []).filter(v => !v.isOnline);
  offline.sort((a, b) => {
    const ta = a.lastPosition?.recordedAt ? new Date(a.lastPosition.recordedAt).getTime() : 0;
    const tb = b.lastPosition?.recordedAt ? new Date(b.lastPosition.recordedAt).getTime() : 0;
    return ta - tb; // oldest first = longest offline first
  });
  return offline;
}

function markOnline(offlineList, vehicleId) {
  const idx = offlineList.findIndex(v => v.id === vehicleId);
  if (idx === -1) return offlineList; // no-op
  return [...offlineList.slice(0, idx), ...offlineList.slice(idx + 1)];
}

function formatOfflineSince(v, now = Date.now()) {
  const recordedAt = v.lastPosition?.recordedAt;
  if (!recordedAt) return 'Dernière position inconnue';
  const diffMs = now - new Date(recordedAt).getTime();
  if (diffMs < 0 || !isFinite(diffMs)) return 'Hors ligne';
  const minutes = Math.floor(diffMs / 60000);
  if (minutes < 1) return 'Hors ligne depuis quelques secondes';
  if (minutes < 60) return `Hors ligne depuis ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `Hors ligne depuis ${hours} h`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `Hors ligne depuis ${days} j`;
  const months = Math.floor(days / 30);
  return `Hors ligne depuis ${months} mois`;
}

// ----- Test harness -----
let pass = 0, fail = 0;
function assert(name, cond, detail = '') {
  if (cond) { console.log(`  PASS  ${name}`); pass++; }
  else      { console.log(`  FAIL  ${name}${detail ? ' — ' + detail : ''}`); fail++; }
}
function group(title, fn) {
  console.log(`\n[${title}]`);
  fn();
}

// ----- Fixtures (realistic tenant with 5 vehicles) -----
const NOW = new Date('2026-04-23T16:00:00Z').getTime();
const MIN_5   = new Date(NOW - 5 * 60 * 1000).toISOString();
const HOUR_2  = new Date(NOW - 2 * 60 * 60 * 1000).toISOString();
const HOUR_12 = new Date(NOW - 12 * 60 * 60 * 1000).toISOString();
const DAY_3   = new Date(NOW - 3 * 24 * 60 * 60 * 1000).toISOString();
const DAY_45  = new Date(NOW - 45 * 24 * 60 * 60 * 1000).toISOString();

const fixture = [
  { id: 1, name: '229 TU 9662', plate: 'A3',    isOnline: true,  lastPosition: { recordedAt: MIN_5 } },
  { id: 2, name: '234 TU 4624', plate: 'Tiguan',isOnline: false, lastPosition: { recordedAt: HOUR_2 } },
  { id: 3, name: '236 TU 6523', plate: 'IBIZA', isOnline: false, lastPosition: { recordedAt: DAY_3 } },
  { id: 4, name: '236 TU 6527', plate: 'IBIZA', isOnline: false, lastPosition: { recordedAt: HOUR_12 } },
  { id: 5, name: 'Legacy',      plate: 'OLD',   isOnline: false, lastPosition: null },
];

// ----- Scenarios -----
group('filterAndSort — keeps only offline vehicles', () => {
  const r = filterAndSort(fixture);
  assert('excludes online vehicles', r.every(v => !v.isOnline));
  assert('correct count (4 offline out of 5)', r.length === 4, `got ${r.length}`);
});

group('filterAndSort — sort order: oldest recordedAt first (= longest offline first)', () => {
  const r = filterAndSort(fixture);
  // With vehicle 5 (no lastPosition, time=0) first, then DAY_3, HOUR_12, HOUR_2
  assert('#1 = vehicle with unknown last position (treated as oldest)', r[0]?.id === 5, `got id=${r[0]?.id}`);
  assert('#2 = 3 days offline (id=3)',  r[1]?.id === 3, `got id=${r[1]?.id}`);
  assert('#3 = 12 hours offline (id=4)', r[2]?.id === 4, `got id=${r[2]?.id}`);
  assert('#4 = 2 hours offline (id=2)',  r[3]?.id === 2, `got id=${r[3]?.id}`);
});

group('markOnline — removes vehicle from offline list when a SignalR update arrives', () => {
  const offline = filterAndSort(fixture);
  assert('initial count = 4', offline.length === 4);
  const after1 = markOnline(offline, 3);
  assert('after id=3 update → count = 3', after1.length === 3, `got ${after1.length}`);
  assert('after id=3 update → id=3 absent', !after1.some(v => v.id === 3));
  const after2 = markOnline(after1, 2);
  assert('after id=2 update → count = 2', after2.length === 2);
  const after3 = markOnline(after2, 999);
  assert('unknown id → no-op (count still = 2)', after3.length === 2);
  assert('immutability: original list unchanged', offline.length === 4);
});

group('formatOfflineSince — human-readable durations', () => {
  assert('null lastPosition → inconnue',
    formatOfflineSince({ lastPosition: null }, NOW) === 'Dernière position inconnue');
  assert('2 hours',
    formatOfflineSince({ lastPosition: { recordedAt: HOUR_2 } }, NOW) === 'Hors ligne depuis 2 h');
  assert('3 days',
    formatOfflineSince({ lastPosition: { recordedAt: DAY_3 } }, NOW) === 'Hors ligne depuis 3 j');
  assert('45 days = 1 mois',
    formatOfflineSince({ lastPosition: { recordedAt: DAY_45 } }, NOW) === 'Hors ligne depuis 1 mois',
    `got "${formatOfflineSince({ lastPosition: { recordedAt: DAY_45 } }, NOW)}"`);
  assert('5 minutes',
    formatOfflineSince({ lastPosition: { recordedAt: MIN_5 } }, NOW) === 'Hors ligne depuis 5 min');
  assert('30 seconds → "quelques secondes"',
    formatOfflineSince({ lastPosition: { recordedAt: new Date(NOW - 30 * 1000).toISOString() } }, NOW)
      === 'Hors ligne depuis quelques secondes');
});

group('Edge cases', () => {
  assert('empty input → empty list', filterAndSort([]).length === 0);
  assert('null input → empty list', filterAndSort(null).length === 0);
  assert('all online → empty list',
    filterAndSort([{ id: 1, isOnline: true, lastPosition: { recordedAt: MIN_5 } }]).length === 0);
  assert('all offline → all returned',
    filterAndSort([
      { id: 1, isOnline: false, lastPosition: { recordedAt: HOUR_2 } },
      { id: 2, isOnline: false, lastPosition: { recordedAt: DAY_3 } },
    ]).length === 2);
});

console.log(`\n${pass}/${pass + fail} tests passed${fail > 0 ? ` — ${fail} FAILED` : ''}`);
process.exit(fail > 0 ? 1 : 0);
