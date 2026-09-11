using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using HikScanner;
using HikScannerLib = HikScanner.HikScanner;

namespace HikScannerBenchmarks;

/// <summary>
/// 工具方法性能基准测试。
/// 覆盖 UTF8 检测、条码类型名查找、异常错误码描述查找。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class UtilityBenchmarks
{
    private byte[] _asciiData;
    private byte[] _utf8ChineseData;
    private byte[] _utf8MixedData;
    private byte[] _invalidUtf8Data;

    [Params(100, 1000, 10000)]
    public int TextLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // 纯 ASCII
        var ascii = new StringBuilder();
        for (int i = 0; i < TextLength; i++)
            ascii.Append((char)('a' + (i % 26)));
        _asciiData = Encoding.ASCII.GetBytes(ascii.ToString());

        // 纯中文 UTF8
        var chinese = new StringBuilder();
        string chars = "你好世界测试条码扫描器性能基准";
        for (int i = 0; i < TextLength; i++)
            chinese.Append(chars[i % chars.Length]);
        _utf8ChineseData = Encoding.UTF8.GetBytes(chinese.ToString());

        // 混合 ASCII + UTF8
        var mixed = new StringBuilder();
        for (int i = 0; i < TextLength; i++)
        {
            if (i % 2 == 0)
                mixed.Append((char)('a' + (i % 26)));
            else
                mixed.Append("测试");
        }
        _utf8MixedData = Encoding.UTF8.GetBytes(mixed.ToString());

        // 无效 UTF8 序列
        _invalidUtf8Data = new byte[TextLength];
        var rand = new Random(42);
        for (int i = 0; i < TextLength; i++)
            _invalidUtf8Data[i] = (byte)(0x80 | (rand.Next() & 0x3F));
    }

    [Benchmark(Description = "IsTextUtf8 - 纯ASCII")]
    public bool IsTextUtf8_Ascii()
    {
        return HikScannerLib.IsTextUtf8(_asciiData);
    }

    [Benchmark(Description = "IsTextUtf8 - 中文UTF8")]
    public bool IsTextUtf8_Chinese()
    {
        return HikScannerLib.IsTextUtf8(_utf8ChineseData);
    }

    [Benchmark(Description = "IsTextUtf8 - 混合UTF8")]
    public bool IsTextUtf8_Mixed()
    {
        return HikScannerLib.IsTextUtf8(_utf8MixedData);
    }

    [Benchmark(Description = "IsTextUtf8 - 无效序列")]
    public bool IsTextUtf8_Invalid()
    {
        return HikScannerLib.IsTextUtf8(_invalidUtf8Data);
    }

    // ─── 条码类型名查找 ───

    [Benchmark(Description = "GetCodeTypeName - QR码")]
    public string GetCodeTypeName_QR()
    {
        return HikBarcodeResult.GetCodeTypeName(1); // MV_CODEREADER_TDCR_QR
    }

    [Benchmark(Description = "GetCodeTypeName - Code128")]
    public string GetCodeTypeName_Code128()
    {
        return HikBarcodeResult.GetCodeTypeName(11); // MV_CODEREADER_BCR_CODE128
    }

    [Benchmark(Description = "GetCodeTypeName - 未知码")]
    public string GetCodeTypeName_Unknown()
    {
        return HikBarcodeResult.GetCodeTypeName(999);
    }

    // ─── 错误码描述查找 ───

    [Benchmark(Description = "GetErrorDescription - 网络错误")]
    public string GetErrorDescription_Network()
    {
        return HikScannerException.GetErrorDescription(unchecked((int)0x80040101));
    }

    [Benchmark(Description = "GetErrorDescription - 未知错误码")]
    public string GetErrorDescription_Unknown()
    {
        return HikScannerException.GetErrorDescription(unchecked((int)0x99999999));
    }

    // ─── HikDeviceInfo.ToString ───

    [Benchmark(Description = "DeviceInfo.ToString - GigE设备")]
    public string DeviceInfoToString_GigE()
    {
        var info = new HikDeviceInfo
        {
            UserDefinedName = "Reader01",
            SerialNumber = "SN12345678",
            TLayerType = (uint)HikDeviceType.GigE
        };
        return info.ToString();
    }
}
