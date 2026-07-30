using UnityEngine;
using UnityEngine.Rendering;

namespace srpMobile
{
    public class CustomRenderPipeline : RenderPipeline
    {
        CameraRenderer renderer;

        bool useDynamicBatching, useGPUInstancing;
        
        CameraBufferSettings cameraBufferSettings;

        PostFXSettings postFXSettings;

        public CustomRenderPipeline(
            CameraBufferSettings cameraBufferSettings,
            PostFXSettings postFXSettings,
            bool useDynamicBatching, bool useGPUInstancing, bool useSRPBatcher, Shader cameraRendererShader
        )
        {
            this.cameraBufferSettings = cameraBufferSettings;
            this.postFXSettings = postFXSettings;
            this.useDynamicBatching = useDynamicBatching;
            this.useGPUInstancing = useGPUInstancing;
            GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
            renderer = new CameraRenderer(cameraRendererShader);
        }

        protected override void Render(
            ScriptableRenderContext context, Camera[] cameras
        )
        {
            foreach (Camera camera in cameras)
            {
                renderer.Render(
                    context, camera, cameraBufferSettings, postFXSettings,
                    useDynamicBatching, useGPUInstancing
                );
            }
        }
        
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            renderer.Dispose();
        }
    }
}
