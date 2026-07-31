using System;
using UnityEngine;

namespace srpMobile
{
    [Serializable]
    public class PostFXSettings
    {
        public bool enabled = true;

        public BloomSettings bloom = new BloomSettings
        {
            enabled = true,
            threshold = 0.8f,
            intensity = 0.25f,
            width = 1f,
            exposureScale = 1f
        };

        public ToneMappingSettings toneMapping = new ToneMappingSettings
        {
            enabled = true,
            averageIlluminance = new Vector4(1.6f, 1.6f, 1.6f, 1f),
            weatherColor = Color.white,
            adjustAlpha = 0f
        };

        [Serializable]
        public struct BloomSettings
        {
            public bool enabled;

            [Min(0f)]
            public float threshold;

            [Min(0f)]
            public float intensity;

            [Min(0f)]
            public float width;

            [Min(0f)]
            public float exposureScale;
        }

        [Serializable]
        public struct ToneMappingSettings
        {
            public bool enabled;

            public Vector4 averageIlluminance;

            public Color weatherColor;

            [Min(0f)]
            public float adjustAlpha;
        }
    }
}
