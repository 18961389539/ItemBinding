# -*- coding: utf-8 -*-
"""长轴轴向规范约定选型：量化各候选约定下的趟内 180° 翻转率。
一次性分析脚本，用于确定 BuildMinAreaRect 的规范半平面。"""
import math, random, shutil, sqlite3, sys
from collections import defaultdict
from datetime import datetime

import numpy as np

sys.path.insert(0, "tools")
from brightness_replay import snapshot_db, load_roi, belt_level, estimate_mask

DB = r"MainAPP\bin\Release\net10.0-windows\publish\DataBase\barcode_data.db"
IMG = r"D:\Pictures\jijing"
SCALE = 8.0
MAX_PASSES = 200


def axis_angle(gray, arr):
    belt = belt_level(gray)
    m = estimate_mask(gray, arr, belt, belt + 15)
    if m.sum() < 200:
        return None
    ys, xs = np.nonzero(m)
    pts = np.column_stack([xs, ys]).astype(float)
    ctr = pts.mean(0)
    vals, vecs = np.linalg.eigh(np.cov((pts - ctr).T))
    u = vecs[:, int(np.argmax(vals))]
    return math.atan2(u[1], u[0])


def canon(rot_deg):
    """返回一个把无向轴角(弧度)规范化的函数：在旋转了 rot_deg 的框架内取 uy>0 半平面。"""
    s = math.sin(math.radians(rot_deg))
    c = math.cos(math.radians(rot_deg))

    def fn(r):
        x, y = math.cos(r), math.sin(r)
        xr, yr = x * c + y * s, -x * s + y * c
        if yr < 0:
            xr, yr = -xr, -yr
        x2, y2 = xr * c - yr * s, xr * s + yr * c
        return math.degrees(math.atan2(y2, x2))

    return fn


def main():
    tmp, snap = snapshot_db(DB)
    con = sqlite3.connect(f"file:{snap}?mode=ro", uri=True)
    con.row_factory = sqlite3.Row
    rows = list(con.execute(
        "SELECT Barcode, DetectTime, ImageX, ImageY, Width, Height, ImageFullName "
        "FROM BarcodeData WHERE BrightnessDiff IS NOT NULL AND ImageFullName IS NOT NULL "
        "ORDER BY DetectTime"))
    shutil.rmtree(tmp, ignore_errors=True)

    g = defaultdict(list)
    for r in rows:
        g[r["Barcode"]].append(r)
    bursts = []
    for bc, v in g.items():
        cur = [v[0]]
        for p_, n_ in zip(v, v[1:]):
            try:
                dt = (datetime.fromisoformat(n_["DetectTime"])
                      - datetime.fromisoformat(p_["DetectTime"])).total_seconds()
            except Exception:
                dt = 999
            if dt <= 1.5:
                cur.append(n_)
            else:
                bursts.append(cur); cur = [n_]
        bursts.append(cur)
    bursts = [b for b in bursts if len(b) >= 3]
    print(f"全库 >=3 帧的趟数：{len(bursts)}", flush=True)
    random.seed(11)
    random.shuffle(bursts)
    bursts = bursts[:MAX_PASSES]

    data = []
    for b in bursts:
        seq = []
        for r in b:
            gg = load_roi(r, IMG, SCALE)
            if not gg:
                continue
            a = axis_angle(gg[0], gg[1])
            if a is not None:
                seq.append(a)
        if len(seq) >= 3:
            data.append(seq)
    print(f"有效趟数：{len(data)}（覆盖 {sum(len(x) for x in data)} 帧）", flush=True)
    print()

    def jump_rate(fn):
        bad = 0
        for seq in data:
            cs = [fn(a) for a in seq]
            for p_, n_ in zip(cs, cs[1:]):
                d = (n_ - p_ + 180.0) % 360.0 - 180.0
                if abs(d) > 100:
                    bad += 1
                    break
        return 100.0 * bad / len(data)

    def jitter(fn):
        devs = []
        for seq in data:
            cs = [fn(a) for a in seq]
            base = cs[0]
            dev = [(x - base + 180.0) % 360.0 - 180.0 for x in cs]
            if all(abs(x) <= 90 for x in dev):
                devs.append(float(np.std(dev)))
        return float(np.median(devs)) if devs else float("nan")

    print(f"{'规范约定':<40}{'趟内180°翻转率':>14}{'角抖动中位':>12}")
    print("-" * 68)
    cases = [
        ("① 无规范（当前 eigh 原始符号）", lambda r: math.degrees(r)),
        ("② 边界 0°/180°（uy>0）", canon(0.0)),
        ("③ 边界 ±90°（ux>0）", canon(90.0)),
        ("④ 边界 45°/225°", canon(45.0)),
        ("⑤ 边界 135°/315°", canon(135.0)),
    ]
    for name, fn in cases:
        print(f"{name:<40}{jump_rate(fn):>13.1f}%{jitter(fn):>11.2f}°", flush=True)


if __name__ == "__main__":
    main()
