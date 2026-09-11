# -*- coding: utf-8 -*-
"""
灰度判向数据诊断脚本
====================
用途：分析生产库 BarcodeData 表中 BrightMean / DarkMean / BrightnessDiff 三列的分布，
     判断"两侧灰度接近"的根因属于下列哪一种，从而决定投入方向：

     A. 过曝（两端都接近饱和）      -> 先调曝光
     B. 特征弱（差值整体偏小）      -> 走通道选择 / 对比度增强 / 光照
     C. 度量钝（差值小但符号一致）  -> 改判据（端点环带采样 / 亮度重心）即可救
     D. 噪声主导（符号本身乱跳）    -> 改算法白费，必须回到光照/硬件

用法：
    python brightness_diagnose.py --db <barcode_data.db 路径> [--deadband 5.0] [--out 报告.md]

说明：
    会自动把 .db / -wal / -shm 三个文件一并快照到临时目录后再打开，
    避免生产程序正在写入（WAL 未合并）时读数不全或触发锁冲突。
"""

from __future__ import annotations

import argparse
import os
import shutil
import sqlite3
import statistics
import sys
import tempfile
from collections import defaultdict

# 全系统灰度判向死区默认值（AlgorithmSettings.BrightnessDirectionDeadband）
DEFAULT_DEADBAND = 5.0
# 过曝判定阈值：半区平均灰度高于此值即认为该侧已接近饱和，差值被压平
OVEREXPOSURE_MEAN = 200.0
# 参与"符号一致性"统计所需的最少同条码样本数
MIN_GROUP_SIZE = 5


def snapshot_db(db_path: str) -> tuple[str, str]:
    """把 db + WAL + SHM 一并复制到临时目录，返回 (临时目录, 临时 db 路径)。"""
    tmp_dir = tempfile.mkdtemp(prefix="brightness_diag_")
    dst = os.path.join(tmp_dir, os.path.basename(db_path))
    for suffix in ("", "-wal", "-shm"):
        src = db_path + suffix
        if os.path.exists(src):
            shutil.copy2(src, dst + suffix)
    return tmp_dir, dst


def connect_readonly(db_path: str) -> sqlite3.Connection:
    """只读打开快照库（快照是私有副本，即使写锁也不影响原库）。"""
    conn = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
    conn.row_factory = sqlite3.Row
    return conn


def table_columns(conn: sqlite3.Connection, table: str) -> list[str]:
    return [r["name"] for r in conn.execute(f"PRAGMA table_info({table})")]


def percentile(sorted_values: list[float], q: float) -> float:
    """线性插值分位数，q 取 0~100。"""
    if not sorted_values:
        return float("nan")
    if len(sorted_values) == 1:
        return sorted_values[0]
    pos = (len(sorted_values) - 1) * q / 100.0
    lo = int(pos)
    hi = min(lo + 1, len(sorted_values) - 1)
    frac = pos - lo
    return sorted_values[lo] * (1 - frac) + sorted_values[hi] * frac


def histogram(values: list[float], edges: list[float]) -> list[tuple[str, int]]:
    """按给定边界分箱，返回 [(区间标签, 计数)]。"""
    out = []
    for i in range(len(edges) - 1):
        lo, hi = edges[i], edges[i + 1]
        cnt = sum(1 for v in values if lo <= v < hi)
        out.append((f"[{lo:g}, {hi:g})", cnt))
    tail = sum(1 for v in values if v >= edges[-1])
    out.append((f">= {edges[-1]:g}", tail))
    return out


def main() -> int:
    parser = argparse.ArgumentParser(description="灰度判向数据诊断")
    parser.add_argument("--db", required=True, help="barcode_data.db 路径")
    parser.add_argument("--deadband", type=float, default=DEFAULT_DEADBAND, help="死区阈值（默认 5.0）")
    parser.add_argument("--out", default=None, help="Markdown 报告输出路径")
    args = parser.parse_args()

    if not os.path.exists(args.db):
        print(f"[错误] 找不到数据库文件：{args.db}", file=sys.stderr)
        return 2

    tmp_dir, snap = snapshot_db(args.db)
    lines: list[str] = []

    def emit(text: str = "") -> None:
        print(text)
        lines.append(text)

    try:
        conn = connect_readonly(snap)
        cols = table_columns(conn, "BarcodeData")
        emit(f"# 灰度判向数据诊断报告")
        emit()
        emit(f"- 数据源：`{args.db}`")
        emit(f"- 死区阈值：`{args.deadband:g}`")
        emit()

        required = {"BrightMean", "DarkMean", "BrightnessDiff"}
        missing = required - set(cols)
        if missing:
            emit(f"**库中没有灰度判向列：{sorted(missing)}**")
            emit()
            emit("说明该库是 2026-09-08 之前产生的（当时还未落库判向统计），"
                 "需要用新版程序跑一段生产数据后再诊断。")
            return 1

        total = conn.execute("SELECT COUNT(*) FROM BarcodeData").fetchone()[0]
        judged = conn.execute(
            "SELECT COUNT(*) FROM BarcodeData WHERE BrightnessDiff IS NOT NULL"
        ).fetchone()[0]
        emit("## 1. 数据概览")
        emit()
        emit(f"- 总记录数：{total}")
        emit(f"- 含判向统计的记录数：{judged}（{100.0 * judged / total:.1f}%）" if total else "- 空库")
        if judged == 0:
            emit()
            emit("没有一条记录带判向统计，无法诊断。常见原因：角度检测模型已启用（走模型路径，不做判向），"
                 "或灰度判向总开关关闭。")
            return 1

        rows = conn.execute(
            "SELECT Barcode, BrightMean, DarkMean, BrightnessDiff, Angle "
            "FROM BarcodeData WHERE BrightnessDiff IS NOT NULL"
        ).fetchall()
        emit()

        diffs = [r["BrightnessDiff"] for r in rows]
        brights = [r["BrightMean"] for r in rows]
        darks = [r["DarkMean"] for r in rows]
        levels = [(b + d) / 2.0 for b, d in zip(brights, darks)]

        # ---------- 2. 灰度水平 / 过曝 ----------
        emit("## 2. 灰度水平（过曝检查）")
        emit()
        over = sum(1 for v in levels if v > OVEREXPOSURE_MEAN)
        emit(f"- 两侧平均灰度的均值：{statistics.fmean(levels):.1f} / 255")
        emit(f"- 半区平均灰度 > {OVEREXPOSURE_MEAN:g} 的样本：{over}（{100.0 * over / len(levels):.1f}%）")
        emit(f"- 两侧中较暗一侧的平均：{statistics.fmean(darks):.1f}")
        emit(f"- 两侧中较亮一侧的平均：{statistics.fmean(brights):.1f}")
        emit()
        if over / len(levels) > 0.3:
            emit("**判读：过曝比例偏高。两端都逼近饱和时，灰度差会被物理压平——"
                 "先降曝光/增益，比改算法收益大。**")
        else:
            emit("判读：曝光水平正常，不是过曝导致的差值小。")
        emit()

        # ---------- 3. 差值分布 ----------
        emit("## 3. |BrightnessDiff| 分布")
        emit()
        absd = sorted(abs(v) for v in diffs)
        emit("| 分位 | \\|diff\\| |")
        emit("|---|---|")
        for q in (1, 5, 10, 25, 50, 75, 90, 95, 99):
            emit(f"| p{q} | {percentile(absd, q):.1f} |")
        emit()
        below = sum(1 for v in absd if v < args.deadband)
        emit(f"- 落在死区内的样本：{below} / {len(absd)}（**{100.0 * below / len(absd):.1f}%** 判为不可判）")
        emit(f"- 中位数：{percentile(absd, 50):.1f}")
        emit()
        edges = [0, 1, 2, 3, 5, 8, 12, 20, 35, 60]
        emit("| 区间 | 样本数 | 占比 |")
        emit("|---|---|---|")
        for label, cnt in histogram(absd, edges):
            emit(f"| {label} | {cnt} | {100.0 * cnt / len(absd):.1f}% |")
        emit()

        # ---------- 4. 同产品跨帧符号一致性 ----------
        emit("## 4. 同产品跨帧符号一致性（关键判据）")
        emit()
        emit("> 说明：按条码分组，统计同一产品所有帧 |diff| 的符号是否一致。")
        emit("> 符号高度一致 = 特征真的存在，只是度量钝，改算法可救；")
        emit("> 符号乱跳 = 差值里主要是噪声，改算法白费。")
        emit()
        groups: dict[str, list[float]] = defaultdict(list)
        for r in rows:
            bc = (r["Barcode"] or "").strip()
            if bc and bc.lower() != "noread":
                groups[bc].append(r["BrightnessDiff"])

        eligible = {k: v for k, v in groups.items() if len(v) >= MIN_GROUP_SIZE}
        emit(f"- 有条码的产品组数：{len(groups)}，其中样本数 >= {MIN_GROUP_SIZE} 的：{len(eligible)}")
        emit()
        if not eligible:
            emit("样本数不足，无法评估跨帧一致性（条码大多为 noread，或同一条码重复帧太少）。")
        else:
            consistent = 0
            ratios: list[float] = []
            for v in eligible.values():
                pos = sum(1 for x in v if x > 0)
                neg = sum(1 for x in v if x < 0)
                nonzero = pos + neg
                if nonzero == 0:
                    continue
                ratio = max(pos, neg) / nonzero
                ratios.append(ratio)
                if ratio >= 0.9:
                    consistent += 1
            emit(f"- 同产品符号一致率 >= 90% 的组：{consistent} / {len(ratios)}"
                 f"（{100.0 * consistent / len(ratios):.1f}%）" if ratios else "- 全部样本 diff 恒为 0")
            if ratios:
                ratios.sort()
                emit(f"- 符号一致率中位数：{percentile(ratios, 50):.3f}")
                emit()
                emit("| 符号一致率 | 组数 |")
                emit("|---|---|")
                for label, cnt in histogram(ratios, [0, 0.6, 0.7, 0.8, 0.9, 0.95, 1.0001]):
                    emit(f"| {label} | {cnt} |")
                emit()
                med = percentile(ratios, 50)
                if med >= 0.9:
                    emit("**判读：符号一致性高。特征稳定存在，问题出在度量被稀释 —— "
                         "改端點环带采样 / 亮度重心最直接。**")
                elif med >= 0.7:
                    emit("**判读：符号一致性中等。特征存在但信噪比不足，"
                         "建议先做通道选择 + 对比度增强，再改度量。**")
                else:
                    emit("**判读：符号一致性低。差值里噪声占主导，"
                         "改度量/算法都难救 —— 必须回到光照、通道或换判据（几何/角度模型）。**")
        emit()

        # ---------- 5. 角度稳定性交叉验证 ----------
        emit("## 5. 角度稳定性（交叉验证）")
        emit()
        spread_rows = []
        for bc, v in groups.items():
            if len(v) < MIN_GROUP_SIZE:
                continue
            angles = [r["Angle"] for r in rows if (r["Barcode"] or "").strip() == bc]
            angles = [a for a in angles if a is not None and a > -1000]
            if len(angles) < MIN_GROUP_SIZE:
                continue
            # 角度环绕处理：只取偏差在 ±90 内的主簇，避免 180 翻转污染统计
            base = angles[0]
            cluster = []
            for a in angles:
                d = (a - base + 180.0) % 360.0 - 180.0
                if abs(d) <= 90.0:
                    cluster.append(d)
            if len(cluster) >= MIN_GROUP_SIZE:
                spread_rows.append(statistics.pstdev(cluster))
        if spread_rows:
            spread_rows.sort()
            emit(f"- 同产品角度标准差（组内，共 {len(spread_rows)} 组）："
                 f"中位数 {percentile(spread_rows, 50):.2f}°，p90 {percentile(spread_rows, 90):.2f}°")
            hard = sum(1 for v in spread_rows if v > 5.0)
            emit(f"- 角度标准差 > 5° 的组：{hard}（{100.0 * hard / len(spread_rows):.1f}%）")
            emit()
            if hard / len(spread_rows) > 0.2:
                emit("判读：存在明显角度抖动，与差值偏小相互印证——方向判定不稳。")
            else:
                emit("判读：角度总体稳定。")
        else:
            emit("样本不足，跳过。")
        emit()

        # ---------- 6. 结论 ----------
        emit("## 6. 结论与建议投入")
        emit()
        med_diff = percentile(absd, 50)
        med_cons = percentile(sorted(ratios), 50) if eligible and ratios else float("nan")
        over_ratio = over / len(levels)
        if over_ratio > 0.3:
            verdict = "**过曝主导** → 先降曝光/增益，再复测本报告"
        elif med_diff >= args.deadband * 2 and (med_cons != med_cons or med_cons >= 0.9):
            verdict = "**特征可信、度量钝** → 端点环带采样 / 亮度重心法"
        elif med_cons == med_cons and med_cons < 0.7:
            verdict = "**噪声主导** → 光照 + 通道选择 + 背景扣除，或换几何/模型判据"
        else:
            verdict = "**特征偏弱但可分辨** → 最佳通道 + 对比度增强，叠加端点环带采样"
        emit(f"诊断结论：{verdict}")
        emit()
        emit(f"（关键指标：|diff| 中位数 = {med_diff:.1f}，死区 = {args.deadband:g}，"
             f"符号一致率中位数 = {med_cons:.3f}，过曝占比 = {100.0 * over_ratio:.1f}%）")

    finally:
        shutil.rmtree(tmp_dir, ignore_errors=True)

    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
        print(f"\n[已写出] {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
