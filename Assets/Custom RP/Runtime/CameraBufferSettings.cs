using System;
using UnityEngine;

namespace srpMobile
{
    [Serializable]
    public struct CameraBufferSettings
    {
        public bool allowHDR;

        public bool copyColor;
        
        public ColorTextureResolution colorTextureResolution;
        
        public bool copyColorReflection, copyDepth, copyDepthReflection;
        
        public enum ColorTextureResolution
        {
            Full,
            Half,
            Quarter
        }
        
        public int ColorTextureDivisor =>
            1 << (int)colorTextureResolution;
        
        // [Range(CameraRenderer.renderScaleMin, CameraRenderer.renderScaleMax)]
        // public float renderScale;

        // public enum BicubicRescalingMode
        // {
        //     Off,
        //     UpOnly,
        //     UpAndDown
        // }

        // public BicubicRescalingMode bicubicRescaling;
        
        // [Serializable]
        // public struct FXAA
        // {
        //     public bool enabled;
        //     
        //     [Range(0.0312f, 0.0833f)]
        //     public float fixedThreshold;
        //     
        //     [Range(0.063f, 0.333f)]
        //     public float relativeThreshold;
        //     
        //     [Range(0f, 1f)]
        //     public float subpixelBlending;
        //
        //     public enum Quality
        //     {
        //         Low,
        //         Medium,
        //         High
        //     }
        //
        //     public Quality quality;
        // }

        // public FXAA fxaa;
    }
}
