using System.Globalization;
using System.Security.Cryptography;
using LicenseCore;

namespace LicenseTool;

/// <summary>
/// 命令行激活码生成工具。
/// 用法：
///   licensetool newkey --out &lt;目录&gt;                    生成密钥对（license_private.pem / license_public.pem）
///   licensetool gen --key &lt;私钥&gt; --machine &lt;机器码&gt;
///                    [--product &lt;id&gt;] [--days &lt;N&gt; | --expire &lt;yyyy-MM-dd&gt;]
///                    [--machines &lt;N&gt;] [--type single|universal]
///   licensetool batch --key &lt;私钥&gt; --csv &lt;文件&gt; [--out &lt;目录&gt;]
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            return args[0].ToLowerInvariant() switch
            {
                "newkey" => NewKey(args),
                "gen" => GenerateOne(args),
                "batch" => GenerateBatch(args),
                _ => Unknown(args),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 1;
        }
    }

    private static int NewKey(string[] args)
    {
        var options = ParseArgs(args);
        var outDir = options.Get("--out") ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outDir);

        var (privatePem, publicPem) = LicenseKeys.CreateKeyPair();
        var privPath = Path.Combine(outDir, "license_private.pem");
        var pubPath = Path.Combine(outDir, "license_public.pem");
        File.WriteAllText(privPath, privatePem);
        File.WriteAllText(pubPath, publicPem);

        Console.WriteLine($"已生成密钥对:");
        Console.WriteLine($"  私钥(厂商保管): {privPath}");
        Console.WriteLine($"  公钥(内置客户端): {pubPath}");
        Console.WriteLine("警告: 私钥泄露等于任何人都能签发激活码，务必妥善保管!");
        return 0;
    }

    private static int GenerateOne(string[] args)
    {
        var options = ParseArgs(args);
        var keyPath = options.Require("--key", "请用 --key 指定私钥 PEM 文件");
        var machine = options.Require("--machine", "请用 --machine 指定机器码");
        var productId = ushort.Parse(options.Get("--product") ?? "1", CultureInfo.InvariantCulture);
        var type = (options.Get("--type") ?? "single").ToLowerInvariant() == "universal"
            ? ActivationPayload.TypeUniversal : ActivationPayload.TypeSingleMachine;
        var maxMachines = byte.Parse(options.Get("--machines") ?? "1", CultureInfo.InvariantCulture);
        var expireUnix = ParseExpire(options);

        using var privateKey = LicenseKeys.LoadPrivateKey(File.ReadAllText(keyPath));

        // 单机绑定码需要机器哈希；通用码机器哈希置 0
        byte[] machineHash;
        if (type == ActivationPayload.TypeSingleMachine)
        {
            var fp = MachineCode.TryParse(machine)
                     ?? throw new ArgumentException("机器码格式非法（应为 4 组 Base32 字符，如 XXXXX-XXXXX-XXXXX-XXXXX）");
            machineHash = MachineCode.ComputeHash(fp);
        }
        else
        {
            machineHash = new byte[8];
        }

        var payload = ActivationPayload.Create(type, productId, machineHash, expireUnix, maxMachines);
        var code = ActivationCode.Generate(privateKey, payload);
        Console.WriteLine(code);
        return 0;
    }

    private static int GenerateBatch(string[] args)
    {
        var options = ParseArgs(args);
        var keyPath = options.Require("--key", "请用 --key 指定私钥 PEM 文件");
        var csvPath = options.Require("--csv", "请用 --csv 指定 CSV 文件");
        var outDir = options.Get("--out") ?? Path.Combine(Directory.GetCurrentDirectory(), "licenses");
        Directory.CreateDirectory(outDir);

        // CSV 列：机器码,产品ID,到期(天数N或日期yyyy-MM-dd或留空=永久),机器数(可选,默认1)
        var lines = File.ReadAllLines(csvPath).Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'));
        using var privateKey = LicenseKeys.LoadPrivateKey(File.ReadAllText(keyPath));

        int count = 0;
        foreach (var raw in lines)
        {
            var cols = raw.Split(',');
            if (cols.Length < 3)
            {
                Console.Error.WriteLine($"跳过无效行: {raw}");
                continue;
            }

            var machine = cols[0].Trim();
            var productId = ushort.Parse(cols[1].Trim(), CultureInfo.InvariantCulture);
            var expire = ParseExpireCell(cols[2].Trim());
            var machines = cols.Length > 3 && !string.IsNullOrWhiteSpace(cols[3])
                ? byte.Parse(cols[3].Trim(), CultureInfo.InvariantCulture)
                : (byte)1;

            var fp = MachineCode.TryParse(machine)
                     ?? throw new ArgumentException($"机器码格式非法: {machine}");
            var payload = ActivationPayload.Create(
                ActivationPayload.TypeSingleMachine, productId,
                MachineCode.ComputeHash(fp), expire, machines);
            var code = ActivationCode.Generate(privateKey, payload);

            var safeName = new string(machine.Where(char.IsLetterOrDigit).ToArray());
            File.WriteAllText(Path.Combine(outDir, $"{safeName}.lic"), code + Environment.NewLine);
            Console.WriteLine($"{machine} → {code}");
            count++;
        }

        Console.WriteLine($"完成: 生成 {count} 条激活码 → {outDir}");
        return 0;
    }

    private static uint ParseExpire(Dictionary<string, string> options)
    {
        if (options.TryGetValue("--expire", out var dateStr) && !string.IsNullOrWhiteSpace(dateStr))
        {
            var date = DateTime.ParseExact(dateStr.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return (uint)new DateTimeOffset(date, TimeSpan.Zero).ToUnixTimeSeconds();
        }
        if (options.TryGetValue("--days", out var daysStr) && !string.IsNullOrWhiteSpace(daysStr))
        {
            var days = int.Parse(daysStr, CultureInfo.InvariantCulture);
            if (days <= 0) return 0; // 0/负数 = 永久
            return (uint)DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeSeconds();
        }
        return 0; // 默认永久
    }

    private static uint ParseExpireCell(string cell)
    {
        if (string.IsNullOrEmpty(cell) || cell.Equals("0", StringComparison.Ordinal)) return 0;
        if (int.TryParse(cell, out var days)) return days <= 0 ? 0u : (uint)DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeSeconds();
        var date = DateTime.ParseExact(cell, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (uint)new DateTimeOffset(date, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--") && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                map[args[i].ToLowerInvariant()] = args[i + 1];
                i++;
            }
        }
        return map;
    }

    private static string? Get(this Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var v) ? v : null;

    private static string Require(this Dictionary<string, string> map, string key, string message) =>
        map.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)
            ? v
            : throw new ArgumentException(message);

    private static int Unknown(string[] args)
    {
        Console.Error.WriteLine($"未知命令: {args[0]}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            LicenseTool — 离线激活码生成工具

            用法:
              licensetool newkey --out <目录>
                    生成密钥对 (license_private.pem 厂商保管 / license_public.pem 内置客户端)

              licensetool gen --key <私钥.pem> --machine <机器码>
                              [--product <产品ID,默认1>] [--days <N> | --expire <yyyy-MM-dd>]
                              [--machines <N,默认1>] [--type single|universal]
                    单条生成激活码, 输出到 stdout

              licensetool batch --key <私钥.pem> --csv <文件.csv> [--out <目录>]
                    CSV 批量生成 (列: 机器码,产品ID,到期天数或日期或留空,机器数)
                    # 开头为注释行

            示例:
              licensetool newkey --out ./keys
              licensetool gen --key ./keys/license_private.pem --machine ABCDF-GHJKM-NPQRV-23456 --days 365
              licensetool gen --key ./keys/license_private.pem --machine ABCDF-GHJKM-NPQRV-23456 --expire 2027-12-31 --machines 5
            """);
    }
}
