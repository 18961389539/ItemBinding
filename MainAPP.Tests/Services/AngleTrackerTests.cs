using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Services
{
    /// <summary>
    /// AngleTracker 跨帧角度锁定单元测试。
    /// 场景：输送线上同一产品连续成像，特征时有时无，角度应跨帧稳定。
    /// 匹配方式：编码器差分预测（encoder 非 0）或 世界坐标（encoder=0）。
    /// 未识别到特征且无锁定时输出未知哨兵 UnknownAngle（-9999）。
    /// </summary>
    public class AngleTrackerTests
    {
        private static AngleTracker CreateTracker() => new();

        // ---------- 位置匹配（encoder=0）场景 ----------

        [Fact]
        public void FirstFrameFail_OutputsUnknownAngle()
        {
            var tracker = CreateTracker();

            // 第 1 帧：未拍到特征，无锁定 → -9999（未知哨兵）
            var frame1 = tracker.Resolve(encoder: 0, worldX: 10, worldY: 20, modelAngle: null);
            Assert.Equal(AngleTracker.UnknownAngle, frame1);

            // 第 2 帧：拍到特征 → 锁定 270
            var frame2 = tracker.Resolve(encoder: 0, worldX: 10.5, worldY: 20.2, modelAngle: 270);
            Assert.Equal(270, frame2);

            // 第 3 帧：未拍到特征 → 沿用锁定角 270
            var frame3 = tracker.Resolve(encoder: 0, worldX: 11, worldY: 20.5, modelAngle: null);
            Assert.Equal(270, frame3);
        }

        [Fact]
        public void FirstFrameSuccess_LocksImmediately()
        {
            var tracker = CreateTracker();

            var frame1 = tracker.Resolve(encoder: 0, worldX: 0, worldY: 0, modelAngle: 123.4);
            Assert.Equal(123.4, frame1);

            var frame2 = tracker.Resolve(encoder: 0, worldX: 0.2, worldY: 0.1, modelAngle: null);
            Assert.Equal(123.4, frame2);
        }

        [Fact]
        public void PositionBeyondThreshold_TreatedAsNewProduct()
        {
            var tracker = CreateTracker();

            tracker.Resolve(encoder: 0, worldX: 100, worldY: 100, modelAngle: 100);
            // 位置超阈值 → 新产品，无特征 → 未知哨兵
            var other = tracker.Resolve(encoder: 0, worldX: 200, worldY: 100, modelAngle: null);
            Assert.Equal(AngleTracker.UnknownAngle, other);
        }

        [Fact]
        public void LockedAngle_UpdatesOnNewSuccess()
        {
            var tracker = CreateTracker();

            tracker.Resolve(encoder: 0, worldX: 5, worldY: 5, modelAngle: 90);
            tracker.Resolve(encoder: 0, worldX: 5.3, worldY: 5.2, modelAngle: 180);
            var later = tracker.Resolve(encoder: 0, worldX: 5.6, worldY: 5.4, modelAngle: null);
            Assert.Equal(180, later);
        }

        [Fact]
        public void Angle_IsNormalizedToRobotDomain_Neg180To180()
        {
            var tracker = CreateTracker();

            // 2026-09-05: 规范域 [0,360) → (-180,180]（机器人 RZ 发送域）
            var a = tracker.Resolve(encoder: 0, worldX: 0, worldY: 0, modelAngle: 370);
            Assert.Equal(10, a, 3);

            var b = tracker.Resolve(encoder: 0, worldX: 1, worldY: 0, modelAngle: -30);
            Assert.Equal(-30, b, 3);

            // 越界负输入 → 归一化到 (-180,180]（-190 → 170）
            var c = tracker.Resolve(encoder: 0, worldX: 2, worldY: 0, modelAngle: -190);
            Assert.Equal(170, c, 3);

            // 边界：181 → -179（旧域 181 保留，新域应跨 180 取反）
            var d = tracker.Resolve(encoder: 0, worldX: 3, worldY: 0, modelAngle: 181);
            Assert.Equal(-179, d, 3);
        }

        // ---------- 编码器差分匹配场景 ----------

        [Fact]
        public void EncoderMode_FirstFrameFail_SecondFrameLock_ThirdFrameFail_UsesLockedAngle()
        {
            var tracker = CreateTracker();

            // 第 1 帧：编码器 1000，未拍到特征 → 未知哨兵
            var frame1 = tracker.Resolve(encoder: 1000, worldX: 10, worldY: 20, modelAngle: null);
            Assert.Equal(AngleTracker.UnknownAngle, frame1);

            // 第 2 帧：编码器 1030（帧间位移 30 counts），拍到特征 → 锁定 270
            var frame2 = tracker.Resolve(encoder: 1030, worldX: 10.5, worldY: 20.2, modelAngle: 270);
            Assert.Equal(270, frame2);

            // 第 3 帧：编码器 1060，未拍到特征 → 沿用锁定角 270
            var frame3 = tracker.Resolve(encoder: 1060, worldX: 11, worldY: 20.5, modelAngle: null);
            Assert.Equal(270, frame3);
        }

        [Fact]
        public void EncoderMode_EncoderWrapsAround_StillMatches()
        {
            var tracker = CreateTracker();

            // 首帧在回绕边界前
            tracker.Resolve(encoder: uint.MaxValue - 5, worldX: 0, worldY: 0, modelAngle: 30);
            // 第二帧编码器回绕到 10：无符号前进距离 = 15 counts，应匹配同一产品
            var frame2 = tracker.Resolve(encoder: 10, worldX: 0.5, worldY: 0.2, modelAngle: null);
            Assert.Equal(30, frame2);
        }

        [Fact]
        public void EncoderMode_DifferentProduct_ByLargeEncoderGap_NotMatched()
        {
            var tracker = CreateTracker();

            tracker.Resolve(encoder: 1000, worldX: 0, worldY: 0, modelAngle: 45);
            // 编码器跳变 10000（远超 Δ̂ 容差）→ 视为新产品，无特征 → 未知哨兵
            var other = tracker.Resolve(encoder: 11000, worldX: 50, worldY: 50, modelAngle: null);
            Assert.Equal(AngleTracker.UnknownAngle, other);
        }
    }
}
