sampler2D Input : register(s0);
float PaletteIndex : register(c0);

float luminance(float3 rgb)
{
    return dot(rgb, float3(0.299, 0.587, 0.114));
}

float3 hotPalette(float t)
{
    return saturate(float3(t * 3.0, t * 3.0 - 1.0, t * 3.0 - 2.0));
}

float3 jetPalette(float t)
{
    float r = saturate(1.5 - abs(4.0 * t - 3.0));
    float g = saturate(1.5 - abs(4.0 * t - 2.0));
    float b = saturate(1.5 - abs(4.0 * t - 1.0));
    return float3(r, g, b);
}

float3 viridisSegment(float t, float startT, float endT, float3 startColor, float3 endColor)
{
    float amount = saturate((t - startT) / (endT - startT));
    return lerp(startColor, endColor, amount);
}

float3 viridisPalette(float t)
{
    if (t < 0.33)
    {
        return viridisSegment(t, 0.0, 0.33, float3(0.2667, 0.0039, 0.3294), float3(0.2314, 0.3216, 0.5451));
    }

    if (t < 0.66)
    {
        return viridisSegment(t, 0.33, 0.66, float3(0.2314, 0.3216, 0.5451), float3(0.1294, 0.5686, 0.5490));
    }

    return viridisSegment(t, 0.66, 1.0, float3(0.1294, 0.5686, 0.5490), float3(0.9922, 0.9059, 0.1451));
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 inputColor = tex2D(Input, uv);
    float intensity = saturate(luminance(inputColor.rgb));

    if (PaletteIndex < 0.5)
    {
        return inputColor;
    }

    if (PaletteIndex < 1.5)
    {
        return float4(hotPalette(intensity), inputColor.a);
    }

    if (PaletteIndex < 2.5)
    {
        return float4(jetPalette(intensity), inputColor.a);
    }

    return float4(viridisPalette(intensity), inputColor.a);
}
