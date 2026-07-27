#ifndef CUSTOM_CAMERA_RENDERER_PASSES_INCLUDED
#define CUSTOM_CAMERA_RENDERER_PASSES_INCLUDED

TEXTURE2D(_SourceTexture);
TEXTURE2D(_BloomTexture1);
TEXTURE2D(_BloomTexture2);
TEXTURE2D(_BloomTexture3);

float4 _BloomTextureSize;
float _BloomThreshold;

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

#endif
