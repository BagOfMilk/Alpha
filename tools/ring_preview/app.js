// Сторінка анімацій «Рингу»: інтерфейс, режими «Кліпи / Переходи / Мікси», ланцюг бою, window.ring для перевірки.
import { THREE, makeStage, AnimLib, Actor, loadGLB, RING, placeRel, wrapPi, smooth, BONE, solveTwoBone } from './engine.js';

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const f2 = (n, d = 2) => Number(n).toFixed(d).replace('.', ',');
const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const el = (tag, cls, html) => { const e = document.createElement(tag); if (cls) e.className = cls; if (html != null) e.innerHTML = html; return e; };

const CAT = [
  ['idle', 'Спокій'], ['locomotion', 'Ходьба (UAL)'], ['move', 'Рух у стійці'], ['run', 'Біг'], ['stance', 'Стійка'], ['guard', 'Захист'],
  ['transition', 'Переходи між позами'], ['strike', 'Удари'], ['lockup', 'Захоплення (локап)'], ['chain', 'Ланцюгові холди'], ['center', 'Центр рингу'],
  ['whip', 'Кидки в канати'], ['rope', 'Канати'], ['corner', 'Кут'], ['air', 'Верхні канати'], ['bump', 'Падіння (бампи)'], ['getup', 'Вставання'],
  ['throw', 'Кидки'], ['pin', 'Укриття (пін)'], ['hold', 'Утримання'], ['react', 'Реакції'], ['taunt', 'Глузування'], ['promo', 'Промо'],
  ['entrance', 'Вихід'], ['ref', 'Суддя'], ['weapon', 'Зброя'], ['prop', 'Реквізит'], ['sit', 'Сидячи'], ['layer', 'Шари'], ['misc', 'Інше'],
];
const CATN = Object.fromEntries(CAT);
const PROP_NODES = { spray: ['spray_tank', 'spray_gun'] };
const PROP_LABEL = { belt: 'Пояс чемпіона', chair: 'Стілець', kendo: 'Кендо (бамбуковий меч)', case: 'Кейс', mic: 'Мікрофон', spray: 'Балон і пульверизатор' };
const ROLE_UA = { hero: 'герой', villain: 'лиходій' };

const S = {
  data: null, tokens: null, libs: {}, actors: {}, avail: {}, propsLib: null, propsState: 'невідомо',
  me: null, meId: null, partner: null, partnerId: null, mode: 'clips', stage: null,
  paused: false, speed: 1, repeat: true, roam: true, follow: false, ik: false, stairs: false, dmg: 0, outer: true, propSel: '', heldMode: 'free',
  cur: null, pairActors: null, bout: null, trans: null, edgeRun: null, slide: null, mix: {}, errors: [], last: 0, lastFrame: 0, sim: 0,
  contact: null, restartAt: null, pairInfo: null, mixTimers: {}, ready: false,
};

// ---------------------------------------------------------------- допоміжне
function status(msg, err = false) { const e = $('#status'); e.textContent = msg; e.classList.toggle('err', err); }
function overlay(msg, err = false, hide = false) { const o = $('#overlay'); o.hidden = hide; o.classList.toggle('err', err); $('#overlayTxt').textContent = msg || ''; }
window.addEventListener('error', (e) => { S.errors.push(String(e.message)); status('Помилка: ' + e.message, true); });
window.addEventListener('unhandledrejection', (e) => { const m = String(e.reason && e.reason.stack || e.reason); S.errors.push(m); status('Помилка: ' + (e.reason && e.reason.message || e.reason), true); });

const texLoader = new THREE.TextureLoader(); const texC = new Map();
function tex(url, srgb) {
  if (!texC.has(url)) { let done; const ready = new Promise((r) => { done = r; }); const t = texLoader.load(url, () => done(t), undefined, () => done(t)); t.ready = ready; t.flipY = false; t.colorSpace = srgb ? THREE.SRGBColorSpace : THREE.NoColorSpace; t.anisotropy = 4; texC.set(url, t); }
  return texC.get(url);
}
const jget = async (u) => { const r = await fetch(u); if (!r.ok) throw new Error(`${u}: ${r.status}`); return r.json(); };
const meta = (a, n) => a && a.metaOf(n);

// ---------------------------------------------------------------- завантаження даних і акторів
async function ensureLib(sex) {
  if (!S.libs[sex]) { S.libs[sex] = AnimLib.load(sex, S.data.clips[sex]); }
  return S.libs[sex];
}
async function ensureActor(id) {
  if (S.actors[id]) return S.actors[id];
  if (!S.avail[id]) return null;
  const cfg = S.data.cast.characters[id];
  const lib = await ensureLib(cfg.skeleton);
  if (S.actors[id]) return S.actors[id];
  const a = await Actor.create(id, cfg, lib, S.data.clips[cfg.skeleton], S.data.graph);
  a.terrain = S.stage.terrain; a.onEvent = onEvent; a.holder.visible = false;
  S.stage.scene.add(a.holder); S.actors[id] = a;
  a.setDamage(S.dmg, tex); a.setOuterVisible(S.outer);
  return a;
}
async function loadProps() {
  try {
    const h = await fetch('data/props.glb', { method: 'HEAD' });
    if (!h.ok) { S.propsState = 'props.glb ще немає'; return; }
    const g = await loadGLB('data/props.glb'); const m = new Map();
    const WANT = ['belt', 'chair', 'kendo', 'case', 'mic', 'spray_tank', 'spray_gun'];
    g.scene.traverse((o) => { const n = o.name && o.name.startsWith('Prop_') ? o.name.slice(5) : null; if (n && WANT.includes(n) && !m.has(n)) m.set(n, o); });
    S.propsLib = m; S.propsState = 'реквізит: ' + [...m.keys()].join(', ');
  } catch (e) { S.propsState = 'props.glb не прочитано'; }
}

// ---------------------------------------------------------------- вибір борця
async function selectActor(id, opts = {}) {
  S.meId = id;
  // «Авто»: партнер підбирається наново під нового борця; вибраний власноруч лишається, якщо це не він сам
  if (!S.partnerManual || S.partnerManual === id) { S.partnerId = null; S.partner = null; }
  renderCast(); buildClipList(); fillPartner();
  const cfg = S.data.cast.characters[id];
  S.stage.setRulerHeight(cfg.height);
  S.bout = null; S.slide = null; S.trans = null;
  for (const a of Object.values(S.actors)) a.holder.visible = false;
  if (!S.avail[id]) {
    S.me = null; overlay(`Модель «${cfg.name}» ще збирається.`, true); status('Модель ще збирається'); refreshAll(); return;
  }
  overlay(`Завантаження: ${cfg.name}…`);
  try {
    S.me = await ensureActor(id);
  } catch (e) { overlay('Не вдалося завантажити: ' + e.message, true); S.errors.push(String(e)); return; }
  overlay('', false, true);
  if (S.partner === S.me) S.partner = null;
  S.me.holder.visible = true;
  if (S.meId !== id) return;
  syncLook(); fillPartner();
  enterMode(S.mode, true);
  status(`${cfg.name} · ${S.propsState}`);
}
function pickPartner() {
  const me = S.me; if (!me) return null;
  if (S.partnerId && S.partnerId !== S.meId && S.avail[S.partnerId]) return S.partnerId;
  const ids = S.data.cast.order.filter((i) => i !== S.meId && S.avail[i] && S.data.cast.characters[i].role === 'wrestler');
  const sex = me.cfg.skeleton;
  ids.sort((a, b) => {
    const ca = S.data.cast.characters[a], cb = S.data.cast.characters[b];
    const sa = ca.skeleton === sex ? 0 : 1, sb = cb.skeleton === sex ? 0 : 1;
    return sa - sb || Math.abs(ca.height - me.cfg.height) - Math.abs(cb.height - me.cfg.height);
  });
  return ids[0] || null;
}
async function ensurePartner() {
  const id = pickPartner(); if (!id) return null;
  S.partnerId = id; fillPartner();
  S.partner = await ensureActor(id);
  return S.partner;
}
function fillPartner() {
  const sel = $('#partnerSel'); const prev = S.partnerId;
  const ids = S.data.cast.order.filter((i) => i !== S.meId && S.data.cast.characters[i].role === 'wrestler');
  sel.innerHTML = '<option value="">Авто (той самий скелет, найближчий зріст)</option>' + ids.map((i) => { const c = S.data.cast.characters[i]; return `<option value="${i}" ${S.avail[i] ? '' : 'disabled'}>${esc(c.name)} · ${f2(c.height)} м${S.avail[i] ? '' : ' (модель ще збирається)'}</option>`; }).join('');
  sel.value = S.partnerManual && ids.includes(S.partnerManual) ? S.partnerManual : '';
  if (!sel.value) S.partnerId = null;
  void prev;
}

// ---------------------------------------------------------------- вигляд (пошкодження, реквізит, халат)
function activeActors() { const r = []; if (S.me) r.push(S.me); if (S.partner && S.pairActors || S.partner && S.bout) r.push(S.partner); return r; }
function propNodesFor(a, name) { return PROP_NODES[name] || [name]; }
function wantedProps(a) {
  const w = [];
  if (a === S.me) {
    if (S.propSel) w.push(...propNodesFor(a, S.propSel));
    const m = S.cur && S.cur.name && meta(a, S.cur.name);
    const layerM = a.layer && a.layer.meta;
    for (const mm of [m, layerM, S.mixProp]) if (mm && mm.prop) w.push(...propNodesFor(a, mm.prop));
    if (S.mixUpMeta && S.mixUpMeta.prop) w.push(...propNodesFor(a, S.mixUpMeta.prop));
  }
  return [...new Set(w)];
}
function syncLook() {
  for (const a of Object.values(S.actors)) {
    a.setDamage(S.dmg, tex); a.setOuterVisible(S.outer);
    a.inPlace = !S.roam && !S.stairs; a.steer = S.roam; a.ik.on = S.ik;
    a.setProps(wantedProps(a), S.propsLib);
  }
  S.stage.terrain.stairs = S.stairs; S.stage.stairs.visible = S.stairs;
  S.stage.grid.visible = $('#gridChk').checked; S.stage.ruler.visible = $('#rulerChk').checked;
  const me = S.me;
  $('#rowOuter').hidden = !(me && me.hasOuter());
  const cfg = S.data.cast.characters[S.meId];
  const props = cfg ? cfg.props || [] : [];
  $('#rowProp').hidden = !props.length;
  $('#propSel').innerHTML = '<option value="">Без реквізиту (кліп покаже свій)</option>' + props.map((p) => `<option value="${p}">${PROP_LABEL[p] || p}</option>`).join('');
  if (!props.includes(S.propSel)) S.propSel = '';
  $('#propSel').value = S.propSel;
  $('#heldBox').hidden = !(me && me.hasHeld());
}
function updatePropsOnly() { for (const a of Object.values(S.actors)) a.setProps(wantedProps(a), S.propsLib); }

// ---------------------------------------------------------------- старт кліпів
function reachEnd(a, name) {      // світова поза в кінці кліпа з початку (0,0,0)
  const m = meta(a, name) || {}; return placeRel({ x: 0, z: 0, yaw: 0 }, m.net || [0, 0, 0], a.hs);
}
// Місце в куті: спиною до стійки (-H, -H), обличчям у ринг. dist — відстань таза від стійки по діагоналі.
function cornerSpot(dist) {
  const H = RING.post, d = Math.SQRT2 * H - dist;
  return { x: -d * Math.SQRT1_2, z: -d * Math.SQRT1_2, yaw: Math.PI / 4 };
}
function startPlacement(a, name) {
  const m = meta(a, name) || {};
  // кліпи, що відбуваються в куті: без кута вони читаються як рух у повітрі
  if (/^(Corner_Climb_Top|Top_Rope_Perch_Loop|Top_Dive_)/.test(name)) return cornerSpot(0.76 * a.hs);
  if (/^Move_Corner_(Lean_Loop|Exit)/.test(name)) return cornerSpot(0.42 * a.hs);
  if (/^Move_Corner_Enter/.test(name)) { const c = cornerSpot(0.42 * a.hs); const n = m.net || [0, 0, 0]; const e = placeRel({ x: 0, z: 0, yaw: c.yaw }, n, a.hs); return { x: c.x - e.x, z: c.z - e.z, yaw: c.yaw }; }
  if (/^Corner_Strike/.test(name)) { const c = cornerSpot(1.05 * a.hs); return { x: c.x, z: c.z, yaw: c.yaw + Math.PI }; }
  if (m.loop) return { x: 0, z: 0, yaw: 0 };
  // Вихід на ринг. Сходи й підйом на фартух починаються на підлозі зали, лазка в канати — на лінії канатів (ring_anims_entrance.py).
  if (name === 'Entrance_Climb_Steps' && RING.stepsSpot) { const q = RING.stepsSpot; return { x: q.x, z: q.z, yaw: Math.atan2(-q.x, -q.z), floor: true }; }
  if (name === 'U_ClimbUp_1m_RM') return { x: 0, z: -(RING.half - 0.15) - Math.abs((m.net || [0, 0])[1]) * a.hs, yaw: 0, floor: true };
  // Площина канатів — за 0,18 / 0,32 зросту попереду старту; фартух у справжнього рингу вужчий за задуманий, тож стопи лишаємо на ньому
  if (name === 'Entrance_Through_Ropes') return { x: 0, z: -Math.min(RING.rope + 0.18 * a.hs, RING.half - 0.06), yaw: 0 };
  if (name === 'Entrance_Vault_Over_Top') return { x: 0, z: -Math.min(RING.rope + 0.32 * a.hs, RING.half - 0.06), yaw: 0 };
  if (name === 'Entrance_Roll_In') return { x: 0, z: -(RING.half + 0.36), yaw: 0, floor: true };   // із підлоги біля фартуха
  if (/^Entrance_Pose_Turnbuckle/.test(name)) { const d = RING.rope * Math.SQRT2 - 0.9 * a.hs; return { x: -d * Math.SQRT1_2, z: -d * Math.SQRT1_2, yaw: -0.75 * Math.PI, second: true }; }
  if (name === 'Entrance_Corner_Pose') return cornerSpot(0.95 * a.hs);
  if (isRopeRun(name, m) && m.root) {
    let far = 0, fx = 0, fz = 0;
    for (const r of m.root) { const x = r[0] * a.hs, z = -r[1] * a.hs, d = Math.hypot(x, z); if (d > far) { far = d; fx = x; fz = z; } }
    const phi = Math.atan2(fx, fz), yaw = -phi;
    // найглибша точка кліпу — трохи за лінією каната: канат прогинається під тілом (ropes.js)
    return { x: 0, z: Math.max(-1.5, Math.min(1.8, RING.rope + 0.1 - far)), yaw };
  }
  const e = reachEnd(a, name); return { x: -e.x / 2, z: -e.z / 2, yaw: 0 };
}
function isRopeRun(name, m) { return m.category === 'rope' || name === 'Move_Rope_Rebound'; }
// Кліп із підйомом: ступні в кліпі піднімаються на rise × зріст; сторінка веде постать із підлоги зали на фартух
function startClimb(a, name) { a.yRamp = { name, from: -RING.mat, to: -a.hs, k: 1 / 0.86 }; a.yOff = -RING.mat; a.apply(); }
// Кліп ставить стопи на другий канат за зростом постаті, а канат на своїй висоті: стопи опускаємо на нього
function startOnSecondRope(a, name) { a.yRamp = { name, from: 0, to: 0.86 * (1 - a.hs), k: 1 / 0.35 }; a.apply(); }
// Верхній канат: кліп ставить стопи на висоту за зростом постаті, а канат скрізь на своїй висоті. Стопи — на канат.
function perchY(a) { return 1.31 - (a.cfg.sex === 'f' ? 1.034 : 1.145) * a.hs; }
function applyTopRope(a, name) {
  const y = perchY(a);
  if (name === 'Corner_Climb_Top') a.yRamp = { name, from: a.yOff || 0, to: y, k: 1 };
  else if (name === 'Top_Rope_Perch_Loop') { a.yRamp = null; a.yOff = y; }
  else if (/^Top_Dive_/.test(name)) a.yRamp = { name, from: y, to: 0, k: 1 / 0.45 };
  else return;
  a.apply();
}
// Руки на верхньому канаті: кліп тримає кисті на висоті каната свого зросту, справжній канат нижчий
const WHIP_GRIP = /^Whip_Corner(_Fast|_Two_Hand|_Resist|_Left)?_B$/;
function gripWeight(v) {
  const u = v.pose() / v.dur;
  if (v.name === 'Move_Corner_Lean_Loop') return 1;
  if (v.name === 'Move_Corner_Enter') return smooth((u - 0.55) / 0.35);
  if (v.name === 'Move_Corner_Exit') return 1 - smooth(u / 0.3);
  if (WHIP_GRIP.test(v.name)) return smooth((u - 0.72) / 0.2);
  return 0;
}
const _gh = new THREE.Vector3(), _gt = new THREE.Vector3();
function ropeGrip(a) {
  const v = a.curBase(); if (!v) return;
  const w = gripWeight(v); if (w <= 0.001) return;
  a.holder.updateMatrixWorld(true);
  for (const side of ['Left', 'Right']) {
    const up = a.bones[`mixamorig${side}Arm`], mid = a.bones[`mixamorig${side}ForeArm`], end = a.bones[`mixamorig${side}Hand`];
    if (!up || !mid || !end) continue;
    _gh.setFromMatrixPosition(end.matrixWorld);
    const n = S.stage.ropes.nearestTop([_gh.x, _gh.y, _gh.z]);
    if (!n || n.d > 0.75) continue;                       // канат далеко (кліп не в куті): руку не чіпаємо
    _gt.set(n.p[0], n.p[1] + 0.04, n.p[2]).lerp(_gh, 1 - w);
    solveTwoBone(up, mid, end, _gt);
  }
  a.holder.updateMatrixWorld(true);
}
function baseFor(layerName) {
  if (/walk/i.test(layerName)) return 'U_Walk_Loop';
  if (/^Held_Sit/.test(layerName)) return 'Sit_Mat_Idle_Loop';
  if (/^Held_Kneel/.test(layerName)) return 'Kneel_Idle_Loop';
  if (/^Held_Stand|^Layer_Held|^Corner_Strike/.test(layerName)) return 'Ring_Stance_Loop';
  return 'U_Idle_Loop';
}
function resetPartner() { if (S.partner) { S.partner.reset(); S.partner.holder.visible = false; } S.pairActors = null; S.slide = null; }

function playClip(name, o = {}) {
  const me = S.me; if (!me) { status('Моделі ще немає: нічого програвати', true); return; }
  const m = meta(me, name); if (!m) { status('Немає кліпа: ' + name, true); return; }
  S.bout = null; S.trans = null; S.restartAt = null; S.contact = null;
  me.reset(); resetPartner();
  S.mixProp = null; S.mixUpMeta = null;
  syncLook();
  S.cur = { name };
  if (m.role === 'A' || m.role === 'B') { playPair(m.base, m.role); return; }
  const p = startPlacement(me, name);
  me.place(p.x, p.z, p.yaw);
  me.play(name, { fade: 0 });
  if (p.floor) startClimb(me, name);
  if (p.second) startOnSecondRope(me, name);
  applyTopRope(me, name);
  S.heldMode = 'free'; syncHeld(); updatePropsOnly();
  if (S.paused && o.keepPaused === false) S.paused = false;
  refreshAll();
}

async function playPair(base, roleMe) {
  const me = S.me; const p = S.partner || await ensurePartner();
  if (me !== S.me) return;
  if (!p) { status('Немає партнера з готовою моделлю', true); return; }
  const roleP = roleMe === 'A' ? 'B' : 'A';
  const nMe = `${base}_${roleMe}`, nP = `${base}_${roleP}`;
  if (!p.has(nP) || !me.has(nMe)) { status(`У партнера немає кліпа ${nP}`, true); return; }
  p.holder.visible = true; me.reset(); p.reset();
  const A = roleMe === 'A' ? me : p, B = roleMe === 'A' ? p : me;
  const mA = A.metaOf(`${base}_A`), mB = B.metaOf(`${base}_B`);
  const off = mA.offset || [0, 0, 0], rot = (mA.rot || 0) * Math.PI / 180, s = (A.hs + B.hs) / 2;
  const A0 = { x: 0, z: 0, yaw: 0 };
  const Bp = placeRel(A0, [off[0], off[1], 0], s); const B0 = { x: Bp.x, z: Bp.z, yaw: rot };
  const eA = placeRel(A0, mA.net || [0, 0, 0], A.hs), eB = placeRel(B0, mB.net || [0, 0, 0], B.hs);
  const xs = [A0.x, B0.x, eA.x, eB.x], zs = [A0.z, B0.z, eA.z, eB.z];
  const cx = (Math.min(...xs) + Math.max(...xs)) / 2, cz = (Math.min(...zs) + Math.max(...zs)) / 2;
  // Кут і канати: пара обертається й зсувається так, щоб B закінчив біля кута чи каната. Решта кліпів — посередині рингу.
  let ph = 0, tx = -cx, tz = -cz;
  const toTarget = (ux, uz, gx, gz, P = eB) => {
    ph = wrapPi(Math.atan2(ux, uz) - Math.atan2(P.x, P.z));
    const c = Math.cos(ph), sn = Math.sin(ph);
    tx = gx - (P.x * c + P.z * sn); tz = gz - (-P.x * sn + P.z * c);
  };
  if (/^Whip_Corner/.test(base)) toTarget(Math.SQRT1_2, Math.SQRT1_2, RING.post - 0.32 * B.hs, RING.post - 0.32 * B.hs);
  else if (/^Lockup_(To_Ropes|Drive_Back_Ropes)/.test(base)) toTarget(0, 1, 0, RING.rope - 0.26 * B.hs);
  else if (/^Whip_Ropes/.test(base) && mB.root) {      // суперника кинуто в канат: кінець забігу — біля самого каната
    let best = null, far = -1;
    for (const r of mB.root) { const q = placeRel(B0, [r[0], r[1], 0], B.hs), d = Math.hypot(q.x, q.z); if (d > far) { far = d; best = q; } }
    toTarget(0, 1, 0, RING.rope - 0.22, best);
  }
  const rt = (p) => ({ x: p.x * Math.cos(ph) + p.z * Math.sin(ph) + tx, z: -p.x * Math.sin(ph) + p.z * Math.cos(ph) + tz });
  const a0 = rt(A0), b0 = rt(B0);
  A.place(a0.x, a0.z, ph); B.place(b0.x, b0.z, wrapPi(rot + ph));
  A.inPlace = B.inPlace = false;
  A.play(`${base}_A`, { fade: 0, forceReal: true }); B.play(`${base}_B`, { fade: 0, forceReal: true });
  S.pairActors = { A, B, base }; S.cur = { name: nMe, pair: base, role: roleMe };
  S.mixProp = null; updatePropsOnly(); refreshAll();
}

// ---------------------------------------------------------------- події кліпів, контакт, бризки
const _a = new THREE.Vector3(), _b = new THREE.Vector3(), _c = new THREE.Vector3();
function contactPoint(a, nm, out) {
  const bw = (n, o) => a.boneWorld(n, o);
  const mid = (n1, n2) => { const p = bw(n1, _b), q = bw(n2, _c); return p && q ? out.copy(p).add(q).multiplyScalar(0.5) : null; };
  const side = /_l$/.test(nm) ? 'Left' : 'Right';
  if (/^lowerarm/.test(nm)) return mid(`mixamorig${side}ForeArm`, `mixamorig${side}Hand`);
  if (/^upperarm/.test(nm)) return mid(`mixamorig${side}Arm`, `mixamorig${side}ForeArm`);
  if (/^hand/.test(nm)) return bw(`mixamorig${side}Hand`, out);
  if (/^thigh/.test(nm)) return mid(`mixamorig${side}UpLeg`, `mixamorig${side}Leg`);
  if (/^calf/.test(nm)) return mid(`mixamorig${side}Leg`, `mixamorig${side}Foot`);
  if (/^foot/.test(nm)) return bw(`mixamorig${side}Foot`, out);
  const map = { spine_03: 'Spine2', spine_02: 'Spine1', spine_01: 'Spine', pelvis: 'Hips', head: 'Head', neck_01: 'Neck', neck: 'Neck' };
  return map[nm] ? bw('mixamorig' + map[nm], out) : null;
}
function onEvent(actor, voice, ev) {
  S.lastEvent = { actor: actor.id, clip: voice.name, ...ev };
  if (ev.event === 'spray') spawnSpray(actor);
  if (ev.event === 'contact' && S.pairActors) {
    const { A, B } = S.pairActors; if (actor !== A) return;       // один раз на пару
    const pa = contactPoint(A, ev.a, new THREE.Vector3()), pb = contactPoint(B, ev.b, new THREE.Vector3());
    if (pa && pb) {
      const d = pa.distanceTo(pb);
      S.contact = { t: voice.t, a: ev.a, b: ev.b, dist: d, at: pa.clone().add(pb).multiplyScalar(0.5).toArray() };
      flash(S.contact.at);
    }
  }
}

// відстань між кістками контакту прямо зараз (для перевірки «контакт у контакт» при перемотуванні)
function measureContact() {
  if (!S.pairActors) return null; const { A, B, base } = S.pairActors; const m = A.metaOf(`${base}_A`); const ev = (m.events || []).filter((e) => e.event === 'contact');
  if (!ev.length) return null; const va = A.curBase(); const e = ev.reduce((p, c) => (Math.abs(c.t - va.pose()) < Math.abs(p.t - va.pose()) ? c : p));
  const pa = contactPoint(A, e.a, new THREE.Vector3()), pb = contactPoint(B, e.b, new THREE.Vector3()); if (!pa || !pb) return null;
  // найближча кінцівка (кисті, передпліччя, голова, стопи) одного борця до осі торса іншого, см
  const spineAxis = (x) => ['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck'].map((n) => x.boneWorld('mixamorig' + n, new THREE.Vector3()));
  const tips = (x) => ['LeftHand', 'RightHand', 'LeftForeArm', 'RightForeArm', 'Head', 'LeftFoot', 'RightFoot', 'LeftLeg', 'RightLeg'].map((n) => x.boneWorld('mixamorig' + n, new THREE.Vector3()));
  const seg = (p, q, r) => { const d = q.clone().sub(p), t = Math.max(0, Math.min(1, r.clone().sub(p).dot(d) / (d.lengthSq() || 1))); return p.clone().addScaledVector(d, t).distanceTo(r); };
  let best = 9;
  for (const [x, y] of [[A, B], [B, A]]) { const ax = spineAxis(y); for (const t of tips(x)) for (let i = 0; i < ax.length - 1; i++) best = Math.min(best, seg(ax[i], ax[i + 1], t)); }
  return { a: e.a, b: e.b, evt: e.t, now: +va.pose().toFixed(3), cm: +(pa.distanceTo(pb) * 100).toFixed(1), limbToTorsoCm: +(best * 100).toFixed(1) };
}
let flashMesh = null, flashT = 0;
function flash(p) {
  if (!flashMesh) { flashMesh = new THREE.Mesh(new THREE.SphereGeometry(0.07, 14, 10), new THREE.MeshBasicMaterial({ color: 0xE8BA3C, transparent: true, opacity: 0.9, depthTest: false })); flashMesh.renderOrder = 6; S.stage.scene.add(flashMesh); }
  flashMesh.position.fromArray(p); flashMesh.visible = true; flashT = 0.6;
}
// бризки води (менеджерка, spray_water)
const spray = { n: 220, pos: null, vel: null, life: null, pts: null };
function initSpray() {
  spray.pos = new Float32Array(spray.n * 3).fill(-999); spray.vel = new Float32Array(spray.n * 3); spray.life = new Float32Array(spray.n);
  const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(spray.pos, 3));
  const cv = document.createElement('canvas'); cv.width = cv.height = 32; const cx = cv.getContext('2d'); const gr = cx.createRadialGradient(16, 16, 2, 16, 16, 15); gr.addColorStop(0, '#fff'); gr.addColorStop(1, '#fff0'); cx.fillStyle = gr; cx.fillRect(0, 0, 32, 32);
  spray.pts = new THREE.Points(g, new THREE.PointsMaterial({ color: 0xB7C6E0, size: 0.06, map: new THREE.CanvasTexture(cv), transparent: true, opacity: 0.9, depthWrite: false }));
  spray.pts.frustumCulled = false; S.stage.scene.add(spray.pts); spray.k = 0;
}
function spawnSpray(a) {
  const o = a.socketWorld('spray_gun', new THREE.Vector3()) || a.boneWorld('mixamorigRightHand', new THREE.Vector3()); if (!o) return;
  const fx = Math.sin(a.world.yaw), fz = Math.cos(a.world.yaw);
  for (let i = 0; i < 40; i++) {
    const k = (spray.k++) % spray.n, sp = 2.0 + Math.random() * 1.4, ang = (Math.random() - 0.5) * 0.35;
    const dx = fx * Math.cos(ang) + fz * Math.sin(ang), dz = fz * Math.cos(ang) - fx * Math.sin(ang);
    spray.pos.set([o.x, o.y, o.z], k * 3); spray.vel.set([dx * sp, 0.5 + Math.random() * 0.6, dz * sp], k * 3); spray.life[k] = 0.55 + Math.random() * 0.35;
  }
}
function updateSpray(dt) {
  if (!spray.pts) return; let any = false;
  for (let k = 0; k < spray.n; k++) {
    if (spray.life[k] <= 0) { spray.pos[k * 3 + 1] = -999; continue; }
    any = true; spray.life[k] -= dt; spray.vel[k * 3 + 1] -= 6.5 * dt;
    for (let j = 0; j < 3; j++) spray.pos[k * 3 + j] += spray.vel[k * 3 + j] * dt;
    if (spray.pos[k * 3 + 1] < 0) spray.life[k] = 0;
  }
  if (any) spray.pts.geometry.attributes.position.needsUpdate = true;
}
// шланг між баком і пульверизатором
function updateHose(a) {
  const t = a.propNodes.spray_tank, g = a.propNodes.spray_gun; const on = t && g && t.visible && g.visible;
  if (!on) { if (a.hose) a.hose.visible = false; return; }
  const p0 = a.socketWorld('spray_tank', new THREE.Vector3()), p1 = a.socketWorld('spray_gun', new THREE.Vector3()); if (!p0 || !p1) return;
  if (!a.hose) { a.hose = new THREE.Mesh(new THREE.BufferGeometry(), new THREE.MeshStandardMaterial({ color: 0x2E2A39, roughness: 0.6 })); a.hose.frustumCulled = false; a.hose.castShadow = true; S.stage.scene.add(a.hose); }
  const sag = Math.min(0.35, 0.12 + p0.distanceTo(p1) * 0.25);
  const m1 = p0.clone().lerp(p1, 0.33); m1.y -= sag; const m2 = p0.clone().lerp(p1, 0.66); m2.y -= sag;
  const curve = new THREE.CatmullRomCurve3([p0, m1, m2, p1]);
  a.hose.geometry.dispose(); a.hose.geometry = new THREE.TubeGeometry(curve, 18, 0.009, 6, false); a.hose.visible = true;
}

// ---------------------------------------------------------------- головний цикл
function primaryVoice() {
  const me = S.me; if (!me) return null;
  if (S.mode === 'mix') return me.layer || me.reaction || me.curBase();
  if (me.layer && S.cur && S.cur.layer) return me.layer;
  return me.curBase();
}
function applyTimeFlags() { for (const a of Object.values(S.actors)) { a.paused = S.paused; a.speedMul = S.speed; } }
function tickApp(dt) {
  const sdt = S.paused ? 0 : dt * S.speed;
  S.sim += sdt;
  applyTimeFlags();
  if (S.bout) boutTick(sdt);
  graphTick(sdt);
  const acts = Object.values(S.actors).filter((a) => a.holder.visible);
  for (const a of acts) {
    a.update(dt);
    if (a.yRamp) { const v = a.curBase(); if (v && v.name === a.yRamp.name) { const r = a.yRamp; a.yOff = r.from + (r.to - r.from) * smooth(v.pose() / v.dur * r.k); a.apply(); } }
  }
  if (S.stage.ring.visible) { S.stage.ropes.update(acts, sdt); for (const a of acts) ropeGrip(a); }
  autoRepeat(sdt);
  for (const a of Object.values(S.actors)) { if (a.holder.visible && a.propNodes.spray_tank) updateHose(a); else if (a.hose) a.hose.visible = false; }
  updateSpray(sdt);
  if (flashT > 0) { flashT -= dt; if (flashMesh) { flashMesh.visible = flashT > 0; flashMesh.scale.setScalar(1 + (0.6 - flashT)); flashMesh.material.opacity = Math.max(0, flashT / 0.6); } }
}
function autoRepeat(sdt) {
  const me = S.me; if (!me) return;
  if (S.mode === 'clips' && S.cur && !S.bout) {
    const v = primaryVoice(); if (!v) return;
    let done;
    if (S.cur.layer) done = me.layerFinished;
    else if (S.pairActors) done = S.pairActors.A.curBase().finished && S.pairActors.B.curBase().finished;
    else done = v.kind === 'base' && !v.loop && v.finished;
    if (done && S.repeat && !S.paused) {
      if (S.restartAt == null) S.restartAt = S.sim + 0.45;
      else if (S.sim >= S.restartAt) { S.restartAt = null; const n = S.cur.pair ? `${S.cur.pair}_${S.cur.role}` : S.cur.name; const keep = S.paused; playClip(n); S.paused = keep; }
    } else if (!done) S.restartAt = null;
  }
  if (S.mode === 'mix' && S.mix.rep) {
    const T = S.mixTimers;
    if (me.layerFinished && S.mix.up) { if (T.l == null) T.l = S.sim + 0.5; else if (S.sim >= T.l) { T.l = null; me.setLayer(S.mix.up, { mask: S.mix.mask, weight: S.mix.upW, fade: 0.15 }); } }
    if (me.reactionFinished && S.mix.add) { if (T.r == null) T.r = S.sim + 0.7; else if (S.sim >= T.r) { T.r = null; me.reactionFinished = false; me.setReaction(S.mix.add, S.mix.addW); } }
  }
}
function followCamera() {
  const st = S.stage, c = st.controls;
  let tx = 0, tz = 0, n = 0;
  if (S.follow) {
    for (const a of activeActors()) { tx += a.world.x; tz += a.world.z; n++; }
    if (n && !(S.me && S.me.treadActive)) {
      tx /= n; tz /= n; const ty = 0.95;
      const nx = c.target.x + (tx - c.target.x) * 0.12, nz = c.target.z + (tz - c.target.z) * 0.12;
      st.camera.position.x += nx - c.target.x; st.camera.position.z += nz - c.target.z; c.target.set(nx, ty, nz);
    }
  }
}
function updateFloor() {
  const st = S.stage, me = S.me;
  const tread = !!(me && me.treadActive && me.holder.visible);
  st.tread.visible = tread; st.ring.visible = !tread; st.stairs.visible = S.stairs && !tread;
  if (tread) {
    const D = me.treadMatrix(new THREE.Matrix4());
    const inv = D.clone().invert(); const g0 = new THREE.Vector3(me.base.x, 0, me.base.z).applyMatrix4(inv);
    const cx = Math.round(g0.x / RING.cell) * RING.cell, cz = Math.round(g0.z / RING.cell) * RING.cell;
    D.multiply(new THREE.Matrix4().makeTranslation(cx, 0, cz));
    st.treadInner.matrix.copy(D); st.treadInner.matrixWorldNeedsUpdate = true;
    st.ruler.position.set(me.base.x - 1.15, 0, me.base.z);
  } else st.ruler.position.set(-RING.half + 0.1, 0, -RING.half + 0.95);
}
let frameTimer = null;
function frame(fromTimer) {
  if (!S.stage) return;
  const now = performance.now(); const dt = Math.min(0.1, Math.max(0, (now - S.last) / 1000)); S.last = now;
  if (!fromTimer) S.lastFrame = now;
  try {
    tickApp(dt); followCamera(); S.stage.controls.update(); updateFloor();
    S.stage.renderer.render(S.stage.scene, S.stage.camera);
    uiTick(now);
  } catch (e) { S.errors.push(String(e.stack || e)); status('Помилка кадру: ' + e.message, true); console.error(e); }
}
function loop() { requestAnimationFrame(loop); frame(false); }
setInterval(() => { if (performance.now() - S.lastFrame > 150) frame(true); }, 40);   // запас на випадок прихованої вкладки

// ---------------------------------------------------------------- інтерфейс: борці
function renderCast() {
  const box = $('#cast'); box.innerHTML = '';
  for (const id of S.data.cast.order) {
    const c = S.data.cast.characters[id]; const ok = S.avail[id];
    const b = el('button', `card${id === S.meId ? ' on' : ''}${ok ? '' : ' off'}`);
    b.dataset.id = id; b.dataset.role = /лиході/.test(c.archetype) ? 'villain' : 'hero';
    const letter = c.letter || c.name.replace(/[^A-Za-zА-Яа-яІіЇїЄєҐґ]/g, '')[0] || '·';
    b.innerHTML = `<span class="let">${esc(letter)}</span><div class="nm">${esc(c.name)}</div><div class="meta">${f2(c.height)} м · ${esc(c.archetype)}</div>${ok ? '' : '<div class="miss">модель ще збирається</div>'}`;
    b.onclick = () => selectActor(id);
    box.append(b);
  }
}

// ---------------------------------------------------------------- інтерфейс: список кліпів
function clipEntries() {
  const cfg = S.data.cast.characters[S.meId]; if (!cfg) return [];
  const sex = cfg.skeleton, M = S.data.clips[sex]; const seen = new Set(); const out = [];
  for (const n of cfg.clips) {
    const m = M[n]; if (!m || seen.has(n)) continue;
    if (m.role === 'A' || m.role === 'B') {
      const base = m.base; if (seen.has('pair:' + base)) continue; seen.add('pair:' + base);
      out.push({ key: base, pair: true, m: M[`${base}_A`] || m, hasA: !!M[`${base}_A`], hasB: !!M[`${base}_B`], cat: m.category });
    } else { seen.add(n); out.push({ key: n, pair: false, m, cat: m.category }); }
  }
  return out;
}
function buildClipList() {
  const q = $('#q').value.trim().toLowerCase(), cat = $('#catSel').value, st = $('#statSel').value;
  const list = $('#clipList'); list.innerHTML = '';
  const entries = clipEntries();
  // фільтр категорій
  const cats = [...new Set(entries.map((e) => e.cat))].sort((a, b) => { const ia = CAT.findIndex((c) => c[0] === a), ib = CAT.findIndex((c) => c[0] === b); return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib); });
  const cs = $('#catSel'); if (cs.dataset.for !== S.meId) { cs.innerHTML = '<option value="">Усі категорії</option>' + cats.map((c) => `<option value="${c}">${esc(CATN[c] || c)}</option>`).join(''); cs.dataset.for = S.meId; cs.value = cat && cats.includes(cat) ? cat : ''; }
  const catNow = cs.value;
  const match = (e) => {
    if (catNow && e.cat !== catNow) return false;
    if (q && !e.key.toLowerCase().includes(q)) return false;
    if (st === 'ready' && e.m.status !== 'ready') return false;
    if (st === 'authored' && e.m.status !== 'authored') return false;
    if (st === 'loop' && !e.m.loop) return false;
    if (st === 'pair' && !e.pair) return false;
    return true;
  };
  const filtered = entries.filter(match); let total = 0;
  const curKey = S.cur ? (S.cur.pair || S.cur.name) : null;
  for (const c of cats) {
    const items = filtered.filter((e) => e.cat === c); if (!items.length) continue; total += items.length;
    const d = el('details'); d.open = !!(q || catNow || items.some((e) => e.key === curKey) || (c === 'idle' && !curKey)); d.dataset.cat = c;
    d.innerHTML = `<summary>${esc(CATN[c] || c)}<em>${items.length}</em></summary>`;
    for (const e of items) {
      const row = el('div', `clip${e.key === curKey ? ' on' : ''}`); row.dataset.key = e.key;
      let h = `<span class="n">${esc(e.key)}</span>`;
      if (e.m.status === 'authored') h += '<span class="b own" title="Власний кліп: чорновий або поза поверх кліпу">власний</span>';
      if (e.m.loop && !e.pair) h += '<span class="b lp">цикл</span>';
      if (e.pair) h += `<span class="ab">${e.hasA ? '<button data-r="A" title="Я граю A, партнер B">A</button>' : ''}${e.hasB ? '<button data-r="B" title="Я граю B, партнер A">B</button>' : ''}</span>`;
      row.innerHTML = h;
      row.onclick = (ev) => {
        const r = ev.target.dataset && ev.target.dataset.r;
        playClip(e.pair ? `${e.key}_${r || (e.hasA ? 'A' : 'B')}` : e.key);
      };
      d.append(row);
    }
    list.append(d);
  }
  if (!total) list.append(el('div', 'note', 'Нічого не знайдено.'));
  // чого ще немає
  const miss = S.data.graph.missing || [];
  if (miss.length && !q && !catNow && !st) {
    const d = el('details'); d.innerHTML = `<summary>Ще немає (за списком даних)<em>${miss.length}</em></summary>`;
    for (const m of miss) { d.append(el('div', 'clip miss', `<span class="n">${esc(m.name)}</span><span class="b">ще немає</span>`)); d.append(el('div', 'missnote', esc(m.note || ''))); }
    list.append(d);
  }
  fillInfo();
}
function fillInfo() {
  const box = $('#clipInfo'); const me = S.me; const nm = S.cur && (S.cur.layer ? S.cur.name : S.cur.name);
  const m = nm && me && meta(me, nm);
  if (!m) { box.innerHTML = '<div class="t">Кліп не вибрано</div><div class="note">Виберіть кліп у списку нижче. Парні кліпи мають кнопки A і B: хто з двох борців «я».</div>'; return; }
  const net = m.net || [0, 0, 0]; const dist = Math.hypot(net[0], net[1]);
  const chips = [`<span class="chip ${m.status === 'authored' ? 'own' : 'ready'}">${m.status === 'authored' ? 'власний' : 'готовий'}</span>`];
  if (m.role) chips.push(`<span class="chip pair">пара · роль ${m.role}</span>`);
  if (m.loop) chips.push('<span class="chip loop">цикл</span>');
  const ev = (m.events || []).map((e) => `${e.event} ${f2(e.t)} с`).join('; ');
  box.innerHTML = `<div class="t">${esc(m.name)}</div><div>${chips.join('')}</div><dl>
    <dt>Тривалість</dt><dd>${f2(m.duration)} с · ${m.frames} кадр.</dd>
    <dt>Цикл</dt><dd>${m.loop ? 'так' : 'ні'}</dd>
    <dt>Швидкість</dt><dd>${m.speed ? f2(m.speed) + ' м/с' : '—'}</dd>
    <dt>Рух кореня</dt><dd>${dist > 0.005 ? `${f2(dist)} м (x ${f2(net[0])}, y ${f2(net[1])})` : 'на місці'}${Math.abs(net[2]) > 0.01 ? ` · поворот ${f2(net[2] * 180 / Math.PI, 0)}°` : ''}</dd>
    <dt>Джерело</dt><dd>${esc(m.source || '—')}</dd>
    ${m.node_from || m.node_to ? `<dt>Стани</dt><dd>${esc(m.node_from || '—')} → ${esc(m.node_to || '—')}</dd>` : ''}
    ${m.layer_mask ? `<dt>Маска шару</dt><dd>${esc(m.layer_mask)}</dd>` : ''}
    ${m.prop ? `<dt>Реквізит</dt><dd>${esc(PROP_LABEL[m.prop] || m.prop)}</dd>` : ''}
    ${ev ? `<dt>Події</dt><dd>${esc(ev)}</dd>` : ''}</dl>${m.note ? `<div class="nt">${esc(m.note)}</div>` : ''}`;
}

// ---------------------------------------------------------------- транспорт
function buildTicks() {
  const t = $('#ticks'); t.innerHTML = ''; const v = primaryVoice(); if (!v || !v.meta.events) return;
  for (const e of v.meta.events) { const i = el('i', e.event === 'spray' ? 'sp' : e.event === 'contact' ? 'ct' : ''); i.style.left = `${(e.t / v.dur) * 100}%`; i.title = `${e.event} · ${f2(e.t)} с`; t.append(i); }
}
let lastPrimaryId = -1, lastUi = 0;
function uiTick(now) {
  const v = primaryVoice();
  if (v && v.id !== lastPrimaryId) { lastPrimaryId = v.id; buildTicks(); }
  if (now - lastUi < 66) return; lastUi = now;
  const tr = $('#timeRange');
  if (v) {
    const p = v.pose(); if (!S.dragging) tr.value = Math.round(p / v.dur * 1000);
    $('#timeTxt').textContent = `${f2(p)} / ${f2(v.dur)} с`;
    $('#hudTitle').textContent = v.name;
    const m = v.meta; const chips = [];
    chips.push(`<span class="chip ${m.status === 'authored' ? 'own' : 'ready'}">${m.status === 'authored' ? 'власний' : 'готовий'}</span>`);
    if (S.pairActors) chips.push(`<span class="chip pair">пара: ${esc(S.me.cfg.name.split('«')[1]?.replace('»', '') || S.me.id)} + ${esc(S.partner ? (S.partner.cfg.name.split('«')[1] || S.partner.id).replace('»', '') : '')}</span>`);
    if (v.loop) chips.push('<span class="chip loop">цикл</span>');
    let sub = chips.join('');
    if (S.me.treadActive) sub += '<span style="color:var(--dim)">на місці · підлога їде назустріч</span>';
    if (S.contact && S.pairActors) sub += `<br><span style="color:var(--gold)">контакт ${esc(S.contact.a)} → ${esc(S.contact.b)}: ${f2(S.contact.dist * 100, 0)} см між осями кісток</span>`;
    if (S.stairs && S.ik) sub += '<br><span style="color:var(--dim)">IK: таз ' + f2(S.me.ik.p * 100, 0) + ' см</span>';
    $('#hudSub').innerHTML = sub;
  } else { $('#hudTitle').textContent = S.me ? '' : 'Немає моделі'; $('#hudSub').textContent = ''; }
  $('#playBtn').textContent = S.paused ? '▶' : '⏸';
  if (S.mode === 'graph') { weightsUi($('#weights')); graphUi(); }
  if (S.mode === 'mix') weightsUi($('#mixWeights'));
}
function weightsUi(box) {
  const me = S.me; if (!me) { box.innerHTML = ''; return; }
  const rows = [];
  for (const v of me.voices) if (v.kind === 'base') rows.push({ n: v.name, w: v.w, c: '' });
  for (const l of [me.layer, ...(me.oldLayers || [])]) if (l) rows.push({ n: `${l.name} (шар · ${l.mask})`, w: l.layerW, c: 'layer' });
  if (me.reaction) rows.push({ n: `${me.reaction.name} (адитивно)`, w: me.reaction.userW, c: 'add' });
  if (S.pairActors && S.partner) { const pv = S.partner.curBase(); if (pv) rows.push({ n: `партнер: ${pv.name}`, w: pv.w, c: 'layer' }); }
  const key = rows.map((r) => r.n).join('|');
  if (box.dataset.key !== key) { box.dataset.key = key; box.innerHTML = rows.map((r) => `<div class="w ${r.c}"><span>${esc(r.n)}</span><span class="mono"></span><div class="bar"><i></i></div></div>`).join(''); }
  [...box.children].forEach((d, i) => { const r = rows[i]; if (!r) return; d.querySelector('.mono').textContent = f2(r.w); d.querySelector('i').style.width = `${Math.max(0, Math.min(1, r.w)) * 100}%`; });
}

function seek(t) {
  const me = S.me; if (!me) return;
  const act = [me]; if (S.pairActors) act.push(S.partner);
  for (const a of act) {
    if (!a) continue;
    const vs = (a === me && S.cur && S.cur.layer && me.layer) ? [me.layer] : [a.curBase()];
    for (const v of vs) { if (!v) continue; v.t = v.loop ? t : Math.min(t, v.dur); v.tPrev = v.t; v.finished = !v.loop && v.t >= v.dur; }
    if (a.fade) { for (const v of a.voices) v.w = v === a.fade.to ? 1 : 0; a.fade = null; }
  }
  const keep = S.paused; for (const a of act) { if (a) { a.paused = true; a.update(0); } } S.paused = keep; S.restartAt = null;
}
function wireTransport() {
  $('#playBtn').onclick = () => { if (S.paused) { const v = primaryVoice(); if (v && !v.loop && v.finished && S.cur) { S.paused = false; if (S.mode === 'clips') playClip(S.cur.pair ? `${S.cur.pair}_${S.cur.role}` : S.cur.name); } } S.paused = !S.paused; };
  $('#repBtn').onclick = (e) => { S.repeat = !S.repeat; e.currentTarget.setAttribute('aria-pressed', S.repeat); };
  const tr = $('#timeRange');
  tr.addEventListener('pointerdown', () => { S.dragging = true; });
  addEventListener('pointerup', () => { S.dragging = false; });
  tr.addEventListener('input', () => { const v = primaryVoice(); if (!v) return; S.paused = true; seek(tr.value / 1000 * v.dur); });
  $('#speedRange').oninput = (e) => { S.speed = +e.target.value; $('#speedTxt').textContent = f2(S.speed); };
  addEventListener('keydown', (e) => { if (e.target.matches('input[type=search],select')) return; if (e.code === 'Space') { e.preventDefault(); $('#playBtn').click(); } });
}

// ---------------------------------------------------------------- режим «Переходи»
const NODEPOS = {
  walk: [62, 42], jog: [184, 42], run: [306, 42], stance_walk: [428, 42], top_rope: [550, 42],
  idle: [62, 150], stance: [306, 150], corner_lean: [428, 150], lying_back: [550, 150], lying_front: [672, 100],
  promo: [62, 258], entrance_walk: [184, 258], crouch: [306, 258], held_stand: [428, 258],
  kneel: [306, 362], sit_mat: [428, 362], held_kneel: [550, 300], held_sit: [550, 380],
};
const NW = 112, NH = 46;
function stateAvail(a, id) { const s = S.data.graph.states[id]; return !!(s && a && a.has(s.clip)); }
function edgesOf(a) { const g = S.data.graph; const list = (g.edges[a.cfg.skeleton] || g.edges.m); return list.filter((e) => stateAvail(a, e.from) && stateAvail(a, e.to)); }
function buildGraph() {
  const a = S.me; const svg = $('#graphSvg'); svg.innerHTML = '';
  $('#gActor').textContent = a ? a.cfg.name : '—';
  if (!a) return;
  const g = S.data.graph, NS = 'http://www.w3.org/2000/svg';
  const mk = (t, at = {}) => { const e = document.createElementNS(NS, t); for (const k in at) e.setAttribute(k, at[k]); return e; };
  const defs = mk('defs'); defs.innerHTML = '<marker id="ar" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0 0L10 5L0 10z"/></marker>'; svg.append(defs);
  const gE = mk('g'), gN = mk('g'); svg.append(gE, gN);
  const edges = edgesOf(a);
  edges.forEach((e, i) => {
    const p = NODEPOS[e.from], q = NODEPOS[e.to]; if (!p || !q) return;
    const dx = q[0] - p[0], dy = q[1] - p[1], L = Math.hypot(dx, dy) || 1, nx = -dy / L, ny = dx / L;
    const clip = (c, o, sg) => { const t = Math.min((NW / 2 + 3) / Math.abs(o[0] || 1e-6), (NH / 2 + 3) / Math.abs(o[1] || 1e-6)); return [c[0] + o[0] * t * sg, c[1] + o[1] * t * sg]; };
    const s0 = clip(p, [dx, dy], 1), s1 = clip(q, [dx, dy], -1);
    const off = 14; const cx = (s0[0] + s1[0]) / 2 + nx * off, cy = (s0[1] + s1[1]) / 2 + ny * off;
    const path = mk('path', { d: `M${s0[0]} ${s0[1]} Q${cx} ${cy} ${s1[0]} ${s1[1]}`, class: 'edge', 'marker-end': 'url(#ar)', 'data-i': i });
    path.onclick = () => runEdge(e);
    gE.append(path);
  });
  for (const id of Object.keys(g.states)) {
    const pos = NODEPOS[id]; if (!pos) continue; const st = g.states[id]; const ok = stateAvail(a, id);
    const n = mk('g', { class: `node${ok ? '' : ' na'}`, transform: `translate(${pos[0] - NW / 2},${pos[1] - NH / 2})`, 'data-id': id });
    n.innerHTML = `<rect width="${NW}" height="${NH}"/><text class="${st.label.length > 14 ? 'l' : ''}" x="${NW / 2}" y="19"${st.label.length > 14 ? ` textLength="${NW - 10}" lengthAdjust="spacingAndGlyphs"` : ''}>${esc(st.label)}</text><text class="s" x="${NW / 2}" y="35">${esc(st.clip.length > 24 ? st.clip.slice(0, 23) + '…' : st.clip)}</text>`;
    gN.append(n);
  }
  S.graphEdges = edges;
}
function runEdge(e) {
  const a = S.me; if (!a) return; S.edgeRun = null;
  const g = S.data.graph, to = g.states[e.to];
  S.bout = null; S.contact = null; S.cur = { name: to.clip }; S.edgeMove = null;
  if (e.via && a.has(e.via)) {
    const start = () => { a.play(e.via, { fade: e.fade_in ?? e.fade ?? 0.2, forceReal: true }); applyTopRope(a, e.via); S.trans = { edge: e }; S.edgeRun = e; updatePropsOnly(); graphUi(true); };
    a.inPlace = /^(Move_Corner_Enter|Corner_Climb_Top|Entrance_Through_Ropes)$/.test(e.via) ? false : a.inPlace;
    if (e.via === 'Entrance_Through_Ropes') { const P = startPlacement(a, e.via); a.place(P.x, P.z, P.yaw); }   // поза рингом ходи немає: кадр уже на фартуху
    if (/^(Move_Corner_Enter|Corner_Climb_Top)$/.test(e.via)) {
      const P = startPlacement(a, e.via), dd = Math.hypot(P.x - a.world.x, P.z - a.world.z);
      S.edgeRun = e; S.edgeMove = { after: null };
      const go = () => { S.edgeMove = null; start(); };
      if (dd > RUN_FROM) runTo(S.edgeMove, a, P.x, P.z, P.yaw, go);
      else slideTo(S.edgeMove, [{ a, dx: P.x - a.world.x, dz: P.z - a.world.z, dy: wrapPi(P.yaw - a.world.yaw) }], go);
      updatePropsOnly(); graphUi(true);
      return;
    }
    start();
    return;
  } else {
    a.play(to.clip, { fade: e.fade ?? 0.25 }); a.stateId = e.to; S.trans = null; S.edgeRun = e; S.edgeRunUntil = S.sim + (e.fade ?? 0.25);
  }
  updatePropsOnly(); graphUi(true);
}
function graphTick(sdt) {
  if (S.edgeMove && moveTick(S.edgeMove, sdt)) return;
  const a = S.me; if (!a || !S.trans) { if (S.edgeRun && S.edgeRunUntil && S.sim > S.edgeRunUntil) { S.edgeRun = null; S.edgeRunUntil = 0; } return; }
  const e = S.trans.edge, v = a.curBase(); const g = S.data.graph;
  if (v && v.name === e.via && v.t >= v.dur - (e.fade_out ?? 0.2) - 1e-4) {
    const to = g.states[e.to]; a.play(to.clip, { fade: e.fade_out ?? 0.2 }); a.stateId = e.to; S.trans = null; S.cur = { name: to.clip };
    S.edgeRunUntil = S.sim + (e.fade_out ?? 0.2);
  }
}
let lastGraphKey = '';
function graphUi(force) {
  const a = S.me; if (!a) return;
  const cur = a.stateId; const key = `${cur}|${S.edgeRun ? S.edgeRun.from + S.edgeRun.to : ''}|${S.trans ? 1 : 0}`;
  if (key === lastGraphKey && !force) return; lastGraphKey = key;
  $('#gCur').textContent = cur ? S.data.graph.states[cur].label : '—';
  $$('#graphSvg .node').forEach((n) => n.classList.toggle('cur', n.dataset.id === cur));
  const outs = (S.graphEdges || []).filter((e) => e.from === cur);
  $$('#graphSvg .node').forEach((n) => n.classList.toggle('out', outs.some((e) => e.to === n.dataset.id)));
  $$('#graphSvg .edge').forEach((p) => { const e = S.graphEdges[+p.dataset.i]; p.classList.toggle('out', e.from === cur); p.classList.toggle('run', !!S.edgeRun && e.from === S.edgeRun.from && e.to === S.edgeRun.to); });
  const box = $('#edgeBtns'); box.innerHTML = '';
  for (const e of outs) {
    const st = S.data.graph.states[e.to];
    const b = el('button', 'btn', `<span>→ ${esc(st.label)}</span><small>${e.via ? esc(e.via) + ` · ${f2(e.fade_in)}+${f2(e.fade_out)} с` : `кросфейд ${f2(e.fade)} с`}</small>`);
    b.onclick = () => runEdge(e); box.append(b);
  }
  if (!outs.length) box.append(el('div', 'note', 'З цього стану немає вихідних ребер.'));
}
function graphEnter() {
  const a = S.me; if (!a) return;
  S.bout = null; resetPartner();
  a.reset(); a.place(0, 0, 0); S.cur = null; S.trans = null; S.edgeRun = null; S.edgeMove = null;
  const first = ['idle', 'stance'].find((s) => stateAvail(a, s)); if (first) { a.play(S.data.graph.states[first].clip, { fade: 0 }); a.stateId = first; S.cur = { name: S.data.graph.states[first].clip }; }
  syncLook(); buildGraph(); buildBoutUi(); lastGraphKey = ''; graphUi(true);
}

// ---------------------------------------------------------------- ланцюг бою
function boutSteps() { return S.data.graph.bout; }
function log(msg, err = false) { const l = $('#boutLog'); l.innerHTML += `<div class="${err ? 'e' : ''}">${esc(msg)}</div>`; l.scrollTop = l.scrollHeight; }
function buildBoutUi() {
  const box = $('#boutSteps'); box.innerHTML = '';
  boutSteps().forEach((st, i) => { const b = el('button', 'btn', esc(st.label)); b.dataset.i = i; b.onclick = () => boutRun([i], false); box.append(b); });
  const ch = boutSteps().find((s) => s.choice);
  $('#hitSel').innerHTML = ch ? ch.choice.map((c) => `<option value="${c}">${c.replace(/^Center_/, '').replace(/([a-z])([A-Z])/g, '$1 $2')}</option>`).join('') : '';
  $('#boutPartner').textContent = S.partnerId ? S.data.cast.characters[S.partnerId].name : '—';
}
function boutMarks() { $$('#boutSteps .btn').forEach((b) => { const i = +b.dataset.i; b.classList.toggle('cur', !!S.bout && S.bout.cur === i); b.classList.toggle('done', !!S.bout && S.bout.done.has(i)); }); }
async function boutRun(idxs, full) {
  const me = S.me; if (!me) return;
  const p = S.partner || await ensurePartner();
  if (me !== S.me) return;
  if (!p) { log('Немає партнера з готовою моделлю', true); return; }
  $('#boutPartner').textContent = p.cfg.name;
  p.holder.visible = true; S.slide = null; S.trans = null;
  if (full || !me.curBase()) {
    me.reset(); p.reset(); me.inPlace = p.inPlace = false;
    me.place(0, -Math.min(2.3, RING.rope - 0.45), 0); p.place(0, 0.2, Math.PI);
    p.play('Ring_Stance_Loop', { fade: 0, forceReal: true }); me.play('U_Idle_Loop', { fade: 0, forceReal: true }); $('#boutLog').innerHTML = '';
  }
  me.inPlace = p.inPlace = false; me.steer = p.steer = false; S.pairActors = null; S.contact = null;
  S.bout = { q: idxs.slice(), qi: -1, cur: null, done: new Set(), step: null, t0: 0, phase: 'idle', finished: false };
  log(full ? 'Весь ланцюг: старт' : `Крок: ${boutSteps()[idxs[0]].label}`);
  boutNext();
}
function boutNext() {
  const B = S.bout; if (!B) return;
  if (B.cur != null) B.done.add(B.cur);
  B.qi++; if (B.qi >= B.q.length) { log('Готово'); S.bout = null; S.boutDone = true; boutMarks(); S.pairActors = null; return; }
  B.cur = B.q[B.qi]; B.step = boutSteps()[B.cur]; B.t0 = S.sim; B.phase = 'start'; B.pair = null; B.approach = null; B.slideInfo = null; B.cut = null; boutMarks();
  try { boutStart(B); } catch (e) { log('Помилка кроку: ' + e.message, true); S.errors.push(String(e.stack)); S.bout = null; }
}
function faceTarget(a, tx, tz, rate, sdt) {
  const des = Math.atan2(tx - a.world.x, tz - a.world.z), d = wrapPi(des - a.world.yaw); const st = Math.max(-rate * sdt, Math.min(rate * sdt, d)); if (Math.abs(st) > 1e-5) a.shift(0, 0, st);
}
function pairTarget(base, roleMe) {
  const me = S.me, p = S.partner; const A = roleMe === 'A' ? me : p, B = roleMe === 'A' ? p : me;
  const mA = A.metaOf(`${base}_A`); const off = mA.offset || [0, 0, 0], rot = (mA.rot || 0) * Math.PI / 180, s = (A.hs + B.hs) / 2;
  if (roleMe === 'A') { const w = placeRel({ x: me.world.x, z: me.world.z, yaw: me.world.yaw }, [off[0], off[1], rot], s); return { x: w.x, z: w.z, yaw: w.yaw }; }
  // я B: партнер A стоїть так, що B = A + Ry(yawA)*(dx*s, -dy*s), yawB = yawA + rot
  const yawA = me.world.yaw - rot, c = Math.cos(yawA), sn = Math.sin(yawA);
  const dx = off[0] * s, dz = -off[1] * s;
  return { x: me.world.x - (dx * c + dz * sn), z: me.world.z - (dz * c - dx * sn), yaw: yawA };
}
const RUN_FROM = 0.75;      // далі цього борець добігає, а не ковзає
function runTo(B, a, tx, tz, tyaw, after, extra) {
  const c = ['Run_Loop', 'Run_Jog_Loop', 'U_Walk_Loop'].find((n) => a.has(n));
  a.play(c, { fade: 0.18, forceReal: true });
  B.run = { a, tx, tz, tyaw, el: 0, d0: Math.hypot(tx - a.world.x, tz - a.world.z), extra: extra || null, done: 0 };
  B.after = after; B.phase = 'runto';
}
function isDown(a) { const h = a.boneWorld('mixamorigHips', _a); return !!h && h.y < 0.5 * a.hs; }
function slideTo(B, slides, after) {
  const far = Math.max(0, ...slides.map((x) => Math.hypot(x.dx, x.dz)));
  B.slides = slides; B.slideDur = Math.min(0.6, 0.25 + far * 0.12); B.slideEl = 0; B.slideProg = 0; B.after = after; B.phase = 'slide';
  return far;
}
// Куди стати партнерові, щоб пара вмістилася в канатах: кінцеві точки обох борців
function pairPlan(base, role) {
  const me = S.me, p = S.partner, tgt = pairTarget(base, role);
  const A = role === 'A' ? me : p, Bb = role === 'A' ? p : me;
  const A0 = role === 'A' ? { x: me.world.x, z: me.world.z, yaw: me.world.yaw } : tgt, B0 = role === 'A' ? tgt : { x: me.world.x, z: me.world.z, yaw: me.world.yaw };
  const eA = placeRel(A0, A.metaOf(`${base}_A`).net || [0, 0, 0], A.hs), eB = placeRel(B0, Bb.metaOf(`${base}_B`).net || [0, 0, 0], Bb.hs);
  const xs = [A0.x, B0.x, eA.x, eB.x], zs = [A0.z, B0.z, eA.z, eB.z], L = RING.rope - 0.15;
  const fit = (lo, hi) => (hi - lo > 2 * L ? -(hi + lo) / 2 : hi > L ? L - hi : lo < -L ? -L - lo : 0);
  const sx = fit(Math.min(...xs), Math.max(...xs)), sz = fit(Math.min(...zs), Math.max(...zs));
  const slides = [{ a: p, dx: tgt.x - p.world.x + sx, dz: tgt.z - p.world.z + sz, dy: wrapPi(tgt.yaw - p.world.yaw) }];
  if (Math.hypot(sx, sz) > 0.02) slides.push({ a: me, dx: sx, dz: sz, dy: 0 });
  return { tgt, sx, sz, slides, pFar: Math.hypot(slides[0].dx, slides[0].dz) };
}
// Біг від канатів: наступний прийом починається, щойно борець добіг до місця, де пара вміщається (без ковзання назад)
function ropeCutPlan(B, mm) {
  const nx = boutSteps()[B.q[B.qi + 1]];
  if (!nx || !(nx.pair || nx.choice) || !mm.root) return null;
  let iF = 0, far = 0; mm.root.forEach((r, i) => { const d = Math.hypot(r[0], r[1]); if (d > far) { far = d; iF = i; } });
  return { base: nx.pair || ($('#hitSel').value || nx.choice[0]), role: nx.me_role || 'A', tFar: iF / Math.max(1, mm.root.length - 1) * mm.duration, best: 1e9 };
}
function boutStart(B) {
  const me = S.me, p = S.partner, st = B.step;
  const opt = { fade: 0.25, forceReal: true };
  const isPair = st.pair || st.choice;
  if (isPair) {
    const base = st.pair || ($('#hitSel').value || st.choice[0]); const role = st.me_role || 'A';
    B.pair = { base, role };
    // суперник лежить: він лишається на місці, підходить нападник
    if (role === 'A' && isDown(p)) {
      const mA = me.metaOf(`${base}_A`); const off = mA.offset || [0, 0, 0], rot = (mA.rot || 0) * Math.PI / 180, s = (me.hs + p.hs) / 2;
      const yawA = p.world.yaw - rot, c = Math.cos(yawA), sn = Math.sin(yawA), dx = off[0] * s, dz = -off[1] * s;
      const tm = { x: p.world.x - (dx * c + dz * sn), z: p.world.z - (dz * c - dx * sn), yaw: yawA };
      const start = () => {
        const nMe = `${base}_A`, nP = `${base}_B`;
        if (!me.has(nMe) || !p.has(nP)) { log(`  немає кліпів ${nMe}/${nP}`, true); boutNext(); return; }
        me.play(nMe, { fade: 0.2, forceReal: true }); p.play(nP, { fade: 0.2, forceReal: true });
        S.pairActors = { A: me, B: p, base }; S.cur = { name: nMe, pair: base, role }; S.contact = null;
        B.phase = 'run'; B.t0 = S.sim;
      };
      const d = Math.hypot(tm.x - me.world.x, tm.z - me.world.z);
      if (d > RUN_FROM) { runTo(B, me, tm.x, tm.z, tm.yaw, start); log(`  ${st.label}: ${base} (я A); суперник лежить, борець добігає ${f2(d)} м`); }
      else { slideTo(B, [{ a: me, dx: tm.x - me.world.x, dz: tm.z - me.world.z, dy: wrapPi(tm.yaw - me.world.yaw) }], start); log(`  ${st.label}: ${base} (я A); суперник лежить, борець підходить ${f2(d)} м`); }
      return;
    }
    const { tgt, sx, sz, slides, pFar } = pairPlan(base, role);
    const cutIn = B.cutIn; B.cutIn = false;
    if (pFar <= RUN_FROM && p.curBase() && p.curBase().name !== 'Ring_Stance_Loop' && p.has('Ring_Stance_Loop')) p.play('Ring_Stance_Loop', opt);
    const startPair = () => {
      const rp = role === 'A' ? 'B' : 'A', nMe = `${base}_${role}`, nP = `${base}_${rp}`;
      if (!me.has(nMe) || !p.has(nP)) { log(`  немає кліпів ${nMe}/${nP}`, true); boutNext(); return; }
      me.play(nMe, { fade: 0.2, forceReal: true }); p.play(nP, { fade: 0.2, forceReal: true });
      S.pairActors = { A: role === 'A' ? me : p, B: role === 'A' ? p : me, base }; S.cur = { name: nMe, pair: base, role }; S.contact = null;
      B.phase = 'run'; B.t0 = S.sim;
    };
    const shifted = Math.hypot(sx, sz) > 0.02 ? ', пару зсунуто в межі рингу' : '';
    if (cutIn) {                                       // борець уже біжить: прийом починається одразу, партнер доходить під час нього
      startPair(); B.bg = { slides, dur: Math.min(0.6, 0.3 + pFar * 0.3), el: 0, prog: 0 };
      log(`  ${st.label}: ${base} (я ${role}); просто з бігу, партнер підходить ${f2(pFar)} м${shifted}`);
      return;
    }
    if (pFar > RUN_FROM) {
      runTo(B, p, p.world.x + slides[0].dx, p.world.z + slides[0].dz, tgt.yaw, startPair, slides[1] || null);
      log(`  ${st.label}: ${base} (я ${role}); партнер добігає ${f2(pFar)} м${shifted}`);
    } else {
      const far = slideTo(B, slides, startPair);
      log(`  ${st.label}: ${base} (я ${role}); партнер підходить ${f2(far)} м за ${f2(B.slideDur)} с${shifted}`);
    }
    return;
  }
  // одиночний крок
  const mm = st.me ? me.metaOf(st.me) : null;
  const go = () => {
    const cm = me.curBase();
    if (st.me) { if (!cm || cm.name !== st.me || !cm.loop) me.play(st.me, opt); }
    if (st.partner && p.has(st.partner)) { const pm = p.curBase(); if (!pm || pm.name !== st.partner || !pm.loop) p.play(st.partner, opt); }
    if (st.approach) {
      const nx = boutSteps().slice(S.bout.qi + 1).find((x) => x.pair || x.choice);
      const base = nx ? (nx.pair || nx.choice[0]) : 'Forearm_Strike'; const m = me.metaOf(`${base}_A`) || {}; const off = m.offset || [0, -0.9, 0];
      B.approach = Math.hypot(off[0], off[1]) * (me.hs + p.hs) / 2 + 0.05;
    }
    B.phase = 'run'; B.t0 = S.sim;
    B.cut = mm && isRopeRun(st.me, mm) ? ropeCutPlan(B, mm) : null;
  };
  if (mm && isRopeRun(st.me, mm)) {          // забіг у канати: борця плавно підводимо до стартової точки
    const P = startPlacement(me, st.me);
    const dd = Math.hypot(P.x - me.world.x, P.z - me.world.z);
    if (dd > RUN_FROM) { runTo(B, me, P.x, P.z, P.yaw, go); log(`  ${st.label}: ${st.me}; борець добігає до стартової точки (${f2(dd)} м)`); return; }
    const far = slideTo(B, [{ a: me, dx: P.x - me.world.x, dz: P.z - me.world.z, dy: wrapPi(P.yaw - me.world.yaw) }], go);
    log(`  ${st.label}: ${st.me}; борець підходить до стартової точки (${f2(far)} м за ${f2(B.slideDur)} с)`);
    return;
  }
  go(); log(`  ${st.label}: ${st.me || ''}${st.partner ? ' / ' + st.partner : ''}`);
}
function slideTick() { }
// Добіг і підхід: спільні для ланцюга бою й режиму переходів. true — рух ще триває.
function moveTick(B, sdt) {
  if (B.phase === 'runto') {
    const r = B.run, a = r.a; r.el += sdt;
    const headErr = Math.abs(wrapPi(Math.atan2(r.tx - a.world.x, r.tz - a.world.z) - a.world.yaw));
    if (headErr > 0.5 && r.px !== undefined) a.shift(r.px - a.world.x, r.pz - a.world.z, 0);
    faceTarget(a, r.tx, r.tz, 9, sdt);
    r.px = a.world.x; r.pz = a.world.z;
    const dx = r.tx - a.world.x, dz = r.tz - a.world.z, d = Math.hypot(dx, dz);
    if (r.extra) { const pr = Math.max(r.done, Math.min(1, 1 - d / Math.max(r.d0, 0.01))), k = pr - r.done; r.done = pr; r.extra.a.shift(r.extra.dx * k, r.extra.dz * k, 0); }
    if (d < 0.32 || r.el > 4) {
      // зупинка: кліп бігу більше не веде корінь, інакше борець проскакує точку ще на метр
      const stop = ['Ring_Stance_Loop', 'U_Idle_Loop'].find((n) => a.has(n));
      if (stop) a.play(stop, { fade: 0.22, forceReal: true });
      const slides = [{ a, dx: r.tx - a.world.x, dz: r.tz - a.world.z, dy: wrapPi(r.tyaw - a.world.yaw) }];
      if (r.extra && r.done < 1) slides.push({ a: r.extra.a, dx: r.extra.dx * (1 - r.done), dz: r.extra.dz * (1 - r.done), dy: 0 });
      B.slides = slides; B.slideDur = 0.26; B.slideEl = 0; B.slideProg = 0; B.phase = 'slide';
    }
    return true;
  }
  if (B.phase === 'slide') {
    B.slideEl += sdt; const pr = smooth(B.slideEl / B.slideDur), d = pr - B.slideProg; B.slideProg = pr;
    for (const x of B.slides) x.a.shift(x.dx * d, x.dz * d, x.dy * d);
    if (B.slideEl >= B.slideDur) { const f = B.after; B.after = null; f && f(); }
    return true;
  }
  return false;
}
function boutTick(sdt) {
  const B = S.bout; if (!B) return; const me = S.me, p = S.partner, st = B.step;
  if (B.bg) {
    const g = B.bg; g.el += sdt; const pr = smooth(g.el / g.dur), d = pr - g.prog; g.prog = pr;
    for (const x of g.slides) x.a.shift(x.dx * d, x.dz * d, x.dy * d);
    if (g.el >= g.dur) B.bg = null;
  }
  if (moveTick(B, sdt)) return;
  if (B.phase !== 'run') return;
  const mv = me.curBase(), pv = p.curBase(); const el = S.sim - B.t0;
  if (B.cut && mv.name === st.me) {
    faceTarget(p, me.world.x, me.world.z, 4, sdt);
    if (mv.t > B.cut.tFar + 0.1) {
      const pl = pairPlan(B.cut.base, B.cut.role), sc = Math.hypot(pl.sx, pl.sz) + pl.pFar;
      const go = sc < 0.3 || (sc > B.cut.best + 0.03 && B.cut.best < 1.2);
      B.cut.best = Math.min(B.cut.best, sc);
      if (go) { B.cut = null; B.cutIn = true; boutNext(); return; }
    }
  }
  let done = false;
  if (B.pair) done = mv.finished && pv.finished && el > 0.05;
  else {
    if (st.approach) {
      faceTarget(me, p.world.x, p.world.z, 4, sdt); faceTarget(p, me.world.x, me.world.z, 3, sdt);
      const dist = Math.hypot(p.world.x - me.world.x, p.world.z - me.world.z);
      done = dist <= B.approach || el > 6;
    } else {
      const nonLoop = (!mv.loop) || (!pv.loop && st.partner);
      if (nonLoop) done = (mv.loop || mv.finished) && (pv.loop || pv.finished) && el > 0.05;
      else done = el >= (st.hold || 1);
    }
  }
  if (el > 14) { log('  таймаут кроку', true); done = true; }
  if (done) { if (B.pair) S.pairActors = null; boutNext(); }
}

// ---------------------------------------------------------------- режим «Мікси»
const MASKS = [['upper_body', 'Верх тіла'], ['right_arm', 'Права рука'], ['left_arm', 'Ліва рука'], ['arms', 'Обидві руки'], ['lower_body', 'Низ тіла'], ['full', 'Усе тіло']];
function mixEnter() {
  const a = S.me; if (!a) return;
  resetPartner(); S.bout = null; a.reset(); a.place(0, 0, 0); S.cur = null; S.trans = null;
  const M = a.clipsMeta, names = a.cfg.clips.filter((n) => a.has(n));
  const loops = names.filter((n) => M[n].loop && !M[n].role && !M[n].layer_mask);
  const opt = (n) => `<option value="${n}">${n}${M[n].status === 'authored' ? ' · власний' : ''}</option>`;
  $('#mixBase').innerHTML = loops.map(opt).join('');
  const layers = names.filter((n) => M[n].layer_mask || M[n].upper_layer || (M[n].prop && M[n].loop));
  const others = names.filter((n) => !layers.includes(n) && !(M[n].loop && !M[n].role));
  $('#mixUp').innerHTML = '<option value="">— немає —</option><optgroup label="Шари та реквізит">' + layers.map(opt).join('') + '</optgroup><optgroup label="Інші кліпи (маскою)">' + others.map(opt).join('') + '</optgroup>';
  const reacts = names.filter((n) => M[n].category === 'react');
  $('#mixAdd').innerHTML = '<option value="">— немає —</option><optgroup label="Реакції">' + reacts.map(opt).join('') + '</optgroup><optgroup label="Усі кліпи">' + names.filter((n) => !reacts.includes(n) && !M[n].role).map(opt).join('') + '</optgroup>';
  $('#mixMask').innerHTML = MASKS.map(([v, l]) => `<option value="${v}">${l}</option>`).join('');
  const baseDef = ['U_Walk_Loop', 'Ring_Stance_Loop'].find((n) => loops.includes(n)) || loops[0];
  $('#mixBase').value = S.mix.base && loops.includes(S.mix.base) ? S.mix.base : baseDef;
  const ex = S.data.graph.mix_examples.filter((e) => !e.who || e.who === a.id || S.avail[e.who]);
  const box = $('#mixEx'); box.innerHTML = '';
  ex.forEach((e) => {
    const b = el('button', 'btn', `${esc(e.label)}<small>${esc(e.base)} + ${esc(e.upper || e.additive)}${e.who ? ' · ' + esc(S.data.cast.characters[e.who].name.replace(/.*«(.*)».*/, '$1')) : ''}</small>`);
    b.onclick = async () => {
      $$('#mixEx .btn').forEach((x) => x.classList.remove('on')); b.classList.add('on');
      if (e.who && e.who !== S.meId) { await selectActor(e.who); }
      if (!S.me.has(e.base) || !S.me.has(e.upper || e.additive)) { $('#mixNote').textContent = 'У цього борця немає потрібних кліпів.'; return; }
      $('#mixBase').value = e.base; $('#mixUp').value = e.upper || ''; $('#mixAdd').value = e.additive || '';
      const m = S.me.metaOf(e.upper || ''); $('#mixMask').value = (m && m.layer_mask) || 'upper_body';
      mixApply();
    };
    box.append(b);
  });
  syncLook(); mixApply();
}
function mixApply() {
  const a = S.me; if (!a) return;
  const base = $('#mixBase').value, up = $('#mixUp').value, add = $('#mixAdd').value;
  let mask = $('#mixMask').value; if (up && !$('#mixMask').dataset.touched) { const m = a.metaOf(up); mask = (m && m.layer_mask) || 'upper_body'; $('#mixMask').value = mask; }
  S.mix = { base, up, add, mask, upW: +$('#mixUpW').value, addW: +$('#mixAddW').value, rep: $('#mixRep').checked };
  S.mixTimers = {};
  a.reset(); a.place(0, 0, 0); S.cur = null;
  a.play(base, { fade: 0 });
  if (up) a.setLayer(up, { mask, weight: S.mix.upW, fade: 0.15, forceOver: false });
  if (add) a.setReaction(add, S.mix.addW);
  S.mixUpMeta = up ? a.metaOf(up) : null; S.mixProp = add ? a.metaOf(add) : null;
  updatePropsOnly();
  $('#mixNote').textContent = up && !a.metaOf(base).loop ? 'Базовий кліп не цикл: шар працює лише поверх циклів.' : '';
}

// ---------------------------------------------------------------- режими
function enterMode(m, force) {
  if (!force && m === S.mode) return;
  S.mode = m; S.bout = null; S.slide = null; S.restartAt = null;
  document.body.dataset.mode = m;
  $$('#modes button').forEach((b) => { const on = b.dataset.mode === m; b.classList.toggle('on', on); b.setAttribute('aria-selected', on); });
  $('#paneClips').hidden = m !== 'clips'; $('#paneGraph').hidden = m !== 'graph'; $('#paneMix').hidden = m !== 'mix';
  $('#secPartner').hidden = m === 'mix';
  const me = S.me; if (!me) return;
  if (m === 'clips') { me.reset(); resetPartner(); me.place(0, 0, 0); S.cur = null; const d = ['Ring_Stance_Loop', 'U_Idle_Loop'].find((n) => me.has(n)); buildClipList(); if (d) playClip(d); }
  if (m === 'graph') graphEnter();
  if (m === 'mix') mixEnter();
  lastPrimaryId = -1;
}
function refreshAll() { buildClipList(); fillInfo(); lastPrimaryId = -1; }

function syncHeld() {
  $$('#heldSeg button').forEach((b) => b.classList.toggle('on', b.dataset.v === (S.heldMode === 'free' ? 'free' : 'held')));
}
function setHeld(mode) {
  const me = S.me; if (!me || !me.hasHeld()) return;
  S.heldMode = mode; me.layerOn = true;
  if (mode === 'free') { me.setLayer(null, { fade: 0.25 }); }
  if (mode === 'held') {
    const b = me.curBase();
    if (!b || !b.loop) { me.reset(); me.place(0, 0, 0); me.play('Ring_Stance_Loop', { fade: 0 }); S.cur = { name: 'Ring_Stance_Loop' }; }
    me.setLayer('Layer_Held_Arms_Loop', { fade: 0.3, hold: true });
  }
  if (mode === 'sit' || mode === 'kneel') {
    const [bn, ln] = mode === 'sit' ? ['Sit_Mat_Idle_Loop', 'Held_Sit_Loop'] : ['Kneel_Idle_Loop', 'Held_Kneel_Loop'];
    if (!me.curBase()) { me.place(0, 0, 0); }
    me.play(bn, { fade: 0.35 }); me.setLayer(ln, { fade: 0.3, hold: true }); S.cur = { name: bn };
    if (S.mode === 'graph') { me.stateId = mode === 'sit' ? 'sit_mat' : 'kneel'; }
  }
  syncHeld();
}

// ---------------------------------------------------------------- керування камерою
function camPreset(n) {
  const st = S.stage, t = st.controls.target, d = 6.4;
  const P = { '3q': [0.56, 0.3, 0.77], front: [0, 0.22, 1], side: [1, 0.22, 0], top: [0.001, 1, 0.001] }[n]; if (!P) return;
  const len = Math.hypot(...P); st.camera.position.set(t.x + P[0] / len * d, t.y + P[1] / len * d * (n === 'top' ? 1.1 : 1), t.z + P[2] / len * d); st.controls.update();
}

// ---------------------------------------------------------------- старт
function wireUi() {
  $$('#modes button').forEach((b) => (b.onclick = () => enterMode(b.dataset.mode)));
  $('#q').oninput = buildClipList; $('#catSel').onchange = buildClipList; $('#statSel').onchange = buildClipList;
  $('#partnerSel').onchange = (e) => { S.partnerManual = e.target.value || null; S.partnerId = S.partnerManual; if (S.partner && S.partner.id !== S.partnerId) { resetPartner(); S.partner = null; } if (S.mode === 'graph') $('#boutPartner').textContent = S.partnerId ? S.data.cast.characters[S.partnerId].name : '—'; if (S.mode === 'clips' && S.cur && S.cur.pair) playClip(`${S.cur.pair}_${S.cur.role}`); };
  $$('#dmgSeg button').forEach((b) => (b.onclick = () => { S.dmg = +b.dataset.v; $$('#dmgSeg button').forEach((x) => x.classList.toggle('on', x === b)); syncLook(); }));
  $('#propSel').onchange = (e) => { S.propSel = e.target.value; updatePropsOnly(); };
  $('#outerChk').onchange = (e) => { S.outer = e.target.checked; syncLook(); };
  $('#gridChk').onchange = syncLook; $('#rulerChk').onchange = syncLook;
  $('#ringSel').onchange = async (e) => { await S.stage.setVenue(S.rings[e.target.value]); if (S.mode === 'clips' && S.cur) { const n = S.cur.pair ? `${S.cur.pair}_${S.cur.role}` : S.cur.name; playClip(n); } else if (S.mode === 'graph') graphEnter(); else if (S.mode === 'mix') mixApply(); };
  const replay = () => { syncLook(); if (S.mode === 'clips' && S.cur) { const n = S.cur.pair ? `${S.cur.pair}_${S.cur.role}` : S.cur.name; playClip(n); } else if (S.mode === 'graph') graphEnter(); else if (S.mode === 'mix') mixApply(); };
  $('#roamChk').onchange = (e) => { S.roam = e.target.checked; replay(); };
  $('#followChk').onchange = (e) => { S.follow = e.target.checked; };
  $('#ikChk').onchange = (e) => { S.ik = e.target.checked; syncLook(); };
  $('#stairsChk').onchange = (e) => { S.stairs = e.target.checked; if (S.stairs) { $('#ikChk').checked = true; S.ik = true; $('#roamChk').checked = true; S.roam = true; } syncLook(); replay(); if (S.stairs && S.me) { S.me.shift(0, -1.2, 0); } };
  $$('[data-cam]').forEach((b) => (b.onclick = () => camPreset(b.dataset.cam)));
  $$('#heldSeg button').forEach((b) => (b.onclick = () => setHeld(b.dataset.v)));
  $('#heldSit').onclick = () => setHeld('sit'); $('#heldKneel').onclick = () => setHeld('kneel');
  $('#boutAll').onclick = () => boutRun(boutSteps().map((_, i) => i), true);
  $('#boutReset').onclick = () => { S.bout = null; boutRun([], true); S.bout = null; boutMarks(); };
  $('#boutStop').onclick = () => { S.bout = null; boutMarks(); log('Зупинено'); };
  ['mixBase', 'mixUp', 'mixAdd'].forEach((id) => ($(`#${id}`).onchange = () => { if (id === 'mixUp') delete $('#mixMask').dataset.touched; mixApply(); }));
  $('#mixMask').onchange = () => { $('#mixMask').dataset.touched = '1'; mixApply(); };
  $('#mixUpW').oninput = (e) => { $('#mixUpTxt').textContent = f2(e.target.value); S.mix.upW = +e.target.value; if (S.me && S.me.layer) S.me.layer.userW = +e.target.value; };
  $('#mixAddW').oninput = (e) => { $('#mixAddTxt').textContent = f2(e.target.value); S.mix.addW = +e.target.value; if (S.me && S.me.reaction) S.me.reaction.userW = +e.target.value; };
  $('#mixGo').onclick = mixApply; $('#mixRep').onchange = () => { S.mix.rep = $('#mixRep').checked; };
  wireTransport();
}

async function init() {
  try {
    const [cast, cm, cf, graph, tokens] = await Promise.all([jget('data/cast.json'), jget('data/clips_m.json'), jget('data/clips_f.json'), jget('data/graph.json'), jget('brand_tokens.json')]);
    S.data = { cast, clips: { m: cm, f: cf }, graph }; S.tokens = tokens;
    S.stage = makeStage($('#gl'), tokens);
    initSpray();
    S.rings = await jget('data/rings.json');
    $('#ringSel').innerHTML = Object.values(S.rings).map((r) => `<option value="${r.id}">${esc(r.title)}</option>`).join('');
    await S.stage.setVenue(Object.values(S.rings)[0]);
    await Promise.all(cast.order.map(async (id) => { try { const r = await fetch(`data/${id}.glb`, { method: 'HEAD' }); S.avail[id] = r.ok; } catch { S.avail[id] = false; } }));
    wireUi(); renderCast(); buildClipList(); fillPartner();
    const propsLoaded = loadProps();
    S.last = performance.now(); loop();
    const first = cast.order.find((i) => S.avail[i]) || cast.order[0];
    await selectActor(first);
    await propsLoaded;
    if (!S.errors.length) status(`Готово · ${Object.keys(S.avail).filter((k) => S.avail[k]).length} з ${cast.order.length} моделей · ${S.propsState}`);
  } catch (e) { overlay('Не вдалося запустити: ' + e.message, true); status(e.message, true); S.errors.push(String(e.stack || e)); console.error(e); }
  S.ready = true; window.ring.ready = true;
}

// ---------------------------------------------------------------- програмний інтерфейс для перевірки
function ctxState() {
  const v = primaryVoice(), me = S.me;
  return {
    ready: S.ready, mode: S.mode, id: S.meId, partner: S.partnerId, paused: S.paused, speed: S.speed, dmg: S.dmg,
    clip: v ? v.name : null, t: v ? +v.pose().toFixed(3) : null, dur: v ? +v.dur.toFixed(3) : null, finished: v ? v.finished : null,
    voices: me ? me.voices.map((x) => ({ n: x.name, w: +x.w.toFixed(3), t: +x.t.toFixed(3) })) : [],
    layer: me && me.layer ? { n: me.layer.name, w: +me.layer.layerW.toFixed(3), mask: me.layer.mask } : null,
    reaction: me && me.reaction ? { n: me.reaction.name, w: me.reaction.userW } : null,
    state: me ? me.stateId : null, tread: me ? me.treadActive : null,
    me: me ? { x: +me.world.x.toFixed(3), z: +me.world.z.toFixed(3), yaw: +me.world.yaw.toFixed(3), hs: +me.hs.toFixed(3) } : null,
    partnerPos: S.partner && S.partner.holder.visible ? { x: +S.partner.world.x.toFixed(3), z: +S.partner.world.z.toFixed(3), yaw: +S.partner.world.yaw.toFixed(3) } : null,
    contactNow: measureContact(),
    contact: S.contact ? { a: S.contact.a, b: S.contact.b, cm: +(S.contact.dist * 100).toFixed(1), t: +S.contact.t.toFixed(3) } : null,
    bout: S.bout ? { step: S.bout.cur, phase: S.bout.phase, label: S.bout.step && S.bout.step.label } : null, boutDone: !!S.boutDone,
    ik: me ? { on: me.ik.on, p: +me.ik.p.toFixed(3), active: me.ik.active } : null, props: me ? Object.keys(me.propNodes).filter((k) => me.propNodes[k].visible) : [],
    propsState: S.propsState, errors: S.errors.slice(-8),
  };
}
window.ring = {
  ready: false,
  select: (id) => selectActor(id),
  setMode: (m) => enterMode(m),
  play: (name) => { if (S.mode !== 'clips') enterMode('clips'); playClip(name); return ctxState(); },
  seek: (t) => { seek(t); return ctxState(); },
  step: (dt = 1 / 30, n = 1) => { for (let i = 0; i < n; i++) { const keep = S.paused, ks = S.speed; S.paused = false; S.speed = 1; tickApp(dt); S.paused = keep; S.speed = ks; } followCamera(); updateFloor(); S.stage.renderer.render(S.stage.scene, S.stage.camera); return ctxState(); },
  pause: (p = true) => { S.paused = p; },
  state: ctxState,
  cam: (n) => camPreset(n),
  camTo: (px, py, pz, tx, ty, tz) => { const st = S.stage; st.camera.position.set(px, py, pz); st.controls.target.set(tx, ty, tz); st.controls.update(); },
  frameActors: (dist = 3.2, az = 0.7) => { const acts = activeActors(); let x = 0, z = 0; for (const a of acts) { x += a.world.x; z += a.world.z; } x /= acts.length; z /= acts.length; const st = S.stage; st.controls.target.set(x, 0.95, z); st.camera.position.set(x + Math.sin(az) * dist, 1.5, z + Math.cos(az) * dist); st.controls.update(); },
  setDamage: (n) => { $(`#dmgSeg button[data-v="${n}"]`).click(); },
  prop: (p) => { $('#propSel').value = p; $('#propSel').onchange({ target: $('#propSel') }); },
  held: (m) => setHeld(m),
  edge: (from, to) => { const e = (S.graphEdges || []).find((x) => x.from === from && x.to === to); if (e) runEdge(e); return !!e; },
  boutAll: () => $('#boutAll').click(),
  boutStep: (i) => boutRun([i], false),
  mix: (o) => { if (o.base) $('#mixBase').value = o.base; if ('up' in o) $('#mixUp').value = o.up; if ('add' in o) $('#mixAdd').value = o.add; mixApply(); },
  toggle: (id, v) => { const c = $(`#${id}`); c.checked = v; c.dispatchEvent(new Event('change')); },
  venue: async (id) => { $('#ringSel').value = id; await S.stage.setVenue(S.rings[id]); return { ...RING }; },
  ropeSag: () => +S.stage.ropes.maxDeflection().toFixed(3),
  boneY: (n) => { const v = new THREE.Vector3(); const b = S.me.boneWorld(`mixamorig${n}`, v); return b ? +b.y.toFixed(3) : null; },
  ropeTop: () => { const h = S.stage.ropes.nearestTop([0, 1.2, RING.rope]); return h ? +h.p[1].toFixed(3) : null; },
  hit: (v) => { $('#hitSel').value = v; },
  async shot(name) {
    S.stage.renderer.render(S.stage.scene, S.stage.camera);
    // toDataURL синхронний: toBlob у прихованій панелі чекає секунду на кадр
    const url = S.stage.renderer.domElement.toDataURL('image/jpeg', 0.9);
    const bin = atob(url.slice(url.indexOf(',') + 1)); const buf = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) buf[i] = bin.charCodeAt(i);
    const res = await fetch(`/__shot?name=${encodeURIComponent(name)}`, { method: 'POST', body: buf });
    return { ok: res.ok, size: buf.length };
  },
  pixelStats() { const g = S.stage.renderer.domElement; const c = document.createElement('canvas'); c.width = 64; c.height = 64; const x = c.getContext('2d'); S.stage.renderer.render(S.stage.scene, S.stage.camera); x.drawImage(g, 0, 0, 64, 64); const d = x.getImageData(0, 0, 64, 64).data; let r = 0, gg = 0, b = 0; for (let i = 0; i < d.length; i += 4) { r += d[i]; gg += d[i + 1]; b += d[i + 2]; } const n = d.length / 4; return [r / n, gg / n, b / n].map((v) => +v.toFixed(1)); },
  S,
};
init();
