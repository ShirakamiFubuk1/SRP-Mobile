using UnityEngine;
using UnityEngine.Rendering;

namespace srpMobile
{
    [CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline")]
    public class CustomRenderPipelineAsset : RenderPipelineAsset
    {
        [SerializeField] bool useDynamicBatching = true, useGPUInstancing = true, useSRPBatcher = true;

        [SerializeField] private CameraBufferSettings cameraBuffer = new CameraBufferSettings();
        
        [SerializeField]
        Shader cameraRendererShader = default;
        
        protected override RenderPipeline CreatePipeline()
        {
            return new CustomRenderPipeline(
                cameraBuffer, useDynamicBatching, useGPUInstancing, useSRPBatcher,
                cameraRendererShader
                );
        }
    }
}