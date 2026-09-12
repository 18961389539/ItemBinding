using System;
using System.Windows;
using System.Windows.Controls;
using MainAPP.Services;
using Microsoft.Web.WebView2.Core;

namespace MainAPP.Views
{
    /// <summary>
    /// AI 对话面板（浏览器级体验）。
    ///
    /// <para>UI 全部由 Deep Chat（MIT，Web Component）承担——气泡、Markdown、代码高亮、流式输出、
    /// 打字指示都不在 C# 侧写。本控件只做两件事：把 <see cref="CoreWebView2"/> 指到
    /// <see cref="AiWebHost"/> 的本地端点，以及在 WebView2 Runtime 缺失时给出可操作的兜底提示。</para>
    ///
    /// <para>模型生命周期由 <see cref="AiChatService"/> 的按需加载管理：首个提问触发
    /// <c>EnsureLoadedAsync</c>，本控件不参与加载，避免打开标签页就占显存。</para>
    /// </summary>
    public partial class AiChatView : UserControl
    {
        public AiChatView()
        {
            InitializeComponent();

            // WebView2 Runtime 缺失时 InitializeCoreWebView2Async 会失败，
            // 若不在此拦截会表现为"白屏 + 无提示"，现场无从排查（参见本机对 CameraWebHost 的经验）。
            ChatWebView.CoreWebView2InitializationCompleted += OnCoreWebView2InitializationCompleted;
        }

        private void OnCoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                LogService.Instance.Info("AI 对话面板 WebView2 初始化完成");
                return;
            }

            var message = e.InitializationException?.Message ?? "未知原因";
            LogService.Instance.Error($"AI 对话面板 WebView2 初始化失败: {message}");
            FallbackText.Text = "WebView2 运行时不可用，无法显示 AI 对话面板。\n" +
                                "请安装 Microsoft Edge WebView2 Runtime 后重启本程序。\n" +
                                $"原因：{message}";
            FallbackText.Visibility = Visibility.Visible;
            ChatWebView.Visibility = Visibility.Collapsed;
        }
    }
}
