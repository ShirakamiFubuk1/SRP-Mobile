using UnityEngine;
using UnityEngine.Rendering;

namespace srpMobile
{
    public partial class CameraRenderer
    {
        const string bufferName = "Render Camera";

        static ShaderTagId unlitShaderTagId = new ShaderTagId("SRPDefaultUnlit");

        CommandBuffer buffer = new CommandBuffer
        {
            name = bufferName
        };

        ScriptableRenderContext context;

        Camera camera;

        CullingResults cullingResults;

        public void Render(
            ScriptableRenderContext context, Camera camera,
            bool useDynamicBatching, bool useGPUInstancing
        )
        {
            this.context = context;
            this.camera = camera;

            PrepareBuffer();
            PrepareForSceneWindow();
            if (!Cull())
            {
                return;
            }

            Setup();
            DrawVisibleGeometry(useDynamicBatching, useGPUInstancing);
            DrawUnsupportedShaders();
            DrawGizmos();
            Submit();
        }

        bool Cull()
        {
            if (camera.TryGetCullingParameters(out ScriptableCullingParameters p))
            {
                cullingResults = context.Cull(ref p);
                return true;
            }

            return false;
        }

        void Setup()
        {
            context.SetupCameraProperties(camera);
            CameraClearFlags flags = camera.clearFlags;
            
            bool clearColor =
                flags == CameraClearFlags.Color ||
                flags == CameraClearFlags.Skybox;

            bool clearDepth =
                flags != CameraClearFlags.Nothing;
            
            buffer.ClearRenderTarget(
                clearDepth,
                clearColor,
                clearColor
                    ? camera.backgroundColor.linear
                    : Color.clear
            );
            
            buffer.BeginSample(SampleName);
            ExecuteBuffer();
        }

        void Submit()
        {
            buffer.EndSample(SampleName);
            ExecuteBuffer();
            context.Submit();
        }

        void ExecuteBuffer()
        {
            context.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        void DrawVisibleGeometry(bool useDynamicBatching, bool useGPUInstancing)
        {
            var sortingSettings = new SortingSettings(camera)
            {
                criteria = SortingCriteria.CommonOpaque
            };
            var drawingSettings = new DrawingSettings(
                unlitShaderTagId, sortingSettings
            )
            {
                enableDynamicBatching = useDynamicBatching,
                enableInstancing = useGPUInstancing
            };
            var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);

            context.DrawRenderers(
                cullingResults, ref drawingSettings, ref filteringSettings
            );

            sortingSettings.criteria = SortingCriteria.CommonTransparent;
            drawingSettings.sortingSettings = sortingSettings;
            filteringSettings.renderQueueRange = RenderQueueRange.transparent;

            context.DrawRenderers(
                cullingResults, ref drawingSettings, ref filteringSettings
            );
        }
    }
}