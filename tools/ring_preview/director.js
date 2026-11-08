// Режисер ланцюга бою на сторінці анімацій «Рингу».
//
// Ланцюг розраховується НАПЕРЕД: із кривих кореня кліпів (data/clips_*.json), зросту борців і розміру рингу виходить
// план — для кожного борця послідовність відрізків «кліп · з якої миті кліпу · коли · від якої бази · з якими
// поправками». Програвання кінематичне: у кожну мить борець стоїть там, де його поставив план, тож немає ні ковзань
// між кроками, ні телепортів, ні «добігань назад», а час і місце кожного кроку відомі до старту.
//
// Правила постановки (власник: «Прийоми — у центрі рингу», «Відскок — спиною до канатів», без затримок у переходах):
//  · пари — біля центру; канати — лише там, де кліп справді з канатом;
//  · кидок у канат → забіг продовжується відскоком без зупинки; лінія каната кліпу лягає точно на канат рингу, як
//    задумав генератор (ring_anims_ropes.py: канат за ROPE_D + 0,12 = 1,83 одиниці ригу від старту кліпу);
//  · стики кліпів — у мить, коли поза кінцівок і швидкість кореня найближчі (зіставлення поз по кадрах);
//  · хто має перейти на інше місце — переходить кроками (поворот, крок у стійці, хода), а не ковзає;
//  · пара ставиться за геометрією в мить контакту (з поправкою на різний зріст борців).
import { THREE, RING, FPS, placeRel, baseFrom, rootRel, wrapPi, smooth, se2 } from './engine.js';

const PI = Math.PI;
const lerp = (a, b, u) => a + (b - a) * u;
const clamp = (x, a, b) => Math.min(b, Math.max(a, x));
const hyp = Math.hypot;
const fwd = (yaw) => [Math.sin(yaw), Math.cos(yaw)];        // світ three: yaw 0 — обличчям у +z
const lerpPose = (a, b, u) => ({ x: lerp(a.x, b.x, u), z: lerp(a.z, b.z, u), yaw: a.yaw + wrapPi(b.yaw - a.yaw) * u });
const se2inv = (a) => { const c = Math.cos(a[2]), s = Math.sin(a[2]); return [-(c * a[0] + s * a[1]), -(-s * a[0] + c * a[1]), -a[2]]; };

// ---------------------------------------------------------------- канати: геометрія генератора
// Лінія каната від старту кліпу (одиниці ригу): для варіантів ring_anims_ropes.py — ROPE_D + 0,12 = 1,83;
// для старого Move_Rope_Rebound — найдальша точка кореня + 0,10 (той самий запас, що в Rope_Back_L).
export const ROPE_LINE = 1.83;
export function ropeLineRel(m) {
  if (m.name && m.name.startsWith('Rope_')) return ROPE_LINE;
  let far = 0; for (const r of m.root || []) far = Math.max(far, hyp(r[0], r[1]));
  return far + 0.10;
}

// швидкість кореня в системі борця [уперед, ліворуч], м/с
export function rootVel(m, t, hs) {
  const h = 1 / 60, t0 = Math.max(0, t - h), t1 = t + h;
  const a = rootRel(m, t0), b = rootRel(m, t1), dt = t1 - t0;
  const c = Math.cos(-a[2]), s = Math.sin(-a[2]), dx = b[0] - a[0], dy = b[1] - a[1];
  const lx = c * dx - s * dy, ly = s * dx + c * dy;
  return [(-ly / dt) * hs, (lx / dt) * hs];
}
export const speedAt = (m, t, hs) => hyp(...rootVel(m, t, hs));
// до якої миті кліп відскоку ще біжить обличчям уперед (далі — розворот спиною; входити в нього посеред розвороту не можна)
export function ropeEntryMax(m) { const r = m.root || []; for (let i = 0; i < r.length; i++) if (Math.abs(r[i][2] - r[0][2]) > 0.12) return Math.max(0.05, i / FPS - 0.03); return 0.45; }

// ---------------------------------------------------------------- банк поз: стопи, кисті, голова й таз у кожному кадрі
const FEAT_BONES = ['mixamorigLeftFoot', 'mixamorigRightFoot', 'mixamorigLeftHand', 'mixamorigRightHand', 'mixamorigHead', 'mixamorigHips'];
const FEAT_W = [1.6, 1.6, 0.7, 0.7, 0.6, 1.2];
export class PoseBank {
  constructor() { this.c = new Map(); }
  get(a, name) {
    const key = a.id + '|' + name; if (this.c.has(key)) return this.c.get(key);
    const raw = a.lib.raw.get(name); if (!raw) return null;
    const clip = new THREE.AnimationClip('__pose__' + name, raw.dur, a.tracksFor(name));
    const act = a.mixer.clipAction(clip); act.setLoop(THREE.LoopRepeat, Infinity); act.play(); act.weight = 1; act.enabled = true;
    const hp = a.holder.position.clone(), hr = a.holder.rotation.clone();
    a.holder.position.set(0, 0, 0); a.holder.rotation.set(0, 0, 0);
    const N = Math.max(2, Math.round(raw.dur * FPS) + 1), F = new Float32Array(N * 18), v = new THREE.Vector3();
    for (let i = 0; i < N; i++) {
      act.time = Math.min(i / FPS, raw.dur - 1e-4); a.mixer.update(0); a.holder.updateMatrixWorld(true);
      FEAT_BONES.forEach((b, j) => { const o = a.bones[b]; if (o) { v.setFromMatrixPosition(o.matrixWorld); F[i * 18 + j * 3] = v.x; F[i * 18 + j * 3 + 1] = v.y; F[i * 18 + j * 3 + 2] = v.z; } });
    }
    act.stop(); a.mixer.uncacheAction(clip); a.mixer.uncacheClip(clip);
    a.holder.position.copy(hp); a.holder.rotation.copy(hr); a.holder.updateMatrixWorld(true);
    const r = { N, F, loop: !!(a.metaOf(name) || {}).loop };
    this.c.set(key, r); return r;
  }
}
function featAt(P, t) {
  let i, i0, j;
  if (P.loop) { const n = P.N - 1; i = ((Math.round(t * FPS) % n) + n) % n; i0 = (i - 1 + n) % n; j = (i + 1) % n; }
  else { i = clamp(Math.round(t * FPS), 0, P.N - 1); i0 = Math.max(0, i - 1); j = Math.min(P.N - 1, i + 1); }
  const k = FPS / (P.loop ? 2 : Math.max(1, j - i0));
  const pos = P.F.subarray(i * 18, i * 18 + 18), a = P.F.subarray(i0 * 18, i0 * 18 + 18), b = P.F.subarray(j * 18, j * 18 + 18);
  return { pos, a, b, k };
}
// ціна стику: X у мить tx → Y у мить ty (поза кінцівок, їхні швидкості, швидкість кореня)
export function cutCost(bank, actor, X, tx, Y, ty) {
  const PX = bank.get(actor, X), PY = bank.get(actor, Y); if (!PX || !PY) return 1e9;
  const fx = featAt(PX, tx), fy = featAt(PY, ty);
  let c = 0;
  for (let j = 0; j < 6; j++) {
    for (let q = 0; q < 3; q++) {
      if (j === 5 && q !== 1) continue;                                   // таз — лише висота
      const k = j * 3 + q, dp = fx.pos[k] - fy.pos[k];
      const dv = ((fx.b[k] - fx.a[k]) * fx.k - (fy.b[k] - fy.a[k]) * fy.k) * 0.1;
      c += FEAT_W[j] * (dp * dp + dv * dv);
    }
  }
  const vX = rootVel(actor.metaOf(X), tx, actor.hs), vY = rootVel(actor.metaOf(Y), ty, actor.hs);
  return c + 0.06 * ((vX[0] - vY[0]) ** 2 + (vX[1] - vY[1]) ** 2);
}
export function bestCut(bank, actor, X, xw, Y, yw, extra) {
  let best = { tx: xw[0], ty: yw[0], cost: 1e9 };
  const sx = Math.max(0, Math.round((xw[1] - xw[0]) * FPS)), sy = Math.max(0, Math.round((yw[1] - yw[0]) * FPS));
  for (let i = 0; i <= sx; i++) {
    const tx = sx ? xw[0] + (xw[1] - xw[0]) * i / sx : xw[0];
    for (let j = 0; j <= sy; j++) {
      const ty = sy ? yw[0] + (yw[1] - yw[0]) * j / sy : yw[0];
      const c = cutCost(bank, actor, X, tx, Y, ty) + (extra ? extra(tx, ty) : 0);
      if (c < best.cost) best = { tx, ty, cost: c };
    }
  }
  return best;
}

// ---------------------------------------------------------------- доріжка одного борця
export class Track {
  constructor(actor, F0, T0 = 0) { this.a = actor; this.hs = actor.hs; this.segs = []; this.T = T0; this.F = { ...F0 }; }
  m(name) { const m = this.a.metaOf(name); if (!m) throw new Error(`немає кліпа ${name} у «${this.a.cfg.name}»`); return m; }
  has(name) { return this.a.has(name); }
  last() { return this.segs[this.segs.length - 1] || null; }
  // відрізок: кліп name з кліпового часу t0, триває dur глобальних секунд зі швидкістю rate; стикується з поточною позою
  add(name, o = {}) {
    if (!this.has(name)) throw new Error(`у «${this.a.cfg.name}» немає кліпа ${name}`);
    const m = this.m(name), t0 = o.t0 || 0, rate = o.rate || 1;
    const dur = o.dur ?? Math.max(0, (m.duration - t0) / rate);
    const B = baseFrom(this.F, rootRel(m, t0), this.hs);
    const s = { clip: name, m, at: this.T, t0, rate, dur, fade: o.fade ?? 0.25, B, warps: [], step: o.step ?? null, pair: o.pair || null, role: o.role || null };
    this.segs.push(s); this.T += dur; this.F = this.worldIn(s, this.T); return s;
  }
  // цикл (стійка, лежання) до глобального часу until; той самий цикл продовжується без шва
  hold(name, until, o = {}) {
    const d = until - this.T; if (d <= 1e-3) return null;
    const l = this.last(), t0 = o.t0 ?? (l && l.clip === name ? l.t0 + l.dur * l.rate : 0);
    return this.add(name, { ...o, dur: d, t0 });
  }
  // обрізати останній відрізок так, щоб він закінчився в глобальний час T
  cut(T) { const s = this.last(); s.dur = Math.max(0, T - s.at); this.T = T; this.F = this.worldIn(s, T); }
  clipTime(s, T) { return s.t0 + (T - s.at) * s.rate; }
  worldIn(s, T) {
    const w = s.arc ? this.arcPose(s, T) : placeRel(s.B, rootRel(s.m, this.clipTime(s, T)), this.hs);
    for (const q of s.warps) { const p = smooth((T - s.at - q.a) / Math.max(1e-3, q.b - q.a)); w.x += q.dx * p; w.z += q.dz * p; w.yaw += q.dyaw * p; }
    return w;
  }
  // Рух по дузі: шлях кліпу вперед згинається з кривиною k (1/м), поворот тіла — разом зі шляхом (ходьба й біг по колу).
  // Відносно старту відрізку: f — пройдено вперед, l — убік, ψ — власний поворот кліпу.
  arcPose(s, T) {
    const r0 = rootRel(s.m, s.t0), r = rootRel(s.m, this.clipTime(s, T));
    const c0 = Math.cos(-r0[2]), s0 = Math.sin(-r0[2]), dx = r[0] - r0[0], dy = r[1] - r0[1];
    const lx = c0 * dx - s0 * dy, ly = s0 * dx + c0 * dy, f = -ly * this.hs, l = lx * this.hs;
    const F0 = s.F0, k = s.arc, psi0 = F0.yaw, psi = psi0 + k * f;
    let px, pz;
    if (Math.abs(k) < 1e-6) { px = F0.x + Math.sin(psi0) * f; pz = F0.z + Math.cos(psi0) * f; }
    else { px = F0.x + (Math.cos(psi0) - Math.cos(psi)) / k; pz = F0.z + (Math.sin(psi) - Math.sin(psi0)) / k; }
    // бічний зсув кліпу (розгойдування) — у системі дуги; +X ригу = ліворуч = (cos ψ, −sin ψ) у світі
    return { x: px + Math.cos(psi) * l, z: pz - Math.sin(psi) * l, yaw: psi + (r[2] - r0[2]) };
  }
  // відрізок по дузі: кліп name, триває dur, кривина k
  addArc(name, k, o = {}) {
    const F0 = { ...this.F }, s = this.add(name, o); s.arc = k; s.F0 = F0;
    this.F = this.worldIn(s, this.T); return s;
  }
  // поправка до відрізку s: зсув і поворот, розподілені плавно між a і b (секунди від початку відрізку)
  warp(s, dx, dz, dyaw, a = 0, b = null) {
    if (Math.abs(dx) + Math.abs(dz) + Math.abs(dyaw) < 1e-6) return;
    s.warps.push({ dx, dz, dyaw, a: clamp(a, 0, s.dur), b: clamp(b ?? s.dur, Math.min(a + 0.05, s.dur), s.dur) });
    if (s === this.last()) this.F = this.worldIn(s, this.T);
  }
  // довести кінець останнього відрізку до пози F* (поправкою на його другу половину)
  landAt(Fs, from = 0.4) {
    const s = this.last(); const e = this.worldIn(s, this.T);
    this.warp(s, Fs.x - e.x, Fs.z - e.z, wrapPi(Fs.yaw - e.yaw), s.dur * from, s.dur);
  }
  segAt(T) { let k = -1; for (let i = 0; i < this.segs.length; i++) { if (this.segs[i].at <= T + 1e-9) k = i; else break; } return k; }
  // поза на довільний час: у вікні кросфейду світ змішує продовження попереднього відрізку з новим
  pose(T) {
    const k = this.segAt(T);
    if (k < 0) return this.segs[0] ? this.worldIn(this.segs[0], this.segs[0].at) : { ...this.F };
    const s = this.segs[k], w = this.worldIn(s, T);
    if (k > 0 && s.fade > 0 && T - s.at < s.fade) return lerpPose(this.worldIn(this.segs[k - 1], T), w, smooth((T - s.at) / s.fade));
    return w;
  }
  // прибрати все, що починається після T, і обрізати останній відрізок на T
  truncate(T) { this.segs = this.segs.filter((g) => g.at < T - 1e-6); if (this.segs.length) this.cut(T); else { this.T = T; } }
  clone() { const t = new Track(this.a, this.F, this.T); t.segs = this.segs.map((g) => ({ ...g, warps: g.warps.map((q) => ({ ...q })), B: { ...g.B }, F0: g.F0 ? { ...g.F0 } : undefined })); return t; }
  adopt(o) { this.segs = o.segs; this.T = o.T; this.F = { ...o.F }; }
  // повернути все заплановане на ψ навколо початку координат і зсунути
  transform(psi, tx, tz) {
    const c = Math.cos(psi), s = Math.sin(psi);
    const tr = (p) => ({ x: p.x * c + p.z * s + tx, z: -p.x * s + p.z * c + tz, yaw: p.yaw + psi });
    for (const g of this.segs) { g.B = tr(g.B); if (g.F0) g.F0 = tr(g.F0); for (const q of g.warps) { const dx = q.dx * c + q.dz * s, dz = -q.dx * s + q.dz * c; q.dx = dx; q.dz = dz; } }
    this.F = tr(this.F);
  }
}
// перетворення пози тим самим поворотом і зсувом, що Track.transform
const xform = (G, p) => { const c = Math.cos(G.psi), s = Math.sin(G.psi); return { x: p.x * c + p.z * s + G.tx, z: -p.x * s + p.z * c + G.tz, yaw: p.yaw + G.psi }; };

// ---------------------------------------------------------------- пара: де стати, щоб у мить контакту геометрія була задумана
function pairRefTime(mA) {
  const ev = (mA.events || []).find((e) => e.event === 'contact'); if (ev) return ev.t;
  const off = mA.offset || [0, 0, 0], r = mA.root; if (!r) return 0;
  let best = 0, bd = 1e9; for (let i = 0; i < r.length; i++) { const d = hyp(r[i][0] - off[0], r[i][1] - off[1]); if (d < bd) { bd = d; best = i / FPS; } }
  return Math.min(best, mA.duration);
}
function pairDelta(mA, mB) {
  const off = mA.offset || [0, 0, 0], rot = (mA.rot || 0) * PI / 180, tr = pairRefTime(mA);
  const rA = rootRel(mA, tr), rB = se2.compose([off[0], off[1], rot], rootRel(mB, tr));
  return { d: se2.compose(se2inv(rA), rB), tr };
}
// поза B у кліповий час tb, якщо A у кліповий час ta стоїть у FA (обидва кліпи синхронні: ta = tb)
export function pairPoseB(FA, a, b, base, t = 0) {
  const mA = a.metaOf(`${base}_A`), mB = b.metaOf(`${base}_B`), s = (a.hs + b.hs) / 2, { d, tr } = pairDelta(mA, mB);
  const WA = placeRel(baseFrom(FA, rootRel(mA, t), a.hs), rootRel(mA, tr), a.hs);
  return placeRel(baseFrom(placeRel(WA, d, s), rootRel(mB, tr), b.hs), rootRel(mB, t), b.hs);
}
export function pairPoseA(FB, a, b, base, t = 0) {
  const mA = a.metaOf(`${base}_A`), mB = b.metaOf(`${base}_B`), s = (a.hs + b.hs) / 2, { d, tr } = pairDelta(mA, mB);
  const WB = placeRel(baseFrom(FB, rootRel(mB, t), b.hs), rootRel(mB, tr), b.hs);
  return placeRel(baseFrom(placeRel(WB, se2inv(d), s), rootRel(mA, tr), a.hs), rootRel(mA, t), a.hs);
}
// Почати пару в час T з кліпового часу t: «якір» (anchor) стартує звідки є, другий має стояти точно на місці пари.
// Різницю, якщо вона є, другий добирає поправкою на своєму попередньому русі (до 0,6 с) — без стрибка.
function startPair(trA, trB, base, T, t, anchor, step, fades = [0.2, 0.2], o = {}) {
  const free = anchor === 'A' ? trB : trA, fix = anchor === 'A' ? trA : trB;
  // B, що ще доходить (o.bLate), починає свій кліп пізніше на ту саму різницю — пара лишається синхронною за кліповим часом
  if (o.bLate && anchor === 'A' && trB.T > T + 1e-3) {
    const raw = trB.T - T, dMax = Math.max(0, pairRefTime(trA.m(`${base}_A`)) - 0.2 - t);
    if (!o.noCap && trB.T - T > dMax) { const over = trB.T - T - dMax; trB.cut(Math.max(trB.last().at + 0.05, trB.T - over)); }
    const d = Math.max(0, trB.T - T), want = pairPoseB(trA.F, trA.a, trB.a, base, t), l = trB.last();
    const e = { x: want.x - trB.F.x, z: want.z - trB.F.z, yaw: wrapPi(want.yaw - trB.F.yaw) };
    if (l) trB.warp(l, e.x, e.z, e.yaw, Math.max(0, l.dur - 0.6), l.dur); else trB.F = want;
    const sA = trA.add(`${base}_A`, { t0: t, fade: fades[0], step, pair: base, role: 'A' });
    const sB = trB.add(`${base}_B`, { t0: t + d, fade: fades[1], step, pair: base, role: 'B' });
    return { sA, sB, err: hyp(e.x, e.z), errYaw: e.yaw, late: d, rawLate: raw };
  }
  if (anchor === 'A' && trB.T < T - 1e-3) trB.hold('Ring_Stance_Loop', T, { fade: 0.25 });
  const want = anchor === 'A' ? pairPoseB(fix.F, trA.a, trB.a, base, t) : pairPoseA(fix.F, trA.a, trB.a, base, t);
  const err = { x: want.x - free.F.x, z: want.z - free.F.z, yaw: wrapPi(want.yaw - free.F.yaw) };
  const l = free.last();
  if (l && (hyp(err.x, err.z) > 0.002 || Math.abs(err.yaw) > 0.003)) {
    const a = Math.max(0, l.dur - 0.6); free.warp(l, err.x, err.z, err.yaw, a, l.dur);
  } else free.F = want;
  const sA = trA.add(`${base}_A`, { t0: t, fade: fades[0], step, pair: base, role: 'A' });
  const sB = trB.add(`${base}_B`, { t0: t, fade: fades[1], step, pair: base, role: 'B' });
  return { sA, sB, err: hyp(err.x, err.z), errYaw: err.yaw };
}

// ---------------------------------------------------------------- перейти на місце кроками
// Великий поворот — кліпом повороту (Move_Turn_*), зсув — кроком у стійці (уперед/назад/убік) або ходою,
// залишок — поправкою на рухомий відрізок. Повертає мить, коли борець став на місце.
const TURN_CLIPS = [['Move_Turn_180', PI], ['Move_Turn_L90', PI / 2], ['Move_Turn_R90', -PI / 2]];
// Ціль target = {x,z,yaw}; deadline — до коли стати (далі стоїть у стійці); null — без очікування.
// o.budget — скільки часу є на сам перехід (за замовчуванням до deadline); швидкість кліпів — до o.rateMax.
export function goTo(tr, target, deadline, o = {}) {
  const stance = o.stance || 'Ring_Stance_Loop', t0 = tr.T, rateMax = o.rateMax ?? 1.45;
  const budget = Math.max(0.35, o.budget ?? (deadline != null ? deadline - tr.T : 9));
  const dist = () => hyp(target.x - tr.F.x, target.z - tr.F.z);
  // далі ~0,35 м — як людина: розвернутись на ціль, пройти прямо, довернутись. Ближче — переступити на місці.
  const [hx, hz] = fwd(target.yaw), dX = target.x - tr.F.x, dZ = target.z - tr.F.z;
  const sideT = Math.abs(dX * hz - dZ * hx), alongT = Math.abs(dX * hx + dZ * hz);   // у системі кінцевої пози
  const walkFrom = o.walk ? 0.45 : 1.15;                     // o.walk — іти звичайною ходою (не в стійці) уже з пів метра
  const far = dist() > walkFrom || (dist() > (o.farFrom ?? 0.55) && sideT > 1.2 * alongT);
  const walkClip = dist() > walkFrom && tr.has('U_Walk_Loop') && !o.stanceOnly ? 'U_Walk_Loop' : 'Move_Walk_Fwd_Loop';
  const walkFar = far && tr.has(walkClip);
  // природна тривалість переходу — щоб розкласти час і, якщо треба, пришвидшити всі кліпи однаково
  const turnPick = (dyaw) => TURN_CLIPS.filter(([n]) => tr.has(n)).map(([n, ang]) => ({ n, ang, res: Math.abs(wrapPi(dyaw - ang)) })).sort((p, q) => p.res - q.res)[0];
  const head0 = Math.atan2(target.x - tr.F.x, target.z - tr.F.z);
  const turnA = walkFar ? wrapPi(head0 - tr.F.yaw) : wrapPi(target.yaw - tr.F.yaw);
  const turnB = walkFar ? wrapPi(target.yaw - head0) : 0;
  const tDur = (a) => (Math.abs(a) > (o.turnFrom ?? 0.75) && turnPick(a) ? tr.m(turnPick(a).n).duration : 0);
  const stepSp = 0.36 * tr.hs, walkSp = walkClip === 'U_Walk_Loop' ? ((tr.m('U_Walk_Loop').speed || 1.1) * tr.hs) : (tr.has(walkClip) ? speedAt(tr.m(walkClip), 0.5, tr.hs) || stepSp : stepSp);
  const natural = tDur(turnA) + tDur(turnB) + (walkFar ? dist() / walkSp : dist() / stepSp) + 0.05;
  const rate = clamp(natural / budget, 1, rateMax);
  // поворот: кліпом, якщо кут великий (залишок — поправкою в ньому ж), інакше — поправкою на кінець попереднього
  const turn = (goalYaw, carry) => {
    const dyaw = wrapPi(goalYaw - tr.F.yaw);
    const pk = Math.abs(dyaw) > (o.turnFrom ?? 0.75) ? turnPick(dyaw) : null;
    if (pk && pk.res < 1.05) {
      const s = tr.add(pk.n, { rate: clamp(rate, 0.85, rateMax), fade: 0.22 });
      const e = tr.F, cx = carry ? (target.x - e.x) : 0, cz = carry ? (target.z - e.z) : 0;
      tr.warp(s, cx, cz, wrapPi(goalYaw - e.yaw), s.dur * 0.12, s.dur * 0.95);
      return true;
    }
    if (Math.abs(dyaw) > 0.01) {
      const l = tr.last();
      if (l && l.dur > 0.3) tr.warp(l, 0, 0, dyaw, Math.max(0, l.dur - 0.5), l.dur);
      else { tr.add(stance, { dur: 0.4, fade: 0.25, t0: l && l.clip === stance ? l.t0 + l.dur * l.rate : 0 }); tr.warp(tr.last(), 0, 0, dyaw, 0, 0.4); }
    }
    return false;
  };
  if (walkFar) {
    // розвернутись на ціль, іти прямо, довернути на кінцеву позу
    turn(Math.atan2(target.x - tr.F.x, target.z - tr.F.z), false);
    const d = dist(), dur = Math.max(0.4, d / (walkSp * rate));
    tr.add(walkClip, { dur, rate, fade: walkClip === 'U_Walk_Loop' ? 0.3 : 0.25, t0: o.walkPhase || 0 });
    tr.landAt({ x: target.x, z: target.z, yaw: tr.F.yaw }, 0.4);
    turn(target.yaw, false);
    const e = tr.F; if (hyp(target.x - e.x, target.z - e.z) > 0.002) tr.landAt({ ...target, yaw: tr.F.yaw }, 0);
  } else {
    const d0 = dist();
    turn(target.yaw, d0 < 0.45);
    const d = dist();
    if (d > 0.04) {
      const [fx, fz] = fwd(tr.F.yaw), dx = target.x - tr.F.x, dz = target.z - tr.F.z, along = dx * fx + dz * fz, side = dx * fz - dz * fx;
      const lateral = Math.abs(side) > 1.4 * Math.abs(along);
      const n = lateral ? (side > 0 ? 'Move_Circle_R_Loop' : 'Move_Circle_L_Loop') : (along >= 0 ? 'Move_Walk_Fwd_Loop' : 'Move_Walk_Back_Loop');
      if (tr.has(n)) {
        const m = tr.m(n), sp = speedAt(m, 0.5, tr.hs) || stepSp, main = lateral ? Math.abs(side) : Math.abs(along);
        const dur = Math.max(0.35, main / (sp * rate)), l = tr.last();
        tr.add(n, { dur, rate, fade: 0.25, t0: l && l.clip === n ? l.t0 + l.dur * l.rate : 0 });
      } else tr.add(stance, { dur: 0.5, fade: 0.25 });
      tr.landAt(target, 0);
    } else if (d > 0.002) tr.landAt({ ...target, yaw: tr.F.yaw }, 0.2);
  }
  const ready = tr.T;
  if (deadline != null && deadline > tr.T + 1e-3) tr.hold(stance, deadline, { fade: 0.25 });
  return { ready, late: deadline != null ? Math.max(0, ready - deadline) : 0, took: ready - t0, rate };
}

// ---------------------------------------------------------------- план ланцюга бою
// steps — graph.bout (кроки з id); choice — обраний удар; bank — PoseBank. Повертає план для Director.
// Кидок має два боки (повз праве чи ліве плече): від боку залежить, на якій лінії бігун повертається з канатів і скільки
// треба переступити суперникові до ухилу й удару. Рахуються обидва, береться той, де переходи коротші й ніхто не спізнюється.
export function planBout(me, p, steps, choice, bank, opts = {}) {
  const base = (steps.find((x) => x.id === 'whip') || {}).pair || 'Whip_Ropes';
  const variants = [base, base + '_Left', base.replace(/_Left$/, '')].filter((v, i, a) => a.indexOf(v) === i && me.has(v + '_B') && p.has(v + '_A'));
  let best = null, lastErr = null;
  for (const w of variants) {
    let plan; try { plan = planBoutOnce(me, p, steps, choice, bank, { ...opts, whip: w }); } catch (e) { lastErr = e; continue; }
    const r = (k) => plan.report.find((x) => x.k === k) || {};
    const score = 10 * plan.late + (r('duck').centre || 0) + (r('hit').centre || 0) + (r('pin').centre || 0) + (r('hit').pTravel || 0) + (r('duck').pTravel || 0);
    plan.score = score; plan.whip = w;
    if (!best || score < best.score) best = plan;
  }
  if (!best) throw lastErr || new Error('ланцюг не розраховується');
  // Повний ланцюг (ухил і другий забіг) не вміщається в ринг для цієї пари (високий борець на малому рингу) —
  // скорочений: кидок у канат → відскок → одразу удар. Про це — у журналі.
  if (best.late > 0.4 || best.notes.some((n) => /надто близько/.test(n))) {
    let sb = null;
    for (const w of variants) { let plan; try { plan = planBoutOnce(me, p, steps, choice, bank, { ...opts, whip: w, short: true }); } catch (e) { continue; }
      const r = (k) => plan.report.find((x) => x.k === k) || {}; plan.score = 10 * plan.late + (r('hit').centre || 0) + (r('pin').centre || 0) + (r('hit').pTravel || 0); plan.whip = w;
      if (!sb || plan.score < sb.score) sb = plan; }
    if (sb && sb.late < best.late) { sb.notes.unshift(`ринг замалий для повного ланцюга цієї пари — без ухилу й другого забігу`); sb.short = true; return sb; }
  }
  return best;
}
function planBoutOnce(me, p, steps, choice, bank, opts = {}) {
  const notes = [], report = [];
  const st = (id) => steps.find((x) => x.id === id) || {};
  const marks = steps.map(() => ({ t0: null, t1: null }));
  const mark = (id, t0, t1) => { const i = steps.findIndex((x) => x.id === id); if (i >= 0) { marks[i].t0 = t0; marks[i].t1 = t1; } };
  const ROPE = RING.rope;
  const strikeBase = st('strike').pair || 'Forearm_Strike', revBase = st('reversal').pair || 'Chain_Wristlock_Spin';
  const whipBase = opts.whip || st('whip').pair || 'Whip_Ropes', duckBase = st('dodge').pair || 'Center_Duck';
  let late = 0;
  const short = !!opts.short;
  const rope1 = st('rebound').me || 'Rope_Back_L', rope2 = st('rebound2').me || 'Rope_Duck_Return';
  const coverBase = st('pin').pair || 'Cover';
  const hitBase = choice || (st('hit').choice || ['Center_ShoulderBlock'])[0];
  const duckUsed = opts.short ? hitBase : duckBase;
  const jog = 'Run_Jog_Loop', walk = 'U_Walk_Loop', stance = 'Ring_Stance_Loop';
  const mJ = me.metaOf(jog), vJ = speedAt(mJ, 0.3, me.hs);

  // ===== 1. Відкриття — у локальній системі (суперник там, де стоїть, вісь бою — +z). Будується вперед кліпами;
  // глобальна постановка (поворот і зсув) обирається після кидка так, щоб канат і центр лягли куди треба.
  const pStart = { x: 0, z: 0, yaw: PI };
  const meStrike = pairPoseA(pStart, me, p, strikeBase);
  const idleT = opts.idle ?? 0.5;
  const mW = me.metaOf(walk), cyc = mW.duration, vW = (mW.speed || 1.1) * me.hs;
  const c0 = bestCut(bank, me, 'U_Idle_Loop', [idleT, idleT], walk, [0, cyc]);
  const cE = bestCut(bank, me, walk, [0, cyc], 'Tr_Idle_To_Stance', [0, 0.1]);
  let Tw = ((cE.tx - c0.ty) % cyc + cyc) % cyc;
  while (Tw < (opts.walkMin ?? 1.2)) Tw += cyc;
  const M = new Track(me, placeRel(meStrike, [0, vW * Tw / me.hs, 0], me.hs), 0);
  const P = new Track(p, pStart, 0);
  M.add('U_Idle_Loop', { dur: idleT, fade: 0, step: 'idle' });
  M.add(walk, { t0: c0.ty, dur: Tw, fade: 0.35, step: 'walk' });
  M.landAt(meStrike, 0.3);
  const tr = M.add('Tr_Idle_To_Stance', { t0: cE.ty, fade: 0.2, step: 'to_stance' });
  M.cut(M.T - 0.1);                                          // удар — на хвості переходу в стійку
  const T1 = M.T;
  mark('idle', 0, idleT); mark('walk', idleT, idleT + Tw); mark('to_stance', tr.at, Math.max(tr.at, T1 - 0.25)); mark('stance', Math.max(tr.at, T1 - 0.25), T1 + 0.3);
  P.hold(stance, T1, { fade: 0 });
  // удар (я A) і удар у відповідь (суперник A) — у спільну мить, коли обидва знову в стійці
  startPair(M, P, strikeBase, T1, 0, 'A', 'strike', [0.15, 0.2]);
  const mSA = me.metaOf(`${strikeBase}_A`);
  let cs = { tx: mSA.duration, cost: 1e9 };
  for (let tx = Math.min(1.2, mSA.duration); tx <= mSA.duration + 1e-6; tx += 1 / FPS) {
    const c = cutCost(bank, me, `${strikeBase}_A`, tx, `${strikeBase}_B`, 0) + cutCost(bank, p, `${strikeBase}_B`, tx, `${strikeBase}_A`, 0);
    if (c < cs.cost) cs = { tx, cost: c };
  }
  const T2 = T1 + cs.tx; M.cut(T2); P.cut(T2); mark('strike', T1, T2);
  startPair(P, M, strikeBase, T2, 0, 'A', 'hit_back', [0.2, 0.2]);
  // бамп — з реакції на удар, у мить, коли поза найближча до старту падіння
  const mSB = me.metaOf(`${strikeBase}_B`);
  const cb = bestCut(bank, me, `${strikeBase}_B`, [0.45, Math.min(1.9, mSB.duration)], 'Bump_Back', [0, 0.2]);
  const T3 = T2 + cb.tx; M.cut(T3); mark('hit_back', T2, T3);
  const mBump = me.metaOf('Bump_Back');
  M.add('Bump_Back', { t0: cb.ty, fade: 0.18, step: 'bump' }); M.cut(M.T - 0.12);
  const T4 = M.T; mark('bump', T3, T4);
  M.add('Lying_Back_Loop', { dur: opts.lie ?? 0.45, fade: 0.25, step: 'down' });
  const T5 = M.T; mark('down', T4, T5);
  const mGU = me.metaOf('GetUp_Back');
  const cg = bestCut(bank, me, 'GetUp_Back', [Math.max(1.5, mGU.duration - 0.8), mGU.duration], `${revBase}_A`, [0, 0.15]);
  M.add('GetUp_Back', { dur: cg.tx, fade: 0.25, step: 'getup' });
  const T6 = M.T; mark('getup', T5, T6);
  // суперник після удару — у стійку й на місце реверсу (кроком)
  const mPA = p.metaOf(`${strikeBase}_A`);
  const cpa = bestCut(bank, p, `${strikeBase}_A`, [Math.min(1.1, mPA.duration), mPA.duration], stance, [0, p.metaOf(stance).duration]);
  P.cut(T2 + cpa.tx);
  P.add(stance, { t0: cpa.ty, dur: 0.25, fade: 0.3 });
  const pRev = pairPoseB(M.F, me, p, revBase, cg.ty);
  goTo(P, pRev, T6 - 0.05, { stanceOnly: true });
  startPair(M, P, revBase, T6, cg.ty, 'A', 'reversal', [0.25, 0.25]);
  // кінець реверсу — у стійку; суперник (тепер A кидка) підходить на дистанцію кидка
  const mRA = me.metaOf(`${revBase}_A`);
  const cr = bestCut(bank, me, `${revBase}_A`, [Math.max(cg.ty + 1.0, mRA.duration - 0.7), mRA.duration], stance, [0, me.metaOf(stance).duration]);
  const T7 = T6 + (cr.tx - cg.ty); M.cut(T7); P.cut(T7); mark('reversal', T6, T7);
  const pWhip = pairPoseA(M.F, p, me, whipBase);
  const gapW = hyp(pWhip.x - P.F.x, pWhip.z - P.F.z);
  const T8 = T7 + clamp(gapW / (0.36 * p.hs) / 1.3, 0.5, 1.2);
  M.add(stance, { t0: cr.ty, dur: T8 - T7, fade: 0.3, step: 'whip' });
  goTo(P, pWhip, T8, { stanceOnly: true });

  // ===== 2. Кидок у канат і відскок 1. Кидок обривається на бігу, відскок підхоплює з тієї ж швидкості й кроку.
  const whipB = `${whipBase}_B`, mWB = me.metaOf(whipB), mR1 = me.metaOf(rope1);
  const relW = (mWB.events || []).find((e) => e.event === 'release');
  const twMin = (relW ? relW.t : 0.72) + 0.42;
  // кидок — від пози, що є на T8 (локально); для кожного обриву — де ляже лінія каната
  const meW = M.F;
  const cand = [];
  for (let tw = twMin; tw <= mWB.duration + 1e-6; tw += 1 / FPS) {
    const c = bestCut(bank, me, whipB, [tw, tw], rope1, [0.06, Math.min(0.42, ropeEntryMax(mR1))], (tx, ty) => 0.6 * (speedAt(mWB, tx, me.hs) - speedAt(mR1, ty, me.hs)) ** 2);
    const Fw = placeRel(baseFrom(meW, rootRel(mWB, 0), me.hs), rootRel(mWB, tw), me.hs);
    const Br = baseFrom(Fw, rootRel(mR1, c.ty), me.hs);
    cand.push({ tw, t0r: c.ty, cost: c.cost, Br, line: placeRel(Br, [0, -ropeLineRel(mR1), 0], me.hs), head: Br.yaw });
  }
  if (!cand.length) throw new Error('кидок у канат: немає місця для обриву');
  // стики виходу з канатів у ухил (від постановки не залежать) і де буде суперник після кидка
  const mDA = me.metaOf(`${duckUsed}_A`);
  const relR = (mR1.events || []).find((e) => e.event === 'rope_release'), relT = relR ? relR.t : 0.94;
  const win = [relT + 0.04, Math.min(mR1.duration - 0.05, relT + 0.32)];
  const cRD = bestCut(bank, me, rope1, win, `${duckUsed}_A`, [0, 0.2], (tx, ty) => 0.6 * (speedAt(mR1, tx, me.hs) - speedAt(mDA, ty, me.hs)) ** 2);
  const cRJ = bestCut(bank, me, rope1, win, jog, [0, mJ.duration], (tx) => 0.6 * (speedAt(mR1, tx, me.hs) - vJ) ** 2);
  const cJD = bestCut(bank, me, jog, [0, mJ.duration], `${duckUsed}_A`, [0, 0.2]);
  const mR2 = me.metaOf(rope2);
  const mWA = p.metaOf(`${whipBase}_A`), relA = (mWA.events || []).find((e) => e.event === 'release');
  const cutWA = Math.min(mWA.duration, (relA ? relA.t : 0.72) + 0.15);
  const pWhipEnd = placeRel(baseFrom(pWhip, rootRel(mWA, 0), p.hs), rootRel(mWA, cutWA), p.hs);
  // місце ухилу для відскоку з базою Br (світ): з коротким бігом чи без — ближче до центру й до суперника
  const duckPlan = (Br, pFrom) => {
    let b = null;
    for (let i = 0; i <= 30; i++) {
      const useJog = i > 0, jd = useJog ? i * 0.05 : 0;
      let F = placeRel(Br, rootRel(mR1, useJog ? cRJ.tx : cRD.tx), me.hs);
      if (useJog) F = placeRel(baseFrom(F, rootRel(mJ, cRJ.ty), me.hs), rootRel(mJ, cRJ.ty + jd / vJ), me.hs);
      const ty = useJog ? cJD.ty : cRD.ty, Fb = pairPoseB(F, me, p, duckUsed, ty);
      const travel = hyp(Fb.x - pFrom.x, Fb.z - pFrom.z), turnA = Math.abs(wrapPi(Fb.yaw - pFrom.yaw));
      // після ухилу бігун має добігти до других канатів: від миті проходу до лінії каната — не менше, ніж забіг кліпу відскоку
      const Fp = placeRel(baseFrom(F, rootRel(mDA, ty), me.hs), rootRel(mDA, (pairRefTime(mDA) || 0.62) + 0.08), me.hs), [qx, qz] = fwd(F.yaw);
      const room = ROPE - (Fp.x * qx + Fp.z * qz), needR = (ropeLineRel(mR2) + rootRel(mR2, ropeEntryMax(mR2))[1]) * me.hs;
      const cost = hyp(Fb.x, Fb.z) ** 2 + 1.2 * travel ** 2 + 0.15 * turnA ** 2 + 0.15 * (useJog ? cRJ.cost + cJD.cost : cRD.cost) + (short ? 0 : 40 * Math.max(0, needR - room) ** 2);
      if (!b || cost < b.cost) b = { useJog, jd, ty, Fb, cost, travel };
    }
    return b;
  };
  // глобальна постановка: напрям бігу → +z, лінія каната → канат; по x — середина відкриття й лінії бігу;
  // і щоб суперникові після кидка було близько до місця ухилу (менші ринги — мало часу)
  const keyPts = [meStrike, pStart, M.segs[0].B, pRev, meW, pWhip];
  let best = null;
  for (const k of cand) {
    const G = { psi: -k.head, tx: 0, tz: 0 };
    const L = xform(G, k.line); G.tz = ROPE - L.z;
    const pts = keyPts.map((q) => xform(G, q));
    const xs = pts.map((q) => q.x).concat([L.x]); G.tx = -(Math.min(...xs) + Math.max(...xs)) / 2;
    const P2 = pts.map((q) => ({ x: q.x + G.tx, z: q.z }));
    const mid = (a, b) => hyp((a.x + b.x) / 2, (a.z + b.z) / 2);
    const walkStart = P2[2];
    const out = Math.max(0, Math.abs(walkStart.z) - (ROPE - 0.55)) + Math.max(0, Math.abs(walkStart.x) - (ROPE - 0.55));
    const costPos = mid(P2[0], P2[1]) ** 2 + 0.7 * mid(P2[3], P2[2 + 2]) ** 2 + 0.7 * mid(P2[4], P2[5]) ** 2 + 20 * out * out;
    const dk = duckPlan(xform(G, k.Br), xform(G, pWhipEnd));
    const total = costPos + 0.3 * k.cost + dk.cost;
    if (!best || total < best.total) best = { k, G, total, costPos, out, dk };
  }
  if (best.out > 0.01) notes.push(`старт ходи на ${best.out.toFixed(2)} м ближче до каната, ніж хотілось`);
  M.transform(best.G.psi, best.G.tx, best.G.tz); P.transform(best.G.psi, best.G.tx, best.G.tz);
  report.push({ k: 'opening', strikeMid: (() => { const a = xform(best.G, meStrike), b = xform(best.G, pStart); return +hyp((a.x + b.x) / 2 + 0, (a.z + b.z) / 2).toFixed(3); })(), walkStart: xform(best.G, M.segs[0].B) });
  // кидок: суперник A — якір, я підходжу точно
  startPair(P, M, whipBase, T8, 0, 'A', 'whip', [0.2, 0.22]);
  M.cut(T8 + best.k.tw);
  const T9 = M.T; mark('whip', T7, T9);
  const r1 = M.add(rope1, { t0: best.k.t0r, dur: 0.01, fade: 0.12, step: 'rebound' });
  // точна лінія каната: залишок — поправкою на біг кидка після відпускання
  {
    const w = M.segs[M.segs.length - 2], line = placeRel(r1.B, [0, -ropeLineRel(mR1), 0], me.hs);
    const [ux, uz] = fwd(r1.B.yaw), along = line.x * ux + line.z * uz, err = ROPE - along;
    M.warp(w, ux * err, uz * err, 0, Math.max(0, w.dur - 0.7), w.dur);
    r1.B = baseFrom(M.worldIn(w, T9), rootRel(mR1, best.k.t0r), me.hs);
    report.push({ k: 'rope1', err: +err.toFixed(3), line: placeRel(r1.B, [0, -ropeLineRel(mR1), 0], me.hs) });
  }

  // ===== 3. Ухил: бігун із канатів (за потреби з коротким бігом) — у Center_Duck_A; суперник чекає обличчям до нього
  // місце ухилу — для точної бази відскоку (після поправки лінії каната)
  P.cut(T8 + cutWA);
  const bD = duckPlan(r1.B, P.F);
  M.cut(r1.at + ((bD.useJog ? cRJ.tx : cRD.tx) - best.k.t0r));
  if (bD.useJog) M.add(jog, { t0: cRJ.ty, dur: bD.jd / vJ, fade: 0.12, step: 'rebound' });
  const T10 = M.T; mark('rebound', T9, T10);
  // суперник: одразу після відпускання — на місце ухилу обличчям до бігуна
  const pBeforeDuck = { ...P.F };
  goTo(P, bD.Fb, null, { stanceOnly: true, budget: T10 - 0.06 - P.T, rateMax: 1.8 });
  const sd = startPair(M, P, duckUsed, T10, bD.ty, 'A', short ? 'hit' : 'dodge', [0.14, 0.2], { bLate: true });
  { const lt = (sd.rawLate ?? sd.late ?? 0) - 0.3; if (lt > 0) { late += lt; notes.push(`суперник на місце ${short ? 'удару' : 'ухилу'} запізнюється на ${lt.toFixed(2)} с — частину переходу дотягує поправка`); } }
  report.push({ k: short ? 'hit' : 'duck', base: duckUsed, B: bD.Fb, centre: +hyp(bD.Fb.x, bD.Fb.z).toFixed(3), pErr: +sd.err.toFixed(3), pTravel: +hyp(bD.Fb.x - pBeforeDuck.x, bD.Fb.z - pBeforeDuck.z).toFixed(3) });

  // ===== 4–5. Після ухилу — у протилежні канати без гальмування (відскок 2, лінія каната на канаті), удар у центрі.
  // Варіант відскоку обирається так, щоб суперник після ухилу встиг розвернутись: якщо звичайний відскок надто швидкий —
  // повільніший біля канатів (рев, важкий, вагання). Кожен варіант пробується на копіях доріжок.
  const secondRun = (M, P, rope2X) => {
  const mR2X = me.metaOf(rope2X);
  // мить обриву ухилу (бігун уже пробіг повз) × мить входу у відскок: щоб лінія каната лягла на канат (на малому рингу
  // для високого борця — пізніше в кліпі відскоку й раніше з ухилу), з коротким бігом між ними, якщо є місце
  const duckSeg = M.last(), tDuck0 = pairRefTime(mDA) || 0.62;
  const [ux, uz] = fwd(M.worldIn(duckSeg, T10).yaw);
  let bR = null; const notesX = [], reportX = [];
  for (let tc = Math.max(bD.ty + 0.25, tDuck0 + 0.08); tc <= Math.max(bD.ty + 0.25, tDuck0 + 0.08) + 0.4; tc += 1 / FPS) {
    const Fc = M.worldIn(duckSeg, T10 + (tc - bD.ty)), along = Fc.x * ux + Fc.z * uz;
    const cj = bestCut(bank, me, `${duckBase}_A`, [tc, tc], jog, [0, mJ.duration], (tx) => 0.6 * (speedAt(mDA, tx, me.hs) - vJ) ** 2);
    for (let t0 = 0.04; t0 <= ropeEntryMax(mR2X); t0 += 1 / FPS) {
      const ahead = (ropeLineRel(mR2X) + rootRel(mR2X, t0)[1]) * me.hs, jd = (ROPE - along) - ahead;
      if (jd < -0.03) continue;
      let c;
      if (jd > 0.05) { const jdur = jd / vJ; c = cj.cost + cutCost(bank, me, jog, cj.ty + jdur, rope2X, t0) + 0.6 * (vJ - speedAt(mR2X, t0, me.hs)) ** 2 + (jd < 0.15 ? 0.3 : 0); bR = !bR || c < bR.c ? { tc, t0, jdur, jph: cj.ty, c } : bR; }
      else { c = cutCost(bank, me, `${duckBase}_A`, tc, rope2X, t0) + 0.6 * (speedAt(mDA, tc, me.hs) - speedAt(mR2X, t0, me.hs)) ** 2 + 0.2; bR = !bR || c < bR.c ? { tc, t0, jdur: 0, jph: 0, c } : bR; }
    }
  }
  if (!bR) { bR = { tc: tDuck0 + 0.08, t0: ropeEntryMax(mR2X), jdur: 0, jph: 0, c: 0 }; notesX.push('до других канатів надто близько'); }
  M.cut(T10 + (bR.tc - bD.ty)); const T11 = M.T;
  if (bR.jdur > 0.02) M.add(jog, { t0: bR.jph, dur: bR.jdur, fade: 0.12, step: 'rebound2' });
  const r2 = M.add(rope2X, { t0: bR.t0, dur: 0.01, fade: 0.12, step: 'rebound2' });
  {
    const w = M.segs[M.segs.length - 2], line = placeRel(r2.B, [0, -ropeLineRel(mR2X), 0], me.hs);
    const [vx, vz] = fwd(r2.B.yaw), along = line.x * vx + line.z * vz, err = ROPE - along;
    M.warp(w, vx * err, vz * err, 0, 0, w.dur);
    r2.B = baseFrom(M.worldIn(w, r2.at), rootRel(mR2X, bR.t0), me.hs);
    reportX.push({ k: 'rope2', rope: rope2X, err: +err.toFixed(3), line: placeRel(r2.B, [0, -ropeLineRel(mR2X), 0], me.hs) });
  }

  // ===== 5. Удар у центрі: суперник після ухилу розвертається й стає; бігун із канатів — у прийом
  const mHA = me.metaOf(`${hitBase}_A`);
  const relR2 = (mR2X.events || []).find((e) => e.event === 'rope_release'), relT2 = relR2 ? relR2.t : 0.94;
  const win2 = [relT2 + 0.04, Math.min(mR2X.duration - 0.05, relT2 + 0.32)];
  const cRH = bestCut(bank, me, rope2X, win2, `${hitBase}_A`, [0, 0.2], (tx, ty) => 0.6 * (speedAt(mR2X, tx, me.hs) - speedAt(mHA, ty, me.hs)) ** 2);
  const cRJ2 = bestCut(bank, me, rope2X, win2, jog, [0, mJ.duration], (tx) => 0.6 * (speedAt(mR2X, tx, me.hs) - vJ) ** 2);
  const cJH = bestCut(bank, me, jog, [0, mJ.duration], `${hitBase}_A`, [0, 0.2]);
  const mDB = p.metaOf(`${duckBase}_B`);
  // суперник розвертається одразу, щойно бігун пробіг повз (мить проходу + 0,1 с), — бігун уже біжить до других канатів
  const tDuck = pairRefTime(mDA);
  const cDB = bestCut(bank, p, `${duckBase}_B`, [Math.max(bD.ty + 0.15, tDuck), Math.min(mDB.duration, tDuck + 0.6)], 'Move_Turn_180', [0, 0.15], (tx) => 0.6 * (tx - tDuck));
  const pAfter = P.worldIn(P.last(), T10 + (cDB.tx - bD.ty));
  let bH = null;
  for (let i = 0; i <= 30; i++) {
    const useJog = i > 0, jd = useJog ? i * 0.04 : 0;
    let F = placeRel(r2.B, rootRel(mR2X, useJog ? cRJ2.tx : cRH.tx), me.hs);
    if (useJog) F = placeRel(baseFrom(F, rootRel(mJ, cRJ2.ty), me.hs), rootRel(mJ, cRJ2.ty + jd / vJ), me.hs);
    const ty = useJog ? cJH.ty : cRH.ty;
    const Fb = pairPoseB(F, me, p, hitBase, ty);
    const travel = hyp(Fb.x - pAfter.x, Fb.z - pAfter.z);
    // чи встигне суперник: розворот (до 1,8×) і переступ — до старту удару (+0,3 с, поки B у кліпі ще стоїть)
    const hitStart = r2.at + ((useJog ? cRJ2.tx : cRH.tx) - bR.t0) + (useJog ? jd / vJ : 0);
    const pReady = T10 + (cDB.tx - bD.ty) + 1.5 / 1.8 + Math.max(0, travel - 0.3) / (0.36 * p.hs * 1.6);
    const cost = hyp(Fb.x, Fb.z) ** 2 + 1.2 * travel ** 2 + 0.15 * (useJog ? cRJ2.cost + cJH.cost : cRH.cost) + 8 * Math.max(0, pReady - hitStart - 0.3) ** 2;
    if (!bH || cost < bH.cost) bH = { useJog, jd, ty, Fb, cost, travel };
  }
  M.cut(r2.at + ((bH.useJog ? cRJ2.tx : cRH.tx) - bR.t0));
  if (bH.useJog) M.add(jog, { t0: cRJ2.ty, dur: bH.jd / vJ, fade: 0.12, step: 'rebound2' });
  const T12 = M.T;
  P.cut(T10 + (cDB.tx - bD.ty));
  goTo(P, bH.Fb, null, { stanceOnly: true, budget: T12 - 0.06 - P.T, rateMax: 1.8 });
  const sh = startPair(M, P, hitBase, T12, bH.ty, 'A', 'hit', [0.14, 0.2], { bLate: true });
  const lateX = Math.max(0, (sh.rawLate ?? sh.late ?? 0) - 0.3); if (lateX > 0) notesX.push(`суперник на місце удару запізнюється на ${lateX.toFixed(2)} с — частину переходу дотягує поправка`);
  reportX.push({ k: 'hit', base: hitBase, B: bH.Fb, centre: +hyp(bH.Fb.x, bH.Fb.z).toFixed(3), pTravel: +bH.travel.toFixed(3), pErr: +sh.err.toFixed(3) });
  return { M, P, T11, T12, bH, sh, r2, bR, late: lateX, notes: notesX, report: reportX, rope: rope2X };
  };
  let T11, T12, bH, sh;
  if (!short) {
    const ropeVariants = [rope2, 'Rope_Roar', 'Rope_Heavy', 'Rope_Hesitate'].filter((n, i, a) => a.indexOf(n) === i && me.has(n));
    let run2 = null;
    for (const v of ropeVariants) {
      let r; try { r = secondRun(M.clone(), P.clone(), v); } catch (e) { continue; }
      if (!run2 || r.late < run2.late - 0.05) run2 = r;
      if (r.late <= 0.001) break;
    }
    if (!run2) run2 = secondRun(M.clone(), P.clone(), rope2);
    M.adopt(run2.M); P.adopt(run2.P);
    ({ T11, T12, bH, sh } = run2); late += run2.late; notes.push(...run2.notes); report.push(...run2.report);
    mark('dodge', T10, T11); mark('rebound2', T11, T12);
  } else { T11 = T12 = T10; bH = { ty: bD.ty, Fb: bD.Fb }; sh = sd; }
  const mHA = me.metaOf(`${hitBase}_A`);
  // бігун гальмує й зупиняється; суперник падає й лежить
  let tStop = mHA.duration; { const r = mHA.root || []; for (let i = Math.round((bH.ty + 0.8) * FPS); i < r.length - 1; i++) { if (hyp(r[i + 1][0] - r[i][0], r[i + 1][1] - r[i][1]) * FPS < 0.06) { tStop = i / FPS + 0.2; break; } } }
  M.cut(T12 + (Math.min(mHA.duration, tStop) - bH.ty)); const T13 = M.T; mark('hit', T12, T13);
  const fall = P.last(), mHB = p.metaOf(`${hitBase}_B`);
  const tFallEnd = fall.at + (mHB.duration - bH.ty);
  const pLie = P.worldIn(fall, tFallEnd);
  // ===== 6. Удержання: бігун повертається до лежачого й накриває
  const meCover = pairPoseA(pLie, me, p, coverBase);
  // Підхід — збоку до тулуба: останні пів метра прямо на лежачого; тіло (голова — стопи) не перетинати, обходити з кінця.
  // руки лежачого розкинуті (поза лежання — ±0,65 м убік на рівні грудей): останній відрізок — навскіс з боку ніг, повз руку
  const [bx, bz] = fwd(pLie.yaw), head = { x: pLie.x - bx * 0.8 * p.hs, z: pLie.z - bz * 0.8 * p.hs }, feet = { x: pLie.x + bx * 1.0 * p.hs, z: pLie.z + bz * 1.0 * p.hs };
  const [cx, cz] = fwd(meCover.yaw);
  const appr = (() => { const ux = -cx * Math.cos(0.6) + bx * Math.sin(0.6), uz = -cz * Math.cos(0.6) + bz * Math.sin(0.6), n = hyp(ux, uz) || 1; return [ux / n, uz / n]; })();
  const Wc = { x: meCover.x + appr[0] * 0.6, z: meCover.z + appr[1] * 0.6, yaw: Math.atan2(-appr[0], -appr[1]) };
  const segDist = (P0, P1, Q0, Q1) => {             // найменша відстань між відрізками (вибірка)
    let d = 1e9; for (let i = 0; i <= 20; i++) { const u = i / 20, px = lerp(P0.x, P1.x, u), pz = lerp(P0.z, P1.z, u);
      const vx = Q1.x - Q0.x, vz = Q1.z - Q0.z, t = clamp(((px - Q0.x) * vx + (pz - Q0.z) * vz) / (vx * vx + vz * vz || 1), 0, 1); d = Math.min(d, hyp(px - Q0.x - vx * t, pz - Q0.z - vz * t)); }
    return d;
  };
  // прямий шлях до місця удержання — якщо він не проходить по тілу й по розкинутій руці (рука — на 0,65 м убік від грудей)
  const hand = { x: pLie.x - bx * 0.21 * p.hs + (meCover.x - pLie.x) * 1.0, z: pLie.z - bz * 0.21 * p.hs + (meCover.z - pLie.z) * 1.0 };
  // кінцева точка сама стоїть за ~0,33 м від кисті (так задумана пара), тож перевіряється шлях без останніх 0,3 м
  const dl = hyp(meCover.x - M.F.x, meCover.z - M.F.z) || 1, cut3 = Math.max(0, 1 - 0.3 / dl);
  const nearEnd = { x: lerp(M.F.x, meCover.x, cut3), z: lerp(M.F.z, meCover.z, cut3) };
  const clearDirect = segDist(M.F, meCover, head, feet) > 0.45 && segDist(M.F, nearEnd, hand, hand) > 0.4;
  if (clearDirect) goTo(M, meCover, null, { rateMax: 1.2, walk: true });
  else {
  if (segDist(M.F, Wc, head, feet) < 0.5) {
    // обхід через ближчий кінець тіла (відступ 0,6 м від голови чи стоп і вбік на бік підходу)
    const side = Math.sign((Wc.x - pLie.x) * bz - (Wc.z - pLie.z) * bx) || 1;
    const around = (E, k) => ({ x: E.x + bx * k * 0.6 + bz * side * 0.5, z: E.z + bz * k * 0.6 - bx * side * 0.5 });
    const viaH = around(head, -1), viaF = around(feet, 1);
    const len = (V) => hyp(V.x - M.F.x, V.z - M.F.z) + hyp(Wc.x - V.x, Wc.z - V.z);
    const V = len(viaH) < len(viaF) ? viaH : viaF;
    goTo(M, { ...V, yaw: Math.atan2(Wc.x - V.x, Wc.z - V.z) }, null, { rateMax: 1.2, walk: true });
  }
  goTo(M, { ...Wc, yaw: Math.atan2(Wc.x - M.F.x, Wc.z - M.F.z) }, null, { rateMax: 1.2, walk: true, turnFrom: 9 });
  goTo(M, meCover, null, { rateMax: 1.2, walk: true, farFrom: 9 });
  }
  const T14 = Math.max(M.T, tFallEnd + 0.15);
  if (M.T < T14 - 1e-3) M.hold(stance, T14, { fade: 0.25 });
  P.cut(Math.min(P.T, tFallEnd));
  P.hold('Lying_Back_Loop', T14, { fade: 0.35 });
  startPair(M, P, coverBase, T14, 0, 'B', 'pin', [0.3, 0.35]);
  const Tend = Math.max(M.T, P.T);
  mark('pin', T13, Tend);
  report.push({ k: 'pin', centre: +hyp((meCover.x + pLie.x) / 2, (meCover.z + pLie.z) / 2).toFixed(3) });
  return { tracks: [M, P], marks, T: Tend, notes, report, hitBase, late };
}

// ---------------------------------------------------------------- рух по рингу для циклу (режим «Кліпи», стани руху в «Переходах»)
// Біг — від каната до каната, у канат спиною (варіант відскоку — за швидкістю бігу); хода й біг підтюпцем — колом навколо
// центру; кружляння в стійці — навколо центру (центр кола рахується з кривої самого кліпу); решта — на місці.
const ROPE_RET = ['Rope_Back_L', 'Rope_Back_R', 'Rope_Fast', 'Rope_Back_R_Fast', 'Rope_Light', 'Rope_Heavy', 'Rope_Deep', 'Rope_Back_Low', 'Rope_Roar', 'Rope_Elbow_Hook'];
function ropeFor(tr, v) {
  const c = ROPE_RET.filter((n) => tr.has(n)).map((n) => { const m = tr.m(n); let b = 9; for (let t = 0.05; t < 0.45; t += 1 / FPS) b = Math.min(b, Math.abs(speedAt(m, t, tr.hs) - v)); return { n, b }; });
  c.sort((p, q) => p.b - q.b); return c.slice(0, 2).map((x) => x.n);
}
// скільки місця до каната треба бігуну циклом run, щоб увійти у свій відскок ще до розвороту (м): найдовший з двох
// варіантів, які бере roamOn
export function ropeNeed(a, run) {
  const tr = new Track(a, { x: 0, z: 0, yaw: 0 }, 0); if (!tr.has(run)) return 0;
  const v = speedAt(tr.m(run), 0.3, tr.hs);
  let need = 0;
  for (const n of ropeFor(tr, v)) { const mR = tr.m(n); need = Math.max(need, (ropeLineRel(mR) + rootRel(mR, ropeEntryMax(mR))[1]) * tr.hs); }
  return need;
}
// скільки грати цикл m від фази ph, щоб пробігти d метрів уперед — за кривою кореня (швидкість у циклі нерівна)
function durFor(tr, m, ph, d) {
  if (d <= 0) return 0;
  const a = rootRel(m, ph), c = Math.cos(-a[2]), s = Math.sin(-a[2]);
  const dist = (t) => { const b = rootRel(m, ph + t); return -(s * (b[0] - a[0]) + c * (b[1] - a[1])) * tr.hs; };
  let lo = 0, hi = Math.max(0.05, 1.5 * d / Math.max(0.3, speedAt(m, 0.3, tr.hs)));
  for (let i = 0; i < 8 && dist(hi) < d; i++) hi *= 1.6;
  for (let i = 0; i < 30; i++) { const mid = (lo + hi) / 2; if (dist(mid) < d) lo = mid; else hi = mid; }
  return (lo + hi) / 2;
}
// добігти циклом run до каната попереду й відскочити (rope — кліп відскоку); повертає кінець відскоку
function runIntoRope(tr, run, rope, bank, o = {}) {
  const ROPE = RING.rope, mR = tr.m(rope), mJ = tr.m(run), vJ = speedAt(mJ, 0.3, tr.hs);
  // канат попереду — перпендикулярно до найближчої осі рингу; відстань до нього міряємо вздовж осі, а біг (можливо, під
  // невеликим кутом до неї) — уздовж курсу: проєкція — множником cos кута
  const axYaw = Math.round(tr.F.yaw / (PI / 2)) * (PI / 2), [ux, uz] = fwd(axYaw), along = tr.F.x * ux + tr.F.z * uz;
  const cosA = Math.max(0.7, Math.cos(wrapPi(tr.F.yaw - axYaw)));
  const ph0 = o.phase ?? 0;
  const gap = (t0) => ((ROPE - along) - (ropeLineRel(mR) + rootRel(mR, t0)[1]) * tr.hs * cosA) / cosA;   // скільки бігти до входу
  let best = null;
  for (let t0 = 0.04; t0 <= ropeEntryMax(mR); t0 += 1 / FPS) {
    const jd = gap(t0);
    if (jd < -0.02) continue;
    const jdur = durFor(tr, mJ, ph0, jd);
    const c = cutCost(bank, tr.a, run, ph0 + jdur, rope, t0) + 0.6 * (vJ - speedAt(mR, t0, tr.hs)) ** 2;
    if (!best || c < best.c) best = { t0, jdur, jd: Math.max(0, jd), c };
  }
  if (!best) best = { t0: ropeEntryMax(mR), jdur: 0, jd: 0, c: 0 };
  if (best.jdur <= 0.02) {
    // без вставки бігу — точна мить входу між кадрами, щоб лінія каната лягла без поправки
    let lo = Math.max(0.04, best.t0 - 1 / FPS), hi = Math.min(ropeEntryMax(mR), best.t0 + 1 / FPS);
    if (gap(lo) < 0 && gap(hi) > 0) { for (let i = 0; i < 30; i++) { const mid = (lo + hi) / 2; if (gap(mid) < 0) lo = mid; else hi = mid; } best.t0 = (lo + hi) / 2; best.jdur = 0; best.jd = 0; }
  }
  if (best.jdur > 0.02) {
    // Відскоки розвертають не рівно на 180° (наприклад, 188°), і від каната до каната бігун розходився б убік. Курс
    // повертається на вісь дугою самого бігу — шлях гнеться разом із тілом, без ковзання вбік. У канат — завжди
    // перпендикулярно: хто заходить під кутом, той після відскоку дзеркально йде під тим самим кутом назад. Бічний зсув
    // знімається S-дугою посередині забігу: дві половини з протилежною кривиною, курс на кінці — рівно по осі.
    if (o.axis) {
      const E = tr.F, axisYaw = Math.round(E.yaw / (PI / 2)) * (PI / 2);     // найближча з чотирьох осей
      const latL = E.x * Math.cos(axisYaw) - E.z * Math.sin(axisYaw);          // зсув ліворуч від лінії через центр
      const e0 = wrapPi(E.yaw - axisYaw), L = best.jd, fade = o.fade ?? 0.15;
      if (L >= 0.6) {
        // малі кути: бічний зсув за дві дуги k1, k2 (по L/2) — 0,75·e0·L + k1·L²/4; курс на кінці 0 ⇒ k2 = −k1 − 2·e0/L.
        // Довжина L — така, щоб проєкція S-дуги на вісь дорівнювала відстані до входу в канат (у канат — уже по осі).
        const pk = 0.25, Dax = (ROPE - along) - (ropeLineRel(mR) + rootRel(mR, best.t0)[1]) * tr.hs;
        let k1 = 0, k2 = 0, Ls = Dax;
        const shape = (Lx) => {
          k1 = (-latL - 0.75 * e0 * Lx) * 4 / (Lx * Lx);
          k1 = clamp(clamp(k1, (-pk - e0) * 2 / Lx, (pk - e0) * 2 / Lx), -0.6, 0.6);
          k2 = clamp(-k1 - 2 * e0 / Lx, -0.6, 0.6);
          let proj = 0; const n = 48, ds = Lx / n;
          for (let i = 0; i < n; i++) { const sx = (i + 0.5) * ds; proj += Math.cos(sx < Lx / 2 ? e0 + k1 * sx : e0 + k1 * Lx / 2 + k2 * (sx - Lx / 2)) * ds; }
          return proj;
        };
        for (let it = 0; it < 5; it++) Ls += Dax - shape(Ls);
        shape(Ls);
        const jdur = durFor(tr, mJ, ph0, Ls), d1 = durFor(tr, mJ, ph0, Ls / 2);
        tr.addArc(run, k1, { t0: ph0, dur: d1, fade });
        tr.addArc(run, k2, { t0: ph0 + d1, dur: jdur - d1, fade: 0 });
      } else tr.addArc(run, clamp(-e0 / Math.max(0.3, L), -0.6, 0.6), { t0: ph0, dur: best.jdur, fade });
    } else tr.add(run, { t0: ph0, dur: best.jdur, fade: o.fade ?? 0.15 });
  }
  const s = tr.add(rope, { t0: best.t0, dur: 0.01, fade: 0.12 });
  const w = tr.segs[tr.segs.length - 2];
  if (w) {
    // Поправка лінії каната — на вставку бігу; якщо вставки немає (відскок одразу в наступний канат) — на попередній
    // відрізок, але лише після відштовхування: поки спина на канаті, борець не ковзає. Коротка вставка (до 0,35 с) ділить
    // поправку з кінцем попереднього відрізку (вибіг відскоку чи останні 0,5 с), щоб зсув не стискався в соті частки секунди.
    const relOf = (q) => { const r = q.m.events && q.m.events.find((e) => e.event === 'rope_release'); return r ? clamp((r.t - q.t0) / (q.rate || 1) + 0.02, 0, Math.max(0, q.dur - 0.1)) : null; };
    const shiftBase = (q, dx, dz) => { if (q.arc) q.F0 = { ...q.F0, x: q.F0.x + dx, z: q.F0.z + dz }; else q.B = { ...q.B, x: q.B.x + dx, z: q.B.z + dz }; };
    // точка кліпу на лінії каната має лягти на площину каната (нормаль — вісь рингу); поправка — уздовж осі
    const line = placeRel(s.B, [0, -ropeLineRel(mR), 0], tr.hs), [vx, vz] = fwd(Math.round(s.B.yaw / (PI / 2)) * (PI / 2)), err = ROPE - (line.x * vx + line.z * vz);
    const p = tr.segs[tr.segs.length - 3], aW = relOf(w) ?? 0;
    // якщо й разом із попереднім на поправку менше 0,25 с, частина лягає на розбіг самого відскоку (до дотику до каната)
    let pSpan = 0, pa = 0;
    if (w.clip === run && w.dur < 0.35 && p) { pa = relOf(p) ?? Math.max(0, p.dur - 0.5); pSpan = p.dur - pa > 0.1 ? p.dur - pa : 0; }
    const span = (w.dur - aW) + pSpan, hit = (mR.events || []).find((e) => e.event === 'rope_hit');
    const rs = hit ? hit.t - best.t0 - 0.1 : 0, fr = span < 0.25 && rs > 0.1 ? rs / (rs + span) : 0;
    const e1 = err * (1 - fr);
    if (pSpan) {
      const sh = pSpan / (pSpan + (w.dur - aW));
      tr.warp(p, vx * e1 * sh, vz * e1 * sh, 0, pa, p.dur);
      shiftBase(w, vx * e1 * sh, vz * e1 * sh);                                 // вставка зсувається разом із кінцем попереднього
      tr.warp(w, vx * e1 * (1 - sh), vz * e1 * (1 - sh), 0, aW, w.dur);
    } else tr.warp(w, vx * e1, vz * e1, 0, aW, w.dur);
    s.B = baseFrom(tr.worldIn(w, s.at), rootRel(mR, best.t0), tr.hs);
    if (fr) s.pendingWarp = { dx: vx * err * fr, dz: vz * err * fr, b: rs };
  }
  // вихід із канатів — у біг за швидкістю
  const rel = (mR.events || []).find((e) => e.event === 'rope_release'), relT = rel ? rel.t : 0.94;
  const c = bestCut(bank, tr.a, rope, [relT + 0.04, Math.min(mR.duration - 0.05, relT + 0.32)], run, [0, mJ.duration], (tx) => 0.6 * (speedAt(mR, tx, tr.hs) - vJ) ** 2);
  const tRel = relT + 0.06;
  const applyPending = () => { const q = s.pendingWarp; if (q) { delete s.pendingWarp; tr.warp(s, q.dx, q.dz, 0, 0, q.b); } };
  if (o.axis && c.tx - tRel > 0.08) {
    // вибіг після відштовхування — тим самим кліпом, але дугою: розворот відскоку не рівно 180°, і курс вирівнюється вже тут
    // (на малому рингу вставки бігу між канатами майже немає)
    tr.cut(s.at + (tRel - best.t0)); applyPending();
    const P = placeRel(s.B, rootRel(mR, c.tx), tr.hs), axisYaw = Math.round(P.yaw / (PI / 2)) * (PI / 2);
    // на вибігу — курс на вісь і лише легкий приціл до центру (вставки бігу між канатами може не бути зовсім)
    const latL = P.x * Math.cos(axisYaw) - P.z * Math.sin(axisYaw), want = axisYaw + clamp(-0.2 * latL, -0.08, 0.08);
    const r0 = rootRel(mR, tRel), r1 = rootRel(mR, c.tx), c0 = Math.cos(-r0[2]), s0 = Math.sin(-r0[2]);
    const L = Math.max(0.3, -(s0 * (r1[0] - r0[0]) + c0 * (r1[1] - r0[1])) * tr.hs);       // пройдено вперед, як в arcPose
    tr.addArc(rope, clamp(wrapPi(want - P.yaw) / L, -0.6, 0.6), { t0: tRel, dur: c.tx - tRel, fade: 0 });
  } else { tr.cut(s.at + (c.tx - best.t0)); applyPending(); }
  return { phase: c.ty };
}
// Цикл рухається назад (біг спиною, хода назад): кліпи відскоку починаються бігом обличчям до каната, тож такий цикл
// канатами не бігає — лише колом навколо центру, як хода.
const movesBack = (m) => rootRel(m, m.duration)[1] > 0.05;      // +Y ригу — назад
// Продовжити цикл руху з того місця, де борець уже є (доріжка tr): біг — канатами вздовж осі, хода — колом до центру.
export function roamOn(tr, clip, bank, Tend) {
  const m = tr.m(clip), v = speedAt(m, Math.min(0.3, m.duration / 2), tr.hs), turn = rootRel(m, m.duration)[2];
  if (Math.abs(turn) > 0.05 && Math.abs(turn) < 1.5 && v < 1.2) { tr.add(clip, { dur: Tend - tr.T, fade: 0.25 }); return 'circle'; }
  const ropes = v > 1.7 && !movesBack(m) ? ropeFor(tr, v) : [];
  if (ropes.length) {
    let ph = 0, i = 0; const l = tr.last(); if (l && l.clip === clip) ph = l.t0 + l.dur * l.rate;
    // спершу — дугою на вісь рингу, де попереду найбільше місця (біг канатами йде вздовж осі, канат — перпендикулярно)
    let bestAx = null;
    // оцінка осі — після дуги розвороту (радіус 1 м): місце попереду (не менше розгону в канат), поворот, бічний зсув від
    // лінії через центр; дуга, що підходить до канатів ближче за 0,45 м, — майже заборонена
    const Rt = 1.0, F = tr.F;
    let need = 1.3;                                       // місце на вхід у відскок до розвороту (у високого борця більше)
    for (const n of ropes) { const mR = tr.m(n); need = Math.max(need, (ropeLineRel(mR) + rootRel(mR, ropeEntryMax(mR))[1]) * tr.hs + 0.25); }
    for (const ax of [0, PI / 2, PI, -PI / 2]) {
      const dpsi = wrapPi(ax - F.yaw); let E = F, maxc = Math.max(Math.abs(F.x), Math.abs(F.z));
      if (Math.abs(dpsi) > 0.08) {
        const k = Math.sign(dpsi) / Rt;
        for (let u = 0.125; u <= 1.0001; u += 0.125) {
          const psi = F.yaw + dpsi * u; E = { x: F.x + (Math.cos(F.yaw) - Math.cos(psi)) / k, z: F.z + (Math.sin(psi) - Math.sin(F.yaw)) / k, yaw: psi };
          maxc = Math.max(maxc, Math.abs(E.x), Math.abs(E.z));
        }
      }
      const room = RING.rope - (E.x * Math.sin(ax) + E.z * Math.cos(ax)), lat = Math.abs(E.x * Math.cos(ax) - E.z * Math.sin(ax));
      const sc = room - 0.6 * Math.abs(dpsi) - 0.5 * lat - (room < need ? 3 : 0) - (maxc > RING.rope - 0.45 ? 5 : 0);
      if (!bestAx || sc > bestAx.sc) bestAx = { ax, sc, room };
    }
    const dpsi = wrapPi(bestAx.ax - tr.F.yaw);
    if (Math.abs(dpsi) > 0.08) { const d = Math.abs(dpsi) * Rt; tr.addArc(clip, dpsi / d, { dur: d / v, fade: 0.2, t0: ph }); ph += d / v; }
    while (tr.T < Tend && i < 60) { const r = runIntoRope(tr, clip, ropes[i % ropes.length], bank, { phase: ph, fade: 0.14, axis: true }); ph = r.phase; i++; }
    return 'ropes';
  }
  if (v > 0.15) { orbitOn(tr, clip, Tend, { fade: 0.25 }); return 'arc'; }
  tr.add(clip, { dur: Tend - tr.T, fade: 0.25 }); return 'still';
}
// Хода по рингу з будь-якої точки: керованими дугами (кривина не більша за 1/Rmin) — на орбіту навколо центру радіуса Rc,
// далі — рівне коло. Напрям обходу — той, куди борець уже йде. Так хода від каната не виходить за канати, а з центру
// не кружляє на місці.
export function orbitOn(tr, clip, Tend, o = {}) {
  const m = tr.m(clip), v = Math.max(0.2, speedAt(m, Math.min(0.3, m.duration / 2), tr.hs)), back = movesBack(m) ? -1 : 1;
  const Rc = o.R ?? clamp(RING.rope - 0.95, 1.1, 1.8), kmax = 1 / (o.Rmin ?? 0.8), dt = 0.2;
  const l = tr.last(); let ph = l && l.clip === clip ? l.t0 + l.dur * l.rate : (o.t0 ?? 0);
  // Кути — у відліку yaw: напрям (sin a, cos a). Рух дотичною σ·(z, −x)/d повертає радіус θr зі швидкістю σ/d на метр,
  // тож рівне коло — кривина курсу σ/Rc. Дуга (Track.addArc) гне шлях на k за метр руху ВПЕРЕД; біг спиною йде назад,
  // тому для нього k = −(кривина курсу).
  const trav = (F) => F.yaw + (back < 0 ? PI : 0);                          // курс руху (біг спиною — проти обличчя)
  const F0 = tr.F, [ux, uz] = fwd(trav(F0)), cr = ux * F0.z - uz * F0.x;   // проєкція руху на дотичну σ = +1
  const sig = Math.abs(cr) > 0.05 ? (cr > 0 ? 1 : -1) : 1;                  // обходимо туди, куди вже йдемо
  let first = true, n = 0;
  while (tr.T < Tend - 1e-3 && n < 400) {
    const F = tr.F, d = hyp(F.x, F.z), pt = trav(F);
    const thT = Math.atan2(F.x, F.z) + sig * PI / 2;                        // дотична до кола через цю точку, у напрямі обходу
    const e = d - Rc, want = d < 0.25 ? pt : thT + sig * clamp(1.4 * e, -1.1, 1.1);   // поза орбітою — доверт усередину
    const herr = wrapPi(want - pt);
    if (!first && Math.abs(e) < 0.04 && Math.abs(herr) < 0.04) {          // на орбіті: рівне коло до кінця
      tr.addArc(clip, back * sig / Rc, { dur: Tend - tr.T, fade: 0, t0: ph });
      return;
    }
    const kff = d < 0.25 ? 0 : sig / Rc;
    const kappa = clamp(kff + herr / Math.max(0.35, v * 0.6), -kmax, kmax);   // курс виправляється за ~0,6 с руху
    const dur = Math.min(dt, Tend - tr.T);
    tr.addArc(clip, back * kappa, { dur, fade: first ? (o.fade ?? 0.25) : 0, t0: ph });
    ph += dur; first = false; n++;
  }
  if (tr.T < Tend - 1e-3) tr.addArc(clip, back * sig / Rc, { dur: Tend - tr.T, fade: 0, t0: ph });
}
export function planRoam(a, clip, bank, o = {}) {
  const tr = new Track(a, o.F0 || { x: 0, z: 0, yaw: 0 }, 0), Tend = o.T ?? 60, m = a.metaOf(clip);
  const v = speedAt(m, Math.min(0.3, m.duration / 2), a.hs);
  const net = rootRel(m, m.duration), turn = net[2];
  // кружляння: центр обертання з одного циклу; ставимо борця так, щоб цей центр був у центрі рингу
  if (Math.abs(turn) > 0.05 && Math.abs(turn) < 1.5 && v < 1.2) {
    const P1 = placeRel({ x: 0, z: 0, yaw: 0 }, net, a.hs), c = Math.cos(turn), s = Math.sin(turn);
    // C: точка, яку поворот на turn навколо неї переводить (0,0) у P1 (обертання в площині x–z узгоджене з yaw)
    const A11 = 1 - c, A12 = -s, A21 = s, A22 = 1 - c, det = A11 * A22 - A12 * A21;
    const Cx = (A22 * P1.x - A12 * P1.z) / det, Cz = (-A21 * P1.x + A11 * P1.z) / det;
    tr.F = { x: -Cx, z: -Cz, yaw: 0 };
    tr.add(clip, { dur: Tend, fade: 0 });
    return { tracks: [tr], marks: [], T: tr.T, notes: [], report: [], kind: 'circle' };
  }
  const ropes = v > 1.7 && !movesBack(m) ? ropeFor(tr, v) : [];
  if (ropes.length) {
    // біг канатами: уздовж осі z, перший забіг — від протилежного боку
    tr.F = o.F0 || { x: 0, z: -(RING.rope - 1.2), yaw: 0 };
    let ph = 0, i = 0;
    while (tr.T < Tend && i < 40) { const r = runIntoRope(tr, clip, ropes[i % ropes.length], bank, { phase: ph, fade: i ? 0.14 : 0, axis: true }); ph = r.phase; i++; }
    return { tracks: [tr], marks: [], T: tr.T, notes: [], report: [], kind: 'ropes' };
  }
  if (v > 0.15) {
    // хода чи біг підтюпцем: по колу навколо центру
    const R = clamp(RING.rope - 0.95, 1.1, 1.8);
    tr.F = o.F0 || { x: R, z: 0, yaw: 0 };          // на колі, обличчям уздовж дотичної; центр дуги — центр рингу (k = −1/R)
    tr.addArc(clip, o.k ?? -1 / R, { dur: Tend, fade: o.F0 ? 0.3 : 0, t0: o.t0 || 0 });
    return { tracks: [tr], marks: [], T: tr.T, notes: [], report: [], kind: 'arc' };
  }
  tr.add(clip, { dur: Tend, fade: 0 });
  return { tracks: [tr], marks: [], T: tr.T, notes: [], report: [], kind: 'still' };
}

// ---------------------------------------------------------------- програвання плану
export class Director {
  constructor(plan, onPair, onSeg) { this.plan = plan; this.T = 0; this.k = new Map(); this.end = plan.T; this.onPair = onPair || null; this.onSeg = onSeg || null; this.done = false; }
  start(T0 = 0, T1 = null) {
    this.T = T0; this.end = T1 ?? this.plan.T; this.done = false;
    for (const tr of this.plan.tracks) { tr.a.reset(); tr.a.holder.visible = true; this.k.set(tr, -1); }
    this.apply(true);
  }
  tick(sdt) {
    if (this.done) return;
    this.T = Math.min(this.end, this.T + sdt);
    this.apply(false);
    if (this.T >= this.end - 1e-6) this.done = true;
  }
  seek(T) { this.T = clamp(T, 0, this.plan.T); this.done = this.T >= this.end - 1e-6; this.apply(true); }
  apply(jump) {
    let pair = null;
    for (const tr of this.plan.tracks) {
      const a = tr.a, k = tr.segAt(this.T); if (k < 0) continue;
      const s = tr.segs[k];
      if (jump || k !== this.k.get(tr)) {
        const seq = !jump && k === this.k.get(tr) + 1;
        const v = a.play(s.clip, { fade: seq ? s.fade : 0, t: tr.clipTime(s, this.T), forceReal: true, speed: s.rate, noPhase: true });
        if (v) { v.clock = () => tr.clipTime(s, this.T); v.tPrev = v.t; }
        a.driveWorld = () => tr.pose(this.T);
        this.k.set(tr, k);
        if (this.onSeg) this.onSeg(a, s, jump);
      }
      if (s.pair) { if (!pair) pair = { base: s.pair, roles: {} }; if (pair.base === s.pair) pair.roles[s.role] = a; }
    }
    if (this.onPair) this.onPair(pair && pair.roles.A && pair.roles.B ? { A: pair.roles.A, B: pair.roles.B, base: pair.base } : null);
    if (jump) for (const tr of this.plan.tracks) { const a = tr.a, keep = a.paused; a.paused = true; a.update(0); a.paused = keep; }
  }
  stepAt(T = this.T) { let cur = null; this.plan.marks.forEach((m, i) => { if (m.t0 != null && T >= m.t0 - 1e-6 && T <= (m.t1 ?? m.t0) + 1e-6) cur = i; }); return cur; }
}
