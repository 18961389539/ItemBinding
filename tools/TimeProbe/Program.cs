using HikCodeReader;
using MvCodeReaderSDKNet;
using System.Reflection;

// ============================================================
// 扫码枪时间信息探针（2026-09-18）
// 目的：实测这台读码器到底能提供哪些时间信息：
//   1) 参数节点扫查（时间相关候选键 × 五种值类型）
//   2) 帧内时间字段（TimeStamp / 条码触发时刻原始值 / 耗时）
//   3) 硬触发回调在软触发下是否触发（TRIGGER_INFO_DATA 的双时钟观测）
// ============================================================

Console.WriteLine("===== 扫码枪时间信息探针 =====");

// ---------- 1. 枚举设备 ----------
var devices = MvCodeReaderDevice.EnumDevices();
Console.WriteLine($"发现 {devices.Count} 台设备");
if (devices.Count == 0) { Console.WriteLine("无设备，退出"); return; }
foreach (var d in devices)
    Console.WriteLine($"  [{d.Index}] {d.ModelName} SN={d.SerialNumber} IP={d.IpAddress} Ver={d.DeviceVersion}");

var dev = new MvCodeReaderDevice(devices[0]);
Console.WriteLine($"打开: {devices[0].ModelName} ...");
dev.Open();
Console.WriteLine("已打开（设备内部已注册其触发回调，探针随后覆盖为自己的计数回调）");

// ---------- 2. 自有触发回调：计数 + 捕获双时钟观测 ----------
int triggerFired = 0;
ulong lastDevTick = 0;
long lastHostFileTime = 0;
uint lastTriggerIndex = 0;
MvCodeReader.cbTriggerdelegate trigCb = (ptr, user) =>
{
    if (ptr == IntPtr.Zero) return;
    try
    {
        var ti = (MvCodeReader.MV_CODEREADER_TRIGGER_INFO_DATA)System.Runtime.InteropServices.Marshal.PtrToStructure(
            ptr, typeof(MvCodeReader.MV_CODEREADER_TRIGGER_INFO_DATA));
        if (ti.nTriggerFlag != 1) return;
        Interlocked.Increment(ref triggerFired);
        lastDevTick = ((ulong)ti.nTriggerTimeHigh << 32) | ti.nTriggerTimeLow;
        lastHostFileTime = ti.nHostTimeStamp;
        lastTriggerIndex = ti.nTriggerIndex;
    }
    catch { /* 探针回调静默 */ }
};
var regRet = dev.DeviceHandle.MV_CODEREADER_RegisterTriggerCallBack_NET(trigCb, IntPtr.Zero);
Console.WriteLine($"探针触发回调注册: 0x{regRet:X8}");

// ---------- 3. 参数节点扫查（时间相关候选键） ----------
Console.WriteLine();
Console.WriteLine("===== 参数节点扫查（时间相关候选） =====");
(string method, Type structType)[] probes =
{
    ("MV_CODEREADER_GetStringValue_NET", typeof(MvCodeReader.MV_CODEREADER_STRINGVALUE)),
    ("MV_CODEREADER_GetIntValue_NET",    typeof(MvCodeReader.MV_CODEREADER_INTVALUE_EX)),
    ("MV_CODEREADER_GetFloatValue_NET",  typeof(MvCodeReader.MV_CODEREADER_FLOATVALUE)),
    ("MV_CODEREADER_GetEnumValue_NET",   typeof(MvCodeReader.MV_CODEREADER_ENUMVALUE)),
};
string[] keys =
{
    "DeviceTime", "DeviceClock", "DeviceDateTime", "RealTimeClock", "RtcTime",
    "TimeStampEnable", "TimestampEnable", "TimeStampReset", "TimestampReset", "TimestampValue",
    "TimeSyncEnable", "TimeSyncSource", "TimeSyncInterval", "TimeSyncOffset",
    "NtpEnable", "NtpServerAddr", "NtpPort",
    "GevTimestampTickFrequency", "GevTimestampValue", "GevTimestampControlLatch", "GevTimestampControlReset",
    "TickFrequency", "DeviceClockFrequency",
};
var handle = dev.DeviceHandle;
foreach (var key in keys)
{
    foreach (var (method, structType) in probes)
    {
        var mi = handle.GetType().GetMethod(method);
        if (mi == null) continue;
        try
        {
            var invokeArgs = new object?[] { key, Activator.CreateInstance(structType) };
            var retObj = mi.Invoke(handle, invokeArgs);
            if (retObj is not int ret || ret != 0) continue; // 该键不支持此类型/不存在
            var fields = structType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var vals = string.Join(", ", fields.Select(f => $"{f.Name}={f.GetValue(invokeArgs[1])}"));
            Console.WriteLine($"  [有值] {key} ({method.Replace("MV_CODEREADER_Get", "").Replace("_NET", "")}) = {vals}");
        }
        catch { /* 该键不支持此类型，静默跳过 */ }
    }
}
Console.WriteLine("===== 参数扫查结束 =====");

// ---------- 4. 连续模式抓 3 帧 ----------
Console.WriteLine();
Console.WriteLine("===== 连续模式抓帧 ×3 =====");
dev.StartGrabbing();
for (var i = 0; i < 3; i++)
{
    try
    {
        var img = await dev.GetImageAsync(5000);
        DumpFrame(img);
    }
    catch (Exception ex) { Console.WriteLine($"  抓帧失败: {ex.Message}"); }
}
Console.WriteLine($"  硬触发回调累计触发: {Interlocked.CompareExchange(ref triggerFired, 0, 0)}");
dev.StopGrabbing();

// ---------- 5. 软触发模式 ×3（检验 TRIGGER_INFO_DATA 是否在软触发下携带双时钟） ----------
Console.WriteLine();
Console.WriteLine("===== 软触发模式 ×3 =====");
try
{
    dev.SwitchToSoftwareTrigger();
    dev.StartGrabbing();
    var firedBefore = Interlocked.CompareExchange(ref triggerFired, 0, 0);
    for (var i = 0; i < 3; i++)
    {
        try
        {
            dev.ExecuteSoftwareTrigger();
            await Task.Delay(150);
            var img = await dev.GetImageAsync(5000);
            DumpFrame(img);
        }
        catch (Exception ex) { Console.WriteLine($"  软触发抓帧失败: {ex.Message}"); }
    }
    var firedAfter = Interlocked.CompareExchange(ref triggerFired, 0, 0);
    Console.WriteLine($"  探针触发回调新增触发: {firedAfter - firedBefore} 次");
    Console.WriteLine($"  最近一次触发观测: 触发编号={lastTriggerIndex} 设备tick={lastDevTick} 主机FILETIME={lastHostFileTime} (={DateTime.FromFileTime(lastHostFileTime):HH:mm:ss.fff})");
    dev.StopGrabbing();
    dev.SwitchToContinuousMode();
}
catch (Exception ex)
{
    Console.WriteLine($"软触发测试异常: {ex.Message}");
    try { dev.StopGrabbing(); dev.SwitchToContinuousMode(); } catch { }
}

// ---------- 6. 收尾 ----------
dev.Close();
Console.WriteLine("===== 探针结束 =====");

static void DumpFrame(ImageResult img)
{
    Console.WriteLine($"  帧#{img.FrameNumber} 设备TimeStamp={img.TimeStamp} 抓取时刻={DateTime.Now:HH:mm:ss.fff}");
    if (img.BarcodeResults.Length == 0)
    {
        Console.WriteLine("    （无条码结果）");
        return;
    }
    foreach (var b in img.BarcodeResults)
        Console.WriteLine(
            $"    码={b.CodeString} algo={b.AlgoCost}ms total={b.TotalProcCost}ms " +
            $"trigTime(估算)={b.TriggerTime:HH:mm:ss.fff} TvLow={b.TriggerTimeTvLowRaw} UtvLow={b.TriggerTimeUtvLowRaw}");
}
