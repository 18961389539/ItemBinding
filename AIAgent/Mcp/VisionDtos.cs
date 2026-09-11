namespace AIAgent.Mcp;

public sealed record PointDto
{
    public int X { get; init; }

    public int Y { get; init; }
}

public sealed record BarcodeDto
{
    public string CodeType { get; init; } = string.Empty;

    public string CodeString { get; init; } = string.Empty;

    public uint Confidence { get; init; }

    public PointDto[] Location { get; init; } = [];
}

public sealed record ScannerDeviceInfoDto
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public int Index { get; init; }

    public string ModelName { get; init; } = string.Empty;

    public string SerialNumber { get; init; } = string.Empty;

    public string UserDefinedName { get; init; } = string.Empty;

    public string DeviceVersion { get; init; } = string.Empty;

    public string MacAddress { get; init; } = string.Empty;

    public string IpAddress { get; init; } = string.Empty;

    public string SubnetMask { get; init; } = string.Empty;

    public string DefaultGateway { get; init; } = string.Empty;

    public string InterfaceType { get; init; } = string.Empty;

    public bool IsOpen { get; init; }

    public bool IsGrabbing { get; init; }
}

public sealed record ScannerSettingsDto
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public string DeviceName { get; init; } = string.Empty;

    public string SerialNumber { get; init; } = string.Empty;

    public double ExposureTime { get; init; }

    public double Gain { get; init; }

    public string TriggerMode { get; init; } = string.Empty;

    public string TriggerSource { get; init; } = string.Empty;
}

public sealed record ScannerCountDto
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public int Count { get; init; }
}

public sealed record CaptureDto
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public int Width { get; init; }

    public int Height { get; init; }

    public string PixelFormat { get; init; } = string.Empty;

    public ulong FrameNumber { get; init; }

    public bool AutoSaved { get; init; }

    public string SavedImagePath { get; init; } = string.Empty;

    public IReadOnlyList<BarcodeDto> Barcodes { get; init; } = [];

    public IReadOnlyList<BarcodeDto> QrCodes { get; init; } = [];
}

public sealed record OverlayDto
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public int QrCount { get; init; }

    public IReadOnlyList<BarcodeDto> QrCodes { get; init; } = [];
}

public sealed record InferenceDto
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public string ModelPath { get; init; } = string.Empty;

    public string Task { get; init; } = string.Empty;

    public int PredictionCount { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<string> Predictions { get; init; } = [];
}

public sealed record ImageProcessDto
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public string Operation { get; init; } = string.Empty;

    public double Amount { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}