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
            intensity = 1.5f,
            width = 1f,
            exposureScale = 1f
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
    }
}
