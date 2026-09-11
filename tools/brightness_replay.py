# -*- coding: utf-8 -*-
"""
灰度判向方案回放验证
====================
在真实采集图上重建产品区域与长轴，复现"整半区均值差"基线，再逐项量化各种改进手段。

关键实现说明
------------
1. draw 图 = 推理图 = 原图 / 配方 EdgeDetection.ResizeScale（本项目为 8），
   而库中 ImageX/ImageY/Width/Height 是原图像素，故需除以该系数。
2. 产品区域不能靠"比传送带暗"的纯阈值分割：包装袋上的浅色格纹亮度≈传送带本身。
   做法是先用低于传送带的阈值取暗像素（深色印刷面 + 深色格块），取最大连通域后
   求凸包并填充 —— 凸包恰好覆盖整个包装袋矩形。
3. 阈值相对传送带基准的偏移量用"重建均值对齐落库 BrightMean/DarkMean"自动标定，
   保证回放结果与生产口径可比。

用法：
  python brightness_replay.py --db <barcode_data.db> --images D:\\Pictures\\jijing \
      [--n 120] [--calib 40] [--resize-scale 8] [--deadband 5] [--out 报告.md]
"""

from __future__ import annotations

import argparse
import os
import random
import re
import shutil
import sqlite3
import statistics
import tempfile

import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.spatial import ConvexHull

DEFAULT_DEADBAND = 5.0
DEFAULT_RESIZE_SCALE = 8.0
# 阈值相对传送带基准的偏移，负值 = 更严格（只取更暗的像素）
TH_OFFSET_GRID = [-40, -30, -20, -10, 0, 5, 10, 15, 20, 25, 30]


# ---------------------------------------------------------------- 基础设施

def snapshot_db(db_path: str) -> tuple[str, str]:
    """复制 db + WAL + SHM 到临时目录，避免生产程序写入时读数不全。"""
    tmp = tempfile.mkdtemp(prefix="replay_db_")
    dst = os.path.join(tmp, os.path.basename(db_path))
    for s in ("", "-wal", "-shm"):
        if os.path.exists(db_path + s):
            shutil.copy2(db_path + s, dst + s)
    return tmp, dst


def resolve_image(full_name: str, images_root: str) -> str | None:
    """ImageFullName 可能带 edgeIdx 后缀（draw_123_0.jpg），实际文件是 draw_123.jpg。"""
    if not full_name:
        return None
    d = os.path.dirname(full_name)
    base = os.path.basename(full_name)
    m = re.match(r"(draw_\d+)(?:_\d+)?\.jpg", base, re.IGNORECASE)
    if not m:
        return None
    stem = m.group(1)
    for cand in (d, images_root, os.path.join(images_root, os.path.basename(d))):
        for name in (f"{stem}.jpg", base):
            p = os.path.join(cand, name)
            if os.path.isfile(p):
                return p
    return None


def box_blur(img: np.ndarray, radius: int) -> np.ndarray:
    """积分图均值滤波（用于估大尺度背景）。"""
    h, w = img.shape
    pad = np.pad(img, radius, mode="reflect")
    ii = np.pad(pad.cumsum(0).cumsum(1), ((1, 0), (1, 0)), mode="constant")
    k = 2 * radius + 1
    ys = np.arange(h); xs = np.arange(w)
    y0 = ys[:, None]; y1 = ys[:, None] + k
    x0 = xs[None, :]; x1 = xs[None, :] + k
    return (ii[y1, x1] - ii[y0, x1] - ii[y1, x0] + ii[y0, x0]) / float(k * k)


# ---------------------------------------------------------------- 图像分析

def load_roi(rec, images_root: str, scale: float):
    """载入某条记录的检测框 ROI，返回 (灰度 ROI, RGB ROI) 或 None。"""
    path = resolve_image(rec["ImageFullName"], images_root)
    if not path:
        return None
    try:
        im = Image.open(path).convert("RGB")
    except Exception:
        return None
    cx = rec["ImageX"] / scale; cy = rec["ImageY"] / scale
    hw = rec["Width"] / 2.0 / scale; hh = rec["Height"] / 2.0 / scale
    x0 = int(max(0, cx - hw)); x1 = int(min(im.width, cx + hw))
    y0 = int(max(0, cy - hh)); y1 = int(min(im.height, cy + hh))
    if x1 - x0 < 30 or y1 - y0 < 30:
        return None
    arr = np.asarray(im.crop((x0, y0, x1, y1)), dtype=np.float32)
    gray = 0.299 * arr[:, :, 0] + 0.587 * arr[:, :, 1] + 0.114 * arr[:, :, 2]
    return gray, arr


def belt_level(gray: np.ndarray) -> float:
    """传送带基准：ROI 四角小块灰度中位数（旋转框的 AABB 四角必为背景）。"""
    h, w = gray.shape
    c = max(3, min(12, min(h, w) // 12))
    patches = [gray[:c, :c], gray[:c, -c:], gray[-c:, :c], gray[-c:, -c:]]
    return float(np.median(np.concatenate([p.ravel() for p in patches])))


def overlay_pixels(gray: np.ndarray, arr: np.ndarray, belt: float) -> np.ndarray:
    """检测结果叠加线条：高饱和亮色（框/箭头/分割线）或比传送带还亮（银色封口）。"""
    mx = arr.max(axis=2); mn = arr.min(axis=2)
    return (((mx > 200) & (mn < 80) & ((mx - mn) > 120)) | (gray > belt + 25))


def estimate_mask(gray: np.ndarray, arr: np.ndarray, belt: float, th: float) -> np.ndarray:
    """暗像素 → 最大连通域 → 凸包填充，近似整袋区域。"""
    overlay = overlay_pixels(gray, arr, belt)
    dark = (gray < th) & ~overlay
    if dark.sum() < 200:
        return np.zeros_like(dark)
    lab, n = ndimage.label(dark)
    if n > 1:
        sizes = ndimage.sum(dark, lab, range(1, n + 1))
        dark = lab == (int(np.argmax(sizes)) + 1)
    ys, xs = np.nonzero(dark)
    pts = np.column_stack([xs, ys]).astype(np.float64)
    if pts.shape[0] > 30000:
        idx = np.random.default_rng(0).choice(pts.shape[0], 30000, replace=False)
        pts = pts[idx]
    try:
        hull = ConvexHull(pts)
    except Exception:
        return dark
    yy, xx = np.mgrid[0:gray.shape[0], 0:gray.shape[1]]
    flat = np.column_stack([xx.ravel(), yy.ravel()]).astype(np.float64)
    inside = np.ones(flat.shape[0], dtype=bool)
    for a, b, c in hull.equations[:, :3]:
        inside &= (flat[:, 0] * a + flat[:, 1] * b + c) <= 1e-6
    mask = inside.reshape(gray.shape) & ~overlay
    return mask


def axis_and_split(gray: np.ndarray, mask: np.ndarray):
    """PCA 求长轴 u，返回沿轴归一化位置 s ∈ [0,1]（与生产 minAreaRect 宽度轴等价）。"""
    ys, xs = np.nonzero(mask)
    if xs.size < 200:
        return None
    pts = np.column_stack([xs, ys]).astype(np.float64)
    center = pts.mean(axis=0)
    vals, vecs = np.linalg.eigh(np.cov((pts - center).T))
    u = vecs[:, int(np.argmax(vals))]
    t = (xs - center[0]) * u[0] + (ys - center[1]) * u[1]
    return (t - t.min()) / max(t.max() - t.min(), 1e-9)


def half_diff(v: np.ndarray, s: np.ndarray) -> float:
    lo, hi = v[s < 0.5], v[s >= 0.5]
    if lo.size < 60 or hi.size < 60:
        return float("nan")
    return float(hi.mean() - lo.mean())


def band_diff(v: np.ndarray, s: np.ndarray, band: float) -> float:
    lo, hi = v[s < band], v[s >= 1.0 - band]
    if lo.size < 60 or hi.size < 60:
        return float("nan")
    return float(hi.mean() - lo.mean())


# ---------------------------------------------------------------- 主流程

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--db", required=True)
    ap.add_argument("--images", required=True)
    ap.add_argument("--n", type=int, default=120)
    ap.add_argument("--calib", type=int, default=40, help="用于标定阈值的样本数")
    ap.add_argument("--resize-scale", type=float, default=DEFAULT_RESIZE_SCALE)
    ap.add_argument("--deadband", type=float, default=DEFAULT_DEADBAND)
    ap.add_argument("--seed", type=int, default=20260911)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    random.seed(args.seed)
    np.random.seed(args.seed)
    tmp, snap = snapshot_db(args.db)
    out_lines: list[str] = []

    def emit(t: str = "") -> None:
        print(t); out_lines.append(t)

    try:
        con = sqlite3.connect(f"file:{snap}?mode=ro", uri=True)
        con.row_factory = sqlite3.Row
        rows = list(con.execute(
            "SELECT Id, ImageX, ImageY, Width, Height, ImageFullName, BrightMean, DarkMean, "
            "BrightnessDiff FROM BarcodeData WHERE BrightnessDiff IS NOT NULL "
            "AND ImageFullName IS NOT NULL ORDER BY Id"))
        random.shuffle(rows)
        # 随机抽样（保持生产分布，不刻意挑问题样本）
        picked = rows[: args.n * 2]

        # ---------- 阈值标定 ----------
        calib_loaded = []
        for r in picked[: args.calib]:
            g = load_roi(r, args.images, args.resize_scale)
            if g:
                calib_loaded.append((r, g[0], g[1]))
        best_offset, best_err = None, float("inf")
        calib_table = []
        for off in TH_OFFSET_GRID:
            errs = []
            for r, gray, arr in calib_loaded:
                belt = belt_level(gray)
                mask = estimate_mask(gray, arr, belt, belt + off)
                s = axis_and_split(gray, mask)
                if s is None:
                    continue
                v = gray[mask]
                mlo, mhi = v[s < 0.5].mean(), v[s >= 0.5].mean()
                rb, rd = r["BrightMean"], r["DarkMean"]
                errs.append(min(max(abs(mhi - rb), abs(mlo - rd)),
                                max(abs(mhi - rd), abs(mlo - rb))))
            if errs:
                e = statistics.median(errs)
                calib_table.append((off, e))
                if e < best_err:
                    best_err, best_offset = e, off
        if best_offset is None:
            best_offset = -35
            best_err = float("nan")

        # ---------- 回放 ----------
        recs, skipped = [], 0
        for r in picked:
            g = load_roi(r, args.images, args.resize_scale)
            if not g:
                skipped += 1; continue
            gray, arr = g
            belt = belt_level(gray)
            mask = estimate_mask(gray, arr, belt, belt + best_offset)
            s = axis_and_split(gray, mask)
            if s is None:
                skipped += 1; continue
            recs.append(dict(Id=r["Id"], gray=gray, arr=arr, mask=mask, s=s,
                             rec_b=r["BrightMean"], rec_d=r["DarkMean"],
                             rec_diff=abs(r["BrightnessDiff"])))

        errs = []
        for x in recs:
            v = x["gray"][x["mask"]]; s = x["s"]
            mlo, mhi = v[s < 0.5].mean(), v[s >= 0.5].mean()
            x["e1"] = min(max(abs(mhi - x["rec_b"]), abs(mlo - x["rec_d"])),
                          max(abs(mhi - x["rec_d"]), abs(mlo - x["rec_b"])))
            errs.append(x["e1"])

        emit("# 灰度判向方案回放验证报告")
        emit()
        emit(f"- 数据源：`{args.db}`")
        emit(f"- 抽样回放：{len(recs)} 条（跳过 {skipped} 条），死区阈值 `{args.deadband:g}`")
        emit(f"- 阈值标定：取相对传送带基准偏移 `{best_offset:+d}` 灰度级"
             f"（重建均值偏差中位数 {best_err:.1f} 级）")
        emit()

        # ---------- 各方案 ----------
        vr = {k: [] for k in (
            "① 基线：整半区灰度均值（当前实现）",
            "② 端点环带 30% · 灰度",
            "③ 端点环带 12% · 灰度",
            "④ 整半区 · 最佳通道（R/G/B/饱和度）",
            "⑤ 整半区 · 线性拉伸(p1-p99)",
            "⑥ 端点环带 30% · 线性拉伸",
            "⑦ 整半区 · 线性拉伸 + 最佳通道",
            "⑧ 端点环带 30% · 大尺度背景扣除",
        )}
        over = dict.fromkeys(vr, 0)
        ratios = {k: [] for k in vr}
        for x in recs:
            mask, s = x["mask"], x["s"]
            gray, arr = x["gray"], x["arr"]
            g = gray[mask]
            lo, hi = np.percentile(g, 1), np.percentile(g, 99)
            span = max(hi - lo, 1e-6)
            def stretch(c):
                return np.clip((c - lo) / span * 255.0, 0, 255)
            R, G, B = arr[:, :, 0][mask], arr[:, :, 1][mask], arr[:, :, 2][mask]
            mx = np.maximum(np.maximum(R, G), B)
            mn = np.minimum(np.minimum(R, G), B)

            def best(chans, fn):
                vals = [fn(c, s) for c in chans]
                vals = [v for v in vals if v == v]
                return max(vals, key=abs) if vals else float("nan")

            sg = stretch(gray)[mask]
            bg = box_blur(gray, max(6, int(0.15 * np.sqrt(mask.sum()))))
            vals = [
                half_diff(g, s),
                band_diff(g, s, 0.30),
                band_diff(g, s, 0.12),
                best([R, G, B, mx - mn], half_diff),
                half_diff(sg, s),
                band_diff(sg, s, 0.30),
                best([stretch(c) for c in (R, G, B)], half_diff),
                band_diff((gray - bg)[mask], s, 0.30),
            ]
            for k, v in zip(vr, vals):
                if v is None or v != v:
                    continue
                vr[k].append(abs(v))
                if abs(v) >= args.deadband:
                    over[k] += 1
                if vals[0] is not None and vals[0] == vals[0] and abs(vals[0]) > 0.3:
                    ratios[k].append(abs(v) / abs(vals[0]))

        emit("## 各方案对比（同一批图、同一产品区域、同一分割轴，|diff| 单位=灰度级）")
        emit()
        emit("| 方案 | \\|diff\\| 中位数 | p90 | 超死区占比 | 逐图增益(中位) | 增益>1 的图占比 |")
        emit("|---|---|---|---|---|---|")
        for k, v in vr.items():
            if not v:
                continue
            r = ratios[k]
            win = 100.0 * sum(1 for x in r if x > 1.0) / len(r) if r else float("nan")
            emit(f"| {k} | {statistics.median(v):.2f} | "
                 f"{sorted(v)[int(0.9 * len(v)) - 1]:.2f} | {100.0 * over[k] / len(v):.1f}% | "
                 f"{statistics.median(r) if r else float('nan'):.2f}× | {win:.0f}% |")
        emit()
        med_rec = statistics.median([x["rec_diff"] for x in recs])
        emit(f"参考：这些记录落库的 |BrightnessDiff| 中位数为 {med_rec:.2f} 灰度级；"
             f"回放基线中位数 {statistics.median(vr['① 基线：整半区灰度均值（当前实现）']):.2f}。")
        emit()
        emit("### 阈值标定曲线（偏移 → 重建均值偏差中位数）")
        emit()
        emit("| 偏移 | 偏差中位数 |")
        emit("|---|---|")
        for off, e in calib_table:
            emit(f"| {off:+d} | {e:.1f} |")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            f.write("\n".join(out_lines) + "\n")
        print(f"\n[已写出] {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
