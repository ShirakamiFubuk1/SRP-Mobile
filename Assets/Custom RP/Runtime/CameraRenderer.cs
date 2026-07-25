using UnityEngine;
using UnityEngine.Rendering;

namespace srpMobile
{
    public partial class CameraRenderer
    {
        const string bufferName = "Render Camera";

        static readonly ShaderTagId[] shaderTagIds =
        {
            new ShaderTagId("CustomLit"),
            new ShaderTagId("SRPDefaultUnlit")
        };

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
            DrawOpaque(useDynamicBatching, useGPUInstancing);
            DrawTransparent(useDynamicBatching, useGPUInstancing);
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

        void DrawOpaque(
            bool useDynamicBatching,
            bool useGPUInstancing
        )
        {
            var sortingSettings = new SortingSettings(camera)
            {
                criteria = SortingCriteria.CommonOpaque
            };


            DrawingSettings drawingSettings =
                CreateDrawingSettings(
                    sortingSettings,
                    useDynamicBatching,
                    useGPUInstancing
                );


            var filteringSettings =
                new FilteringSettings(
                    RenderQueueRange.opaque
                );


            context.DrawRenderers(
                cullingResults,
                ref drawingSettings,
                ref filteringSettings
            );
        }
        
        void DrawTransparent(
            bool useDynamicBatching,
            bool useGPUInstancing
        )
        {
            var sortingSettings =
                new SortingSettings(camera)
                {
                    criteria =
                        SortingCriteria.CommonTransparent
                };


            DrawingSettings drawingSettings =
                CreateDrawingSettings(
                    sortingSettings,
                    useDynamicBatching,
                    useGPUInstancing
                );


            var filteringSettings =
                new FilteringSettings(
                    RenderQueueRange.transparent
                );


            context.DrawRenderers(
                cullingResults,
                ref drawingSettings,
                ref filteringSettings
            );
        }

        DrawingSettings CreateDrawingSettings(
            SortingSettings sortingSettings,
            bool useDynamicBatching,
            bool useGPUInstancing
        )
        {
            var drawingSettings = new DrawingSettings(
                shaderTagIds[0],
                sortingSettings
            )
            {
                enableDynamicBatching = useDynamicBatching,
                enableInstancing = useGPUInstancing
            };

            for (int i = 1; i < shaderTagIds.Length; i++)
            {
                drawingSettings.SetShaderPassName(i, shaderTagIds[i]);
            }

            return drawingSettings;
        }
    }
}
