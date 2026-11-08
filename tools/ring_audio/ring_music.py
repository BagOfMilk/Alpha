"""Музика меню «Рингу», раунд 1: нові варіанти (рок-гурт). Запуск з теки tools:
  python -m ring_audio.ring_music --out <тека> [--only music_login.v1]
Виходи: audio/<id>.wav, spec/<id>.png, spec/_music.png, manifest.json. Детерміновано.
"""
import argparse
import json
import sys
import time
from pathlib import Path

import numpy as np

from . import loudness as L
from . import mix as X
from . import ring_music_kit as K
from .synth import SR


def _builders():
    from . import ring_music_login, ring_music_loading, ring_music_settings
    d = {}
    for m in (ring_music_login, ring_music_loading, ring_music_settings):
        d.update(m.BUILDERS)
    return d


def stft_db(x, n=4096, hop=1024, nbins=360, fmin=30.0, fmax=16000.0):
    x = x.mean(axis=1) if x.ndim == 2 else x
    w = np.hanning(n)
    nf = max(1, (len(x) - n) // hop + 1)
    frames = np.stack([x[i * hop:i * hop + n] * w for i in range(nf)])
    mag = np.abs(np.fft.rfft(frames, axis=1)) / (n / 4)
    f = np.fft.rfftfreq(n, 1 / SR)
    fl = np.geomspace(fmin, fmax, nbins)
    idx = np.interp(fl, f, np.arange(len(f)))
    i0 = np.floor(idx).astype(int)
    i1 = np.minimum(i0 + 1, len(f) - 1)
    fr = idx - i0
    sp = mag[:, i0] * (1 - fr) + mag[:, i1] * fr
    return 20 * np.log10(sp + 1e-7).T, fl, hop / SR


def plot_spec(path, title, yq, secs):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig = plt.figure(figsize=(12, 7.2), dpi=90)
    gs = fig.add_gridspec(3, 2, height_ratios=[0.8, 3.2, 1.8])
    ax0 = fig.add_subplot(gs[0, :])
    m = yq.mean(axis=1)
    k = max(1, len(m) // 2400)
    nb = len(m) // k
    mm = m[:nb * k].reshape(nb, k)
    t = np.arange(nb) * k / SR
    ax0.fill_between(t, mm.min(axis=1), mm.max(axis=1), color="#222", lw=0)
    ax0.set_xlim(0, secs)
    ax0.set_ylim(-1, 1)
    ax0.set_title(title, fontsize=10, loc="left")
    ax0.set_xticks([])
    ax1 = fig.add_subplot(gs[1, :])
    S_, fl, dt = stft_db(yq)
    vmax = S_.max()
    ax1.imshow(S_, origin="lower", aspect="auto", cmap="magma", vmin=vmax - 80, vmax=vmax,
               extent=[0, S_.shape[1] * dt, 0, len(fl)])
    ticks = [50, 100, 200, 500, 1000, 2000, 5000, 10000]
    ax1.set_yticks([np.interp(np.log(f_), np.log(fl), np.arange(len(fl))) for f_ in ticks])
    ax1.set_yticklabels([str(f_) for f_ in ticks], fontsize=7)
    ax1.set_xlabel("с")
    for j, (lab, seg) in enumerate((("останні 2 с", yq[-2 * SR:]), ("перші 2 с", yq[:2 * SR]))):
        ax = fig.add_subplot(gs[2, j])
        S2, fl2, dt2 = stft_db(seg, n=2048, hop=512)
        ax.imshow(S2, origin="lower", aspect="auto", cmap="magma", vmin=vmax - 80, vmax=vmax,
                  extent=[0, S2.shape[1] * dt2, 0, len(fl2)])
        ax.set_title("шов: " + lab, fontsize=8)
        ax.set_yticks([])
    fig.tight_layout()
    fig.savefig(path)
    plt.close(fig)


def contact_sheet(out_dir, ids):
    from PIL import Image, ImageDraw
    cols = 3
    slots = []
    for i in ids:
        s = i.split(".")[0]
        if s not in slots:
            slots.append(s)
    cw = 760
    imgs = {}
    for i in ids:
        p = out_dir / "spec" / f"{i}.png"
        if p.exists():
            im = Image.open(p).convert("RGB")
            imgs[i] = im.resize((cw, int(im.height * cw / im.width)))
    ch = max(im.height for im in imgs.values())
    sheet = Image.new("RGB", (cols * cw, len(slots) * ch), (20, 20, 20))
    for r, s in enumerate(slots):
        for c in range(cols):
            i = f"{s}.v{c + 1}"
            if i in imgs:
                sheet.paste(imgs[i], (c * cw, r * ch))
    sheet.save(out_dir / "spec" / "_music.png")


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default=None)
    a = ap.parse_args(argv)
    out = Path(a.out)
    (out / "audio").mkdir(parents=True, exist_ok=True)
    (out / "spec").mkdir(parents=True, exist_ok=True)
    mf = out / "manifest_music.json"
    manifest = json.loads(mf.read_text(encoding="utf-8")) if mf.exists() else {}
    B = _builders()
    for id_, fn in B.items():
        if a.only and not id_.startswith(a.only):
            continue
        t0 = time.time()
        band, cfg, meta = fn()
        r = K.render(band, cfg)
        X.write_wav(out / "audio" / f"{id_}.wav", r["pcm"])
        plot_spec(out / "spec" / f"{id_}.png", f"{id_}  {meta['label']}  {r['seconds']:.1f}с {r['bpm']:.1f}BPM", r["yq"], r["seconds"])
        yq = r["yq"]
        thr = 10 ** (-60 / 20)
        first = int(np.argmax(np.abs(yq).max(axis=1) > thr))
        s = r["seam"]
        print(f"{id_}: {r['seconds']:.2f}s {r['bpm']:.2f}BPM LUFS {r['lufs']:.2f} TP {r['tp']:.2f} first>-60dB {first / SR * 1000:.1f}ms "
              f"seam jump={s['jump']:.4f} med={s['median_adj']:.4f} p99={s['p99_adj']:.4f} rms10 {s['rms_end10']:.4f}/{s['rms_start10']:.4f} "
              f"took {time.time() - t0:.0f}s", flush=True)
        manifest[id_] = dict(id=id_, slot=id_.split(".")[0], label=meta["label"], seconds=r["seconds"], lufs=r["lufs"],
                             peak_dbtp=r["tp"], channels=2, bpm=r["bpm"], first_ms=first / SR * 1000)
        mf.write_text(json.dumps(manifest, ensure_ascii=False, indent=1), encoding="utf-8")
    if not a.only:
        contact_sheet(out, list(manifest.keys()))
    return 0


if __name__ == "__main__":
    sys.exit(main())
