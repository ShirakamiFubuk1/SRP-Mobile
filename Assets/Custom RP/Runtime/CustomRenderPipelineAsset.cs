using UnityEngine;
using UnityEngine.Rendering;

namespace srpMobile
{
    [CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline")]
    public class CustomRenderPipelineAsset : RenderPipelineAsset
    {
        [Header("Rendering")]
        [SerializeField] bool useDynamicBatching = true, useGPUInstancing = true, useSRPBatcher = true;

        [Header("Camera Buffer")]
        [SerializeField] 
        private CameraBufferSettings cameraBuffer = new CameraBufferSettings
        {
            allowHDR = true,
            renderScale = 1f
        };

        [Header("Post Processing")]
        [SerializeField]
        private PostFXSettings postFXSettings = new PostFXSettings();
        
        [Header("Resources")]
        [SerializeField]
        Shader cameraRendererShader = default;
        
        protected override RenderPipeline CreatePipeline()
        {
            return new CustomRenderPipeline(
                cameraBuffer, postFXSettings,
                useDynamicBatching, useGPUInstancing, useSRPBatcher,
                cameraRendererShader
                );
        }
    }
}
