using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace MainAPP.Models
{
    /// <summary>
    /// 条码检测记录的数据库模型。
    /// 存储每次推理检测的完整结果，包括条码信息、世界坐标、图像坐标、
    /// 检测框尺寸、角度、置信度、耗时等，用于历史查询和数据分析。
    /// </summary>
    public class DbModel
    {
        /// <summary>
        /// 主键，自增ID
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// 条码内容（二维码/条形码的解码字符串）
        /// </summary>
        public string Barcode { get; set; } = string.Empty;

        /// <summary>
        /// 编码器值，来自VGT/ABB机器人的编码器反馈，表示传送带位置
        /// </summary>
        public uint Encode { get; set; }

        /// <summary>
        /// 产品中心在世界坐标系中的X坐标（物理坐标，单位mm）
        /// </summary>
        public double WorldX { get; set; }

        /// <summary>
        /// 产品中心在世界坐标系中的Y坐标（物理坐标，单位mm）
        /// </summary>
        public double WorldY { get; set; }

        /// <summary>
        /// 产品中心在图像坐标系中的X坐标（像素）
        /// </summary>
        public double ImageX { get; set; }

        /// <summary>
        /// 产品中心在图像坐标系中的Y坐标（像素）
        /// </summary>
        public double ImageY { get; set; }

        /// <summary>
        /// 产品姿态角度（度）。2026-09-05 起全系统规范域 = 机器人发送域 (-180,180]：
        /// 由 AngleTracker 归一化后落库（角度模型成功/掩码回退/跨帧锁定沿用均此域），
        /// 未知时存储哨兵 -9999（<see cref="AngleTracker.UnknownAngle"/>）。
        /// </summary>
        public double Angle { get; set; }

        /// <summary>
        /// 检测框轴对齐外接矩形面积（像素²），Width × Height。
        /// 注意：对旋转或非矩形目标会高估实际面积，真实掩码面积应通过 MaskArea 获取。
        /// </summary>
        public double Area { get; set; }

        /// <summary>
        /// 检测框宽度（像素）
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// 检测框高度（像素）
        /// </summary>
        public double Height { get; set; }

        /// <summary>
        /// 本次检测的总耗时（毫秒），从图像加载到推理完成
        /// </summary>
        public int CostTime { get; set; }

        /// <summary>
        /// 检测时间戳（本地时间），默认为当前本地时间
        /// M329b: 默认值在构造时求值（DateTime.Now），若对象创建后未赋值即入库，
        /// 时间可能与实际检测时间有偏差。暂不改为 nullable DateTime?（改动大，涉及序列化与数据库映射）。
        /// </summary>
        public DateTime DetectTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 编码器时间戳（本地时间），编码器值对应的接收时间
        /// M329b: 同 DetectTime，默认值在构造时求值，暂不改为 nullable。
        /// </summary>
        public DateTime EncodeTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 检测记录组装时间戳（本地时间）。
        /// 注意：当前实现与 <see cref="DetectTime"/> 完全相同（均为 DateTime.Now），
        /// 并非扫码枪真实采集图像时间。如需真实采集时间，应从 FrameResult/SDK 时间戳获取。
        /// M329b: 默认值在构造时求值，暂不改为 nullable。
        /// </summary>
        public DateTime ImageReceivedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 条码中心在图像坐标系中的X坐标（像素）
        /// </summary>
        public double ImageBarcodeX { get; set; }

        /// <summary>
        /// 条码中心在图像坐标系中的Y坐标（像素）
        /// </summary>
        public double ImageBarcodeY { get; set; }

        /// <summary>
        /// 条码识别评分（扫码枪原始 IDRScore 整数值，通常范围 0~100，非 [0,1] 归一化值）。
        /// 由 <see cref="HikBarcodeResultExtensions.Confidence(HikBarcodeResult)"/> 直接转换，
        /// 未做 /100 归一化，下游消费方不应按 0~1 解释。
        /// </summary>
        public double BarcodeScore { get; set; }

        /// <summary>
        /// YOLO边缘检测的置信度（0~1）
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// 绘制结果图像的完整文件路径，仅在启用保存绘制图时填充
        /// </summary>
        public string ImageFullName { get; set; } = string.Empty;

        /// <summary>
        /// 两次编码器报文之间的时间间隔（毫秒），来源于 <see cref="ToVGT.Speed"/>。
        /// <para>语义说明：</para>
        /// <para>- 存储"间隔"而非"速度"：值越小表示传送带越快（间隔短=报文密集）</para>
        /// <para>- 与"速度"成反比关系，下游消费方不应按"值越大=越快"解释</para>
        /// <para>- 特殊值：<see cref="long"/>-1 表示首次接收；-10（ToVGT.SpeedUninitialized）表示未初始化</para>
        /// M335b: 默认值改为 -1，与"首次接收"语义一致（原默认值 0 会被误解为传送带静止）
        /// 注意：字段名 Speed 仅为向后兼容保留（数据库列名、CSV 导出、下游看板均依赖此名），
        /// 真实语义为 EncoderIntervalMs。
        /// </summary>
        public long Speed { get; set; } = -1;

        /// <summary>
        /// 灰度判向"正向半区"（掩码矩形宽度轴正向 +u 侧）平均灰度 0~255。
        /// 2026-09-08 起随记录落库，供现场标定/验证判向可靠性（头端明暗假设、死区阈值、方向一致性分析）。
        /// <para><b>2026-09-11 口径变更</b>：本值现为"掩码内对比度拉伸之后"的灰度
        /// （把掩码内灰度的 [p1,p99] 线性映射到 [0,255] 后再取半区均值），不再是原始绝对灰度；
        /// 拉伸关闭或窗口过窄（近单色掩码）时退化为原始绝对灰度。跨该日期比较历史数据时需注意口径差异。
        /// 绝对灰度水平可用日志中的"掩码内拉伸窗口"字段判断（如窗口 47~107 说明成像偏暗）。</para>
        /// null = 本帧未执行灰度判向（未启用开关/无灰度图/掩码退化/某侧无像素）。
        /// </summary>
        public double? BrightMean { get; set; }

        /// <summary>
        /// 灰度判向"负向半区"（宽度轴负向 −u 侧）平均灰度 0~255。null 语义与口径同 <see cref="BrightMean"/>。
        /// </summary>
        public double? DarkMean { get; set; }

        /// <summary>
        /// 两侧平均灰度差 = BrightMean − DarkMean（该不变式在拉伸前后均成立）。
        /// 落库后可直接过滤死区边缘样本（|Diff| &lt; 死区）。
        /// null 语义与口径同 <see cref="BrightMean"/>。
        /// </summary>
        public double? BrightnessDiff { get; set; }

        /// <summary>
        /// 头尾特征池判定轨迹（2026-09-13，JSON）：级联来源特征 + 各候选特征的有符号值/
        /// 死区阈值/是否 decisive。诊断用途——验证特征池在现网产品的区分度与"数值大=头"约定一致性。
        /// null = 特征池未运行（开关关闭/无图像/掩码退化）。
        /// </summary>
        public string? HeadFeatures { get; set; }

        /// <summary>
        /// 检测时使用的配方名（2026-09-12 追溯补列）。null = 历史数据或配方未知。
        /// 有了它才能回答「某个配方下的合格率/耗时」这类追溯问题。
        /// </summary>
        public string? RecipeName { get; set; }

        /// <summary>
        /// 检测结果判定（2026-09-12 追溯补列）。约定 "OK" / "NG"；null = 未判定（历史数据）。
        /// 用字符串而非枚举：SQLite 存 TEXT，便于直接 SQL 过滤与导出 CSV 后人类可读。
        /// </summary>
        public string? Result { get; set; }

        /// <summary>
        /// 工位 / 过站标识（2026-09-12 追溯补列）。单机场景填工站名；多工位场景用于把同一件产品的
        /// 各站记录串成过站链。null = 未配置工位。
        /// </summary>
        public string? Station { get; set; }
    }
}
