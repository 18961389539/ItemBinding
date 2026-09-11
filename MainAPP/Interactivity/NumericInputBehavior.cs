using Microsoft.Xaml.Behaviors;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MainAPP.Interactivity
{
    /// <summary>
    /// 限制 TextBox 只能输入数字（可选支持小数/负号）。
    /// 替代 RecipeWindow.xaml.cs 中的 NumberValidation_PreviewTextInput/Pasting。
    /// </summary>
    public class NumericInputBehavior : Behavior<TextBox>
    {
        /// <summary>是否允许小数点</summary>
        public bool AllowDecimal { get; set; } = true;

        /// <summary>是否允许负号</summary>
        public bool AllowNegative { get; set; } = false;

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.PreviewTextInput += OnPreviewTextInput;
            DataObject.AddPastingHandler(AssociatedObject, OnPasting);
        }

        protected override void OnDetaching()
        {
            DataObject.RemovePastingHandler(AssociatedObject, OnPasting);
            AssociatedObject.PreviewTextInput -= OnPreviewTextInput;
            base.OnDetaching();
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsValid(e.Text);
        }

        private void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var text = e.DataObject.GetData(DataFormats.Text) as string;
                if (text == null || !IsValid(text))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        private bool IsValid(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // 构造允许字符的正则
            var pattern = @"^\d+$";
            if (AllowDecimal) pattern = @"^\d*\.?\d*$";
            if (AllowNegative) pattern = AllowDecimal ? @"^-?\d*\.?\d*$" : @"^-?\d*$";
            return Regex.IsMatch(text, pattern);
        }
    }
}
