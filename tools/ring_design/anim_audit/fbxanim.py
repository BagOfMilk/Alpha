#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""fbxanim - читач бінарного FBX 7.x (чистий Python + numpy): світові положення й повороти кісток по кадрах.

    python fbxanim.py dump <fbx>
    python fbxanim.py bake <fbx> <out.npz> [--fps 30] [--only A,B]

Лінива розборка: масиви декодуються лише за потреби, файл читається через mmap,
криві анімації не матеріалізуються у дерево вузлів (лише зсуви KeyTime/KeyValueFloat).
Кватерніони - порядок (w, x, y, z), світ Z-вгору, X-праворуч, Y-вперед (як у Blender), метри.
"""
import sys, os, mmap, struct, zlib, time, argparse
import numpy as np

TICKS = 46186158000.0
_ORDERS = {0: 'XYZ', 1: 'XZY', 2: 'YZX', 3: 'YXZ', 4: 'ZXY', 5: 'ZYX', 6: 'XYZ'}
_DT = {'f': np.float32, 'd': np.float64, 'l': np.int64, 'i': np.int32, 'b': np.uint8}
_SZ = {'f': 4, 'd': 8, 'l': 8, 'i': 4, 'b': 1}


def _lower_priority():
    try:
        import ctypes
        k = ctypes.windll.kernel32
        k.SetPriorityClass(k.GetCurrentProcess(), 0x40)  # IDLE_PRIORITY_CLASS
    except Exception:
        pass


def peak_ws_mb():
    """Пік робочого набору процесу (МБ), Windows; інакше None."""
    try:
        import ctypes
        from ctypes import wintypes

        class PMC(ctypes.Structure):
            _fields_ = [('cb', wintypes.DWORD), ('PageFaultCount', wintypes.DWORD),
                        ('PeakWorkingSetSize', ctypes.c_size_t), ('WorkingSetSize', ctypes.c_size_t),
                        ('QuotaPeakPagedPoolUsage', ctypes.c_size_t), ('QuotaPagedPoolUsage', ctypes.c_size_t),
                        ('QuotaPeakNonPagedPoolUsage', ctypes.c_size_t), ('QuotaNonPagedPoolUsage', ctypes.c_size_t),
                        ('PagefileUsage', ctypes.c_size_t), ('PeakPagefileUsage', ctypes.c_size_t)]
        pmc = PMC()
        pmc.cb = ctypes.sizeof(PMC)
        h = ctypes.windll.kernel32.GetCurrentProcess()
        ctypes.windll.psapi.GetProcessMemoryInfo(ctypes.c_void_p(h), ctypes.byref(pmc), pmc.cb)
        return pmc.PeakWorkingSetSize / 1048576.0, pmc.PeakPagefileUsage / 1048576.0
    except Exception:
        return None


class LazyArr:
    __slots__ = ('t', 'n', 'enc', 'off', 'clen')

    def __init__(self, t, n, enc, off, clen):
        self.t, self.n, self.enc, self.off, self.clen = t, n, enc, off, clen


class Node:
    __slots__ = ('name', 'props', 'children', 'end')

    def __init__(self, name, props, children, end):
        self.name, self.props, self.children, self.end = name, props, children, end

    def child(self, name):
        for c in self.children:
            if c.name == name:
                return c
        return None


class FBX:
    def __init__(self, path):
        self.path = path
        self.f = open(path, 'rb')
        self.mm = mmap.mmap(self.f.fileno(), 0, access=mmap.ACCESS_READ)
        mm = self.mm
        if mm[:21] != b'Kaydara FBX Binary  \x00':
            raise ValueError('не бінарний FBX')
        self.version = struct.unpack_from('<I', mm, 23)[0]
        self.v64 = self.version >= 7500
        self._hs = 25 if self.v64 else 13
        self._hf = '<QQQB' if self.v64 else '<IIIB'
        self.global_settings = {}
        self.models = {}      # id -> dict(name, cls, props, parent)
        self.stacks = []      # (id, name, start, stop)
        self.layers = {}
        self.cnodes = {}      # id -> dict(name, defaults)
        self.curves = {}      # id -> (LazyArr time, LazyArr value)
        self.conns = []
        self._scan()

    # ---------- низький рівень ----------
    def _props(self, pos, n):
        mm = self.mm
        out = []
        up = struct.unpack_from
        for _ in range(n):
            t = chr(mm[pos])
            pos += 1
            if t == 'S' or t == 'R':
                ln = up('<I', mm, pos)[0]
                pos += 4
                b = mm[pos:pos + ln]
                pos += ln
                out.append(b if t == 'R' else b.decode('utf-8', 'replace'))
            elif t == 'L':
                out.append(up('<q', mm, pos)[0]); pos += 8
            elif t == 'I':
                out.append(up('<i', mm, pos)[0]); pos += 4
            elif t == 'D':
                out.append(up('<d', mm, pos)[0]); pos += 8
            elif t == 'F':
                out.append(up('<f', mm, pos)[0]); pos += 4
            elif t == 'Y':
                out.append(up('<h', mm, pos)[0]); pos += 2
            elif t == 'C':
                out.append(mm[pos]); pos += 1
            elif t in _DT:
                n_, enc, cl = up('<III', mm, pos)
                pos += 12
                out.append(LazyArr(t, n_, enc, pos, cl))
                pos += cl
            else:
                raise ValueError('невідомий тип властивості %r' % t)
        return out

    def array(self, la):
        raw = self.mm[la.off:la.off + la.clen]
        if la.enc == 1:
            raw = zlib.decompress(raw)
        return np.frombuffer(raw, dtype=_DT[la.t], count=la.n)

    def _hdr(self, pos):
        end, npr, pl, nl = struct.unpack_from(self._hf, self.mm, pos)
        name = self.mm[pos + self._hs:pos + self._hs + nl].decode('latin1')
        ps = pos + self._hs + nl
        return end, npr, pl, name, ps

    def _node(self, pos):
        end, npr, pl, name, ps = self._hdr(pos)
        props = self._props(ps, npr)
        ch = []
        p = ps + pl
        while p < end:
            if struct.unpack_from('<Q' if self.v64 else '<I', self.mm, p)[0] == 0:
                break
            c = self._node(p)
            ch.append(c)
            p = c.end
        return Node(name, props, ch, end)

    @staticmethod
    def _p70(node):
        """Properties70 -> {ім'я: [значення...]}"""
        d = {}
        if node is None:
            return d
        for c in node.children:
            if c.name == 'P' and len(c.props) >= 5:
                d[c.props[0]] = c.props[4:]
        return d

    @staticmethod
    def _nm(s):
        return s.split('\x00\x01')[0] if isinstance(s, str) else str(s)

    def _scan(self):
        mm, v64 = self.mm, self.v64
        pos = 27
        size = len(mm)
        while pos < size:
            end, npr, pl, name, ps = self._hdr(pos)
            if end == 0:
                break
            if name == 'GlobalSettings':
                n = self._node(pos)
                self.global_settings = self._p70(n.child('Properties70'))
            elif name == 'Objects':
                self._objects(ps + pl, end)
            elif name == 'Connections':
                self._connections(ps + pl, end)
            pos = end

    def _objects(self, p, end):
        mm = self.mm
        up = struct.unpack_from
        while p < end:
            e, npr, pl, name, ps = self._hdr(p)
            if e == 0:
                break
            if name == 'AnimationCurve':
                cid = up('<q', mm, ps + 1)[0]
                pr = ps
                # пропускаємо власні властивості, шукаємо дітей
                q = ps + pl
                kt = kv = None
                while q < e:
                    ce, cnp, cpl, cname, cps = self._hdr(q)
                    if ce == 0:
                        break
                    if cname == 'KeyTime':
                        kt = self._props(cps, 1)[0]
                    elif cname == 'KeyValueFloat':
                        kv = self._props(cps, 1)[0]
                    q = ce
                self.curves[cid] = (kt, kv)
            elif name in ('Model', 'AnimationStack', 'AnimationLayer', 'AnimationCurveNode'):
                n = self._node(p)
                oid = n.props[0]
                nm = self._nm(n.props[1])
                if name == 'Model':
                    self.models[oid] = dict(name=nm, cls=n.props[2], props=self._p70(n.child('Properties70')),
                                            parent=0)
                elif name == 'AnimationStack':
                    pp = self._p70(n.child('Properties70'))
                    self.stacks.append(dict(id=oid, name=nm, start=int(pp.get('LocalStart', [0])[0]),
                                            stop=int(pp.get('LocalStop', [0])[0])))
                elif name == 'AnimationLayer':
                    self.layers[oid] = nm
                else:
                    self.cnodes[oid] = dict(name=nm, defaults=self._p70(n.child('Properties70')))
            p = e

    def _connections(self, p, end):
        mm = self.mm
        up = struct.unpack_from
        conns = self.conns
        while p < end:
            e, npr, pl, name, ps = self._hdr(p)
            if e == 0:
                break
            if name == 'C':
                pr = self._props(ps, npr)
                conns.append(tuple(pr))
            p = e

    # ---------- структура ----------
    def build_index(self):
        self.oo = {}        # parent -> [child]
        self.cn_target = {}  # curvenode -> (model, prop)
        self.cn_curves = {}  # curvenode -> {prop: curve}
        for c in self.conns:
            typ, ch, par = c[0], c[1], c[2]
            if typ == 'OO':
                self.oo.setdefault(par, []).append(ch)
                if ch in self.models and par in self.models:
                    self.models[ch]['parent'] = par
            elif typ == 'OP':
                prop = c[3]
                if ch in self.cnodes and par in self.models:
                    self.cn_target[ch] = (par, prop)
                elif ch in self.curves and par in self.cnodes:
                    self.cn_curves.setdefault(par, {})[prop] = ch

    def axis_matrix(self):
        g = self.global_settings

        def gv(k, d):
            v = g.get(k)
            return v[0] if v else d
        vec = lambda ax, sg: np.eye(3)[int(ax)] * (1 if sg >= 0 else -1)
        up = vec(gv('UpAxis', 1), gv('UpAxisSign', 1))
        co = vec(gv('CoordAxis', 0), gv('CoordAxisSign', 1))
        # Blender-експорт пише FrontAxis, але правий напрямок надійніше з Up x Coord
        fw = np.cross(up, co)
        M = np.stack([co, fw, up])   # рядки: x_ціль, y_ціль, z_ціль
        scale = float(gv('UnitScaleFactor', 1.0)) / 100.0
        M4 = np.eye(4)
        M4[:3, :3] = M * scale
        return M4, M, scale


# ---------- математика ----------
def _rot1(axis, deg):
    a = np.radians(np.asarray(deg, dtype=np.float64))
    c, s = np.cos(a), np.sin(a)
    n = a.shape[0]
    m = np.zeros((n, 4, 4))
    m[:, 3, 3] = 1
    o, z = np.ones(n), np.zeros(n)
    if axis == 'X':
        m[:, 0, 0] = 1; m[:, 1, 1] = c; m[:, 1, 2] = -s; m[:, 2, 1] = s; m[:, 2, 2] = c
    elif axis == 'Y':
        m[:, 1, 1] = 1; m[:, 0, 0] = c; m[:, 0, 2] = s; m[:, 2, 0] = -s; m[:, 2, 2] = c
    else:
        m[:, 2, 2] = 1; m[:, 0, 0] = c; m[:, 0, 1] = -s; m[:, 1, 0] = s; m[:, 1, 1] = c
    return m


def euler_mat(order, rot):
    """rot [F,3] градуси; FBX застосовує осі в порядку запису: XYZ -> Rz*Ry*Rx."""
    rot = np.asarray(rot, dtype=np.float64).reshape(-1, 3)
    ms = {'X': _rot1('X', rot[:, 0]), 'Y': _rot1('Y', rot[:, 1]), 'Z': _rot1('Z', rot[:, 2])}
    a, b, c = order
    return ms[c] @ ms[b] @ ms[a]


def trans_mat(v):
    v = np.asarray(v, dtype=np.float64).reshape(-1, 3)
    m = np.tile(np.eye(4), (v.shape[0], 1, 1))
    m[:, :3, 3] = v
    return m


def scale_mat(v):
    v = np.asarray(v, dtype=np.float64).reshape(-1, 3)
    m = np.tile(np.eye(4), (v.shape[0], 1, 1))
    m[:, 0, 0], m[:, 1, 1], m[:, 2, 2] = v[:, 0], v[:, 1], v[:, 2]
    return m


def mat2quat(R):
    """R [...,3,3] -> [...,4] (w,x,y,z)."""
    r00, r11, r22 = R[..., 0, 0], R[..., 1, 1], R[..., 2, 2]
    w2 = 1 + r00 + r11 + r22
    x2 = 1 + r00 - r11 - r22
    y2 = 1 - r00 + r11 - r22
    z2 = 1 - r00 - r11 + r22
    idx = np.argmax(np.stack([w2, x2, y2, z2], -1), -1)
    q = np.zeros(R.shape[:-2] + (4,))
    d = lambda a: np.sqrt(np.maximum(a, 1e-12))
    # w
    s = 2 * d(w2)
    qw = np.stack([s / 4, (R[..., 2, 1] - R[..., 1, 2]) / s, (R[..., 0, 2] - R[..., 2, 0]) / s,
                   (R[..., 1, 0] - R[..., 0, 1]) / s], -1)
    s = 2 * d(x2)
    qx = np.stack([(R[..., 2, 1] - R[..., 1, 2]) / s, s / 4, (R[..., 0, 1] + R[..., 1, 0]) / s,
                   (R[..., 0, 2] + R[..., 2, 0]) / s], -1)
    s = 2 * d(y2)
    qy = np.stack([(R[..., 0, 2] - R[..., 2, 0]) / s, (R[..., 0, 1] + R[..., 1, 0]) / s, s / 4,
                   (R[..., 1, 2] + R[..., 2, 1]) / s], -1)
    s = 2 * d(z2)
    qz = np.stack([(R[..., 1, 0] - R[..., 0, 1]) / s, (R[..., 0, 2] + R[..., 2, 0]) / s,
                   (R[..., 1, 2] + R[..., 2, 1]) / s, s / 4], -1)
    cand = [qw, qx, qy, qz]
    q = np.where(idx[..., None] == 0, qw, np.where(idx[..., None] == 1, qx,
                 np.where(idx[..., None] == 2, qy, qz)))
    q /= np.linalg.norm(q, axis=-1, keepdims=True)
    return q


class Rig:
    """Статична частина моделей + запікання стека."""

    def __init__(self, fbx):
        self.fbx = fbx
        fbx.build_index()
        M = fbx.models
        self.limbs = [i for i, m in M.items() if m['cls'] == 'LimbNode']
        need = set()
        for i in self.limbs:
            while i and i in M and i not in need:
                need.add(i)
                i = M[i]['parent']
        # топологічний порядок (батьки першими), стабільний за порядком файлу
        order, seen = [], set()

        def visit(i):
            if i in seen or i not in need:
                return
            visit(M[i]['parent'])
            seen.add(i)
            order.append(i)
        for i in M:
            visit(i)
        self.order = order
        self.limb_idx = [i for i in order if M[i]['cls'] == 'LimbNode']
        self.bone_names = [M[i]['name'] for i in self.limb_idx]
        pos = {i: k for k, i in enumerate(self.limb_idx)}
        self.bone_parents = []
        for i in self.limb_idx:
            p = M[i]['parent']
            while p and p in M and p not in pos:
                p = M[p]['parent']
            self.bone_parents.append(pos.get(p, -1))
        self.axis4, self.axis3, self.unit = fbx.axis_matrix()
        self.static = {i: self._static(M[i]) for i in order}

    @staticmethod
    def _v(pp, key, default):
        v = pp.get(key)
        if not v or len(v) < 3:
            return np.array(default, dtype=np.float64)
        return np.array(v[:3], dtype=np.float64)

    def _static(self, m):
        pp = m['props']
        order = _ORDERS.get(int(pp.get('RotationOrder', [0])[0]), 'XYZ')
        d = dict(order=order, T=self._v(pp, 'Lcl Translation', (0, 0, 0)),
                 R=self._v(pp, 'Lcl Rotation', (0, 0, 0)), S=self._v(pp, 'Lcl Scaling', (1, 1, 1)))
        d['inherit'] = int(pp.get('InheritType', [0])[0])
        pre = self._v(pp, 'PreRotation', (0, 0, 0))
        post = self._v(pp, 'PostRotation', (0, 0, 0))
        roff = self._v(pp, 'RotationOffset', (0, 0, 0))
        rp = self._v(pp, 'RotationPivot', (0, 0, 0))
        soff = self._v(pp, 'ScalingOffset', (0, 0, 0))
        sp = self._v(pp, 'ScalingPivot', (0, 0, 0))
        use_pre = int(pp.get('RotationActive', [1])[0]) != 0 or True
        I = np.eye(4)[None]
        A = trans_mat(roff) @ trans_mat(rp) @ euler_mat(order, pre)
        B = np.linalg.inv(euler_mat(order, post)) @ np.linalg.inv(trans_mat(rp)) @ trans_mat(soff) @ trans_mat(sp)
        C = np.linalg.inv(trans_mat(sp))
        d['A'] = None if np.allclose(A, I) else A
        d['B'] = None if np.allclose(B, I) else B
        d['C'] = None if np.allclose(C, I) else C
        return d

    def local(self, mid, T=None, R=None, S=None):
        s = self.static[mid]
        if T is None:
            T = s['T'][None]
        if R is None:
            R = s['R'][None]
        if S is None:
            S = s['S'][None]
        F = max(len(T), len(R), len(S))
        if len(T) == 1 and F > 1: T = np.repeat(T, F, 0)
        if len(R) == 1 and F > 1: R = np.repeat(R, F, 0)
        if len(S) == 1 and F > 1: S = np.repeat(S, F, 0)
        L = trans_mat(T)
        if s['A'] is not None:
            L = L @ s['A']
        L = L @ euler_mat(s['order'], R)
        if s['B'] is not None:
            L = L @ s['B']
        L = L @ scale_mat(S)
        if s['C'] is not None:
            L = L @ s['C']
        return L

    def stack_channels(self, stack):
        """{model_id: {prop: curvenode_id}} для стека."""
        fbx = self.fbx
        out = {}
        for lay in fbx.oo.get(stack['id'], []):
            if lay not in fbx.layers:
                continue
            for cn in fbx.oo.get(lay, []):
                if cn in fbx.cnodes and cn in fbx.cn_target:
                    mid, prop = fbx.cn_target[cn]
                    out.setdefault(mid, {})[prop] = cn
        return out

    def _channel(self, cn, tt, default3):
        """значення каналу [F,3] у моменти tt (тики)."""
        fbx = self.fbx
        dflt = fbx.cnodes[cn]['defaults']
        res = np.empty((len(tt), 3))
        curves = fbx.cn_curves.get(cn, {})
        for k, ax in enumerate('XYZ'):
            cv = curves.get('d|' + ax)
            if cv is None:
                v = dflt.get('d|' + ax)
                res[:, k] = v[0] if v else default3[k]
            else:
                kt, kv = fbx.curves[cv]
                res[:, k] = np.interp(tt, fbx.array(kt).astype(np.float64), fbx.array(kv).astype(np.float64))
        return res

    def bake_stack(self, stack, fps):
        fbx = self.fbx
        dur = (stack['stop'] - stack['start']) / TICKS
        F = int(round(dur * fps)) + 1
        tt = stack['start'] + (np.arange(F) / fps) * TICKS
        chans = self.stack_channels(stack)
        W = {}
        propmap = (('Lcl Translation', 'T'), ('Lcl Rotation', 'R'), ('Lcl Scaling', 'S'))
        for mid in self.order:
            s = self.static[mid]
            kw = {}
            for prop, key in propmap:
                cn = chans.get(mid, {}).get(prop)
                if cn is not None:
                    kw[key] = self._channel(cn, tt, s[key])
            L = self.local(mid, **kw)
            par = fbx.models[mid]['parent']
            W[mid] = (W[par] @ L) if par in W else (self.axis4[None] @ L)
            if len(W[mid]) == 1 and F > 1:
                W[mid] = np.repeat(W[mid], F, 0)
        Wl = np.stack([W[i] for i in self.limb_idx], 1)  # [F,B,4,4]
        pos = Wl[..., :3, 3]
        Rm = Wl[..., :3, :3].copy()
        Rm /= np.maximum(np.linalg.norm(Rm, axis=-2, keepdims=True), 1e-12)
        q = mat2quat(Rm)
        # безперервність півсфери по кадрах
        for f in range(1, F):
            flip = (q[f] * q[f - 1]).sum(-1) < 0
            q[f][flip] *= -1
        return pos.astype(np.float32), q.astype(np.float32), dur


def clip_name(n):
    return n.split('|')[-1]


# ---------- CLI ----------
def cmd_dump(path):
    fbx = FBX(path)
    rig = Rig(fbx)
    g = fbx.global_settings
    print('файл:', path, ' розмір МБ: %.1f' % (os.path.getsize(path) / 1048576))
    print('версія FBX:', fbx.version, '(64-бітні зсуви)' if fbx.v64 else '')
    for k in ('UpAxis', 'UpAxisSign', 'FrontAxis', 'FrontAxisSign', 'CoordAxis', 'CoordAxisSign',
              'OriginalUpAxis', 'OriginalUpAxisSign', 'UnitScaleFactor', 'OriginalUnitScaleFactor'):
        print('  %s = %s' % (k, g.get(k, ['-'])[0]))
    print('матриця осей (файл->світ Z-вгору), масштаб у метри: %s' % rig.unit)
    print(np.round(rig.axis3 / (rig.unit or 1), 3))
    print('моделей:', len(fbx.models), ' кісток (LimbNode):', len(rig.limb_idx))
    clsc = {}
    for m in fbx.models.values():
        clsc[m['cls']] = clsc.get(m['cls'], 0) + 1
    print('класи моделей:', clsc)
    inh = {}
    for i in rig.order:
        inh[rig.static[i]['inherit']] = inh.get(rig.static[i]['inherit'], 0) + 1
    print('InheritType у ланцюгу кісток:', inh)
    print('кістки (ім\'я <- батько):')
    for i, n in enumerate(rig.bone_names):
        p = rig.bone_parents[i]
        print('  %2d %-22s <- %s' % (i, n, rig.bone_names[p] if p >= 0 else '(корінь/Null)'))
    print('непорожні предки-не кістки:', [fbx.models[i]['name'] for i in rig.order if fbx.models[i]['cls'] != 'LimbNode'])
    print('стеків анімації:', len(fbx.stacks), ' кривих:', len(fbx.curves), ' вузлів кривих:', len(fbx.cnodes))
    for s in fbx.stacks[:8]:
        print('  %-40s %.3f с' % (s['name'], (s['stop'] - s['start']) / TICKS))
    if len(fbx.stacks) > 8:
        print('  ...')


def cmd_bake(path, out, fps, only):
    t0 = time.time()
    fbx = FBX(path)
    rig = Rig(fbx)
    t1 = time.time()
    names, lens, data = [], [], {}
    for k, s in enumerate(fbx.stacks):
        nm = clip_name(s['name'])
        if only and nm not in only:
            continue
        pos, q, dur = rig.bake_stack(s, fps)
        data['pos/' + nm] = pos
        data['quat/' + nm] = q
        names.append(nm)
        lens.append(dur)
        if (k + 1) % 25 == 0:
            print('  %d/%d  %.0f с' % (k + 1, len(fbx.stacks), time.time() - t0), flush=True)
    np.savez(out, bones=np.array(rig.bone_names), parents=np.array(rig.bone_parents, dtype=np.int32),
             fps=np.float32(fps), clips=np.array(names), lengths=np.array(lens, dtype=np.float32),
             quat_order=np.array('wxyz'), **data)
    print('готово: %d кліпів, %d кісток -> %s ; розбір %.1f с, разом %.1f с' %
          (len(names), len(rig.bone_names), out, t1 - t0, time.time() - t0))
    pw = peak_ws_mb()
    if pw:
        print('пік робочого набору: %.0f МБ, пік pagefile: %.0f МБ' % pw)


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    _lower_priority()
    ap = argparse.ArgumentParser()
    sp = ap.add_subparsers(dest='cmd', required=True)
    a = sp.add_parser('dump'); a.add_argument('fbx')
    b = sp.add_parser('bake'); b.add_argument('fbx'); b.add_argument('out')
    b.add_argument('--fps', type=float, default=30.0); b.add_argument('--only', default='')
    ns = ap.parse_args()
    if ns.cmd == 'dump':
        cmd_dump(ns.fbx)
        pw = peak_ws_mb()
        if pw: print('пік робочого набору: %.0f МБ' % pw[0])
    else:
        cmd_bake(ns.fbx, ns.out, ns.fps, set(x for x in ns.only.split(',') if x))


if __name__ == '__main__':
    main()
