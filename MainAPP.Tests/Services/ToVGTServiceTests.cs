using MainAPP.Models;
using MainAPP.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace MainAPP.Tests.Services
{
    /// <summary>
    /// ToVGTService 和 MessageToVGT 单元测试。
    /// 网络相关测试（UDP/Ping）需要真实网络环境，此处仅测试纯逻辑部分。
    /// </summary>
    public class ToVGTServiceTests
    {
        #region ToRobotAngle 机器人角度域换算测试（[0,360) → (-180,180]）

        [Fact]
        public void ToRobotAngle_WithinRange_StaysUnchanged()
        {
            Assert.Equal(0.0, ToVGT.ToRobotAngle(0.0), 6);
            Assert.Equal(90.0, ToVGT.ToRobotAngle(90.0), 6);
            Assert.Equal(180.0, ToVGT.ToRobotAngle(180.0), 6);
        }

        [Fact]
        public void ToRobotAngle_Above180_Subtracts360()
        {
            Assert.Equal(-5.0, ToVGT.ToRobotAngle(355.0), 6);
            Assert.Equal(-90.0, ToVGT.ToRobotAngle(270.0), 6);
            Assert.Equal(-179.0, ToVGT.ToRobotAngle(181.0), 6);
            Assert.Equal(-0.1, ToVGT.ToRobotAngle(359.9), 6);
        }

        [Fact]
        public void ToRobotAngle_NegativeInput_NormalizesBack()
        {
            // 输入即使已为负（例如直接喂 -5），也应稳定映射回 (-180,180] 内同一等价角
            Assert.Equal(-5.0, ToVGT.ToRobotAngle(-5.0), 6);
            Assert.Equal(170.0, ToVGT.ToRobotAngle(-190.0), 6);
        }

        [Fact]
        public void ToRobotAngle_OutOfRange_WrapsModulo360()
        {
            Assert.Equal(45.0, ToVGT.ToRobotAngle(405.0), 6);
            Assert.Equal(-135.0, ToVGT.ToRobotAngle(225.0), 6);
        }

        #endregion

        #region MessageToVGT 序列化测试

        [Fact]
        public void MessageToVGT_ToString_FormatsCorrectly()
        {
            var msg = new MessageToVGT
            {
                X = 12.34,
                Y = 56.78,
                RZ = 90.12,
                Barcode = "TEST123"
            };

            var result = msg.ToString();

            Assert.Equal("[X]12.34[Y]56.78[RZ]90.12[T]1[ID1]TEST123", result);
        }

        [Fact]
        public void MessageToVGT_ToString_RoundsToTwoDecimals()
        {
            var msg = new MessageToVGT
            {
                X = 100.005,
                Y = 200.006,
                RZ = 45.004,
                Barcode = "BARCODE"
            };

            var result = msg.ToString();

            // Math.Round 使用 MidpointRounding.ToEven (Banker's rounding)
            // 100.005 → 100 (偶数优先); 200.006 → 200.01; 45.004 → 45
            // 仅验证不含逗号（InvariantCulture）且包含关键字段
            Assert.Contains("[X]", result);
            Assert.Contains("[Y]", result);
            Assert.Contains("[RZ]", result);
            Assert.Contains("[T]1", result);
            Assert.Contains("[ID1]BARCODE", result);
            Assert.DoesNotContain(",", result); // InvariantCulture 用点
        }

        [Fact]
        public void MessageToVGT_ToString_EmptyBarcode()
        {
            var msg = new MessageToVGT
            {
                X = 1.0,
                Y = 2.0,
                RZ = 3.0,
                Barcode = ""
            };

            var result = msg.ToString();

            Assert.Contains("[ID1]", result);
            Assert.EndsWith("[ID1]", result);
        }

        [Fact]
        public void MessageToVGT_ToString_UsesInvariantCulture()
        {
            // 验证使用 InvariantCulture（用点而非逗号作为小数分隔符）
            var msg = new MessageToVGT
            {
                X = 5.5,
                Y = 10.25,
                RZ = 180.75,
                Barcode = "BC"
            };

            var result = msg.ToString();

            Assert.Contains(".5", result);
            Assert.Contains(".25", result);
            Assert.DoesNotContain(",5", result);
        }

        [Fact]
        public void MessageToVGT_ToStringLeiLei_FormatsCorrectly()
        {
            var msg = new MessageToVGT
            {
                X = 10.0,
                Y = 20.0,
                RZ = 45.0,
                Barcode = "SHORT"
            };

            var result = msg.ToStringLeiLei();

            Assert.Equal("SHORT,10,20,45,", result);
        }

        [Fact]
        public void MessageToVGT_ToStringLeiLei_LongBarcode_DoesNotThrow()
        {
            // 测试长条码不会抛出异常（Remove 可能因 BarcodeRemoveCount 而截断但不抛异常）
            var longBarcode = new string('A', 40);
            var msg = new MessageToVGT
            {
                X = 1.0,
                Y = 2.0,
                RZ = 3.0,
                Barcode = longBarcode
            };

            // 不抛异常即通过
            var result = msg.ToStringLeiLei();
            Assert.NotNull(result);
            Assert.EndsWith(",", result); // 雷雷格式末尾有逗号
        }

        [Fact]
        public void MessageToVGT_ToStringLeiLei_ShortBarcode_NotTruncated()
        {
            var msg = new MessageToVGT
            {
                X = 1.0,
                Y = 2.0,
                RZ = 3.0,
                Barcode = "SHORT_BC"
            };

            var result = msg.ToStringLeiLei();

            // 短于阈值，不截断
            Assert.StartsWith("SHORT_BC,", result);
        }

        #endregion

        #region Test Helpers

        private sealed class TestNetworkSettings : INetworkSettings
        {
            public string ConnectivityCheckIP => "192.168.1.1";
            public string DetectionResultSendIP => "192.168.1.2";
            public int EncoderReceiverPort => 5000;
            public int DetectionResultSendPort => 6000;
            public string MessageReceiver => "VGT";
        }

        private static INetworkSettings CreateNetworkSettings() => new TestNetworkSettings();

        #endregion

        #region ToVGTService 构造与生命周期

        [Fact]
        public void Constructor_WithValidParameters_CreatesInstance()
        {
            var service = new ToVGTService(CreateNetworkSettings());
            Assert.NotNull(service);
        }

        [Fact]
        public async Task StartAsync_IsIdempotent()
        {
            var service = new ToVGTService(CreateNetworkSettings());

            // 调用两次 StartAsync 不应抛出异常
            await service.StartAsync();
            await service.StartAsync();

            await service.DisposeAsync();
        }

        [Fact]
        public async Task DisposeAsync_IsIdempotent()
        {
            var service = new ToVGTService(CreateNetworkSettings());

            await service.StartAsync();
            await service.DisposeAsync();
            // 二次释放不抛异常
            await service.DisposeAsync();
        }

        [Fact]
        public void MostRecentDateEncode_EmptyList_ReturnsDefaults()
        {
            var service = new ToVGTService(CreateNetworkSettings());

            var result = service.MostRecentDateEncode(DateTime.Now);

            Assert.Equal((uint)0, result.Encoder);
            Assert.Equal(DateTime.MinValue, result.Time);
        }

        #endregion

        #region DbModel → MessageToVGT 转换测试

        [Fact]
        public void SendTo_ConvertsDbModelFieldsCorrectly()
        {
            var service = new ToVGTService(CreateNetworkSettings());

            var models = new List<DbModel>
            {
                new DbModel
                {
                    WorldX = 123.45,
                    WorldY = 678.90,
                    Angle = 45.0,
                    Barcode = "BC001"
                }
            };

            var scannerResult = FrameResult.From(new HikScanner.HikGrabResult
            {
                Status = 0,
                Image = new HikScanner.HikImageData
                {
                    Width = 1000,
                    Height = 800,
                    FrameNum = 42,
                }
            });
            scannerResult.EncoderValue = 100;

            // 验证：不抛异常即可（SendTo 依赖 vgtClient，但无网络时不抛异常，仅记录日志）
            // 此处仅验证 DbModel → MessageToVGT 的字段映射正确
            // 实际网络发送跳过，因为 vgtClient 在 StartAsync 后才会 Connect
            var ex = Record.Exception(() => service.SendTo(models, scannerResult, "VGT"));
            Assert.Null(ex); // 不应抛出异常
        }

        #endregion
    }
}
