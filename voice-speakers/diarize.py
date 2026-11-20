"""Who speaks when in a recording, told by voice, on the GPU only. No text: only times and voice labels.

usage: python diarize.py <audio> <out.json> [--min-speakers 2] [--max-speakers 4]

Steps: the audio is read as 16 kHz mono (ffmpeg); windows of 1.5 s every 0.5 s; quiet windows are dropped (far
below the loud ones); every window gets a voice print (ECAPA, speechbrain/spkrec-ecapa-voxceleb); the prints are
clustered (spectral clustering, the number of voices by the largest eigengap); every 0.25 s frame takes the voice of
the windows that cover it; runs of one voice become segments. Voices are named S1, S2, ... by their total time.
Exit codes: 0 done, 2 bad input, 3 no GPU (the tool never falls back to the CPU).
"""
import argparse
import json
import os
import subprocess
import sys
import time

import numpy as np
import torch

SR = 16000
WIN_SEC = float(os.environ.get('VOICE_WIN_SEC', '1.5'))
HOP_SEC = float(os.environ.get('VOICE_HOP_SEC', '0.5'))
FRAME_SEC = 0.25
QUIET_DB = 30.0  # a window this far below the loud windows (95th percentile) is taken as silence
NEIGHBOURS = 0.1  # share of the strongest neighbours kept of every window in the affinity
MODEL = 'speechbrain/spkrec-ecapa-voxceleb'
MODEL_DIR = os.environ.get('VOICE_MODEL_DIR', '/models/ecapa')


def load_audio(path):
    cmd = ['ffmpeg', '-nostdin', '-v', 'error', '-i', path, '-ac', '1', '-ar', str(SR), '-f', 'f32le', '-']
    raw = subprocess.run(cmd, check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.float32).copy()


def encoder(device):
    from speechbrain.inference.speaker import EncoderClassifier
    try:
        from speechbrain.utils.fetching import LocalStrategy
        enc = EncoderClassifier.from_hparams(source=MODEL, savedir=MODEL_DIR, run_opts={'device': device},
                                             local_strategy=LocalStrategy.COPY)
    except ImportError:
        enc = EncoderClassifier.from_hparams(source=MODEL, savedir=MODEL_DIR, run_opts={'device': device})
    enc.eval()
    return enc


def voice_prints(enc, wav, starts, win, batch=64):
    """Voice prints of the windows, a batch at a time; a batch the GPU has no memory for is halved and tried again."""
    out = []
    i = 0
    with torch.inference_mode():
        while i < len(starts):
            chunk = None
            try:
                chunk = torch.stack([wav[s:s + win] for s in starts[i:i + batch]])
                out.append(enc.encode_batch(chunk).squeeze(1).float())
                i += batch
            except torch.OutOfMemoryError:
                if batch == 1:
                    raise
                chunk = None
                torch.cuda.empty_cache()
                batch = max(1, batch // 2)
                print('GPU memory short: batch of %d windows' % batch, file=sys.stderr)
    return torch.cat(out)


def kmeans(v, k, iters=100, restarts=10, seed=0):
    g = torch.Generator(device=v.device).manual_seed(seed)
    best, best_inertia = None, None
    for _ in range(restarts):
        idx = [int(torch.randint(len(v), (1,), generator=g, device=v.device))]
        for _ in range(1, k):  # k-means++ start
            d = torch.cdist(v, v[idx]).min(1).values ** 2
            idx.append(int(torch.multinomial(d / d.sum(), 1, generator=g)))
        c = v[idx].clone()
        lab = torch.cdist(v, c).argmin(1)
        for _ in range(iters):
            new = torch.stack([v[lab == j].mean(0) if bool((lab == j).any()) else c[j] for j in range(k)])
            lab = torch.cdist(v, new).argmin(1)
            if torch.allclose(new, c):
                break
            c = new
        inertia = float(((v - c[lab]) ** 2).sum())
        if best_inertia is None or inertia < best_inertia:
            best, best_inertia = lab, inertia
    return best


def spectral(emb, min_k, max_k):
    x = torch.nn.functional.normalize(emb, dim=1)
    a = (x @ x.T).clamp(min=0)
    n = a.shape[0]
    a.fill_diagonal_(0)
    p = min(n - 1, max(10, int(NEIGHBOURS * n)))
    thr = torch.topk(a, p, dim=1).values[:, -1:]
    a = torch.where(a >= thr, a, torch.zeros_like(a))
    a = 0.5 * (a + a.T)
    d = a.sum(1)
    dinv = torch.where(d > 0, d.rsqrt(), torch.zeros_like(d))
    lap = torch.eye(n, device=a.device) - dinv[:, None] * a * dinv[None, :]
    vals, vecs = torch.linalg.eigh(lap)
    top = min(max_k, n - 1)
    gaps = vals[1:top + 1] - vals[:top]  # gaps[i] follows the (i+1)-th smallest eigenvalue: k = i + 1
    k = int(torch.argmax(gaps[min_k - 1:top])) + min_k
    v = torch.nn.functional.normalize(vecs[:, :k], dim=1)
    lab = kmeans(v, k)
    # one pass back in the voice-print space: each window to the nearest voice centre
    cen = torch.nn.functional.normalize(torch.stack([x[lab == j].mean(0) for j in range(k)]), dim=1)
    lab = (x @ cen.T).argmax(1)
    return lab, k, [round(float(e), 4) for e in vals[:top + 1]]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('audio')
    ap.add_argument('out')
    ap.add_argument('--min-speakers', type=int, default=2)
    ap.add_argument('--max-speakers', type=int, default=4)
    a = ap.parse_args()
    if not torch.cuda.is_available():
        print('no GPU: this tool runs on the GPU only', file=sys.stderr)
        return 3
    if not os.path.isfile(a.audio):
        print('no such audio file', file=sys.stderr)
        return 2
    device = 'cuda'
    t0 = time.time()
    wav = load_audio(a.audio)
    duration = len(wav) / SR
    win, hop = int(WIN_SEC * SR), int(HOP_SEC * SR)
    if len(wav) < win:
        wav = np.pad(wav, (0, win - len(wav)))
    gw = torch.from_numpy(wav).to(device)
    starts = list(range(0, len(wav) - win + 1, hop))
    # loudness of every window from a running sum of squares: no copy of the windows
    cs = torch.cat([torch.zeros(1, device=device, dtype=torch.float64), torch.cumsum(gw.double() ** 2, 0)])
    st = torch.tensor(starts, device=device)
    db = (10 * torch.log10((cs[st + win] - cs[st]) / win + 1e-18)).float()
    del cs
    loud = torch.quantile(db, 0.95)
    keep = (db > loud - QUIET_DB).nonzero().squeeze(1).tolist()
    kept = [starts[i] for i in keep]
    t1 = time.time()
    enc = encoder(device)
    emb = voice_prints(enc, gw, kept, win)
    t2 = time.time()
    lab, k, eig = spectral(emb, a.min_speakers, a.max_speakers)
    # frames: the voice of the windows that cover the frame, the window centre weighs most
    centres = torch.tensor([(s + win / 2) / SR for s in kept], device=device, dtype=torch.float64)
    nf = int(np.ceil(duration / FRAME_SEC))
    votes = torch.zeros(nf, k, device=device, dtype=torch.float64)
    reach = int(np.ceil(WIN_SEC / 2 / FRAME_SEC)) + 1
    base = torch.floor(centres / FRAME_SEC).long()
    for off in range(-reach, reach + 1):
        f = base + off
        ok = (f >= 0) & (f < nf)
        w = (1 - ((f.double() + 0.5) * FRAME_SEC - centres).abs() / (WIN_SEC / 2)).clamp(min=0)
        votes.index_put_((f[ok], lab[ok]), w[ok], accumulate=True)
    frame = torch.where(votes.sum(1) > 0, votes.argmax(1), torch.full((nf,), -1, device=device)).tolist()
    t3 = time.time()
    # name the voices by their total time
    total = {j: frame.count(j) for j in range(k)}
    order = sorted(total, key=lambda j: -total[j])
    name = {j: 'S%d' % (i + 1) for i, j in enumerate(order)}
    segments = []
    for i, f in enumerate(frame):
        if f < 0:
            continue
        s, e = round(i * FRAME_SEC, 3), round(min(duration, (i + 1) * FRAME_SEC), 3)
        if segments and segments[-1]['speaker'] == name[f] and abs(segments[-1]['end'] - s) < 1e-6:
            segments[-1]['end'] = e
        else:
            segments.append({'start': s, 'end': e, 'speaker': name[f]})
    res = {
        'audio': os.path.basename(a.audio), 'duration_sec': round(duration, 3), 'model': MODEL,
        'device': torch.cuda.get_device_name(0), 'speakers': k, 'eigenvalues': eig,
        'windows': len(starts), 'windows_kept': len(kept),
        'seconds_by_speaker': {name[j]: round(total[j] * FRAME_SEC, 1) for j in order},
        'timing_sec': {'audio': round(t1 - t0, 2), 'voice_prints': round(t2 - t1, 2), 'clusters': round(t3 - t2, 2)},
        'segments': segments,
    }
    with open(a.out, 'w', encoding='utf-8') as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    print('%s: %.1f min, %d voices %s, %d/%d windows, %.1f s on %s' % (
        res['audio'], duration / 60, k, res['seconds_by_speaker'], len(kept), len(starts), time.time() - t0, res['device']))
    return 0


if __name__ == '__main__':
    sys.exit(main())
