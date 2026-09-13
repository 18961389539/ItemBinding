using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MainAPP.Models;
using MainAPP.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Data;

namespace MainAPP.ViewModels;

/// <summary>
/// 设置页"通讯协议"Tab 的 ViewModel（2026-09-13）。
/// <para>持有自定义协议的增删改、段编辑（可上下移）、策略配置、搜索/停用置底，
/// "试算预览"（逐段命中高亮 + 与生产同一渲染实现），以及"发送测试报文"（真正发 UDP 验证链路）。</para>
/// </summary>
public partial class ProtocolSettingsViewModel : ObservableObject
{
    private readonly Settings _settings = Settings.Instance;

    /// <summary>段条件枚举（供 XAML ComboBox ItemsSource）。</summary>
    public static IReadOnlyList<string> SegmentConditions { get; } =
        ["Always", "BarcodeNonEmpty", "EncoderValid", "AngleKnown"];

    /// <summary>无码行为枚举。</summary>
    public static IReadOnlyList<string> NoBarcodeBehaviors { get; } =
        ["Blank", "Placeholder", "RejectMessage"];

    /// <summary>行结束符枚举。</summary>
    public static IReadOnlyList<string> LineEndings { get; } =
        ["", "CRLF", "LF"];

    public ObservableCollection<ProtocolTemplateConfig> Protocols { get; } = [];

    /// <summary>列表视图：搜索过滤 + 启用置顶（停用置底）。</summary>
    public ICollectionView ProtocolsView { get; }

    /// <summary>协议列表加载（默认模板兜底，保证开箱可用）。</summary>
    public ProtocolSettingsViewModel()
    {
        var fromStorage = _settings.Protocol?.Protocols;
        if (fromStorage is { Count: > 0 })
        {
            foreach (var p in fromStorage)
            {
                Protocols.Add(p);
            }
        }
        else
        {
            var sample = new ProtocolTemplateConfig
            {
                Name = "机器人A",
                Enabled = true,
                Transport = "UDP",
                EndPoint = "192.168.1.50:9001",
                LineEnding = "CRLF",
            };
            sample.Segments.Add(new ProtocolSegment { Condition = "Always", Text = "[X]$X[Y]$Y[RZ]$RZ" });
            sample.Segments.Add(new ProtocolSegment { Condition = "BarcodeNonEmpty", Text = "[ID]$BC" });
            sample.Segments.Add(new ProtocolSegment { Condition = "EncoderValid", Text = "[E]$ENC" });
            Protocols.Add(sample);
        }

        ProtocolsView = CollectionViewSource.GetDefaultView(Protocols);
        ProtocolsView.Filter = FilterProtocol;
        ProtocolsView.SortDescriptions.Add(
            new SortDescription(nameof(ProtocolTemplateConfig.Enabled), ListSortDirection.Descending));

        Selected = Protocols.FirstOrDefault();
    }

    private bool FilterProtocol(object o)
    {
        if (string.IsNullOrWhiteSpace(SearchText) || o is not ProtocolTemplateConfig p)
        {
            return true;
        }

        return p.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => ProtocolsView.Refresh();

    /// <summary>主页「接收方协议」当前值（决定哪个协议在生效）。保存后刷新。</summary>
    [ObservableProperty]
    private string _currentReceiver = Settings.Instance.MessageReceiver;

    [ObservableProperty]
    private ProtocolTemplateConfig? _selected;

    partial void OnSelectedChanged(ProtocolTemplateConfig? value) => UpdatePreview();

    /// <summary>选中协议是否正处于使用中（名称与主页接收方一致）。</summary>
    public bool IsCurrentInUse
        => Selected is not null
           && !string.IsNullOrWhiteSpace(CurrentReceiver)
           && string.Equals(Selected.Name, CurrentReceiver, StringComparison.OrdinalIgnoreCase);

    /// <summary>使用中徽章文案。</summary>
    public string InUseBadgeText
        => IsCurrentInUse ? $"使用中（接收方= {CurrentReceiver}）" : $"接收方协议当前 = {CurrentReceiver}";

    // ---- 协议增删 ----

    [RelayCommand]
    private void AddProtocol()
    {
        var p = new ProtocolTemplateConfig
        {
            Name = $"新协议{Protocols.Count + 1}",
            EndPoint = "192.168.1.50:9001",
            LineEnding = "CRLF",
        };
        p.Segments.Add(new ProtocolSegment { Condition = "Always", Text = "[X]$X[Y]$Y[RZ]$RZ" });
        Protocols.Add(p);
        Selected = p;
    }

    [RelayCommand]
    private void DeleteProtocol()
    {
        if (Selected is not null)
        {
            Protocols.Remove(Selected);
            Selected = Protocols.FirstOrDefault();
        }
    }

    // ---- 段编辑（增删 + 上下移）----

    [RelayCommand]
    private void AddSegment()
    {
        if (Selected is null)
        {
            return;
        }

        Selected.Segments.Add(new ProtocolSegment { Condition = "Always", Text = string.Empty });
        UpdatePreview();
    }

    [RelayCommand]
    private void RemoveSegment(ProtocolSegment? segment)
    {
        if (Selected is null || segment is null)
        {
            return;
        }

        Selected.Segments.Remove(segment);
        UpdatePreview();
    }

    [RelayCommand]
    private void MoveSegmentUp(ProtocolSegment? segment)
    {
        if (Selected is null || segment is null)
        {
            return;
        }

        var list = Selected.Segments;
        var i = list.IndexOf(segment);
        if (i > 0)
        {
            (list[i - 1], list[i]) = (list[i], list[i - 1]);
            UpdatePreview();
        }
    }

    [RelayCommand]
    private void MoveSegmentDown(ProtocolSegment? segment)
    {
        if (Selected is null || segment is null)
        {
            return;
        }

        var list = Selected.Segments;
        var i = list.IndexOf(segment);
        if (i >= 0 && i < list.Count - 1)
        {
            (list[i + 1], list[i]) = (list[i], list[i + 1]);
            UpdatePreview();
        }
    }

    // ---- 段命中预览（试算时逐段标注）----

    /// <summary>单段试算预览：命中/未命中 + 状态文案。</summary>
    public sealed record SegmentPreview(ProtocolSegment Segment, int Index, bool Matched, string Status);

    public ObservableCollection<SegmentPreview> SegmentPreviews { get; } = [];

    // ---- 试算样例输入 ----

    [ObservableProperty]
    private string _sampleX = "12.3456";

    [ObservableProperty]
    private string _sampleY = "56.7890";

    [ObservableProperty]
    private string _sampleRz = "90.1234";

    [ObservableProperty]
    private string _sampleBarcode = "B2C3D4";

    [ObservableProperty]
    private string _sampleEncoder = string.Empty;

    partial void OnSampleXChanged(string value) => UpdatePreview();
    partial void OnSampleYChanged(string value) => UpdatePreview();
    partial void OnSampleRzChanged(string value) => UpdatePreview();
    partial void OnSampleBarcodeChanged(string value) => UpdatePreview();
    partial void OnSampleEncoderChanged(string value) => UpdatePreview();

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private string _previewNote = string.Empty;

    [ObservableProperty]
    private bool _previewRejected;

    private void UpdatePreview()
    {
        SegmentPreviews.Clear();
        OnPropertyChanged(nameof(IsCurrentInUse));
        OnPropertyChanged(nameof(InUseBadgeText));
        if (Selected is null)
        {
            PreviewText = string.Empty;
            PreviewNote = string.Empty;
            PreviewRejected = false;
            return;
        }

        var m = new DbModel
        {
            WorldX = ParseSample(SampleX),
            WorldY = ParseSample(SampleY),
            Angle = ParseSample(SampleRz),
            Barcode = string.IsNullOrWhiteSpace(SampleBarcode) ? "noread" : SampleBarcode.Trim(),
            Encode = uint.TryParse(SampleEncoder, out var enc) ? enc : 0u,
        };

        // 逐段命中标注（与 Render 内部同判据）
        var segments = Selected.Segments ?? [];
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var matched = ProtocolTemplateRenderer.IsSegmentSatisfied(seg, m);
            SegmentPreviews.Add(new SegmentPreview(seg, i + 1, matched, SegmentStatusText(seg, m)));
        }

        var rendered = ProtocolTemplateRenderer.Render(m, Selected);
        PreviewRejected = rendered is null;
        if (rendered is null)
        {
            PreviewText = "（拒发：无码 RejectMessage 或 角度未知）";
            PreviewNote = "本条将被整条拒发，不发送给下游。";
        }
        else
        {
            PreviewText = rendered.TrimEnd('\r', '\n');
            PreviewNote = m.Encode == 0
                ? "样例编码器留空 → Encoder 段省略（若配置了）"
                : string.IsNullOrWhiteSpace(SampleBarcode)
                    ? "样例无码 → Barcode 段省略（若配置了）"
                    : "样例完整，各条件段按条件拼接。";
        }
    }

    private static string SegmentStatusText(ProtocolSegment seg, DbModel m)
    {
        if (seg.Condition == "Always")
        {
            return "恒定输出";
        }

        return ProtocolTemplateRenderer.IsSegmentSatisfied(seg, m)
            ? "条件满足 ✔"
            : "条件未满足（跳过）";
    }

    private static double ParseSample(string s)
        => double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    // ---- 发送测试报文（真正发 UDP，验证链路与格式）----

    [ObservableProperty]
    private string _testSendResult = string.Empty;

    [ObservableProperty]
    private bool _testSendOk;

    [RelayCommand]
    private void SendTest()
    {
        TestSendResult = string.Empty;
        if (Selected is null)
        {
            TestSendResult = "未选择协议";
            return;
        }

        if (PreviewRejected)
        {
            TestSendResult = "当前试算被拒发（无码 RejectMessage 或角度未知），无法发送";
            return;
        }

        if (!TryParseEndPoint(Selected.EndPoint, out var host, out var port))
        {
            TestSendResult = $"端点 {Selected.EndPoint} 无法解析（仅支持 IP:端口）";
            return;
        }

        try
        {
            var data = Encoding.UTF8.GetBytes(PreviewText);
            using var client = new UdpClient();
            client.Send(data, data.Length, host, port);
            TestSendOk = true;
            TestSendResult = $"已发送 {data.Length} 字节 → {Selected.EndPoint}（{data.Length} 字节，UTF-8）";
        }
        catch (Exception ex)
        {
            TestSendOk = false;
            TestSendResult = $"发送失败：{ex.Message}";
        }
    }

    private static bool TryParseEndPoint(string ep, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(ep))
        {
            return false;
        }

        var idx = ep.LastIndexOf(':');
        if (idx <= 0 || idx == ep.Length - 1 || !ushort.TryParse(ep[(idx + 1)..], out var p))
        {
            return false;
        }

        var h = ep[..idx];
        if (!IPAddress.TryParse(h, out _))
        {
            return false;
        }

        host = h;
        port = p;
        return true;
    }

    // ---- 保存 ----

    [RelayCommand]
    private void Save()
    {
        // 停用项置底（重排序绑定源）
        var ordered = Protocols.OrderByDescending(p => p.Enabled).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (!ReferenceEquals(Protocols[i], ordered[i]))
            {
                Protocols.Move(Protocols.IndexOf(ordered[i]), i);
            }
        }

        _settings.Protocol.Protocols = ordered;
        _settings.Save();
        CurrentReceiver = Settings.Instance.MessageReceiver;
        OnPropertyChanged(nameof(IsCurrentInUse));
        OnPropertyChanged(nameof(InUseBadgeText));
        NotificationService.Success("通讯协议已保存并生效。");
    }
}