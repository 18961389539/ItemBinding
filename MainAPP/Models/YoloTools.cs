using System;
using System.Text.Json.Serialization;

namespace MainAPP.Models
{
    /// <summary>
    /// YOLO 相关工具集合。
    /// 包含用于边缘分割和角度检测的模型配置。
    /// </summary>
    public class YoloTools
    {
        /// <summary>
        /// 边缘检测配置（默认已实例化）。
        /// </summary>
        public YoloTool EdgeDetection { get; set; } = new YoloTool();
        /// <summary>
        /// 角度检测配置（可为空）。
        /// L45: 从 AngelDetection 重命名为 AngleDetection（修正拼写），保留 JSON 别名兼容旧配方文件。
        /// L399a/L410b: JSON 属性名 "AngelDetection" 保留拼写错误暂不改，需要迁移策略（旧配方文件批量升级），后续统一处理。
        /// </summary>
        [JsonPropertyName("AngelDetection")]
        public YoloTool? AngleDetection { get; set; } = new YoloTool();
        /// <summary>
        /// 是否启用角度检测流程（默认为 false）。
        /// L45: 从 IsEnableAngelDetection 重命名为 IsAngleDetectionEnabled（修正拼写+命名规范），保留 JSON 别名兼容旧配方文件。
        /// L399a/L410b: JSON 属性名 "IsEnableAngelDetection" 保留拼写错误暂不改，需要迁移策略（旧配方文件批量升级），后续统一处理。
        /// </summary>
        [JsonPropertyName("IsEnableAngelDetection")]
        public bool IsAngleDetectionEnabled { get; set; } = false;

        /// <summary>
        /// 无角度模型时的"灰度判向"总开关（配方级覆盖，仅在未启用角度检测时生效）。
        /// <para>null = 未显式设置，跟随全局设置 <c>Settings.Algorithm.BrightnessDirectionEnabled</c>
        /// （兼容旧配方文件：新字段缺失自动为 null，行为不变）；</para>
        /// <para>true / false = 本配方强制启用 / 禁用。</para>
        /// <para>2026-09-08：启用后对分割掩码最小外接矩形主轴（长轴）两侧平均灰度做头尾判定，
        /// 消除回退角度 180° 方向歧义（头端偏亮约定与死区阈值仍取全局 AlgorithmSettings 对应项）。
        /// 与 <see cref="IsAngleDetectionEnabled"/> 互斥：角度检测启用时走角度模型路径，本开关不参与。</para>
        /// </summary>
        public bool? IsBrightnessDirectionEnabled { get; set; }
    }


    public class YoloTool
    {
        /// <summary>
        /// 是否启用图像缩放（缩放后再推理，仅影响推理输入与结果坐标还原）。
        /// </summary>
        public bool IsResize { get; set; } = true;

        /// <summary>
        /// 图像宽度方向的缩放比例（除数，原图宽度 / ResizeScale = 推理图宽度）。
        /// <para>语义说明：</para>
        /// <para>- 用作除数：<c>scanerResult.Width / ResizeScale</c> 得到推理图宽度</para>
        /// <para>- 用作乘数（还原坐标）：<c>edgeResult.Bounds.X * ResizeScale</c> 得到原图坐标</para>
        /// <para>- 默认值 4：5120 像素宽的原图缩放到 1280 像素推理</para>
        /// <para>JSON 别名 "ResizeWidth" 保留以兼容旧配方文件（旧名暗示"目标宽度"，实际语义为缩放比例）</para>
        /// </summary>
        [JsonPropertyName("ResizeWidth")]
        public int ResizeScale { get; set; } = 4;

        /// <summary>
        /// 图像高度方向的缩放比例（除数，原图高度 / ResizeScaleY = 推理图高度）。
        /// <para>语义同 <see cref="ResizeScale"/>，应用于 Y 方向。</para>
        /// <para>JSON 别名 "ResizeHeight" 保留以兼容旧配方文件。</para>
        /// </summary>
        [JsonPropertyName("ResizeHeight")]
        public int ResizeScaleY { get; set; } = 4;
        /// <summary>
        /// ONNX模型文件路径
        /// </summary>
        public string ModelPath { get; set; } = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Saves", "Models", "best.onnx");

        /// <summary>
        /// 是否使用CUDA加速
        /// </summary>
        public bool UseCuda { get; set; } = false;

        /// <summary>
        /// CUDA设备ID
        /// </summary>
        public int CudaDeviceId { get; set; } = 0;

        /// <summary>
        /// 置信度阈值
        /// </summary>
        public float Confidence { get; set; } = 0.3f;

        /// <summary>
        /// IoU阈值（非极大值抑制）
        /// </summary>
        public float IoU { get; set; } = 0.45f;

        /// <summary>
        /// 是否保持图像宽高比
        /// </summary>
        public bool KeepAspectRatio { get; set; } = true;

        /// <summary>
        /// 是否自动校正图像方向
        /// </summary>
        public bool ApplyAutoOrient { get; set; } = true;

        /// <summary>
        /// 是否禁用并行推理
        /// </summary>
        public bool SuppressParallelInference { get; set; } = false;

        /// <summary>
        /// 角度裁剪 padding 比例（相对 OBB 尺寸，四周各加）。供角度检测处理器使用（AngleDetection 配置）。
        /// </summary>
        public float CropPaddingRatio { get; set; } = 0.10f;

        /// <summary>
        /// 产品掩码填充率下限（MaskArea / 外接矩形面积），低于视为掩码残缺、跳过角度推理。供角度检测处理器使用（AngleDetection 配置）。
        /// </summary>
        public float MinMaskFillRatio { get; set; } = 0.20f;

        /// <summary>
        /// 特征掩码面积占裁剪图面积的比例下限，低于视为推理不可信。供角度检测处理器使用（AngleDetection 配置）。
        /// </summary>
        public float MinFeatureAreaRatio { get; set; } = 0.005f;

        /// <summary>
        /// 特征质心偏移比下限 = |特征质心 − 产品质心| / 产品长轴长度（原图像素口径）。
        /// 角度模型的方向由"产品质心 → 特征质心"的有向向量给出，该比值是这条向量的**方向可信度**：
        /// 比值越大方向越稳；比值趋近 0 时（特征居中/对称）atan2 的分子分母同时趋零，角度将由噪声决定。
        /// <para>低于该阈值判为"方向退化"，不再采信模型角度，改用掩码主轴角 + 灰度判向兜底
        /// （与未启用角度模型时同口径的路径），避免把噪声角度当真值发出。默认 0.03。</para>
        /// <para><b>默认值怎么来的</b>：角度噪声 σ_angle ≈ σ_质心 / 力臂，力臂 = 偏移比 × 长轴长度。
        /// 本项目产品长轴约 1500 原始像素、特征掩码质心精度按 σ_质心 ≈ 0.5px 估：
        /// 0.03 对应力臂 ≈ 45px → σ_angle ≈ 0.5/45 rad ≈ 0.6°，与 2° 的角度匹配阈值相称。
        /// <b>换产品 / 换分辨率后应按目标角度精度反算</b>：力臂 ≥ σ_质心 / 目标角噪声（弧度），
        /// 再除以长轴长度得到本比值。</para>
        /// <para>2026-09-11 新增：此前三道质量门（OBB 有效 / 掩码填充率 / 特征面积占比）都不看两质心距离，
        /// 方向退化帧会静默通过并输出由噪声决定的角度。</para>
        /// </summary>
        public float MinCentroidOffsetRatio { get; set; } = 0.03f;

        /// <summary>
        /// 产品（分割掩码）面积下限（原图像素，配方级覆盖，仅 EdgeDetection 主链生效）。
        /// <para>null = 未设置，回退使用全局设置 <c>Settings.Algorithm.MinMaskAreaPixels</c>；</para>
        /// <para>0 = 显式禁用下限；>0 = 掩码面积低于该值的目标过滤（不落库/不发送/不计数）。</para>
        /// <para>2026-09-07：配合主页表格"掩码面积"列标定，不同型号产品可各设一套阈值。</para>
        /// </summary>
        public double? MinMaskAreaPixels { get; set; }

        /// <summary>
        /// 产品（分割掩码）面积上限（原图像素，配方级覆盖，仅 EdgeDetection 主链生效）。
        /// <para>null = 未设置，回退使用全局设置 <c>Settings.Algorithm.MaxMaskAreaPixels</c>；</para>
        /// <para>0 = 显式禁用上限；>0 = 掩码面积高于该值的目标过滤。</para>
        /// </summary>
        public double? MaxMaskAreaPixels { get; set; }
    }
}
