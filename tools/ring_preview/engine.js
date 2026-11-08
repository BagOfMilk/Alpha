// Рушій сторінки анімацій «Рингу»: сцена, бібліотека кліпів, актор із шарами, кросфейдом і коренем, ноги по рельєфу.
// Нічого не імпортує з гри: лише three.js (r170) із vendor і дані з data/.
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import * as SkeletonUtils from 'three/addons/utils/SkeletonUtils.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { RingRopes } from './ropes.js';

export { THREE };
export const FPS = 30;
// Розміри рингу: значення за замовчуванням — арена; setVenue підставляє їх із data/rings.json (якорі venue_*_anchors.txt).
export const RING = { size: 6.096, half: 3.048, post: 2.828, rope: 2.748, mat: 1.2, cell: 1.5, bound: 2.55 };
export const gltfLoader = new GLTFLoader();
export const loadGLB = (u) => gltfLoader.loadAsync(u);
export const BONE = (n) => n.replace(/[:\s.\/\[\]]/g, '');   // як PropertyBinding.sanitizeNodeName

// ---------------------------------------------------------------- математика кореня (простір ригу: X ліво, Y назад, Z вгору)
export const se2 = {
  id: () => [0, 0, 0],
  compose(a, b) { const c = Math.cos(a[2]), s = Math.sin(a[2]); return [a[0] + c * b[0] - s * b[1], a[1] + s * b[0] + c * b[1], a[2] + b[2]]; },
};
// база {x,z,yaw} у світі three + відносний рух [X,Y,ψ] ригу → світ
export function placeRel(base, rel, hs) {
  const dx = rel[0] * hs, dz = -rel[1] * hs, c = Math.cos(base.yaw), s = Math.sin(base.yaw);
  return { x: base.x + dx * c + dz * s, z: base.z + dz * c - dx * s, yaw: base.yaw + rel[2] };
}
// обернена задача: яка відносна поза (у системі бази) відповідає світовій позі w
export function relFromWorld(base, w, hs) {
  const wx = w.x - base.x, wz = w.z - base.z, c = Math.cos(base.yaw), s = Math.sin(base.yaw);
  const dx = wx * c - wz * s, dz = wx * s + wz * c;
  return [dx / hs, -dz / hs, w.yaw - base.yaw];
}
export const wrapPi = (a) => { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; };
export const smooth = (x) => { x = Math.min(1, Math.max(0, x)); return x * x * (3 - 2 * x); };
// база, з якої кліп у мить t дає світову позу w: placeRel(baseFrom(w, rel, hs), rel, hs) === w
export function baseFrom(w, rel, hs) {
  const yaw = w.yaw - rel[2], c = Math.cos(yaw), s = Math.sin(yaw), dx = rel[0] * hs, dz = -rel[1] * hs;
  return { x: w.x - (dx * c + dz * s), z: w.z - (dz * c - dx * s), yaw };
}
// Рух кореня кліпу [X,Y,ψ] у просторі ригу (без масштабу за зростом) — те саме, що рахує голос під час програвання.
// Цикл без кривої рухається рівномірно за полем speed; неперервний кліп без кривої стоїть на місці.
export function clipNet(meta, dur) { return meta.root ? (meta.net || [0, 0, 0]) : [0, (meta.speed || 0) ? -(meta.speed) * dur : 0, 0]; }
export function rootRel(meta, t, dur = meta.duration, loop = !!meta.loop) {
  const r = meta.root, net = clipNet(meta, dur);
  const curve = (u) => {
    if (!r) { const k = loop ? u / dur : 0; return [net[0] * k, net[1] * k, net[2] * k]; }
    const f = Math.min(Math.max(u * FPS, 0), r.length - 1), i = Math.floor(f), j = Math.min(i + 1, r.length - 1), w = f - i;
    return [r[i][0] + (r[j][0] - r[i][0]) * w, r[i][1] + (r[j][1] - r[i][1]) * w, r[i][2] + (r[j][2] - r[i][2]) * w];
  };
  t = Math.max(0, t);
  if (!loop) return curve(Math.min(t, dur));
  const k = Math.floor(t / dur); let acc = [0, 0, 0];
  for (let i = 0; i < k; i++) acc = se2.compose(acc, net);
  return se2.compose(acc, curve(t - k * dur));
}

// ---------------------------------------------------------------- бібліотека кліпів за статтю
export class AnimLib {
  constructor(sex, data) { this.sex = sex; this.meta = data; this.raw = new Map(); this.additive = new Map(); }
  static async load(sex, meta) {
    const lib = new AnimLib(sex, meta);
    const g = await loadGLB(`data/anim_${sex}.glb`);
    for (const c of g.animations) {
      const tracks = [];
      for (const t of c.tracks) {
        const i = t.name.indexOf('.'), node = t.name.slice(0, i), prop = t.name.slice(i + 1);
        if (prop === 'quaternion') tracks.push(t);
        else if (prop === 'position' && node === 'mixamorigHips') tracks.push(t);   // решта position і весь scale — геть
      }
      lib.raw.set(c.name, { name: c.name, dur: c.duration, tracks });
    }
    return lib;
  }
  has(n) { return this.raw.has(n); }
  // адитивний варіант: лише кватерніони, відносно кадру 0
  additiveTracks(name) {
    if (this.additive.has(name)) return this.additive.get(name);
    const r = this.raw.get(name);
    const clip = new THREE.AnimationClip(name, r.dur, r.tracks.filter((t) => t.name.endsWith('.quaternion')).map((t) => t.clone()));
    THREE.AnimationUtils.makeClipAdditive(clip, 0, clip, FPS);
    this.additive.set(name, clip.tracks);
    return clip.tracks;
  }
}

// ---------------------------------------------------------------- сцена
export function makeStage(canvas, tokens) {
  const C = (k) => new THREE.Color(tokens.colors[k]);
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, preserveDrawingBuffer: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.5));
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.0;
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x1c1a24);
  scene.fog = new THREE.Fog(0x1c1a24, 16, 34);
  const pm = new THREE.PMREMGenerator(renderer);
  scene.environment = pm.fromScene(new RoomEnvironment(), 0.04).texture;
  scene.environmentIntensity = 0.55;

  const camera = new THREE.PerspectiveCamera(34, 1, 0.05, 80);
  camera.position.set(4.3, 2.3, 6.6);
  const controls = new OrbitControls(camera, canvas);
  controls.target.set(0, 0.95, 0);
  controls.enableDamping = true; controls.dampingFactor = 0.12;
  controls.minDistance = 1.2; controls.maxDistance = 16; controls.maxPolarAngle = Math.PI * 0.495;
  controls.update();

  const key = new THREE.DirectionalLight(0xfff0dc, 2.6);
  key.position.set(3.5, 7, 4.5); key.castShadow = true;
  key.shadow.mapSize.set(2048, 2048); key.shadow.bias = -0.0004; key.shadow.normalBias = 0.02;
  Object.assign(key.shadow.camera, { left: -4.6, right: 4.6, top: 4.6, bottom: -4.6, near: 1, far: 20 });
  scene.add(key);
  const rim = new THREE.DirectionalLight(0xb7c6e0, 1.3); rim.position.set(-4, 3.5, -5); scene.add(rim);
  scene.add(new THREE.HemisphereLight(0xdfe6f5, 0x2a2733, 0.7));

  // ---- ринг: ігрова модель із venue_<арена>.fbx (ring_web.py), канати — ігрова фізика (ropes.js). Мат — на y = 0.
  const ring = new THREE.Group(); scene.add(ring);
  const mat = (col, r = 0.8, m = 0) => new THREE.MeshStandardMaterial({ color: col, roughness: r, metalness: m });
  const ground = new THREE.Mesh(new THREE.PlaneGeometry(60, 60), mat(0x23212b, 0.95));
  ground.rotation.x = -Math.PI / 2; ground.position.y = -RING.mat; ground.receiveShadow = true; scene.add(ground);
  const ropes = new RingRopes(ring);
  const venues = new Map();
  // Напис на фартуху — текстом за ключем, як у грі (venue_*_texts.txt), а не в текстурі
  const apronLabel = (l, parent) => {
    const px = 96, c = document.createElement('canvas'), g = c.getContext('2d'), font = `600 ${px}px "Fixel Display", Fixel, sans-serif`;
    g.font = font; const w = Math.ceil(g.measureText(l.text).width) + 24; c.width = w; c.height = Math.ceil(px * 1.5);
    g.font = font; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillStyle = `rgb(${l.color.map((v) => Math.round(v * 255)).join(',')})`; g.fillText(l.text, w / 2, c.height / 2);
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 4;
    const h = l.height * 1.5, m = new THREE.Mesh(new THREE.PlaneGeometry(h * w / c.height, h), new THREE.MeshStandardMaterial({ map: t, transparent: true, depthWrite: false, roughness: 0.8, polygonOffset: true, polygonOffsetFactor: -4 }));
    const toPage = (v) => new THREE.Vector3(v[0], v[2], -v[1]);
    const r = toPage(l.right).normalize(), u = toPage(l.up).normalize(), n = new THREE.Vector3().crossVectors(r, u).normalize();
    m.matrixAutoUpdate = false; m.matrix.makeBasis(r, u, n).setPosition(toPage(l.pos).addScaledVector(n, 0.003)); m.renderOrder = 3; parent.add(m);
  };
  // ---- сітка 1,5 м на полотні: лише клітини, що вміщаються на платформі
  const gridMat = new THREE.LineBasicMaterial({ color: C('gold'), transparent: true, opacity: 0.55 });
  const grid = new THREE.LineSegments(new THREE.BufferGeometry(), gridMat); ring.add(grid);
  const buildGrid = () => {
    const n = Math.floor(RING.size / RING.cell), h = n * RING.cell / 2, gp = [];
    for (let i = 0; i <= n; i++) { const v = -h + i * RING.cell; gp.push(-h, 0.006, v, h, 0.006, v, v, 0.006, -h, v, 0.006, h); }
    grid.geometry.dispose(); grid.geometry = new THREE.BufferGeometry(); grid.geometry.setAttribute('position', new THREE.Float32BufferAttribute(gp, 3));
  };
  async function setVenue(spec) {
    if (!venues.has(spec.id)) {
      const g = await loadGLB(spec.glb), root = new THREE.Group(); root.position.y = -spec.mat; root.add(g.scene);
      g.scene.traverse((o) => {
        if (!o.isMesh) return;
        const m = o.material, nm = m.name || '';
        o.receiveShadow = true; o.castShadow = !/Decal_|Art_|rubber_tiles/.test(nm);
        if (m.map) m.map.anisotropy = 8;
        if (/^Kit_/.test(nm) && o.geometry.attributes.color) m.color.setScalar(4);   // відтінок у вершинах поділено на 4 (ring_venues.py)
        if (/^(Decal_|Art_)/.test(nm)) { m.transparent = true; m.depthWrite = false; m.polygonOffset = true; m.polygonOffsetFactor = -2; o.renderOrder = 2; }
      });
      if (document.fonts) await document.fonts.load('600 96px "Fixel Display"', spec.labels[0] ? spec.labels[0].text : 'РИНГ').catch(() => {});
      for (const l of spec.labels) apronLabel(l, root);
      ring.add(root); venues.set(spec.id, root);
    }
    for (const [id, r] of venues) r.visible = id === spec.id;
    Object.assign(RING, { size: spec.half * 2, half: spec.half, post: spec.post, rope: spec.rope, mat: spec.mat, bound: spec.rope - 0.2, stepsSpot: null });
    const st = Object.keys(spec.anchors).filter((k) => k.startsWith('anchor_steps_')).map((k) => spec.anchors[k]).map((a) => ({ x: a[0], z: -a[1] })).sort((p, q) => q.z - p.z);
    RING.stepsSpot = st[0] || null;
    ground.position.y = -spec.mat;
    buildGrid(); ropes.build(spec);
  }

  // ---- «біжуча доріжка»: велика сітка, що їде назустріч (режим кліпів на місці)
  const tread = new THREE.Group(); tread.visible = false; scene.add(tread);
  const tfloor = new THREE.Mesh(new THREE.PlaneGeometry(60, 60), mat(0x4b4753, 0.92)); tfloor.rotation.x = -Math.PI / 2; tfloor.position.y = -0.002; tfloor.receiveShadow = true; tread.add(tfloor);
  const treadInner = new THREE.Group(); tread.add(treadInner);
  const tp = []; const M = 14; // клітин у кожен бік
  for (let i = -M; i <= M; i++) { const v = i * RING.cell; tp.push(-M * RING.cell, 0.004, v, M * RING.cell, 0.004, v, v, 0.004, -M * RING.cell, v, 0.004, M * RING.cell); }
  const tg = new THREE.BufferGeometry(); tg.setAttribute('position', new THREE.Float32BufferAttribute(tp, 3));
  const treadLines = new THREE.LineSegments(tg, new THREE.LineBasicMaterial({ color: C('gold'), transparent: true, opacity: 0.5 }));
  treadInner.add(treadLines); treadInner.matrixAutoUpdate = false;
  // позначка «ноги не ковзають»: точки в вузлах сітки
  const dots = new THREE.Points((() => { const g = new THREE.BufferGeometry(); const a = []; for (let i = -M; i <= M; i++) for (let j = -M; j <= M; j++) a.push(i * RING.cell, 0.006, j * RING.cell); g.setAttribute('position', new THREE.Float32BufferAttribute(a, 3)); return g; })(), new THREE.PointsMaterial({ color: C('paper'), size: 0.07, transparent: true, opacity: 0.8 }));
  treadInner.add(dots);

  // ---- сходинки (випробувальний рельєф для ніг)
  const stairs = new THREE.Group(); stairs.visible = false; scene.add(stairs);
  const STEPS = [{ z0: 0.5, z1: 1.0, h: 0.14 }, { z0: 1.0, z1: 1.5, h: 0.28 }, { z0: 1.5, z1: 3.0, h: 0.42 }];
  STEPS.forEach((s, i) => {
    const m = new THREE.Mesh(new THREE.BoxGeometry(2.4, s.h, s.z1 - s.z0), mat(i % 2 ? C('ink3') : C('slate'), 0.6));
    m.position.set(0, s.h / 2, (s.z0 + s.z1) / 2); m.castShadow = m.receiveShadow = true; stairs.add(m);
    const edge = new THREE.Mesh(new THREE.BoxGeometry(2.4, 0.012, 0.02), mat(C('gold'), 0.4)); edge.position.set(0, s.h + 0.006, s.z0 + 0.01); stairs.add(edge);
  });
  const terrain = { stairs: false, heightAt(x, z) {
    if (!terrain.stairs || Math.abs(x) > 1.2) return 0;
    let h = 0; for (const s of STEPS) if (z >= s.z0) h = s.h; return h;
  } };

  // ---- лінійка зросту
  const ruler = new THREE.Group(); scene.add(ruler);
  const rmat = new THREE.MeshBasicMaterial({ color: C('paper') });
  { const rod = new THREE.Mesh(new THREE.BoxGeometry(0.012, 2.6, 0.012), rmat); rod.position.set(0, 1.3, 0); ruler.add(rod); }
  const label = (txt, col = '#EDE5D6', size = 40) => {
    const c = document.createElement('canvas'); c.width = 160; c.height = 64; const g = c.getContext('2d');
    g.font = `600 ${size}px Fixel, sans-serif`; g.fillStyle = col; g.textAlign = 'left'; g.textBaseline = 'middle'; g.fillText(txt, 4, 34);
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
    const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: t, transparent: true, depthTest: false })); s.scale.set(0.5, 0.2, 1); s.center.set(0, 0.5); s.renderOrder = 5; return s;
  };
  for (let i = 0; i <= 26; i++) {
    const y = i * 0.1, big = i % 5 === 0;
    const t = new THREE.Mesh(new THREE.BoxGeometry(big ? 0.16 : 0.08, 0.006, 0.006), rmat); t.position.set(big ? 0.08 : 0.04, y, 0); ruler.add(t);
    if (big) { const s = label((y).toFixed(1).replace('.', ',')); s.position.set(0.19, y, 0); ruler.add(s); }
  }
  const hMark = new THREE.Group(); ruler.add(hMark);
  hMark.add(new THREE.Mesh(new THREE.BoxGeometry(0.42, 0.012, 0.012), new THREE.MeshBasicMaterial({ color: C('gold') })));
  hMark.children[0].position.x = 0.2;
  let hLabel = null;
  const setRulerHeight = (h) => {
    hMark.position.y = h;
    if (hLabel) { hMark.remove(hLabel); hLabel.material.map.dispose(); }
    hLabel = label(`${h.toFixed(2).replace('.', ',')} м`, tokens.colors.gold, 44); hLabel.position.set(0.44, 0.02, 0); hMark.add(hLabel);
  };

  function resize() {
    const w = canvas.clientWidth || 800, h = canvas.clientHeight || 600;
    renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix();
  }
  new ResizeObserver(resize).observe(canvas); resize();

  return { renderer, scene, camera, controls, ring, grid, ropes, setVenue, tread, treadInner, stairs, terrain, ruler, setRulerHeight, resize, key };
}

// ---------------------------------------------------------------- ноги: двокістковий аналітичний розв'язок
const _v = Array.from({ length: 12 }, () => new THREE.Vector3());
const _q = Array.from({ length: 6 }, () => new THREE.Quaternion());
function worldQ(o, out) { o.matrixWorld.decompose(_v[11], out, _v[10]); return out; }
function rotateBoneBy(bone, dq) {      // світовий поворот dq застосувати до кістки
  const wq = worldQ(bone, _q[3]);
  const pq = worldQ(bone.parent, _q[4]);
  bone.quaternion.copy(pq.invert().multiply(dq.clone().multiply(wq)));
}
export function solveTwoBone(up, mid, end, target) {
  const A = _v[0].setFromMatrixPosition(up.matrixWorld), B = _v[1].setFromMatrixPosition(mid.matrixWorld), C = _v[2].setFromMatrixPosition(end.matrixWorld);
  const l1 = B.distanceTo(A), l2 = C.distanceTo(B);
  const endWq = worldQ(end, new THREE.Quaternion());
  const dir = _v[3].copy(target).sub(A); let d = dir.length(); if (d < 1e-5) return; dir.divideScalar(d);
  d = Math.min(d, l1 + l2 - 1e-4); d = Math.max(d, Math.abs(l1 - l2) + 1e-4);
  const ac = _v[4].copy(C).sub(A).normalize();
  const pole = _v[5].copy(B).sub(A); pole.addScaledVector(ac, -pole.dot(ac));
  if (pole.lengthSq() < 1e-8) pole.set(0, 0, 1);
  pole.addScaledVector(dir, -pole.dot(dir)); pole.normalize();
  const a = (l1 * l1 - l2 * l2 + d * d) / (2 * d), h = Math.sqrt(Math.max(0, l1 * l1 - a * a));
  const Bn = _v[6].copy(A).addScaledVector(dir, a).addScaledVector(pole, h);
  const T = _v[7].copy(A).addScaledVector(dir, d);
  const dq1 = new THREE.Quaternion().setFromUnitVectors(_v[8].copy(B).sub(A).normalize(), _v[9].copy(Bn).sub(A).normalize());
  rotateBoneBy(up, dq1); up.updateMatrixWorld(true);
  const B2 = _v[1].setFromMatrixPosition(mid.matrixWorld), C2 = _v[2].setFromMatrixPosition(end.matrixWorld);
  const dq2 = new THREE.Quaternion().setFromUnitVectors(_v[8].copy(C2).sub(B2).normalize(), _v[9].copy(T).sub(B2).normalize());
  rotateBoneBy(mid, dq2); mid.updateMatrixWorld(true);
  // стопа зберігає свій світовий поворот
  const pq = worldQ(end.parent, _q[4]);
  end.quaternion.copy(pq.invert().multiply(endWq)); end.updateMatrixWorld(true);
}

// ---------------------------------------------------------------- голос: один відтворюваний кліп
let voiceSeq = 0;
class Voice {
  constructor(actor, name, kind = 'base') {
    this.actor = actor; this.name = name; this.kind = kind;       // base | layer | additive
    this.meta = actor.metaOf(name) || {};
    this.raw = actor.lib.raw.get(name);
    this.dur = this.raw.dur; this.loop = !!this.meta.loop && kind !== 'additive';
    this.t = 0; this.tPrev = 0; this.w = 0; this.w0 = 0; this.speed = 1;
    this.finished = false; this.parts = {}; this.id = ++voiceSeq;
    this.root = this.meta.root || null; this.layerW = 0;
    this.net = clipNet(this.meta, this.dur);
    this.clock = null;            // режисер ланцюга задає час голосу сам (функція → секунди кліпу)
    this.moving = this.loop && (Math.hypot(this.net[0], this.net[1]) > 1e-3 || Math.abs(this.net[2]) > 1e-3);
    this.maskName = this.meta.layer_mask || 'upper_body';
  }
  tracks() { return this.kind === 'additive' ? this.actor.lib.additiveTracks(this.name) : this.actor.tracksFor(this.name); }
  part(kind, maskName) {
    const key = kind === 'full' ? 'full' : `${kind}|${maskName}`;
    if (this.parts[key]) return this.parts[key];
    let tr = this.tracks();
    if (kind !== 'full') {
      const set = this.actor.maskSet(maskName);
      tr = tr.filter((t) => { const inM = set.has(t.name.slice(0, t.name.indexOf('.'))); return kind === 'up' ? inM : !inM; });
    }
    const clip = new THREE.AnimationClip(`${this.name}#${this.id}#${key}`, this.dur, tr);
    const act = this.actor.mixer.clipAction(clip);
    act.setLoop(THREE.LoopRepeat, Infinity); act.clampWhenFinished = false;
    if (this.kind === 'additive') act.blendMode = THREE.AdditiveAnimationBlendMode;
    act.play(); act.weight = 0; act.enabled = false;
    this.parts[key] = act; return act;
  }
  // rel root: [X,Y,ψ] у просторі ригу (без hipScale)
  rootAt(t) {
    if (this.kind !== 'base') return [0, 0, 0];
    return rootRel(this.meta, t, this.dur, this.loop);
  }
  pose() { return this.loop ? this.t % this.dur : Math.min(this.t, this.dur); }
  dispose() {
    for (const k in this.parts) { const a = this.parts[k]; a.stop(); this.actor.mixer.uncacheClip(a.getClip()); }
    this.parts = {};
  }
}

// ---------------------------------------------------------------- актор
const NO_PROP_SOCKETS = { belt: 'mixamorigHips', chair: 'mixamorigRightHand', kendo: 'mixamorigRightHand', case: 'mixamorigRightHand', mic: 'mixamorigRightHand', spray_gun: 'mixamorigRightHand', spray_tank: 'mixamorigSpine2' };
export class Actor {
  constructor(id, cfg, model, lib, clipsMeta, graph) {
    this.id = id; this.cfg = cfg; this.model = model; this.lib = lib; this.clipsMeta = clipsMeta; this.graph = graph;
    this.holder = new THREE.Group(); this.holder.add(model);
    this.mixer = new THREE.AnimationMixer(model);
    this.hips = model.getObjectByName('mixamorigHips');
    this.hipRest0 = this.hips.position.clone();
    this.hs = this.hips.position.y / cfg.hip_rest;
    this.bones = {}; model.traverse((o) => { if (o.isBone) this.bones[o.name] = o; });
    this.clipCache = new Map(); this.maskCache = new Map();
    this.voices = []; this.fade = null; this.layer = null; this.reaction = null;
    this.base = { x: 0, z: 0, yaw: 0 };       // база корення (світ)
    this.treadV = [0, 0, 0];                   // зсув «доріжки» в системі бази
    this.treadActive = false;
    this.world = { x: 0, z: 0, yaw: 0 };       // фактична поза холдера
    this.speedMul = 1; this.paused = false;
    this.inPlace = true; this.steer = false; this.terrain = null;
    this.ik = { on: false, p: 0, hL: 0, hR: 0, active: false };
    this.onEvent = null; this.layerOn = true;
    this.sockets = {}; this.propNodes = {};
    model.traverse((o) => { if (o.name && o.name.startsWith('Socket_')) this.sockets[o.name.slice(7)] = o; });
    // Предмет, вигнутий під цього персонажа (пояс через плече), лежить у самій моделі: показуємо його, а не копію з props.glb.
    this.ownProps = {};
    model.traverse((o) => { if (o.name && o.name.startsWith('Prop_')) { this.ownProps[o.name.slice(5)] = o; o.visible = false; o.traverse((x) => { if (x.isMesh) { x.castShadow = true; x.frustumCulled = false; } }); } });
    this.materials = {};
    model.traverse((o) => { if (o.isMesh) { o.frustumCulled = false; o.castShadow = true; o.receiveShadow = true; const ms = Array.isArray(o.material) ? o.material : [o.material]; for (const m of ms) this.materials[m.name] = this.materials[m.name] || m; o.userData.matName = ms[0].name; } });
    for (const n in this.materials) { const m = this.materials[n]; if (n.endsWith('_Alpha')) { m.transparent = false; m.alphaTest = 0.5; m.depthWrite = true; m.side = THREE.DoubleSide; m.needsUpdate = true; } }
    this.clipSet = new Set(cfg.clips);
  }
  static async create(id, cfg, lib, clipsMeta, graph) {
    const g = await loadGLB(`data/${id}.glb`);
    const model = g.scene;
    return new Actor(id, cfg, model, lib, clipsMeta, graph);
  }
  metaOf(n) { return this.clipsMeta[n]; }
  has(n) { return this.lib.has(n) && this.clipSet.has(n); }
  tracksFor(name) {
    if (this.clipCache.has(name)) return this.clipCache.get(name);
    const raw = this.lib.raw.get(name); const hs = this.hs;
    const tr = raw.tracks.map((t) => {
      if (!t.name.endsWith('.position')) return t;
      const v = t.values.slice(); for (let i = 0; i < v.length; i++) v[i] *= hs;
      return new THREE.VectorKeyframeTrack(t.name, t.times, v);
    });
    this.clipCache.set(name, tr); return tr;
  }
  maskSet(name) {
    if (this.maskCache.has(name)) return this.maskCache.get(name);
    const s = new Set((this.graph.masks[name] || this.graph.masks.upper_body).map(BONE));
    this.maskCache.set(name, s); return s;
  }
  // ---- відображення
  setDamage(level, texCache) {
    const m = this.materials[`${this.id}_Body`]; if (!m) return;
    if (!this._orig) this._orig = { map: m.map, rough: m.roughnessMap, metal: m.metalnessMap };
    this.damage = level;
    if (level === 0) { m.map = this._orig.map; m.roughnessMap = this._orig.rough; m.metalnessMap = this._orig.metal; }
    else {
      const a = texCache(`data/${this.id}_albedo_d${level}.webp`, true), o = texCache(`data/${this.id}_orm_d${level}.webp`, false);
      // міняємо лише коли обидві карти готові: незавантажена текстура малюється чорною
      Promise.all([a.ready, o.ready]).then(() => {
        if (this.damage !== level) return;
        m.map = a; m.roughnessMap = o; m.metalnessMap = o; m.needsUpdate = true;
      });
      return;
    }
    m.needsUpdate = true;
  }
  setOuterVisible(v) { this.model.traverse((o) => { if (o.isMesh && o.userData.matName && o.userData.matName.endsWith('_Outer')) o.visible = v; }); }
  hasOuter() { return !!this.materials[`${this.id}_Outer`]; }
  hasHeld() { return this.id === 'komet' && this.has('Layer_Held_Arms_Loop'); }

  // ---- місце на сцені
  place(x, z, yaw) { this.base = { x, z, yaw }; this.world = { x, z, yaw }; this.treadV = [0, 0, 0]; this.treadActive = false; this.apply(); }
  cur() { return this.voices.length ? this.voices[this.voices.length - 1] : null; }
  curBase() { for (let i = this.voices.length - 1; i >= 0; i--) if (this.voices[i].kind === 'base') return this.voices[i]; return null; }
  // зафіксувати поточну позу як нову базу (перед стартом нового кліпу)
  commit() {
    const v = this.curBase(); if (!v) return;
    const rel = v.rootAt(v.t);
    if (this.treadActive) { this.treadV = se2.compose(this.treadV, rel); }
    else this.base = placeRel(this.base, rel, this.hs);
  }

  // ---- програвання
  play(name, o = {}) {
    if (!this.has(name)) return null;
    const fade = o.fade ?? 0;
    const prev = this.curBase();
    const wasTread = this.treadActive;
    this.commit();
    const v = new Voice(this, name, 'base'); v.speed = 1;
    if (o.t) v.t = o.t;
    // синхронізація фази циклів
    if (prev && prev.loop && v.loop && prev.meta.phase && v.meta.phase && !o.noPhase) v.t = v.dur * ((prev.t % prev.dur) / prev.dur);
    if (o.speed) v.speed = o.speed;
    const goesTread = this.inPlace && v.moving && !o.forceReal;
    if (!goesTread || !wasTread) this.treadV = [0, 0, 0];
    this.treadActive = goesTread;
    // Кліп, що стартує не з нуля (фаза циклу, середина кліпу), не має стрибати: база така, щоб поза в світі лишилась та сама.
    if (!goesTread && prev) this.base = baseFrom(this.world, v.rootAt(v.t), this.hs);
    this.voices.push(v);
    if (fade > 0.001 && this.voices.length > 1) {
      for (const o2 of this.voices) o2.w0 = o2.w;
      v.w0 = 0; v.w = 0; this.fade = { dur: fade, el: 0, to: v };
    } else {
      for (const o2 of this.voices) if (o2 !== v && o2.kind === 'base') o2.dispose();
      this.voices = this.voices.filter((x) => x === v || x.kind !== 'base'); v.w = 1; this.fade = null;
    }
    this.lastPlayed = v; return v;
  }
  setLayer(name, o = {}) {      // верхній шар (за маскою кліпу); null — прибрати
    const ok = !!name && this.has(name);
    const mask = ok ? (o.mask || this.metaOf(name).layer_mask || 'upper_body') : null;
    this.oldLayers = this.oldLayers || [];
    if (this.layer) {
      const old = this.layer; this.layer = null;
      if (ok && old.mask !== mask) old.dispose();
      else { old.fadeOut = o.fade ?? 0.2; old.userW = 0; this.oldLayers.push(old); }
    }
    if (!ok) return null;
    const v = new Voice(this, name, 'layer');
    v.layerW = 0; v.fadeIn = o.fade ?? 0.2; v.fadeOut = o.fade ?? 0.2; v.userW = o.weight ?? 1;
    v.mask = mask; v.hold = !!o.hold; v.forceOver = !!o.forceOver;
    this.layer = v; this.layerFinished = false; return v;
  }
  setReaction(name, w = 1) {
    if (this.reaction) { this.reaction.dispose(); this.reaction = null; }
    if (!name || !this.has(name)) return null;
    const v = new Voice(this, name, 'additive'); v.loop = false; v.userW = w; v.w = 1; this.reaction = v; return v;
  }

  // ---- кадр
  update(dt, hooks = {}) {
    const sdt = this.paused ? 0 : dt * this.speedMul;
    // кросфейд
    if (this.fade) {
      const f = this.fade; f.el += sdt;       // фейд іде в масштабі часу кліпів
      const s = smooth(f.el / f.dur);
      for (const v of this.voices) { if (v === f.to) v.w = s; else if (v.kind === 'base') v.w = v.w0 * (1 - s); }
      if (f.el >= f.dur) {
        for (const v of this.voices.slice()) if (v !== f.to && v.kind === 'base') { v.dispose(); }
        this.voices = this.voices.filter((v) => v === f.to || v.kind !== 'base'); f.to.w = 1; this.fade = null;
      }
    }
    const base = this.curBase();
    // час голосів
    const adv = (v) => {
      v.tPrev = v.t;
      if (v.clock) { v.t = Math.max(0, v.clock()); v.finished = !v.loop && v.t >= v.dur; if (v.finished) v.t = v.dur; }
      else { v.t += sdt * v.speed; if (!v.loop && v.t >= v.dur) { v.t = v.dur; v.finished = true; } }
      const evs = v.meta.events;
      if (evs && this.onEvent && sdt > 0) {
        const p0 = v.loop ? v.tPrev % v.dur : v.tPrev, p1 = v.loop ? v.t % v.dur : v.t;
        for (const e of evs) {
          const hit = p1 >= p0 ? (e.t > p0 && e.t <= p1) : (e.t > p0 || e.t <= p1);
          if (hit) this.onEvent(this, v, e);
        }
      }
    };
    for (const v of this.voices) if (v.kind === 'base') adv(v);
    // шар
    const layers = (this.oldLayers || []);
    if (this.layer) {
      const L = this.layer; adv(L);
      const tw = (this.layerOn && (!base || base.loop || L.forceOver)) ? L.userW : 0;
      const rate = 1 / Math.max(0.05, tw > L.layerW ? L.fadeIn : L.fadeOut);
      L.layerW += Math.max(-rate * sdt, Math.min(rate * sdt, tw - L.layerW));
      if (L.finished && !L.loop && !L.hold) { L.userW = 0; if (L.layerW <= 0.001) { L.dispose(); this.layer = null; this.layerFinished = true; } }
    }
    for (const L of layers.slice()) {
      adv(L); L.layerW = Math.max(0, L.layerW - sdt / Math.max(0.05, L.fadeOut));
      if (L.layerW <= 0.001) { L.dispose(); layers.splice(layers.indexOf(L), 1); }
    }
    if (this.reaction) { const R = this.reaction; adv(R); if (R.finished && !R.keep) { R.dispose(); this.reaction = null; this.reactionFinished = true; } }

    this.applyMixer();
    // корінь і холдер
    this.updateRoot(sdt);
    this.apply();
    this.updateIK(dt);
  }
  applyMixer() {
    const L = this.layer, olds = this.oldLayers || [];
    const all = [L, ...olds].filter(Boolean);
    const main = all.find((x) => x.layerW > 0.001);
    const maskName = main ? main.mask : null;
    const sumW = all.filter((x) => x.mask === maskName).reduce((s, x) => s + x.layerW, 0);
    const upScale = main ? Math.max(0, 1 - sumW) : 1;
    const setW = (act, w) => { act.weight = w; act.enabled = w > 1e-4; };
    for (const v of this.voices) {
      if (v.kind !== 'base') continue;
      const pose = v.pose();
      if (maskName) {
        const lo = v.part('low', maskName), up = v.part('up', maskName);
        lo.time = pose; up.time = pose; setW(lo, v.w); setW(up, v.w * upScale);
        if (v.parts.full) setW(v.parts.full, 0);
      } else {
        const f = v.part('full'); f.time = pose; setW(f, v.w);
        for (const k in v.parts) if (k !== 'full') setW(v.parts[k], 0);
      }
    }
    for (const x of all) {
      if (x.layerW > 0.001 && x.mask === maskName) { const a = x.part('up', x.mask); a.time = x.pose(); setW(a, x.layerW); }
      else for (const k in x.parts) setW(x.parts[k], 0);
    }
    if (this.reaction) { const R = this.reaction; const a = R.part('full'); a.time = R.pose(); setW(a, R.userW); }
    this.mixer.update(0);
  }
  updateRoot(sdt) {
    if (this.driveWorld) { const w = this.driveWorld(); this.world = { x: w.x, z: w.z, yaw: w.yaw }; return; }   // місце задає режисер ланцюга
    const v = this.curBase(); if (!v) return;
    const rel = v.rootAt(v.t);
    if (this.treadActive) {
      this.world = { x: this.base.x, z: this.base.z, yaw: this.base.yaw };
    } else {
      this.world = placeRel(this.base, rel, this.hs);
      if (this.steer && v.moving && sdt > 0) this.steerAtRopes(sdt);
    }
  }
  steerAtRopes(sdt) {
    const w = this.world, v = this.curBase();
    // напрям руху, а не погляду: хода спиною й боком теж мусить повертати від канатів
    const nx = v.net[0] * this.hs, nz = -v.net[1] * this.hs, cy = Math.cos(w.yaw), sy = Math.sin(w.yaw);
    const mx = nx * cy + nz * sy, mz = nz * cy - nx * sy, ml = Math.hypot(mx, mz);
    const speed = ml / v.dur;
    const fx = ml > 1e-4 ? mx / ml : sy, fz = ml > 1e-4 ? mz / ml : cy;
    const lim = RING.rope - 0.02, ox = Math.abs(w.x) - lim, oz = Math.abs(w.z) - lim;      // за канат не виходить за жодних умов
    if (ox > 0 || oz > 0) this.shift(ox > 0 ? -Math.sign(w.x) * ox : 0, oz > 0 ? -Math.sign(w.z) * oz : 0, 0);
    const B = RING.bound - Math.min(1.2, speed * 0.25);               // швидкий кліп починає повертати раніше, щоб не влетіти в канати
    const out = Math.abs(w.x) > B || Math.abs(w.z) > B;
    const toward = -(w.x * fx + w.z * fz);                            // >0: курс до центру
    if (!out && !this.steering) return;
    this.steering = out || toward < 0.35 * Math.hypot(w.x, w.z);
    if (!this.steering) return;
    const cross = (-w.x) * fz - (-w.z) * fx;                          // знак повороту до центру
    this.turnBy((cross >= 0 ? 1 : -1) * Math.max(2.4, speed / 0.9) * sdt);
  }
  // обернути базу навколо поточної точки (світова поза не стрибає)
  turnBy(dpsi) {
    const w = this.world, c = Math.cos(dpsi), s = Math.sin(dpsi), rx = this.base.x - w.x, rz = this.base.z - w.z;
    this.base = { x: w.x + rx * c + rz * s, z: w.z - rx * s + rz * c, yaw: this.base.yaw + dpsi };
    const v = this.curBase(); this.world = placeRel(this.base, v ? v.rootAt(v.t) : [0, 0, 0], this.hs);
  }
  shift(dx, dz, dpsi) {
    if (dpsi) this.turnBy(dpsi);
    this.base.x += dx; this.base.z += dz; this.world.x += dx; this.world.z += dz; this.apply();
  }
  reset() {
    this.yOff = 0; this.yRamp = null; this.driveWorld = null;
    for (const v of this.voices) v.dispose();
    this.voices = []; this.fade = null;
    if (this.layer) { this.layer.dispose(); this.layer = null; }
    for (const l of this.oldLayers || []) l.dispose();
    this.oldLayers = []; if (this.reaction) { this.reaction.dispose(); this.reaction = null; }
    this.treadActive = false; this.treadV = [0, 0, 0]; this.stateId = null; this.steering = false;
    this.layerFinished = false; this.reactionFinished = false;
  }
  apply() {
    this.holder.position.set(this.world.x, this.yOff || 0, this.world.z); this.holder.rotation.set(0, this.world.yaw, 0);
  }
  // матриця, на яку треба зсунути «підлогу доріжки»
  treadMatrix(out) {
    if (!this.treadActive) return out.identity();
    const v = this.curBase(); const tv = se2.compose(this.treadV, v ? v.rootAt(v.t) : [0, 0, 0]);
    const W = placeRel(this.base, tv, this.hs);
    const mk = (p) => new THREE.Matrix4().compose(new THREE.Vector3(p.x, 0, p.z), new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), p.yaw), new THREE.Vector3(1, 1, 1));
    const Mb = mk(this.base), Mw = mk(W);
    return out.copy(Mb).multiply(Mw.invert());
  }
  // ---- IK ніг
  updateIK(dt) {
    const ik = this.ik, ter = this.terrain;
    if (!ik.on || !ter) { if (ik.p !== 0) { ik.p = 0; } return; }
    this.holder.updateMatrixWorld(true);
    const L = this.bones.mixamorigLeftFoot, R = this.bones.mixamorigRightFoot;
    const lt = this.bones.mixamorigLeftToeBase, rt = this.bones.mixamorigRightToeBase;
    const hAt = (foot, toe) => { const a = new THREE.Vector3().setFromMatrixPosition(foot.matrixWorld), b = new THREE.Vector3().setFromMatrixPosition(toe.matrixWorld); return Math.max(ter.heightAt(a.x, a.z), ter.heightAt(b.x, b.z)); };
    const k = 1 - Math.exp(-dt * 16);
    const tl = hAt(L, lt), tr = hAt(R, rt);
    ik.hL += (tl - ik.hL) * k; ik.hR += (tr - ik.hR) * k;
    const pt = Math.min(ik.hL, ik.hR);
    ik.p += (pt - ik.p) * k;
    ik.active = Math.abs(ik.hL) + Math.abs(ik.hR) + Math.abs(ik.p) > 0.004;
    if (!ik.active) return;
    this.hips.position.y += ik.p;
    this.holder.updateMatrixWorld(true);
    for (const [pre, h] of [['Left', ik.hL], ['Right', ik.hR]]) {
      const up = this.bones[`mixamorig${pre}UpLeg`], mid = this.bones[`mixamorig${pre}Leg`], end = this.bones[`mixamorig${pre}Foot`];
      const T = new THREE.Vector3().setFromMatrixPosition(end.matrixWorld); T.y += h - ik.p;
      solveTwoBone(up, mid, end, T);
    }
    this.holder.updateMatrixWorld(true);
  }
  // ---- реквізит
  setProps(wanted, propsLib) {
    // wanted: масив імен вузлів реквізиту ('belt','chair','spray_tank','spray_gun'…)
    for (const n of Object.keys(this.ownProps || {})) { this.ownProps[n].visible = wanted.includes(n); this.propNodes[n] = this.ownProps[n]; }
    for (const n of Object.keys(this.propNodes)) this.propNodes[n].visible = wanted.includes(n);
    if (!propsLib) return;
    for (const n of wanted) {
      if (this.propNodes[n]) continue;
      const proto = propsLib.get(n); if (!proto) continue;
      const clone = proto.clone(true); clone.position.set(0, 0, 0); clone.traverse((o) => { if (o.isMesh) { o.castShadow = true; o.frustumCulled = false; } });
      let sock = this.sockets[n];
      if (!sock) { const b = this.bones[NO_PROP_SOCKETS[n] || 'mixamorigRightHand']; if (!b) continue; sock = b; clone.userData.fallback = true; }
      sock.add(clone); this.propNodes[n] = clone;
    }
  }
  socketWorld(n, out) {
    const s = this.sockets[n] || this.bones[NO_PROP_SOCKETS[n]]; if (!s) return null;
    s.updateWorldMatrix(true, false); return out.setFromMatrixPosition(s.matrixWorld);
  }
  boneWorld(name, out) { const b = this.bones[BONE(name)]; if (!b) return null; b.updateWorldMatrix(true, false); return out.setFromMatrixPosition(b.matrixWorld); }
  dispose() { for (const v of this.voices) v.dispose(); this.voices = []; this.mixer.stopAllAction(); }
}
