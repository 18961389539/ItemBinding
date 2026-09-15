using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using MainAPP.Services;

namespace MainAPP.Models
{
    public class Recipe
    {
        // L260: IncludeFields = true 必须保留：CoordinateTool 中的 Point2f 使用公共字段（非属性），
        // 移除会导致 X/Y 反序列化为默认值 0，坐标系标定数据丢失
        private static readonly JsonSerializerOptions s_jsonOpts = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        /// <summary>
        /// 配方名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 配方描述
        /// </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>
        /// 【已废弃 · 仅用于旧配方迁移】世界坐标系常量平移补偿 X（mm）。
        ///
        /// <para>2026-09-15 起<b>不再参与坐标计算</b>。该做法是世界坐标系常量、方向固定，
        /// 只在单一产品角度下成立；已由产品局部坐标系的抓取点偏移
        /// （<see cref="GrabOffsetLongMm"/> / <see cref="GrabOffsetShortMm"/>）取代。</para>
        ///
        /// <para>保留本字段只为读取旧配方文件并做一次性迁移，见 <see cref="MigrateLegacyOffsets"/>；
        /// 迁移后会被清零，新配方也不再写入有效值。</para>
        /// </summary>
        public float OffsetX { get; set; } = 0;

        /// <summary>【已废弃 · 仅用于旧配方迁移】世界坐标系常量平移补偿 Y（mm）。见 <see cref="OffsetX"/>。</summary>
        public float OffsetY { get; set; } = 0;

        /// <summary>
        /// 抓取点沿产品<b>长轴</b>的偏移（mm）。正值 = 朝产品头部。
        ///
        /// <para>产品局部坐标系，随产品角度一起旋转 —— 用于表达夹爪偏心、抓取点不在产品中心。
        /// 默认 0 表示抓取点即掩码最小外接旋转矩形的几何中心（与改造前行为一致）。</para>
        ///
        /// <para>头部方向由消歧链（二维码 → 模型翻转 → 特征池）确定；当朝向不可信时本偏移不生效
        /// （退化为中心），详见 <c>DetectionRecordService</c> 中的说明。</para>
        /// </summary>
        public float GrabOffsetLongMm { get; set; } = 0;

        /// <summary>
        /// 抓取点沿产品<b>短轴</b>的偏移（mm）。正值 = 面朝头部时的右手侧。
        /// 与 <see cref="GrabOffsetLongMm"/> 同属产品局部坐标系，头尾翻转时两轴同时反向。
        /// </summary>
        public float GrabOffsetShortMm { get; set; } = 0;

        /// <summary>
        /// 抓取点偏移是否已从旧的世界系平移补偿迁移完成（防止重复迁移覆盖现场手填值）。
        /// </summary>
        public bool GrabOffsetMigrated { get; set; } = false;

        /// <summary>
        /// 角度偏移值（度）。允许负值（建议 [-360,360)），计算层叠加后由
        /// AngleTracker.Normalize / ToRobotAngle 统一归一化到全系统规范域 (-180,180]。
        /// 注意：本项是<b>机器人安装/标定</b>的角度校正，与抓取点位置无关，保持不变。
        /// </summary>
        public float OffsetAngle { get; set; } = 0;

        /// <summary>
        /// 创建时间（本地时间）
        /// L414c: 默认值在构造时求值（DateTime.Now），反序列化旧配方文件时若 JSON 中无此字段，
        /// 将使用构造时刻而非原始创建时间。暂不改为 nullable（改动大，涉及序列化与 UI 显示兼容）。
        /// </summary>
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后修改时间（本地时间）
        /// L414c: 同 CreatedTime，默认值在构造时求值，暂不改为 nullable。
        /// </summary>
        public DateTime ModifiedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 图像工具配置
        /// </summary>
        public ImageTool? ImageTool { get; set; } = new ImageTool();

        /// <summary>
        /// YOLO检测工具配置
        /// </summary>
        public YoloTools? YoloTool { get; set; } = new YoloTools();

        /// <summary>
        /// 坐标系工具配置
        /// </summary>
        public CoordinateTool? CoordinateTool { get; set; } = new CoordinateTool();

        /// <summary>
        /// 创建默认配方
        /// </summary>
        public static Recipe CreateDefault()
        {
            return new Recipe
            {
                Name = "默认配方",
                Description = string.Empty,
                ImageTool = new ImageTool(),
                YoloTool = new YoloTools(),
                CoordinateTool = new CoordinateTool()
            };
        }

        /// <summary>
        /// 把旧配方里"世界坐标系常量平移补偿"（<see cref="OffsetX"/> / <see cref="OffsetY"/>）
        /// 一次性迁移到产品局部坐标系的抓取点偏移（<see cref="GrabOffsetLongMm"/> / <see cref="GrabOffsetShortMm"/>）。
        ///
        /// <para><b>为什么必须迁移</b>：2026-09-15 移除了世界系常量叠加，若不迁移，现场已填过补偿值的配方
        /// 会突然失去补偿，产品坐标整体偏移，属于产线事故。</para>
        ///
        /// <para><b>映射依据</b>：世界系补偿在标定参考系下（产品长轴与图像 X 轴同向）与局部系的
        /// (长轴, 短轴) 一一对应，故直接取 (OffsetX, OffsetY) → (长轴, 短轴)。
        /// 两者只在<b>产品角度恒定</b>时严格等价；若现场角度变化较大，迁移后应按实际抓取效果重新核对。</para>
        ///
        /// <para>幂等：迁移后清零旧字段并置 <see cref="GrabOffsetMigrated"/>，不会覆盖现场后来手填的值。</para>
        /// </summary>
        /// <returns>是否发生了迁移。</returns>
        public bool MigrateLegacyOffsets()
        {
            if (GrabOffsetMigrated || (OffsetX == 0 && OffsetY == 0))
            {
                return false;
            }

            var legacyX = OffsetX;
            var legacyY = OffsetY;

            GrabOffsetLongMm = legacyX;
            GrabOffsetShortMm = legacyY;
            OffsetX = 0;
            OffsetY = 0;
            GrabOffsetMigrated = true;

            LogService.Instance.Warning(
                $"[配方迁移]「{Name}」的世界系平移补偿 OffsetX={legacyX:F2} / OffsetY={legacyY:F2} mm " +
                $"已迁移为产品局部系抓取点偏移（长轴 {GrabOffsetLongMm:F2} mm / 短轴 {GrabOffsetShortMm:F2} mm）。" +
                "该做法原先不随产品角度旋转，现改为随产品旋转；若产品角度变化较大，请按实际抓取效果重新核对。");

            return true;
        }

        /// <summary>
        /// 从JSON文件加载配方
        /// </summary>
        public static Recipe? LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                var recipe = JsonSerializer.Deserialize<Recipe>(json, s_jsonOpts);

                // 2026-09-15: 旧配方的"世界系常量平移补偿"一次性迁移为产品局部系抓取点偏移。
                // 放在唯一的反序列化入口，保证所有加载路径都覆盖到。
                recipe?.MigrateLegacyOffsets();

                return recipe;
            }
            catch (Exception ex)
            {
                // M126: 使用 LogService 输出到日志系统，同时显示在主页 UI
                LogService.Instance.Warning($"加载配方失败: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 保存配方到JSON文件
        /// </summary>
        public void SaveToFile(string filePath)
        {
            try
            {
                ModifiedTime = DateTime.Now;
                var json = JsonSerializer.Serialize(this, s_jsonOpts);
                // M30/L33: 原子写入 + ModifiedTime 改为本地时间（与 DbModel 策略一致）
                var tmpPath = filePath + ".tmp";
                File.WriteAllText(tmpPath, json);
                if (File.Exists(filePath))
                    File.Replace(tmpPath, filePath, destinationBackupFileName: null);
                else
                    File.Move(tmpPath, filePath);
            }
            catch (Exception ex)
            {
                // L267: catch 中清理可能残留的 .tmp 文件
                var tmpPath = filePath + ".tmp";
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
                catch (Exception cleanEx) { LogService.Instance.Warning($"清理临时文件失败: {cleanEx}"); }
                // M126: 使用 LogService 输出到日志系统，同时显示在主页 UI
                LogService.Instance.Warning($"保存配方失败: {ex}");
                throw; // 重新抛出，让调用方知道保存失败
            }
        }
    }





}
