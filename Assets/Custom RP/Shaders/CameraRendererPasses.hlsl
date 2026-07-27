#ifndef CUSTOM_CAMERA_RENDERER_PASSES_INCLUDED
#define CUSTOM_CAMERA_RENDERER_PASSES_INCLUDED

TEXTURE2D(_SourceTexture);
TEXTURE2D(_BloomTexture1);
TEXTURE2D(_BloomTexture2);
TEXTURE2D(_BloomTexture3);

float4 _BloomTextureSize;
float _BloomThreshold;
float4 _AverageIlluminance;
float4 _WeatherColor;
float _BloomIntensity;
float _InvGamma;
float _AdjustAlpha;

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : VAR_SCREEN_UV;
};

Varyings DefaultPassVertex(uint vertexID : SV_VertexID)
{
    Varyings output;
    output.positionCS = float4(
        vertexID <= 1 ? -1.0 : 3.0,
        vertexID == 1 ? 3.0 : -1.0,
        0.0, 1.0
    );
    output.screenUV = float2(
        vertexID <= 1 ? 0.0 : 2.0,
        vertexID == 1 ? 2.0 : 0.0
    );
    if (_ProjectionParams.x < 0.0)
    {
        output.screenUV.y = 1.0 - output.screenUV.y;
    }
    return output;
}

float4 CopyPassFragment(Varyings input) : SV_TARGET
{
    return SAMPLE_TEXTURE2D_LOD(_SourceTexture, sampler_linear_clamp, input.screenUV, 0);
}

float CopyDepthPassFragment(Varyings input) : SV_DEPTH
{
    return SAMPLE_DEPTH_TEXTURE_LOD(_SourceTexture, sampler_point_clamp, input.screenUV, 0);
}

float3 ApplyBloomThreshold(float3 color)
{
    color = min(color, 60.0);
    float brightness = max(color.r, max(color.g, color.b));
    float contribution =
        max(brightness - _BloomThreshold, 0.0) /
        max(brightness, 0.00001);
    return color * contribution;
}

float4 BloomPrefilterPassFragment(Varyings input) : SV_TARGET
{
    float2 offset = _BloomTextureSize.xy * 0.5;
    float3 color =
        ApplyBloomThreshold(SAMPLE_TEXTURE2D_LOD(
            _SourceTexture, sampler_linear_clamp,
            input.screenUV + float2(-offset.x, -offset.y), 0
        ).rgb);
    color += ApplyBloomThreshold(SAMPLE_TEXTURE2D_LOD(
        _SourceTexture, sampler_linear_clamp,
        input.screenUV + float2(offset.x, -offset.y), 0
    ).rgb);
    color += ApplyBloomThreshold(SAMPLE_TEXTURE2D_LOD(
        _SourceTexture, sampler_linear_clamp,
        input.screenUV + float2(-offset.x, offset.y), 0
    ).rgb);
    color += ApplyBloomThreshold(SAMPLE_TEXTURE2D_LOD(
        _SourceTexture, sampler_linear_clamp,
        input.screenUV + float2(offset.x, offset.y), 0
    ).rgb);
    return float4(color * 0.25, 1.0);
}

float4 FinalPostFXFragment(Varyings input) : SV_TARGET
{
    // Tex0：包含 Opaque 和 Transparent 的完整 HDR 场景。
    float4 rawSceneColor = SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        input.screenUV,
        0
    );

    // 限制极端 HDR 数值。
    float3 baseColor = min(
        rawSceneColor.rgb,
        float3(60.0, 60.0, 60.0)
    );

    // Tex1、Tex2、Tex3：三张不同分辨率的 Bloom。
    float3 bloomColor =
        SAMPLE_TEXTURE2D_LOD(
            _BloomTexture1,
            sampler_linear_clamp,
            input.screenUV,
            0
        ).rgb * 0.333;

    bloomColor += SAMPLE_TEXTURE2D_LOD(
        _BloomTexture2,
        sampler_linear_clamp,
        input.screenUV,
        0
    ).rgb;
    
    bloomColor += SAMPLE_TEXTURE2D_LOD(
        _BloomTexture3,
        sampler_linear_clamp,
        input.screenUV,
        0
    ).rgb;

    // Bloom 合成。
    float3 colorWithBloom =
        baseColor +
        bloomColor * _BloomIntensity;

    // 曝光。
    float3 exposedColor =
        colorWithBloom /
        max(_AverageIlluminance.x, 0.000001);

    // 对应原 Shader 的 AvageIlluminate.w。
    float shoulder = saturate(
        _AverageIlluminance.w
    );

    // 转换到原 Shader 使用的 ACES 处理空间。
    float3 acesColor = lerp(
        exposedColor.zzz *
        float3(
            0.047377702,
            0.0134532,
            0.86981601
        ) +
        exposedColor.xxx *
        float3(
            0.61309397,
            0.0701956,
            0.0206156
        ) +
        exposedColor.yyy *
        float3(
            0.33952001,
            0.91635698,
            0.10957
        ),
        exposedColor,
        shoulder
    );

    // 项目定制的 ACES fitted 曲线。
    float3 fittedAces =
        acesColor *
        (
            acesColor * 2.51 + 0.03
        ) /
        (
            acesColor *
            (
                acesColor * (2.43 - shoulder) +
                0.59
            ) +
            0.14
        );

    // 从 ACES 处理空间转换回来。
    float3 tonemappedColor = lerp(
        fittedAces.zzz *
        float3(
            -0.083255403,
            -0.0105494,
            1.15297
        ) +
        fittedAces.xxx *
        float3(
            1.70506,
            -0.13026001,
            -0.0240031
        ) +
        fittedAces.yyy *
        float3(
            -0.62178802,
            1.1408,
            -0.128969
        ),
        fittedAces,
        shoulder
    );

    // 固定 1.2 饱和度。
    float luminance = dot(
        tonemappedColor,
        float3(
            0.29899999,
            0.58700001,
            0.114
        )
    );

    float3 saturatedColor = lerp(
        luminance.xxx,
        tonemappedColor,
        1.2
    );

    // WeatherColor 同时影响 RGB 和 Alpha。
    float4 tintedColor = saturate(
        float4(
            saturatedColor,
            rawSceneColor.a
        ) *
        _WeatherColor
    );

    // 当前项目是 Linear Color Space。
    // _InvGamma 默认应先设为 1，避免重复 Gamma。
    float3 finalColor = pow(
        max(tintedColor.rgb, 0.0),
        float3(
            _InvGamma,
            _InvGamma,
            _InvGamma
        )
    );

    float adjustedAlpha =
        tintedColor.a * _AdjustAlpha;

    float finalAlpha =
        _AdjustAlpha != 0.0
            ? step(0.02, adjustedAlpha)
            : tintedColor.a;

    return float4(
        finalColor,
        finalAlpha
    );
}

#endif
