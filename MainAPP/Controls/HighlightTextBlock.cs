using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MainAPP.Controls
{
    /// <summary>
    /// 支持关键字行内高亮的 TextBlock：把 <see cref="PlainText"/> 中所有与 <see cref="HighlightText"/>
    /// 匹配的片段（大小写不敏感）以琥珀色背景 + 深色文字渲染。
    ///
    /// ★ 实现约束（2026-09-12 修复）：高亮文本必须绑定到自有的 <see cref="PlainText"/> 依赖属性，
    /// **禁止绑定继承的 Text 属性**。原因：在 Text 的属性变更回调里修改 Inlines 会与 TextBlock
    /// 内部的 Text-DP 同步互相干扰（回调里写 Inlines → 内部回写 Text 值），叠加 DataGrid 行回收
    /// 虚拟化后绑定/文本状态被破坏，表现为内容列全空白。PlainText 不触碰 Text 绑定，无此冲突。
    /// </summary>
    public class HighlightTextBlock : TextBlock
    {
        private static readonly SolidColorBrush HighlightBackground = CreateFrozenBrush("#FAC775");
        private static readonly SolidColorBrush HighlightForeground = CreateFrozenBrush("#1A1206");

        private bool _isRebuilding;

        public HighlightTextBlock()
        {
            RebuildInlines();
        }

        public static readonly DependencyProperty PlainTextProperty = DependencyProperty.Register(
            nameof(PlainText),
            typeof(string),
            typeof(HighlightTextBlock),
            new PropertyMetadata(string.Empty, OnTextPropertyChanged));

        /// <summary>要显示的完整文本（替代绑定继承的 Text——见类注释的实现约束）。</summary>
        public string PlainText
        {
            get => (string)GetValue(PlainTextProperty);
            set => SetValue(PlainTextProperty, value);
        }

        public static readonly DependencyProperty HighlightTextProperty = DependencyProperty.Register(
            nameof(HighlightText),
            typeof(string),
            typeof(HighlightTextBlock),
            new PropertyMetadata(string.Empty, OnTextPropertyChanged));

        /// <summary>要高亮的关键字（大小写不敏感）。空/空白表示不高亮。</summary>
        public string HighlightText
        {
            get => (string)GetValue(HighlightTextProperty);
            set => SetValue(HighlightTextProperty, value);
        }

        private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((HighlightTextBlock)d).RebuildInlines();
        }

        private static SolidColorBrush CreateFrozenBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private void RebuildInlines()
        {
            // 修改 Inlines 会反写内部文本状态，可能再次触发本方法——用守卫打断递归
            if (_isRebuilding)
            {
                return;
            }

            _isRebuilding = true;
            try
            {
                Inlines.Clear();
                var text = PlainText ?? string.Empty;
                if (text.Length == 0)
                {
                    return;
                }

                var highlight = HighlightText?.Trim() ?? string.Empty;
                if (highlight.Length == 0)
                {
                    Inlines.Add(new Run(text));
                    return;
                }

                // 大小写不敏感地找出全部匹配区间
                var matches = new List<(int Start, int End)>();
                int searchFrom = 0;
                while (true)
                {
                    int pos = text.IndexOf(highlight, searchFrom, StringComparison.OrdinalIgnoreCase);
                    if (pos < 0)
                    {
                        break;
                    }

                    matches.Add((pos, pos + highlight.Length));
                    searchFrom = pos + highlight.Length;
                }

                if (matches.Count == 0)
                {
                    Inlines.Add(new Run(text));
                    return;
                }

                int cursor = 0;
                foreach (var (start, end) in matches)
                {
                    if (start > cursor)
                    {
                        Inlines.Add(new Run(text[cursor..start]));
                    }

                    Inlines.Add(new Run(text[start..end])
                    {
                        Background = HighlightBackground,
                        Foreground = HighlightForeground,
                    });
                    cursor = end;
                }

                if (cursor < text.Length)
                {
                    Inlines.Add(new Run(text[cursor..]));
                }
            }
            finally
            {
                _isRebuilding = false;
            }
        }
    }
}
