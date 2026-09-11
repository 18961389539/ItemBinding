using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ImageViewer.Services;

namespace ImageViewer.Rendering
{
    internal sealed class PseudoColorShaderEffect : ShaderEffect
    {
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(PseudoColorShaderEffect), 0);

        public static readonly DependencyProperty PaletteIndexProperty = DependencyProperty.Register(
            nameof(PaletteIndex),
            typeof(double),
            typeof(PseudoColorShaderEffect),
            new UIPropertyMetadata(0d, PixelShaderConstantCallback(0)));

        public PseudoColorShaderEffect(PseudoColorPalette palette)
        {
            PixelShader = PseudoColorShaderCompiler.GetPixelShader();
            UpdateShaderValue(InputProperty);
            UpdateShaderValue(PaletteIndexProperty);
            PaletteIndex = (double)palette;
        }

        public Brush Input
        {
            get => (Brush)GetValue(InputProperty);
            set => SetValue(InputProperty, value);
        }

        public double PaletteIndex
        {
            get => (double)GetValue(PaletteIndexProperty);
            set => SetValue(PaletteIndexProperty, value);
        }
    }
}
