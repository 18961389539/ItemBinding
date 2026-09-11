using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Effects;

namespace ImageViewer.Rendering
{
    internal static class PseudoColorShaderCompiler
    {
        private const uint D3DCompileOptimizationLevel3 = 1u << 15;
        private static readonly Lazy<PixelShader> Shader = new(CreatePixelShader, isThreadSafe: true);

        public static PixelShader GetPixelShader() => Shader.Value;

        private static PixelShader CreatePixelShader()
        {
            byte[] sourceBytes = LoadShaderSource();
            IntPtr codePointer = IntPtr.Zero;
            IntPtr errorPointer = IntPtr.Zero;

            try
            {
                int hr = D3DCompile(
                    sourceBytes,
                    (nuint)sourceBytes.Length,
                    "PseudoColorEffect.ps.hlsl",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    "main",
                    "ps_2_0",
                    D3DCompileOptimizationLevel3,
                    0,
                    out codePointer,
                    out errorPointer);

                if (hr < 0)
                {
                    string error = errorPointer != IntPtr.Zero ? ReadBlob(errorPointer) : $"HRESULT 0x{hr:X8}";
                    throw new InvalidOperationException($"Failed to compile pseudo color shader. {error}");
                }

                byte[] bytecode = ReadBlobBytes(codePointer);
                var pixelShader = new PixelShader();
                using var stream = new MemoryStream(bytecode, writable: false);
                pixelShader.SetStreamSource(stream);
                if (pixelShader.CanFreeze)
                {
                    pixelShader.Freeze();
                }

                return pixelShader;
            }
            finally
            {
                ReleaseBlob(errorPointer);
                ReleaseBlob(codePointer);
            }
        }

        private static byte[] LoadShaderSource()
        {
            Assembly assembly = typeof(PseudoColorShaderCompiler).Assembly;
            string? resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("Shaders.PseudoColorEffect.ps.hlsl", StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                throw new InvalidOperationException("Pseudo-color shader source resource was not found.");
            }

            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("Unable to open pseudo-color shader source resource stream.");
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }

        private static string ReadBlob(IntPtr blobPointer)
        {
            return Encoding.UTF8.GetString(ReadBlobBytes(blobPointer));
        }

        private static byte[] ReadBlobBytes(IntPtr blobPointer)
        {
            var blob = (ID3DBlob)Marshal.GetObjectForIUnknown(blobPointer);
            try
            {
                int size = checked((int)blob.GetBufferSize());
                byte[] data = new byte[size];
                Marshal.Copy(blob.GetBufferPointer(), data, 0, size);
                return data;
            }
            finally
            {
                Marshal.ReleaseComObject(blob);
            }
        }

        private static void ReleaseBlob(IntPtr blobPointer)
        {
            if (blobPointer != IntPtr.Zero)
            {
                Marshal.Release(blobPointer);
            }
        }

#pragma warning disable CA2101
        [DllImport("d3dcompiler_47.dll", ExactSpelling = true)]
        private static extern int D3DCompile(
            byte[] srcData,
            nuint srcDataSize,
            [MarshalAs(UnmanagedType.LPStr)]
            string sourceName,
            IntPtr defines,
            IntPtr include,
            [MarshalAs(UnmanagedType.LPStr)]
            string entryPoint,
            [MarshalAs(UnmanagedType.LPStr)]
            string target,
            uint flags1,
            uint flags2,
            out IntPtr code,
            out IntPtr errorMsgs);
#pragma warning restore CA2101

        [ComImport]
        [Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ID3DBlob
        {
            [PreserveSig]
            IntPtr GetBufferPointer();

            [PreserveSig]
            nuint GetBufferSize();
        }
    }
}
