#ifndef CUSTOM_CAMERA_RENDERER_PASSES_INCLUDED
#define CUSTOM_CAMERA_RENDERER_PASSES_INCLUDED

TEXTURE2D(_SourceTexture);
TEXTURE2D(_BloomTexture1);
TEXTURE2D(_BloomTexture2);
TEXTURE2D(_BloomTexture3);

float _BloomThreshold;
float4 _AverageIlluminance;
float4 _WeatherColor;
float _BloomIntensity;
float _InvGamma;
float _AdjustAlpha;
float _BloomExposureScale;
float4 _BloomTargetSize;
float _BloomWidth;

static const float3 bloomLuminanceWeights =
    float3(
        0.29899999,
        0.58700001,
        0.114
    );

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

float BloomLuminance(float3 color)
{
    return dot(
        color,
        bloomLuminanceWeights
    );
}

float3 ApplyBloomThreshold(float3 color)
{
    // 对应原 Shader 的最大 HDR 限制。
    color = min(
        color,
        float3(16.0, 16.0, 16.0)
    );

    float luminance =
        BloomLuminance(color);

    float exposedLuminance =
        luminance * _BloomExposureScale;

    float contribution = saturate(
        (
            exposedLuminance -
            _BloomThreshold
        ) /
        (
            exposedLuminance +
            _BloomThreshold +
            0.0001
        )
    );

    return color * contribution;
}

float KarisWeight(float3 color)
{
    return rcp(
        BloomLuminance(color) + 1.0
    );
}

float3 SampleBloomSource(float2 uv)
{
    float3 color = SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv,
        0
    ).rgb;

    return ApplyBloomThreshold(color);
}

float4 BloomPrefilterPassFragment(
    Varyings input
) : SV_TARGET
{
    // _BloomTargetSize.xy = 当前输出 RT 的宽高
    // _BloomTargetSize.zw = 当前输出 RT 的 Texel Size
    float2 offset =
        _BloomTargetSize.zw * 0.33;

    float3 sample0 = SampleBloomSource(
        input.screenUV +
        float2(-offset.x, -offset.y)
    );

    float3 sample1 = SampleBloomSource(
        input.screenUV +
        float2(offset.x, -offset.y)
    );

    float3 sample2 = SampleBloomSource(
        input.screenUV +
        float2(-offset.x, offset.y)
    );

    float3 sample3 = SampleBloomSource(
        input.screenUV +
        float2(offset.x, offset.y)
    );

    float3 sample4 = SampleBloomSource(
        input.screenUV
    );

    float weight0 = KarisWeight(sample0);
    float weight1 = KarisWeight(sample1);
    float weight2 = KarisWeight(sample2);
    float weight3 = KarisWeight(sample3);
    float weight4 = KarisWeight(sample4);

    float weightSum =
        weight0 +
        weight1 +
        weight2 +
        weight3 +
        weight4;

    float3 bloomColor =
        sample0 * weight0 +
        sample1 * weight1 +
        sample2 * weight2 +
        sample3 * weight3 +
        sample4 * weight4;

    // 原 Shader 使用 0.5 / weightSum，
    // 因此这里保留原始能量比例。
    bloomColor *=
        0.5 / max(weightSum, 0.0001);

    return float4(
        bloomColor,
        1.0
    );
}

float4 GetSceneColor(Varyings input)
{
    // Tex0：包含 Opaque 和 Transparent 的完整 HDR 场景。
    float4 rawSceneColor = SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        input.screenUV,
        0
    );

    // 限制极端 HDR 数值。
    rawSceneColor.rgb = min(
        rawSceneColor.rgb,
        float3(60.0, 60.0, 60.0)
    );

    return rawSceneColor;
}

float4 GetColorWithBloom(Varyings input)
{
    float4 rawSceneColor =
        GetSceneColor(input);

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
        rawSceneColor.rgb +
        bloomColor * _BloomIntensity * 1.5;

    return float4(
        colorWithBloom,
        rawSceneColor.a
    );
}

float4 FinalPostFXWithoutToneMappingFragment(
    Varyings input
) : SV_TARGET
{
    return GetColorWithBloom(input);
}

float4 ApplyToneMapping(float4 colorWithBloom)
{
    // 曝光。
    float3 exposedColor =
        colorWithBloom.rgb /
        max(_AverageIlluminance.x, 0.000001);

    // 对应原 Shader 中 AverageIlluminance.w 的曲线控制值。
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
            colorWithBloom.a
        ) *
        _WeatherColor
    );

    // 原方案使用 0.4545。当前项目是 Linear Color Space，
    // 可以通过配置切换为 1，检查最终目标是否发生二次 Gamma。
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

float4 FinalPostFXFragment(Varyings input) : SV_TARGET
{
    return ApplyToneMapping(
        GetColorWithBloom(input)
    );
}

float4 FinalToneMappingWithoutBloomFragment(
    Varyings input
) : SV_TARGET
{
    return ApplyToneMapping(
        GetSceneColor(input)
    );
}

float4 BloomDownsampleFragment(
    Varyings input
) : SV_TARGET
{
    float2 offset =
        _BloomTargetSize.zw * 0.25;

    float4 color = SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        input.screenUV +
        float2(-offset.x, -offset.y),
        0
    );

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        input.screenUV +
        float2(offset.x, -offset.y),
        0
    );

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        input.screenUV +
        float2(-offset.x, offset.y),
        0
    );

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        input.screenUV +
        float2(offset.x, offset.y),
        0
    );

    return color * 0.25;
}

float4 BloomBlurHorizontal5Fragment(
    Varyings input
) : SV_TARGET
{
    float offset =
        _BloomTargetSize.z *
        _BloomWidth;

    float2 uv = input.screenUV;

    float4 color =
        SAMPLE_TEXTURE2D_LOD(
            _SourceTexture,
            sampler_linear_clamp,
            uv + float2(-2.0 * offset, 0.0),
            0
        ) * 0.111703;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(-1.0 * offset, 0.0),
        0
    ) * 0.236476;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv,
        0
    ) * 0.30364099;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(1.0 * offset, 0.0),
        0
    ) * 0.236476;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(2.0 * offset, 0.0),
        0
    ) * 0.111703;

    return color;
}

float4 BloomBlurVertical5Fragment(
    Varyings input
) : SV_TARGET
{
    float offset =
        _BloomTargetSize.w *
        _BloomWidth;

    float2 uv = input.screenUV;

    float4 color =
        SAMPLE_TEXTURE2D_LOD(
            _SourceTexture,
            sampler_linear_clamp,
            uv + float2(0.0, -2.0 * offset),
            0
        ) * 0.111703;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(0.0, -1.0 * offset),
        0
    ) * 0.236476;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv,
        0
    ) * 0.30364099;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(0.0, 1.0 * offset),
        0
    ) * 0.236476;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(0.0, 2.0 * offset),
        0
    ) * 0.111703;

    return color;
}

float4 BloomBlurHorizontal9Fragment(
    Varyings input
) : SV_TARGET
{
    float offset =
        _BloomTargetSize.z *
        _BloomWidth *
        1.5;

    float2 uv = input.screenUV;

    float4 color =
        SAMPLE_TEXTURE2D_LOD(
            _SourceTexture,
            sampler_linear_clamp,
            uv + float2(-4.0 * offset, 0.0),
            0
        ) * 0.032845002;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(-3.0 * offset, 0.0),
        0
    ) * 0.071489997;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(-2.0 * offset, 0.0),
        0
    ) * 0.124602;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(-1.0 * offset, 0.0),
        0
    ) * 0.173896;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv,
        0
    ) * 0.194332;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(1.0 * offset, 0.0),
        0
    ) * 0.173896;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(2.0 * offset, 0.0),
        0
    ) * 0.124602;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(3.0 * offset, 0.0),
        0
    ) * 0.071491003;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(4.0 * offset, 0.0),
        0
    ) * 0.032845002;

    return color;
}

float4 BloomBlurVertical9Fragment(
    Varyings input
) : SV_TARGET
{
    float offset =
        _BloomTargetSize.w *
        _BloomWidth *
        1.5;

    float2 uv = input.screenUV;

    float4 color =
        SAMPLE_TEXTURE2D_LOD(
            _SourceTexture,
            sampler_linear_clamp,
            uv + float2(0.0, -4.0 * offset),
            0
        ) * 0.032845002;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(0.0, -3.0 * offset),
        0
    ) * 0.071489997;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(0.0, -2.0 * offset),
        0
    ) * 0.124602;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(0.0, -1.0 * offset),
        0
    ) * 0.173896;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv,
        0
    ) * 0.194332;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(0.0, 1.0 * offset),
        0
    ) * 0.173896;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(0.0, 2.0 * offset),
        0
    ) * 0.124602;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(0.0, 3.0 * offset),
        0
    ) * 0.071491003;

    color += SAMPLE_TEXTURE2D_LOD(
        _SourceTexture,
        sampler_linear_clamp,
        uv + float2(0.0, 4.0 * offset),
        0
    ) * 0.032845002;

    return color;
}

#endif
