// Канати рингу: той самий верле-канат, що в грі (Core/Ring/RopeSim.cs + Stage/Unity/RingRopes.cs), числа звідти ж.
// Кінці — на якорях арени anchor_rope_<кут>_<1..3>; тіла борців (капсули кісток) виштовхують точки назовні, натяг повертає.
// Координати сторінки: Y угору, мат на y = 0.
import * as THREE from 'three';

const DT = 1 / 60, DAMP = 0.97, GRAVITY = 9.81, HOME = 600, STRETCH = 0.12, CONTACT = 0.004, ITER = 3;
const POINTS = 16, SIDES = 8, RADIUS = 0.026, TILE = 0.18;
const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const len = (a) => Math.sqrt(dot(a, a));
const clamp01 = (x) => Math.max(0, Math.min(1, x));
const lerp = (a, b, k) => add(a, mul(sub(b, a), k));

function closest(c, p) {
  const ab = sub(c.b, c.a), l2 = dot(ab, ab);
  const t = l2 > 1e-9 ? clamp01(dot(sub(p, c.a), ab) / l2) : 0;
  return add(c.a, mul(ab, t));
}
// Найближчі точки двох відрізків: q — на осі капсули, u — параметр на відрізку каната
function segSeg(p1, q1, p2, q2) {
  const d1 = sub(q1, p1), d2 = sub(q2, p2), r = sub(p1, p2);
  const a = dot(d1, d1), e = dot(d2, d2), f = dot(d2, r);
  let s, t;
  if (e < 1e-9) { t = 0; s = a > 1e-9 ? clamp01(-dot(d1, r) / a) : 0; }
  else {
    const c = dot(d1, r), b = dot(d1, d2), den = a * e - b * b;
    s = den > 1e-9 ? clamp01((b * f - c * e) / den) : 0;
    t = (b * s + f) / e;
    if (t < 0) { t = 0; s = a > 1e-9 ? clamp01(-c / a) : 0; }
    else if (t > 1) { t = 1; s = a > 1e-9 ? clamp01((b - c) / a) : 0; }
  }
  return { q: add(p2, mul(d2, t)), u: s };
}

class Span {
  constructor(start, end, out, level) {
    this.out = out; this.level = level; this.y = start[1];
    this.p = []; this.prev = []; this.home = [];
    for (let i = 0; i < POINTS; i++) { const h = lerp(start, end, i / (POINTS - 1)); this.home.push(h); this.p.push(h.slice()); this.prev.push(h.slice()); }
    this.length = len(sub(end, start));
    this.segment = this.length / (POINTS - 1) * (1 + STRETCH);
  }
  step(bodies) {
    const p = this.p, prev = this.prev, dt2 = DT * DT;
    for (let i = 1; i < POINTS - 1; i++) {
      const v = mul(sub(p[i], prev[i]), DAMP), acc = add(mul(sub(this.home[i], p[i]), HOME), [0, -GRAVITY, 0]);
      prev[i] = p[i]; p[i] = add(add(p[i], v), mul(acc, dt2));
    }
    for (let it = 0; it < ITER; it++) {
      for (const b of bodies) { this.pushOut(b); this.pushSegments(b); }
      this.constrain();
    }
    for (const b of bodies) { this.pushSegments(b); this.pushOut(b); }   // тіло — останнє слово
  }
  constrain() {
    const p = this.p;
    for (let i = 0; i < POINTS - 1; i++) {
      const d = sub(p[i + 1], p[i]), l = len(d);
      if (l <= this.segment || l < 1e-6) continue;
      const fix = mul(d, (l - this.segment) / l);
      const a = i === 0 ? 0 : (i + 1 === POINTS - 1 ? 1 : 0.5), b = i + 1 === POINTS - 1 ? 0 : (i === 0 ? 1 : 0.5);
      p[i] = add(p[i], mul(fix, a)); p[i + 1] = sub(p[i + 1], mul(fix, b));
    }
  }
  pushOut(c) {
    const r = c.r + CONTACT, p = this.p;
    for (let i = 1; i < POINTS - 1; i++) {
      const q = closest(c, p[i]); let dist = len(sub(p[i], q));
      if (dist >= r) continue;
      const dir = dot(sub(p[i], q), this.out) >= 0 ? this.out : mul(this.out, -1);   // тіло з боку рингу — канат назовні
      for (let k = 0; k < 4 && dist < r; k++) { p[i] = add(p[i], mul(dir, r - dist + 1e-4)); dist = len(sub(p[i], closest(c, p[i]))); }
    }
  }
  pushSegments(c) {
    const r = c.r + CONTACT, p = this.p;
    for (let i = 0; i < POINTS - 1; i++) {
      const { q, u } = segSeg(p[i], p[i + 1], c.a, c.b), pt = lerp(p[i], p[i + 1], u), dist = len(sub(pt, q));
      if (dist >= r) continue;
      const dir = dot(sub(pt, q), this.out) >= 0 ? this.out : mul(this.out, -1), push = mul(dir, r - dist + 1e-4);
      const wa = i === 0 ? 0 : 1 - u, wb = i + 1 === POINTS - 1 ? 0 : u, norm = wa * wa + wb * wb;
      if (norm < 1e-6) continue;
      p[i] = add(p[i], mul(push, wa / norm)); p[i + 1] = add(p[i + 1], mul(push, wb / norm));
    }
  }
  deflection() { let best = 0; for (let i = 0; i < POINTS; i++) best = Math.max(best, len(sub(this.p[i], this.home[i]))); return best; }
}

// Скручений канат: три пасма по діагоналі, волокна, тінь між пасмами (RopeTexture у RingRopes.cs)
function twistTexture() {
  const W = 128, H = 96, c = document.createElement('canvas'); c.width = W; c.height = H;
  const g = c.getContext('2d'), im = g.createImageData(W, H);
  const hash = (i) => { let x = Math.imul(i, 2654435761) >>> 0; x ^= x >>> 15; x = Math.imul(x, 2246822519) >>> 0; x ^= x >>> 13; return (x & 0xffff) / 65535; };
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const u = x / W, v = y / H, ph = (v * 3 + u * 2) % 1, strand = Math.sin(ph * Math.PI);
    const fib = hash(x * 7 + y * 131) * 0.10 + Math.sin((u * 41 + v * 9) * Math.PI * 2) * 0.035;
    const s = Math.max(0, Math.min(1, 0.52 + 0.40 * Math.pow(strand, 0.7) + fib)) * 255, k = (y * W + x) * 4;
    im.data[k] = s; im.data[k + 1] = s * 0.97; im.data[k + 2] = s * 0.93; im.data[k + 3] = 255;
  }
  g.putImageData(im, 0, 0);
  const t = new THREE.CanvasTexture(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 4;
  return t;
}

const LIMBS = [
  ['LeftUpLeg', 'RightUpLeg', 0.17], ['LeftArm', 'RightArm', 0.13],                                 // таз, плечі
  ['LeftUpLeg', 'LeftLeg', 0.09], ['LeftLeg', 'LeftFoot', 0.07], ['RightUpLeg', 'RightLeg', 0.09], ['RightLeg', 'RightFoot', 0.07],
  ['LeftArm', 'LeftForeArm', 0.055], ['LeftForeArm', 'LeftHand', 0.05], ['RightArm', 'RightForeArm', 0.055], ['RightForeArm', 'RightHand', 0.05],
];
const CORNERS = ['NE', 'NW', 'SW', 'SE'];

export class RingRopes {
  constructor(parent) {
    this.spans = []; this.acc = 0; this.ropeLine = 2.75;
    this.mat = new THREE.MeshStandardMaterial({ map: twistTexture(), roughness: 0.65, metalness: 0, vertexColors: true });
    this.mesh = new THREE.Mesh(new THREE.BufferGeometry(), this.mat);
    this.mesh.frustumCulled = false; this.mesh.castShadow = true; this.mesh.receiveShadow = false; this.mesh.name = 'ropes';
    parent.add(this.mesh);
    this._v = new THREE.Vector3(); this._w = new THREE.Vector3();
  }
  // spec — запис із data/rings.json: якорі Blender (x, y, z угору) → сторінка (x, z − мат, −y)
  build(spec) {
    const P = (k) => { const a = spec.anchors[k]; return [a[0], a[2] - spec.mat, -a[1]]; };
    this.spans = []; this.ropeLine = spec.rope;
    for (let level = 0; level < 3; level++) for (let side = 0; side < 4; side++) {
      const a = P(`anchor_rope_${CORNERS[side]}_${level + 1}`), b = P(`anchor_rope_${CORNERS[(side + 1) % 4]}_${level + 1}`);
      const mx = (a[0] + b[0]) / 2, mz = (a[2] + b[2]) / 2, l = Math.hypot(mx, mz);
      this.spans.push(new Span(a, b, [mx / l, 0, mz / l], level));
    }
    const n = this.spans.length, verts = n * POINTS * SIDES;
    this.pos = new Float32Array(verts * 3); this.nor = new Float32Array(verts * 3);
    const uv = new Float32Array(verts * 2), col = new Float32Array(verts * 3), idx = [];
    const cols = spec.rope_colors.map((c) => new THREE.Color(c));
    this.spans.forEach((sp, s) => {
      const base = s * POINTS * SIDES, c = cols[sp.level] || cols[0];
      for (let i = 0; i < POINTS; i++) for (let j = 0; j < SIDES; j++) {
        const k = base + i * SIDES + j;
        uv[k * 2] = i / (POINTS - 1) * (sp.length / TILE); uv[k * 2 + 1] = j / SIDES;
        col[k * 3] = c.r; col[k * 3 + 1] = c.g; col[k * 3 + 2] = c.b;
      }
      for (let i = 0; i < POINTS - 1; i++) for (let j = 0; j < SIDES; j++) {
        const j2 = (j + 1) % SIDES, a0 = base + i * SIDES + j, a1 = base + i * SIDES + j2, c0 = base + (i + 1) * SIDES + j, c1 = base + (i + 1) * SIDES + j2;
        idx.push(a0, a1, c0, a1, c1, c0);
      }
    });
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(this.pos, 3).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('normal', new THREE.BufferAttribute(this.nor, 3).setUsage(THREE.DynamicDrawUsage));
    g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    g.setAttribute('color', new THREE.BufferAttribute(col, 3));
    g.setIndex(idx);
    this.mesh.geometry.dispose(); this.mesh.geometry = g;
    this.refresh();
  }
  gather(actors) {
    const out = [];
    for (const a of actors) {
      if (!a.holder.visible) continue;
      for (const [n1, n2, r] of LIMBS) {
        const p = a.boneWorld(`mixamorig${n1}`, this._v), q = a.boneWorld(`mixamorig${n2}`, this._w);
        if (p && q) out.push({ a: [p.x, p.y, p.z], b: [q.x, q.y, q.z], r });
      }
    }
    return out;
  }
  update(actors, dt) {
    if (!this.spans.length || dt <= 0) return;
    this.acc += Math.min(dt, 0.1);
    if (this.acc < DT) return;
    const all = this.gather(actors), near = this.ropeLine - 1.0, far = this.ropeLine + 1.3;
    for (let steps = 0; this.acc >= DT && steps < 4; steps++) {
      this.acc -= DT;
      for (const sp of this.spans) {
        const caps = [];
        for (const c of all) {
          const da = c.a[0] * sp.out[0] + c.a[2] * sp.out[2], db = c.b[0] * sp.out[0] + c.b[2] * sp.out[2];
          if ((da < near && db < near) || (da > far && db > far)) continue;
          if ((c.a[1] < sp.y - 0.5 && c.b[1] < sp.y - 0.5) || (c.a[1] > sp.y + 0.5 && c.b[1] > sp.y + 0.5)) continue;
          caps.push(c);
        }
        sp.step(caps);
      }
    }
    if (this.acc > DT) this.acc = 0;
    this.refresh();
  }
  refresh() {
    const pos = this.pos, nor = this.nor, t = new THREE.Vector3(), n1 = new THREE.Vector3(), n2 = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
    this.spans.forEach((sp, s) => {
      const base = s * POINTS * SIDES, P = sp.p;
      for (let i = 0; i < POINTS; i++) {
        const a = P[Math.max(0, i - 1)], b = P[Math.min(POINTS - 1, i + 1)], p = P[i];
        t.set(b[0] - a[0], b[1] - a[1], b[2] - a[2]).normalize();
        n1.crossVectors(t, up).normalize(); n2.crossVectors(t, n1);
        for (let j = 0; j < SIDES; j++) {
          const ang = j * Math.PI * 2 / SIDES, cs = Math.cos(ang), sn = Math.sin(ang), k = (base + i * SIDES + j) * 3;
          const nx = n1.x * cs + n2.x * sn, ny = n1.y * cs + n2.y * sn, nz = n1.z * cs + n2.z * sn;
          pos[k] = p[0] + nx * RADIUS; pos[k + 1] = p[1] + ny * RADIUS; pos[k + 2] = p[2] + nz * RADIUS;
          nor[k] = nx; nor[k + 1] = ny; nor[k + 2] = nz;
        }
      }
    });
    const g = this.mesh.geometry;
    g.attributes.position.needsUpdate = true; g.attributes.normal.needsUpdate = true;
  }
  // Найближча до точки p точка верхнього каната: на ній рука тримається за канат
  nearestTop(p) {
    let best = null, bd = 1e9;
    for (const sp of this.spans) {
      if (sp.level !== 2) continue;
      for (let i = 0; i < POINTS - 1; i++) {
        const q = closest({ a: sp.p[i], b: sp.p[i + 1] }, p), d = len(sub(q, p));
        if (d < bd) { bd = d; best = q; }
      }
    }
    return best ? { p: best, d: bd } : null;
  }
  maxDeflection() { return this.spans.reduce((m, s) => Math.max(m, s.deflection()), 0); }
}
