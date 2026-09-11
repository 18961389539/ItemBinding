# LicenseSystem — 离线激活码管理系统

类 Windows 激活模式的**纯离线授权系统**：客户端生成机器码 → 厂商离线签发激活码 → 客户端内置公钥本地验签。
无服务器、无网络依赖，防篡改、防伪造、防换机。

## 组件

```
LicenseSystem/
├── src/
│   ├── LicenseCore/     # 核心库（被保护软件只需引用它）
│   ├── LicenseManager/  # 激活码生成器（极简：机器码 → 激活码，自动加载私钥）
│   ├── LicenseTool/     # 命令行生成工具（单条 / CSV 批量）
│   └── DemoApp/         # 客户端集成演示
└── tests/
    └── LicenseCore.Tests/  # 核心库单元测试（31 项）
```

## 工作原理

```
客户端: 硬件指纹(CPU/磁盘/网卡/机器名) → SHA256 → 机器码(4组Base32)
         ┌────────────────────────────────────────────┐
         │ 机器码发给厂商                                │
         ▼                                            │
厂商:   激活码 = 授权信息(产品/到期/机器数/盐) + 私钥签名(ECDSA P-256)
         │ 激活码发回客户                                │
         └────────────────────────────────────────────┘
客户端: 内置公钥离线验签 → 校验产品/机器绑定/到期 → Valid 才放行
```

- **机器码**：`XXXXX-XXXXX-XXXXX-XXXXX`（96 bit，Crockford Base32 剔除易混字符）
- **激活码**：约 28 组字符，内含授权载荷 + ECDSA 签名
- **安全**：客户端只持有公钥，即使被逆向也无法伪造激活码；任何一位字符被篡改 → 签名校验失败

## 快速开始

### 1. 厂商侧：生成密钥对

```bash
dotnet run --project src/LicenseTool -- newkey --out ./keys
# 生成 license_private.pem（厂商保管，泄露=可签发）与 license_public.pem（内置客户端）
```

### 2. 客户端：采集机器码

被保护软件调用（或 LicenseManager 里点"采集本机"）：

```csharp
var machineCode = MachineCode.Create(); // 显示给用户 → 发给厂商
```

### 3. 厂商侧：生成激活码

```bash
# 单条（输出到 stdout）
dotnet run --project src/LicenseTool -- gen --key ./keys/license_private.pem \
    --machine XXXXX-XXXXX-XXXXX-XXXXX --days 365 --product 1

# 指定到期日 + 通用码（不绑机器）
dotnet run --project src/LicenseTool -- gen --key ./keys/license_private.pem \
    --machine XXXXX-XXXXX-XXXXX-XXXXX --expire 2027-12-31 --type universal --machines 5

# CSV 批量（列: 机器码,产品ID,到期,机器数；# 注释）
dotnet run --project src/LicenseTool -- batch --key ./keys/license_private.pem --csv list.csv --out ./licenses
```

或使用 **LicenseManager**（极简窗口）：填机器码 → 选有效期 → 点生成（自动复制）。私钥自动加载（默认找 `LicenseSystem\keys\license_private.pem`，可用"选择私钥"更换）。

### 4. 客户端：启动时校验

```csharp
var fingerprint = HardwareFingerprint.ComputeFingerprintBytes();
var result = LicenseValidator.Validate(
    userInputCode,          // 用户输入的激活码
    fingerprint,            // 本机指纹
    embeddedPublicKeyPem,   // 内置公钥（编译进程序，勿从外部文件加载）
    productId: 1);

if (result.Status != LicenseStatus.Valid)
{
    // 拒绝运行（可提示 result.Message）
}
```

## 授权模型

| 维度 | 实现 |
|---|---|
| 到期时间 | 到期日（`--expire`）或天数（`--days`）；留空 = 永久 |
| 授权类型 | **单机绑定**（激活码绑定机器哈希，换机失败）/ **通用**（不绑机器） |
| 机器数 | 声明字段（厂商台账统计用；离线无法全局计数） |
| 产品 | 2 字节产品 ID，防止跨产品复用 |

## 测试

```bash
dotnet test            # 31 项：机器码/签名/篡改/过期/换机/产品/密钥往返
```

## 安全注意事项

1. **私钥必须离线保管**（不要放进任何代码仓库），泄露等于任何人都能签发激活码
2. **公钥应编译进客户端程序集**（DemoApp 从文件加载仅为演示），防止被替换
3. 客户端校验逻辑本身是软件保护范畴——如需对抗逆向，可叠加混淆/壳等加固手段
4. 到期判断基于客户端本地时钟（离线限制）；对时钟篡改敏感的场合可加"时间单调性检测"
