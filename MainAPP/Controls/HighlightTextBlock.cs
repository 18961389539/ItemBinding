using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MainAPP.Controls
{
    /// <summary>
    /// 支持关键字行内高亮的 TextBlock：把 Text 中所有与 HighlightText 匹配的片段
    /// （大小写不敏感）以琥珀色背景 + 深色文字渲染，与日志页深色主题形成强对比。
    /// HighlightText 为空或不匹配时按普通文本渲染。用于日志内容/异常列的搜索词高亮。
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

        static HighlightTextBlock()
        {
            // 监听继承的 Text 属性：文本变化时重建 Inlines
            TextProperty.OverrideMetadata(
                typeof(HighlightTextBlock),
                new FrameworkPropertyMetadata(string.Empty, OnTextPropertyChanged));
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
            // 修改 Inlines 会反写 Text 属性，可能再次触发本方法——用守卫打断递归
            if (_isRebuilding)
            {
                return;
            }

            _isRebuilding = true;
            try
            {
                Inlines.Clear();
                var text = Text ?? string.Empty;
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
