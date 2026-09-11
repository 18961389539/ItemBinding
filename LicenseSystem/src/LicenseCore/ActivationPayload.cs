using System.Buffers.Binary;

namespace LicenseCore;

/// <summary>
/// 激活码明文载荷（21 字节，固定长度序列化）：
/// Version(1) + Type(1) + ProductId(2) + MachineHash(8) + ExpireUnix(4) + MaxMachines(1) + Salt(4)
/// </summary>
public readonly struct ActivationPayload
{
    /// <summary>结构版本，当前恒为 1。</summary>
    public const byte Version = 1;

    /// <summary>授权类型：0 = 单机绑定码（绑定机器哈希），1 = 通用码（不绑机器）。</summary>
    public const byte TypeSingleMachine = 0;
    /// <summary>授权类型：1 = 通用码（不绑机器哈希，机器哈希段填全 0）。</summary>
    public const byte TypeUniversal = 1;

    /// <summary>授权类型（0 = 单机绑定，1 = 通用）。</summary>
    public byte Type { get; init; }
    /// <summary>产品标识（对应生成激活码时的 --product）。</summary>
    public ushort ProductId { get; init; }
    /// <summary>机器码哈希（8 字节）。Type=单机绑定 时有效；通用码为全 0。</summary>
    public byte[] MachineHash { get; init; }
    /// <summary>到期时间（Unix 秒）。0 = 永不过期。</summary>
    public uint ExpireUnix { get; init; }
    /// <summary>声明的授权机器数（0 = 不限制；厂商管理端据此统计）。</summary>
    public byte MaxMachines { get; init; }
    /// <summary>随机盐（4 字节），保证同参数生成的激活码各不相同。</summary>
    public byte[] Salt { get; init; }

    /// <summary>
    /// 创建激活载荷（含参数校验）。
    /// </summary>
    /// <param name="type">授权类型（<see cref="TypeSingleMachine"/> / <see cref="TypeUniversal"/>）。</param>
    /// <param name="productId">产品标识。</param>
    /// <param name="machineHash">机器码哈希（8 字节）；通用码传 8 字节全 0。</param>
    /// <param name="expireUnix">到期时间（Unix 秒），0 = 永不过期。</param>
    /// <param name="maxMachines">声明的授权机器数（0 = 不限制）。</param>
    /// <param name="salt">随机盐（4 字节）；省略则自动生成，保证同参数激活码各不相同。</param>
    /// <exception cref="ArgumentOutOfRangeException">type 不是 0/1 时抛出。</exception>
    /// <exception cref="ArgumentException">machineHash 长度不为 8 时抛出。</exception>
    public static ActivationPayload Create(
        byte type, ushort productId, byte[] machineHash, uint expireUnix, byte maxMachines, byte[]? salt = null)
    {
        if (type is not (TypeSingleMachine or TypeUniversal))
            throw new ArgumentOutOfRangeException(nameof(type), "Type 仅支持 0(单机绑定)/1(通用)");
        if (machineHash.Length != 8)
            throw new ArgumentException("机器哈希必须为 8 字节", nameof(machineHash));

        return new ActivationPayload
        {
            Type = type,
            ProductId = productId,
            MachineHash = (byte[])machineHash.Clone(),
            ExpireUnix = expireUnix,
            MaxMachines = maxMachines,
            Salt = salt ?? RandomNumberGeneratorBytes(4),
        };
    }

    /// <summary>序列化为 21 字节固定长度载荷。</summary>
    public byte[] Serialize()
    {
        var buffer = new byte[21];
        buffer[0] = Version;
        buffer[1] = Type;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2, 2), ProductId);
        MachineHash.CopyTo(buffer.AsSpan(4, 8));
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(12, 4), ExpireUnix);
        buffer[16] = MaxMachines;
        Salt.CopyTo(buffer.AsSpan(17, 4));
        return buffer;
    }

    /// <summary>从 21 字节反序列化。长度不符抛出 <see cref="FormatException"/>。</summary>
    public static ActivationPayload Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length != 21)
            throw new FormatException($"激活码载荷长度错误（期望 21，实际 {buffer.Length}）");
        if (buffer[0] != Version)
            throw new FormatException($"激活码版本不支持（{buffer[0]}）");

        return new ActivationPayload
        {
            Type = buffer[1],
            ProductId = BinaryPrimitives.ReadUInt16BigEndian(buffer[2..4]),
            MachineHash = buffer[4..12].ToArray(),
            ExpireUnix = BinaryPrimitives.ReadUInt32BigEndian(buffer[12..16]),
            MaxMachines = buffer[16],
            Salt = buffer[17..21].ToArray(),
        };
    }

    /// <summary>到期时间转换为本地时间；0 表示永不过期返回 null。</summary>
    public DateTime? ExpireAtUtc => ExpireUnix == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(ExpireUnix).UtcDateTime;

    private static byte[] RandomNumberGeneratorBytes(int length)
    {
        var bytes = new byte[length];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return bytes;
    }
}
