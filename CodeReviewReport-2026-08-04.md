# ItemBinding 代码审查报告

- **审查日期**：2026-08-04
- **审查范围**：解决方案全部自研代码（约 260 个 .cs 文件 / 2.7 万行）
- **审查方法**：4 个并行子代理分区审查 + 人工抽验关键发现（所有 P0 级问题均已核对源码行号）
- **第三方代码（不在范围）**：ScottPlot5 / ScottPlot.WPF、HikCameraLib 原生 SDK DLL、ImageViewerControl 上游代码
- **修复状态**：✅ 全部 13 项 P0 已修复；主要 P1 已修复（见文末"六、修复记录"）；编译 8 项目 0 错误，MainAPP.Tests 489/490（1 项存量失败），JinlongYolo.Tests 178/178

---

## 一、总体评价

**代码质量中上，具备明显的系统性审查迭代文化**（源码中大量 `L### / M### / H### / P0-FIX` 编号注释记录了历次修复），异常处理、幂等 Dispose、退出流程超时控制（App.xaml.cs OnExit）都做得相当扎实。这与一般工业视觉代码库相比是显著优点。

但风险集中在三个方向，均与"实时产线"场景直接相关：

1. **并发与线程安全**（约占严重问题一半）：静态可变状态无锁发布、YOLO 池借出/释放竞态、ArrayPool 双返、入队计数竞态。
2. **异常路径资源泄漏**：原生句柄（读码器 SDK）、ArrayPool 缓冲、ONNX 输出张量在 `try` 失败路径上未释放。
3. **SQLite 并发配置缺失**：未设 `busy_timeout`，在产线高频写入 + 后台清理并发时存在**静默丢数据**风险。

**统计**：严重（P0）13 项、中等（P1）28 项、轻微（P2）15 项。

---

## 二、严重问题（P0）— 建议 1~2 周内修复

### A. MainAPP 主程序

**P0-1. `ViewModels/HomeViewModel.cs:487, 1372` — getter 抛异常导致主循环停摆**
`CoordinateTool => CurrentRecipe?.CoordinateTool ?? throw new InvalidOperationException(...)`（L487）。L1372 的 `if (CoordinateTool is null)` 想处理"未标定"场景，但 getter 直接抛异常，该分支永远走不到。`TryEnsureProcessingReady` 未捕获，异常上抛至 `Loop` 后整个采图主循环终止——**配方配了 YOLO 但未标定时产线直接停摆**，且无友好提示。
**修复**：getter 改为返回可空 `CoordinateTool`，由调用方判空（与 `EdgeDetection` 同风格）。

**P0-2. `Services/BarcodeDataService.cs:64-87, 143-169` — SQLite 无 `busy_timeout`，并发写冲突静默丢数据**
`CreateDbContext` 仅首次执行 `PRAGMA journal_mode=WAL`，连接串未配置超时（SQLite 默认 busy 时立即抛 `SQLITE_BUSY`）。`FlushPendingAsync` 从队列 Drain 出记录后 `SaveChangesAsync` 抛异常，**已出队记录无法回队直接丢失**；且 `UpdateAsync:232` / `DeleteAsync:243` / `BulkDeleteAsync:513` / CSV 导入 `:715` 各自 new DbContext 写库，与 flush 无互斥。
**修复**：连接串统一加 `Default Timeout=30`，每个新连接执行 `PRAGMA busy_timeout=30000`；Flush 失败时把 pending 重新入队重试。

**P0-3. `Services/BarcodeDataService.cs:127-140, 320-336` — DrainPending 计数竞态 + 清库后数据复现**
`DrainPending` 先 `CompareExchange` 检查再 `while TryDequeue` 最后 `Exchange(0)`，三处非原子：A 线程 Drain 期间 B 线程入队，`Exchange(0)` 会把 B 的计数清零但记录仍在队列 → 计数低估、flush 触发延迟。更严重的是 `AddAsync` 入队不取 `_flushLock`，`ClearAllAsync` 的 Drain+Delete 期间新入队的记录会被后续 flush 重新写入 → **"清库后数据复现"**。
**修复**：入队与 Drain 共用同一把锁，或清库采用"拒绝新入队标志 + drain + delete + 清标志"。

**P0-4. `ViewModels/RecipeViewModel.cs:998` — 模拟退火回调中 `FrameResult` 未释放，每迭代泄漏 ArrayPool 缓冲**
`device.GetImageAsync(...).GetAwaiter().GetResult()` 返回的 `FrameResult` 持有 `ArrayPool<byte>.Shared.Rent` 的整帧缓冲区（`Models/FrameResult.cs:87`），本方法内从未 Dispose（同文件 L669 正确使用了 `using`）。优化器每轮迭代泄漏一份帧大小缓冲，长时间参数优化内存持续上涨。
**修复**：`using var image = ...` 包裹。

**P0-5. `Extensions/MainAppTools.cs:34-45, 78-81` + `ViewModels/HomeViewModel.cs:978` — 位图复用是死代码，且共享字段无锁竞争**
复用分支要求 `!existing.IsFrozen`（L38），而新建位图总是 `Freeze()`（L80）——所有返回位图都是冻结的，**复用分支永远不命中，每帧都 new 一个 WriteableBitmap**，与注释声称的"避免每帧分配/LOH 碎片"完全相悖。同时 `_reusableShowBitmap` 被最多 4 个并发推理线程无锁读写（数据竞争），当前因"每次新建"侥幸未崩，一旦有人"修复"复用逻辑就会引入并发写崩溃。
**修复**：显示链路串行化（专用锁或单写线程）；复用位图不 Freeze。

**P0-6. `ViewModels/ChartsViewModel.cs:220-223` — 无时间筛选时全表加载，OOM 风险**
清除筛选时调用 `GetAllAsync()` 把整个检测表装入内存（本系统每帧入库，累计可达百万级）。
**修复**：复用 `DatabaseViewModel.ChartLoadLimit=5000` 的分页/限额策略，强制默认时间范围。

**P0-7. `Devices/Scanners.cs:23-33, 72-91` — 静态可变设备实例无 volatile 发布**
`HikScaner`/`HasScanners` 是普通静态属性，`Initialize` 在后台线程执行 `HikScaner?.Dispose(); HikScaner = new ...` 重建，检测线程同步读取——可能拿到已 Dispose 的实例抛 `ObjectDisposedException`，或读到半初始化状态。
**修复**：改为 `volatile` 字段 + `Interlocked.Exchange` 发布新实例；读取侧先取局部引用再使用。

### B. JinlongYolo 推理库

**P0-8. `Predictor/YoloPredictorPool.cs:383-408, 480-513` — Acquire/Dispose 竞态，可能借出已释放的 predictor**
`Acquire` 先检查 `_disposed` 再 `TryTake`，与 `Dispose` 无同步；Dispose 枚举释放队列中所有 predictor。交错时 `TryTake` 可能取出**已被释放的 predictor**，随后对已释放的 `InferenceSession` 调用 → `ObjectDisposedException` 或原生崩溃。
**修复**：借出与 `_disposed`/释放列表互斥，`TryTake` 成功后校验未被释放。

**P0-9. `Services/Predictor/MemoryAllocator.cs:37-46` + `Memory/MemoryTensorOwner.cs:25-34` — ArrayPool 数组可能被 Return 两次**
`Dispose` 用非原子 null 判断做幂等保护。终结器线程与显式 Dispose 并发时，同一数组可被 `ArrayPool.Return` 两次 → 同一数组被两处同时租用、数据损坏。
**修复**：用 `Interlocked.Exchange(ref _buffer, null)` 先取再归还。

### C. 相机 / 读码器 SDK

**P0-10. `HikCodeReader/MvCodeReaderDevice.cs:27-40` — `ForceIp` 每次调用泄漏一个原生句柄**
创建临时 `MvCodeReader` 句柄（:34）后仅调用 `GIGE_ForceIp_NET`，**从不调用 `DestroyHandle_NET`**，且 `CreateHandleBySerialNumber` 返回值未检查。
**修复**：try/finally 中销毁句柄。

**P0-11. `HikCodeReader/MvCodeReaderDevice.cs:182-212, 350-361` — Open 失败路径句柄永泄漏；Close 异常冒泡到 Dispose**
构造函数创建句柄，但 `Open()` 失败（如回调注册失败 :206）时 `_isOpen` 保持 false，`Dispose()` 不会走 `Close()` → 句柄永泄漏；且 `Close()` 内 :224-230 抛异常会使 `Dispose` 抛异常（Dispose 不应抛）。
**修复**：构造成功后 Dispose 兜底销毁；Close 内吞掉二次清理异常。

### D. 服务 / 安全

**P0-12. `WebLiveView/Program.cs:23-26` + appsettings（AllowedHosts="*"）— `/mjpeg` 端点无鉴权**
任何人访问 `/mjpeg` 即触发扫码枪软触发+抓帧并实时取流（首次请求即启动采集）。当前默认仅 localhost 缓解；一旦以 `--urls http://0.0.0.0:port` 部署即完全裸奔。
**修复**：加 token 鉴权并限制绑定地址；按连接数引用计数启停采集（当前零客户端仍持续 5fps 采集，见 P1-19）。

**P0-13. `AIAgent/Mcp/VisionMcpServer.cs:27` + `VisionWorkspace.cs:376-401` — MCP 无鉴权 + 任意路径加载模型**
MCP HTTP 服务无鉴权 token（仅绑 127.0.0.1），本机任意进程可驱动相机/抓拍；`ResolveModelPath` 接受任意根路径并加载执行任意本地 .onnx。
**修复**：模型目录白名单 + MCP 鉴权 token。

---

## 三、中等问题（P1）— 建议 1~2 个月内修复

### MainAPP

| # | 位置 | 问题 | 建议 |
|---|------|------|------|
| 1 | `HomeViewModel.cs:1059-1067` | 每帧 `Dispatcher.BeginInvoke` 对 `InferenceResults` 全量 Clear+Add，30fps 下 Dispatcher 队列压力大 | 节流到 5-10Hz，仅结果变化时更新 |
| 2 | `HomeViewModel.cs:1071-1106` | `QueueSaveSourceImage` 无界 fire-and-forget 保存队列，每帧 CloneAs 整图 + Task.Run 无并发上限 | `SemaphoreSlim` 限并发，队列满丢弃 |
| 3 | `RecipeViewModel.cs:1096` | `Dispose` 中 `_ = DeactivateAsync()` fire-and-forget，异常路径下扫描枪可能停在软触发 | 同步/带超时等待清理 |
| 4 | `RecipeViewModel.cs:251-265` | `ActivateAsync` 固定 `Task.Delay(12s)` 与主循环取图耗时脆弱耦合 | 用"主循环已暂停"信号量替代固定延时 |
| 5 | `RecipeViewModel.cs:762-804` | `_liveDisplayCts` 旧实例从不 Dispose | 确认旧任务退出后再 Dispose |
| 6 | `DatabaseViewModel.cs:383-393, 722-747` | 线程池线程 `Dispatcher.Invoke` 同步阻塞；`TotalCount` 后台线程触发 INPC；`ChartsViewModel` 构造内同步分析 5000 条 | 改 `InvokeAsync`；状态属性统一 UI 线程设置；构造轻量化 |
| 7 | `LogViewModel.cs:278-287` | 加载查询无 CancellationToken，VM 释放后仍修改集合 | 传入 Cts，回调检查释放标志 |
| 8 | `Services/ToVGTService.cs:343-344, 370-371` | UDP 发送仅捕获 `ObjectDisposedException`，`SocketException` 会冒泡中断检测主循环 | 捕获 SocketException 记 Warning |
| 9 | `Application/DetectionRecordService.cs:166-184` | 多产品角度推理串行，单帧延迟累加 | `Task.WhenAll` 按池大小限并发 |
| 10 | `Application/DetectionRecordService.cs:95-101` | `Bounds.Width * resizeWidth` int×int 可能溢出为负 | 先转 long 再乘并钳制 |
| 11 | `Services/AngleTracker.cs:84-86, 142-157` | 同帧多产品共享同一 EncoderValue，差分匹配交叉错配 | 以帧号/产品索引区分候选（ProductTracker 已有 matchedInBatch，AngleTracker 缺） |
| 12 | `Services/ChartRenderingService.cs:107,135,162,192,332` | 全 0 数据 `SetLimitsY(0,0)` ScottPlot5 抛 "axis limits are equal" | `max > 0 ? max*1.1 : 1` |
| 13 | `Services/BarcodeDataService.cs:543-580, 258-270` | `Contains` → `LIKE '%..%'` 无索引全表扫描；`GetByBarcodeAsync` 无 Take 上限 | 前缀匹配/FTS5；加 Take |
| 14 | `Services/DataRetentionService.cs:147-157` | VACUUM 与 Serilog SQLite 常驻写连接冲突，无 busy_timeout 必然失败且被吞 | 设 busy_timeout+重试，或改 `PRAGMA auto_vacuum` |
| 15 | `Services/LogDatabaseService.cs:35-62` | DB 损坏/锁定时静默返回空，UI 误以为无日志 | 区分"正常空表"与异常并提示 |
| 16 | `Services/RecipeScannerService.cs:87-127` | 伪异步：`await Task.CompletedTask` 后同步调设备 SDK | 设备调用包 `Task.Run` 或明确标注同步 |
| 17 | `Services/BarcodeDataService.cs:368-399` + `LogDatabaseService.cs:213-247` | 清理超 3 万条时报 "too many SQL variables"（32766 上限），异常被吞、数据库持续膨胀 | 分批 500~1000 条 ExecuteDelete |
| 18 | `Services/ProductTracker.cs:110-113` | 持锁逐产品 Info 日志，10FPS 下每秒数十条 | 移出锁，统一摘要 + Debug 级 |

### JinlongYolo

| # | 位置 | 问题 | 建议 |
|---|------|------|------|
| 19 | `Memory/OrtYoloRawOutput.cs:17-27` | `_disposable = result` 在 CreateMemoryTensor 之后赋值，若中途抛异常 ONNX 原生输出永不释放 | 先赋 `_disposable` 或 try/catch 释放后重抛 |
| 20 | `Memory/BitmapBuffer.cs:83-94` | 长度校验失败时 `owner` 未 Dispose，池化数组泄漏 | 失败路径调用 `owner.Dispose()` |
| 21 | `Decoders/Base/AnchorFreeBoxDecoder.cs:29-48`（及 Oriented 版） | `confidence <= threshold` 对 NaN 恒 false，NaN 置信度框绕过过滤进入 NMS | 改 `if (!(confidence > threshold)) continue;` |
| 22 | `Services/Resolvers/PredictorServiceResolver.cs:43-56` | 非线程安全 `Dictionary`，并发 Add 可抛异常；逐帧传新配置会无限增长 | `ConcurrentDictionary` + 值相等复用 |
| 23 | `Metadata/YoloMetadata.cs:263-285` | `names[id]` 直接下标赋值，类别 id 不连续时越界/NRE | 校验 id 范围后填充 |
| 24 | `Decoders/SegmentationDecoder.cs:27-33` | padding 过大时 mask 尺寸变负 → Allocate 抛异常 | `Math.Max(1, ...)` 钳制 |
| 25 | `Services/Predictor/ObbNonMaxSuppression.cs:36-44` | 退化多边形 unionArea=0 → NaN，误抑制相邻框 | `unionArea <= 0` 时返回 0f |
| 26 | `Services/Predictor/SerssionRunnerService.cs:159-181` | `output0` 分配后若 BindOutput 抛异常未释放 | try/catch 释放已分配资源 |
| 27 | `Predictor/YoloPredictorOptions.cs:129-146` | OpenVINO 回退分支 catch 后 `fallbackSession` 未释放 | finally 释放 |
| 28 | `Utilities/CompositeDisposable.cs:18-42` | 无锁，显式 Dispose 与终结器并发可重复释放 | 进入时 `Interlocked.Exchange` 置位 |

### 相机 / Web 服务

| # | 位置 | 问题 | 建议 |
|---|------|------|------|
| 29 | `HikScanner.cs:541` | `GrabOneFrame(int timeoutMs = 6000_000)` 默认 **100 分钟**超时（疑为 6000 笔误），误用会长时间阻塞线程 | 改 `6000` 并文档标注 |
| 30 | `HikScanner.Reconnect.cs:97-108` | 重连无条件注册 `_imageCallback`，Polling/MSC 模式下双路投递 → 重复帧/事件风暴 | 按 `_activeGrabMode` 决定是否注册 |
| 31 | `HikScanner.cs:359-361, 529-531` | `_grabCts` Cancel 后立即 Dispose，采集循环访问 token 抛 ObjectDisposedException（被 faulted 观测吞掉） | 延后 Dispose |
| 32 | `HikScanner.Reconnect.cs:71-88, 159-175` | 重连任务无锁销毁/重建 `_device`，与并发线程对已销毁句柄调用 SDK → use-after-free | 重连期间以 `_grabLock` 保护，或先建新实例再原子交换 |
| 33 | `AIAgent/Mcp/VisionWorkspace.cs:482` | `CaptureCoreAsync` 在全局 `Gate` 内调 `GetImageAsync()`（默认 1 小时超时），抓拍卡死阻塞所有 MCP 工具 | 显式短超时 + 取消令牌 |
| 34 | `WebLiveView/Services/ScannerLiveViewService.cs:294-329` | 逐像素 `SetPixel`（3MP≈310 万次/帧）无法满足 5fps；非 Mono8/JPEG 一律按 RGB8 处理可能越界 | LockBits/ImageSharp 批量转换 + 校验像素格式 |
| 35 | `HikCodeReader/MvCodeReaderDevice.cs:684-696` | `SetAutoExposure` 用 `SetBoolValue("ExposureAuto")` 操作 GenICam **枚举**节点 → 必然失败 | 改枚举接口（参考 HikScanner.cs:146） |
| 36 | `HikScanner.cs:1113-1118, 1051-1067` | BCR/OCR/Waybill 循环按 SDK 返回计数遍历内联数组无上限校验 → 原生越界读 | 与数组容量比对并 clamp |
| 37 | `WebLiveView/Services/ScannerLiveViewService.cs:46-66` | 零客户端时仍以 5fps 持续软触发+抓图直至进程退出 | 按连接数引用计数启停 |
| 38 | `HikCameraLib/HikScanner/ScannerManager.cs:87-93` | `Connect` 抛异常时新建的 `HikScanner` 未 Dispose | catch 内 `camera?.Dispose()` |

---

## 四、轻微问题（P2）— 风格与健壮性（节选）

1. **明文口令**：`Models/AppUser.cs:43,72-101` + `Settings/SecuritySettings.cs:11-16` — 密码明文存 `users.json`，`EnsureDefaultUsers` 用默认空密码创建管理员。建议至少 SHA-256+盐，首次启动强制设置密码。
2. **`Chessboard.cs:793`** — `new Mat(maskedMat, expandedRect)` 未 Dispose（每帧标定泄漏一个 Mat 视图）；`Generate` 中 `baseImage` 异常路径未释放。
3. **`CoordinateTransformer.cs`** — `IsInitialized` 公开可写；`Initialize` 未校验 `distX/distY==0`（除零产生 NaN）；未初始化时调用变换方法返回 NaN 无防护。建议 `private set` + 参数校验 + 未初始化抛异常。
4. **`HikCodeReader/Utilities.cs:19-32`** — 非泛型 `ByteToStruct` 无 try/finally，`AllocHGlobal` 异常泄漏（泛型版已正确）。
5. **`HikCodeReader/ImageResult.cs:302`** — `Encoding.Default` 随系统区域，中文条码可能乱码（HikScanner 已用 GB2312/UTF8 探测）。
6. **`HikCodeReader/MvCodeReaderDevice.cs:333`** — `new byte[imageInfo.nFrameLen]` 无大小上限，异常 nFrameLen 可致 OOM。
7. **`HikScanner.cs:962`、`HikScanner.GigE.cs:63`** — `UnsafeAddrOfPinnedArrayElement` 用于未固定数组，建议 GCHandle/AllocHGlobal。
8. **`HomeViewModel.cs:55-58`** — `s_isPaused` 静态标志无恢复兜底，RecipeWindow 异常关闭后主循环永久暂停。
9. **`HomeViewModel.cs:301-331`** — `ImageForShow` 绑定 getter 无锁读取；L322 的 `Dispose()` 对 WriteableBitmap 是死代码。
10. **`DatabaseViewModel.cs:68-70`** — 匿名 lambda 订阅 `SelectedItems.CollectionChanged` 无退订。
11. **`RecipeViewModel.cs:813-826`** — `DebugCallback` 在后台线程 `Cv2.NamedWindow/ImShow`，OpenCV 窗口宜在 UI 线程。
12. **`ChartsViewModel.cs:184-189`** — VM 直接依赖 `WpfPlot` 控件（MVVM 违规）。
13. **`HikScanner.cs:1173-1178`** — MSC 回调在 SDK 线程直接调用户处理器，触碰 UI 会跨线程异常。
14. **`JinlongYolo` 多处** — `MemoryTensor.cs:90` 未用 checked 乘法；`PlottingContext.cs:218` 每帧 CreateFont；`ImageContoursRecognizer`/`MaskDrawer` 每帧大分配是主要 GC 压力源（实时推理优化点）。
15. **`AIAgent/Mcp/VisionMcpStdioServer.cs` / `HikScanner.Compat.cs:105`** — `GetImageAsync` 默认 1 小时超时在多处误用。

---

## 五、修复优先级建议

**第一优先级（产线稳定性，1~2 周）**：
1. P0-1 主循环停摆（逻辑缺陷，最紧急）
2. P0-2/P0-3 SQLite 丢数据（数据完整性）
3. P0-4 模拟退火内存泄漏
4. P0-10/P0-11 原生句柄泄漏（反复调用会耗尽句柄）
5. P0-8/P0-9 YOLO 池竞态（崩溃/数据损坏）

**第二优先级（安全与并发，1 个月）**：
- P0-12/P0-13 无鉴权端点（部署参数变化即成漏洞）
- P0-5/P0-7 静态状态发布竞态
- P1 中所有线程/超时类问题（P1-29 GrabOneFrame 超时笔误、P1-31/P1-32 相机 CTS/重连）

**第三优先级（性能与体验，持续）**：
- P1-1/P1-2 高帧率 UI 与保存队列
- JinlongYolo 绘图链路每帧分配优化（P2-14）
- SQLite 查询索引（P1-13）

---

## 六、修复记录（2026-08-04）

### ✅ 已修复（P0 全部 13 项）

| # | 位置 | 修复内容 |
|---|------|---------|
| P0-1 | `MainAPP/ViewModels/HomeViewModel.cs` | `CoordinateTool` getter 改可空返回，调用处取局部变量，未标定分支真正生效，主循环不再停摆 |
| P0-2 | `MainAPP/Models/AppDbContext.cs` + `Services/BarcodeDataService.cs` | 连接串加 `Default Timeout=30`（busy_timeout=30s）；`FlushPendingAsync` 写库失败时记录重新入队并上抛，不再静默丢失 |
| P0-3 | `Services/BarcodeDataService.cs` | 入队与 Drain/清库共用 `_flushLock`；`DrainPending` 改"计数快照+出队对应数量"，计数与队列始终一致 |
| P0-4 | `MainAPP/ViewModels/RecipeViewModel.cs:998` | 模拟退火回调 `using var image`，每帧 ArrayPool 缓冲正常归还 |
| P0-5 | `Extensions/MainAppTools.cs` | 删除 `Freeze()`（复用分支真正生效）；加静态锁串行化共享位图写入。**随后修复了因此引入的 WPF 跨线程回归**（位图改在 UI 线程创建，见"跨线程回归修复"） |
| P0-6 | `MainAPP/ViewModels/ChartsViewModel.cs` | 无时间筛选改为 `GetRecentAsync(5000)`（新增 ChartLoadLimit 常量），不再全表加载 |
| P0-7 | `MainAPP/Devices/Scanners.cs` | 静态设备实例改 volatile 字段 + 只读属性，跨线程发布可见 |
| P0-8 | `JinlongYolo/Predictor/YoloPredictorPool.cs` | Acquire 用信号量 + `_sync` 锁内检查/取用，与 Dispose 清理互斥，不再借出已释放 predictor |
| P0-9 | `JinlongYolo` MemoryAllocator / MemoryTensorOwner | Dispose 改 `Interlocked.Exchange` 原子取出，终结器与显式 Dispose 并发只归还一次 |
| P0-10 | `HikCodeReader/MvCodeReaderDevice.cs` | `ForceIp` try/finally 销毁临时句柄 + 校验 CreateHandle 返回值 |
| P0-11 | 同上 | Open 失败路径 Dispose 兜底销毁句柄；Close/清理异常不再冒泡到 Dispose |
| P0-12 | `WebLiveView/Program.cs` + appsettings.json | `/mjpeg` 增加 `?token=` 鉴权（`MjpegSettings:Token`），未配置 503 / 不匹配 401 |
| P0-13 | `AIAgent/Mcp/VisionMcpServer.cs` + `VisionWorkspace.cs` | MCP 增加 Bearer token 鉴权（环境变量/随机生成）；模型加载白名单限定 Models 目录内 `.onnx` |

### ✅ 已修复（P1 及补充）

**MainAPP**：ChartRenderingService 5 处 `SetLimitsY(0,0)` 用 `SafeMaxYScale` 兜底；ToVGTService 两处 UDP 发送补充捕获 SocketException；DetectionRecordService 坐标缩放改 long 乘法 + `ClampToInt`；BarcodeDataService 的 `TrimToMaxCountAsync`/`BulkDeleteAsync` 分批删除（规避 SQLite 32766 变量上限）、`GetByBarcodeAsync` 加 Take 上限。

**JinlongYolo**：OrtYoloRawOutput/BitmapBuffer/SerssionRunnerService/YoloPredictorOptions 异常路径资源释放；AnchorFree/AnchorBased/Oriented 解码器 NaN 置信度过滤；PredictorServiceResolver 改 ConcurrentDictionary；YoloMetadata 类别 id 越界校验；SegmentationDecoder 尺寸钳制；ObbNMS unionArea<=0 返回 0；CompositeDisposable Interlocked 幂等；BoundingBoxTransformer 负尺寸钳制；MemoryTensor checked 乘法。

**相机/读码器/Web/MCP**：HikScanner `GrabOneFrame` 默认超时 6000_000（100 分钟）→ 6000；`GetImageAsync` 默认 1 小时 → 30 秒；`_grabCts` Cancel 后延后 Dispose（采集循环兜底释放）；BCR/OCR/面单内联数组按实际容量钳制；`GetEnumValue` 枚举值数量防御上限；ScannerManager 两处 Connect 失败路径释放实例；HikCodeReader `ExposureAuto` 改枚举接口、`nFrameLen` 加 256MB 上限、`ByteToStruct` try/finally、`CodeString` 改 GB2312/UTF8 探测；VisionWorkspace 抓拍显式 10 秒超时；Chessboard 两处 Mat 泄漏；CoordinateTransformer Initialize 除零校验。

**附带修复**：`HikCodeReader.csproj` HintPath 指向错误的 `MVIDCodeReader.Net.dll`（代码实际使用 `MvCodeReaderSDK.Net.dll` 的 `MvCodeReaderSDKNet` 命名空间，该错误导致项目无法编译，HikCodeReader 原不在解决方案中未被发现）。

### 🔧 跨线程回归修复（运行时报错后追加，共两轮）

**第一轮现象**：运行时报 "必须在与 DependencyObject 相同的 Thread 上创建 DependencySource" 未处理异常。
**第一轮根因**：P0-5 删除 `Freeze()` 后，WriteableBitmap 在**后台推理线程**被 `new` 出来（未冻结），随后被 UI 线程绑定（`ImageSource="{Binding ImageForShow}"`）——WPF 要求未冻结的 DependencyObject 在创建它的线程上被使用。
**第一轮修复**：`Tools.UpdateShow` 把位图创建/复用提升到 UI Dispatcher；`HomeViewModel.ImageForShow` setter 非 UI 线程调用时 `Dispatcher.InvokeAsync` 转发。

**第二轮现象（本轮）**：修完第一轮后 **ImageViewer 完全不显示图片**，日志每帧报错：
`ProcessImageAsync 异步任务异常: System.InvalidOperationException: 调用线程无法访问此对象，因为另一个线程拥有该对象`，堆栈定位 `Tools.UpdateShow ... line 70`（WritePixels）。
**第二轮根因**：`WriteableBitmap` 继承 `DispatcherObject`，**未冻结时只能在创建线程（UI 线程）上访问，包括 `WritePixels`**——第一轮把"创建"提到 UI 线程但把"WritePixels"留在后台线程，后台线程直接写 UI 线程拥有的位图 → VerifyAccess 抛异常 → 每帧失败 → 画面不更新。（此前"WriteableBitmap 支持跨线程写入"的理解有误：跨线程使用必须经由 Dispatcher 调度，而非任意线程直接调用。）
**第二轮修复（`Extensions/MainAppTools.cs`）**：像素转换（`ProcessPixelRows`，唯一耗时部分）留在后台线程；**位图创建/复用 + `WritePixels` 整体提升到 UI 线程**（`Dispatcher.Invoke`，UI 线程 Dispatcher 队列天然串行化并发推理线程，原 `UpdateShowLock` 已移除）。`HomeViewModel.ImageForShow` setter 的 Dispatcher 转发保留。
**最终验证**：Extensions / MainAPP 编译 0 错误；MainAPP.Tests 489/490（1 项存量失败不变）。

> **WPF 位图显示正确模式（已两次踩坑，务必遵守）**：像素转换可后台；WriteableBitmap 的创建、WritePixels、赋值绑定**全部必须在 UI 线程**（经 Dispatcher）。冻结后虽可跨线程读，但无法 WritePixels，故复用场景只能走 UI 线程写入。

### ⏸ 有意未改（风险/收益权衡，需另行评估）

1. **P1-19/20 ScannerLiveViewService 逐像素 SetPixel**（3MP/帧无法满足 5fps）→ 需 LockBits 重构，改动大。
2. **明文口令（P2-1）** → 涉及 AuthService 改造与存量 users.json 迁移。
3. **P1-14 VACUUM 与 Serilog 写连接冲突** → 需调整日志写入窗口或改 auto_vacuum。
4. **P1-13 Barcode 子串查询索引** → 需 FTS5，属功能增强。
5. **P1-5 RecipeViewModel CTS 不 Dispose** → 已有注释设计决策（避免 use-after-dispose），且 Dispose 中兜底释放，维持现状。
6. **P1-6 DatabaseViewModel 同步 Invoke** → 低危，待 UI 线程改造时一并处理。

### 🧪 验证结果

- 8 个项目编译：CoordinateSystemMapping / Extensions / JinlongYolo / HikScanner / HikCodeReader / WebLiveView / AIAgent / MainAPP 全部 **0 错误**（MainAPP 2 个存量 VSTHRD 警告）。
- MainAPP.Tests：**489/490 通过**，唯一失败 `BuildAndSaveAsync_EmptyFolder_ThrowsArgumentException` 为今日已记录的存量失败，与本次修改无关。
- JinlongYolo.Tests：**178/178 通过**。
- CoordinateSystemMapping.Tests 无法编译：存量 csproj 配置问题（测试项目 net8.0 引用 net8.0-windows 库，TargetFramework 不匹配），非本次修改引入。
- 所有修改均添加 `// REVIEW-FIX:` 注释标记，与现有 `L###/M###/P0-FIX` 编号注释风格一致。

---

*本报告基于源码静态审查，未修改任何文件。行号以审查当日代码为准，修复后可能漂移。*
