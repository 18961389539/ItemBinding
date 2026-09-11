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
        /// X偏移值
        /// </summary>
        public float OffsetX { get; set; } = 0;
        /// <summary>
        /// y偏移值
        /// </summary>
        public float OffsetY { get; set; } = 0;
        /// <summary>
        /// 角度偏移值（度）。允许负值（建议 [-360,360)），计算层叠加后由
        /// AngleTracker.Normalize / ToRobotAngle 统一归一化到全系统规范域 (-180,180]。
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
        /// 从JSON文件加载配方
        /// </summary>
        public static Recipe? LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<Recipe>(json, s_jsonOpts);
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
