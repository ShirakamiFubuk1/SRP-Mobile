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
        
        static int
            colorTextureSizeId = Shader.PropertyToID("_CameraColorTextureSize"),
            bufferSizeId = Shader.PropertyToID("_CameraBufferSize"),
            colorAttachmentId = Shader.PropertyToID("_CameraColorAttachment"),
            depthAttachmentId = Shader.PropertyToID("_CameraDepthAttachment"),
            colorTextureId = Shader.PropertyToID("_CameraColorTexture"),
            depthTextureId = Shader.PropertyToID("_CameraDepthTexture"),
            sourceTextureId = Shader.PropertyToID("_SourceTexture"),
            srcBlendId = Shader.PropertyToID("_CameraSrcBlend"),
            dstBlendId = Shader.PropertyToID("_CameraDstBlend");
        
        Texture2D missingTexture;
        
        bool useHDR, useScaledRendering, useColorTexture, useDepthTexture, useIntermediateBuffer;
        
        static CameraSettings defaultCameraSettings = new CameraSettings();
        
        Vector2Int bufferSize;

        private static bool copyTextureSupported =
            SystemInfo.copyTextureSupport > CopyTextureSupport.None;
        
        Material material;
        
        static Rect fullViewRect = new Rect(0f, 0f, 1f, 1f);
        
        int colorTextureDivisor;
        
        const string copyColorSampleName = "Copy Camera Color";
        const string copyDepthSampleName = "Copy Camera Depth";
        const string finalBlitSampleName = "Final Blit";
        
        public const float renderScaleMin = 0.1f, renderScaleMax = 2f;
        
        public void Render(
            ScriptableRenderContext context, Camera camera,
            CameraBufferSettings bufferSettings,
            bool useDynamicBatching, bool useGPUInstancing
        )
        {
            this.context = context;
            this.camera = camera;
            
            var crpCamera = camera.GetComponent<CustomRenderPipelineCamera>();
            CameraSettings cameraSettings =
                crpCamera ? crpCamera.Settings : defaultCameraSettings;
            
            colorTextureDivisor =
                bufferSettings.ColorTextureDivisor;
            
            if (camera.cameraType == CameraType.Reflection)
            {
                useColorTexture = bufferSettings.copyColorReflection;
                useDepthTexture = bufferSettings.copyDepthReflection;
            }
            else
            {
                useColorTexture = bufferSettings.copyColor && cameraSettings.copyColor;
                useDepthTexture = bufferSettings.copyDepth && cameraSettings.copyDepth;
            }

            float renderScale = Mathf.Clamp(bufferSettings.renderScale, renderScaleMin, renderScaleMax);
            useScaledRendering = renderScale < 0.99f || renderScale > 1.01f;
            PrepareBuffer();
            PrepareForSceneWindow();
            if (!Cull())
            {
                return;
            }
            if (useScaledRendering)
            {
                bufferSize.x = Mathf.Max(
                    1, (int)(camera.pixelWidth * renderScale)
                );
                bufferSize.y = Mathf.Max(
                    1, (int)(camera.pixelHeight * renderScale)
                );
            }
            else
            {
                bufferSize.x = camera.pixelWidth;
                bufferSize.y = camera.pixelHeight;
            }

            buffer.SetGlobalVector(bufferSizeId, new Vector4(
                1f / bufferSize.x, 1f / bufferSize.y,
                bufferSize.x, bufferSize.y
            ));
            Setup();
            DrawOpaque(useDynamicBatching, useGPUInstancing);
            if (useColorTexture || useDepthTexture)
            {
                CopyAttachments();
            }
            DrawTransparent(useDynamicBatching, useGPUInstancing);
            DrawUnsupportedShaders();
            if (useIntermediateBuffer)
            {
                DrawFinal(cameraSettings.finalBlendMode);
                ExecuteBuffer();
            }
            DrawGizmos();
            Cleanup();
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
            
            useIntermediateBuffer = useScaledRendering || useColorTexture || useDepthTexture;
            if (useIntermediateBuffer)
            {
                if (flags > CameraClearFlags.Color)
                {
                    flags = CameraClearFlags.Color;
                }
                buffer.GetTemporaryRT(
                    colorAttachmentId, bufferSize.x, bufferSize.y,
                    0, FilterMode.Bilinear, RenderTextureFormat.Default
                );
                buffer.GetTemporaryRT(
                    depthAttachmentId, bufferSize.x, bufferSize.y,
                    32, FilterMode.Point, RenderTextureFormat.Depth
                );
                buffer.SetRenderTarget(
                    colorAttachmentId,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                    depthAttachmentId,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
                );
            }
            
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
            buffer.SetGlobalTexture(colorTextureId, missingTexture);
            buffer.SetGlobalTexture(depthTextureId, missingTexture);
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

        void Cleanup()
        {
            if (useIntermediateBuffer)
            {
                buffer.ReleaseTemporaryRT(colorAttachmentId);
                buffer.ReleaseTemporaryRT(depthAttachmentId);
                if (useColorTexture)
                {
                    buffer.ReleaseTemporaryRT(colorTextureId);
                }
                if (useDepthTexture)
                {
                    buffer.ReleaseTemporaryRT(depthTextureId);
                }
            }
        }
        
        void CopyAttachments()
        {
            bool requiresRenderTargetReset = false;
            
            if (useColorTexture)
            {
                buffer.BeginSample(copyColorSampleName);
                bool canCopyColorDirectly = colorTextureDivisor == 1 && copyTextureSupported;
                int colorWidth = Mathf.Max(1, bufferSize.x / colorTextureDivisor);
                int colorHeight = Mathf.Max(1, bufferSize.y / colorTextureDivisor);

                buffer.SetGlobalVector(colorTextureSizeId, 
                    new Vector4(1f / colorWidth, 1f / colorHeight, colorWidth, colorHeight));
                buffer.GetTemporaryRT(
                    colorTextureId, colorWidth, colorHeight,
                    0, FilterMode.Bilinear, RenderTextureFormat.Default
                );
                if (canCopyColorDirectly)
                {
                    buffer.CopyTexture(colorAttachmentId, colorTextureId);
                }
                else
                {
                    Draw(colorAttachmentId, colorTextureId);
                    requiresRenderTargetReset = true;
                }
                buffer.EndSample(copyColorSampleName);
            }
            if (useDepthTexture)
            {
                buffer.BeginSample(copyDepthSampleName);
                buffer.GetTemporaryRT(
                    depthTextureId, bufferSize.x, bufferSize.y,
                    32, FilterMode.Point, RenderTextureFormat.Depth
                );
                if (copyTextureSupported)
                {
                    buffer.CopyTexture(depthAttachmentId, depthTextureId);
                }
                else
                {
                    Draw(depthAttachmentId, depthTextureId, true);
                    requiresRenderTargetReset = true;
                }
                buffer.EndSample(copyDepthSampleName);
            }
            if (requiresRenderTargetReset)
            {
                buffer.SetRenderTarget(
                    colorAttachmentId,
                    RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                    depthAttachmentId,
                    RenderBufferLoadAction.Load, RenderBufferStoreAction.Store
                );
            }
            ExecuteBuffer();
        }
        
        void Draw(
            RenderTargetIdentifier from, RenderTargetIdentifier to, bool isDepth = false
        ) {
            buffer.SetGlobalFloat(srcBlendId, (float)BlendMode.One);
            buffer.SetGlobalFloat(dstBlendId,(float)BlendMode.Zero);
            buffer.SetGlobalTexture(sourceTextureId, from);
            buffer.SetRenderTarget(
                to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
            );
            buffer.DrawProcedural(
                Matrix4x4.identity, material, isDepth ? 1 : 0, MeshTopology.Triangles, 3
            );
        }
        
        public void Dispose()
        {
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(missingTexture);
        }
        
        void DrawFinal(CameraSettings.FinalBlendMode finalBlendMode)
        {
            buffer.BeginSample(finalBlitSampleName);
            buffer.SetGlobalFloat(srcBlendId, (float)finalBlendMode.source);
            buffer.SetGlobalFloat(dstBlendId, (float)finalBlendMode.destination);
            buffer.SetGlobalTexture(sourceTextureId, colorAttachmentId);
            buffer.SetRenderTarget(
                BuiltinRenderTextureType.CameraTarget,
                finalBlendMode.destination == BlendMode.Zero && camera.rect == fullViewRect?
                    RenderBufferLoadAction.DontCare : RenderBufferLoadAction.Load,
                RenderBufferStoreAction.Store
            );
            buffer.SetViewport(camera.pixelRect);
            buffer.DrawProcedural(
                Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3
            );
            buffer.SetGlobalFloat(srcBlendId, 1f);
            buffer.SetGlobalFloat(dstBlendId, 0f);
            buffer.EndSample(finalBlitSampleName);
        }
        
        public CameraRenderer(Shader shader)
        {
            material = CoreUtils.CreateEngineMaterial(shader);
            missingTexture = new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "Missing"
            };
            missingTexture.SetPixel(0, 0, Color.white * 0.5f);
            missingTexture.Apply(true, true);
        }
    }
}
