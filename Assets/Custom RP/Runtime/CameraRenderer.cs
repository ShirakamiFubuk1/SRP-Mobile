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
            dstBlendId = Shader.PropertyToID("_CameraDstBlend"),
            bloomTexture1Id = Shader.PropertyToID("_BloomTexture1"),
            bloomTexture2Id = Shader.PropertyToID("_BloomTexture2"),
            bloomTexture3Id = Shader.PropertyToID("_BloomTexture3"),
            bloomTemp1Id = Shader.PropertyToID("_BloomTemp1"),
            bloomTemp2Id = Shader.PropertyToID("_BloomTemp2"),
            bloomThresholdId = Shader.PropertyToID("_BloomThreshold"),
            bloomIntensityId = Shader.PropertyToID("_BloomIntensity"),
            bloomExposureScaleId = Shader.PropertyToID("_BloomExposureScale"),
            bloomTargetSizeId = Shader.PropertyToID("_BloomTargetSize"),
            bloomWidthId = Shader.PropertyToID("_BloomWidth"),
            averageIlluminanceId = Shader.PropertyToID("_AverageIlluminance"),
            weatherColorId = Shader.PropertyToID("_WeatherColor"),
            inverseGammaId = Shader.PropertyToID("_InvGamma"),
            adjustAlphaId = Shader.PropertyToID("_AdjustAlpha");
        
        Texture2D missingTexture;
        
        bool useHDR, usePostFX, useBloomTextures, useScaledRendering, useColorTexture, useDepthTexture,
            useIntermediateBuffer;
        
        static CameraSettings defaultCameraSettings = new CameraSettings();
        
        Vector2Int bufferSize;

        private static bool copyTextureSupported =
            SystemInfo.copyTextureSupport > CopyTextureSupport.None;
        
        Material material;
        
        static Rect fullViewRect = new Rect(0f, 0f, 1f, 1f);
        
        int colorTextureDivisor;
        
        const string copyColorSampleName = "Copy Camera Color";
        const string copyDepthSampleName = "Copy Camera Depth";
        const string bloomOneThirdSampleName = "Bloom 1/3";
        const string bloomOneQuarterSampleName = "Bloom 1/4";
        const string bloomOneEighthSampleName = "Bloom 1/8";
        const string finalBlitSampleName = "Final Blit";
        const string finalPostFXSampleName = "Final Post FX";

        public const float renderScaleMin = 0.1f, renderScaleMax = 2f;
        
        const int
            bloomPrefilterPass = 2,
            bloomDownsamplePass = 3,
            bloomHorizontal5Pass = 4,
            bloomVertical5Pass = 5,
            bloomHorizontal9Pass = 6,
            bloomVertical9Pass = 7,
            finalPostFXPass = 8;
        
        public void Render(
            ScriptableRenderContext context, Camera camera,
            CameraBufferSettings bufferSettings,
            PostFXSettings postFXSettings,
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
            
            useHDR =
                bufferSettings.allowHDR &&
                camera.allowHDR;

            usePostFX =
                postFXSettings != null &&
                postFXSettings.enabled &&
                cameraSettings.allowPostFX &&
                camera.cameraType != CameraType.Reflection;

            useBloomTextures =
                usePostFX &&
                useHDR &&
                postFXSettings.bloom.enabled &&
                postFXSettings.bloom.intensity > 0.01f;

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
            
            if (useBloomTextures)
            {
                GenerateBloomTextures(postFXSettings.bloom);
            }
            if (usePostFX)
            {
                DrawPostFX(
                    cameraSettings.finalBlendMode,
                    postFXSettings.bloom
                );
                ExecuteBuffer();
            }
            else if (useIntermediateBuffer)
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
            
            useIntermediateBuffer =
                useHDR || usePostFX || useScaledRendering ||
                useColorTexture || useDepthTexture;
            if (useIntermediateBuffer)
            {
                if (flags > CameraClearFlags.Color)
                {
                    flags = CameraClearFlags.Color;
                }
                buffer.GetTemporaryRT(
                    colorAttachmentId, bufferSize.x, bufferSize.y,
                    0, FilterMode.Bilinear, useHDR ?
                        RenderTextureFormat.DefaultHDR :RenderTextureFormat.Default
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
                if (useBloomTextures)
                {
                    buffer.ReleaseTemporaryRT(bloomTexture1Id);
                    buffer.ReleaseTemporaryRT(bloomTexture2Id);
                    buffer.ReleaseTemporaryRT(bloomTexture3Id);
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
                    0, FilterMode.Bilinear, useHDR ?
                        RenderTextureFormat.DefaultHDR :RenderTextureFormat.Default
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

        void GenerateBloomTextures(
            PostFXSettings.BloomSettings bloomSettings
        )
        {
            RenderTextureFormat bloomFormat = useHDR
                ? RenderTextureFormat.DefaultHDR
                : RenderTextureFormat.Default;

            // 三张最终需要交给 FinalPostFX 的 Bloom 纹理尺寸。
            int thirdWidth = Mathf.Max(1, bufferSize.x / 3);
            int thirdHeight = Mathf.Max(1, bufferSize.y / 3);

            int quarterWidth = Mathf.Max(1, bufferSize.x / 4);
            int quarterHeight = Mathf.Max(1, bufferSize.y / 4);

            int eighthWidth = Mathf.Max(1, bufferSize.x / 8);
            int eighthHeight = Mathf.Max(1, bufferSize.y / 8);

            // 最终保留的三张 Bloom 纹理。
            buffer.GetTemporaryRT(
                bloomTexture1Id,
                thirdWidth,
                thirdHeight,
                0,
                FilterMode.Bilinear,
                bloomFormat
            );
            buffer.GetTemporaryRT(
                bloomTexture2Id,
                quarterWidth,
                quarterHeight,
                0,
                FilterMode.Bilinear,
                bloomFormat
            );
            buffer.GetTemporaryRT(
                bloomTexture3Id,
                eighthWidth,
                eighthHeight,
                0,
                FilterMode.Bilinear,
                bloomFormat
            );

            // 分离式模糊不能同时读取和写入同一张纹理，
            // 因此需要两张不同尺寸的中间纹理。
            buffer.GetTemporaryRT(
                bloomTemp1Id,
                quarterWidth,
                quarterHeight,
                0,
                FilterMode.Bilinear,
                bloomFormat
            );
            buffer.GetTemporaryRT(
                bloomTemp2Id,
                eighthWidth,
                eighthHeight,
                0,
                FilterMode.Bilinear,
                bloomFormat
            );

            buffer.SetGlobalFloat(
                bloomThresholdId,
                bloomSettings.threshold
            );
            buffer.SetGlobalFloat(
                bloomExposureScaleId,
                bloomSettings.exposureScale
            );
            buffer.SetGlobalFloat(
                bloomWidthId,
                bloomSettings.width
            );

            // 第一步：
            // Camera Color -> 1/3 Bloom
            // 阈值提取、五点采样和 Karis Average。
            buffer.BeginSample(bloomOneThirdSampleName);

            DrawBloomPass(
                new RenderTargetIdentifier(colorAttachmentId),
                new RenderTargetIdentifier(bloomTexture1Id),
                thirdWidth,
                thirdHeight,
                bloomPrefilterPass
            );

            buffer.EndSample(bloomOneThirdSampleName);

            // 第二到第四步：
            // 1/3 -> 1/4 Downsample
            // 1/4 Horizontal 5
            // 1/4 Vertical 5
            buffer.BeginSample(bloomOneQuarterSampleName);

            DrawBloomPass(
                new RenderTargetIdentifier(bloomTexture1Id),
                new RenderTargetIdentifier(bloomTexture2Id),
                quarterWidth,
                quarterHeight,
                bloomDownsamplePass
            );

            DrawBloomPass(
                new RenderTargetIdentifier(bloomTexture2Id),
                new RenderTargetIdentifier(bloomTemp1Id),
                quarterWidth,
                quarterHeight,
                bloomHorizontal5Pass
            );

            DrawBloomPass(
                new RenderTargetIdentifier(bloomTemp1Id),
                new RenderTargetIdentifier(bloomTexture2Id),
                quarterWidth,
                quarterHeight,
                bloomVertical5Pass
            );

            buffer.EndSample(bloomOneQuarterSampleName);

            // 第五到第六步：
            // 1/4 Horizontal 9，并输出到 1/8
            // 1/8 Vertical 9，得到最终 BloomTexture3
            buffer.BeginSample(bloomOneEighthSampleName);

            DrawBloomPass(
                new RenderTargetIdentifier(bloomTexture2Id),
                new RenderTargetIdentifier(bloomTemp2Id),
                eighthWidth,
                eighthHeight,
                bloomHorizontal9Pass
            );

            DrawBloomPass(
                new RenderTargetIdentifier(bloomTemp2Id),
                new RenderTargetIdentifier(bloomTexture3Id),
                eighthWidth,
                eighthHeight,
                bloomVertical9Pass
            );

            buffer.EndSample(bloomOneEighthSampleName);

            // 中间纹理后续不再使用，可以在命令序列末尾释放。
            // 三张最终 Bloom 纹理由 Cleanup() 在 FinalPostFX 后释放。
            buffer.ReleaseTemporaryRT(bloomTemp1Id);
            buffer.ReleaseTemporaryRT(bloomTemp2Id);
        }

        // 所有 Bloom 全屏 Pass 共用的绘制方法。
        void DrawBloomPass(
            RenderTargetIdentifier sourceTexture,
            RenderTargetIdentifier destinationTexture,
            int targetWidth,
            int targetHeight,
            int pass
        )
        {
            buffer.SetGlobalTexture(
                sourceTextureId,
                sourceTexture
            );
            buffer.SetGlobalVector(
                bloomTargetSizeId,
                new Vector4(
                    targetWidth,
                    targetHeight,
                    1f / targetWidth,
                    1f / targetHeight
                )
            );

            buffer.SetRenderTarget(
                destinationTexture,
                RenderBufferLoadAction.DontCare,
                RenderBufferStoreAction.Store
            );

            buffer.DrawProcedural(
                Matrix4x4.identity,
                material,
                pass,
                MeshTopology.Triangles,
                3
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
        
        void DrawPostFX(
            CameraSettings.FinalBlendMode finalBlendMode,
            PostFXSettings.BloomSettings bloomSettings
        )
        {
            buffer.BeginSample(finalPostFXSampleName);
            
            buffer.SetGlobalFloat(
                bloomIntensityId,
                useBloomTextures
                    ? bloomSettings.intensity
                    : 0f
            );

            buffer.SetGlobalVector(
                averageIlluminanceId,
                new Vector4(
                    1f, // 曝光除数
                    0f,
                    0f,
                    0f  // Shoulder
                )
            );

            buffer.SetGlobalColor(
                weatherColorId,
                Color.white
            );

            buffer.SetGlobalFloat(
                inverseGammaId,
                1f
            );

            buffer.SetGlobalFloat(
                adjustAlphaId,
                0f
            );

            // Tex0：包含 Opaque 和 Transparent 的完整 HDR 场景。
            buffer.SetGlobalTexture(
                sourceTextureId,
                new RenderTargetIdentifier(colorAttachmentId)
            );

            if (useBloomTextures)
            {
                buffer.SetGlobalTexture(
                    bloomTexture1Id,
                    new RenderTargetIdentifier(bloomTexture1Id)
                );
                buffer.SetGlobalTexture(
                    bloomTexture2Id,
                    new RenderTargetIdentifier(bloomTexture2Id)
                );
                buffer.SetGlobalTexture(
                    bloomTexture3Id,
                    new RenderTargetIdentifier(bloomTexture3Id)
                );
            }
            else
            {
                // Bloom 关闭时仍然执行 FinalPostFX，
                // 因此给三个采样槽提供有效的黑色纹理。
                buffer.SetGlobalTexture(
                    bloomTexture1Id,
                    Texture2D.blackTexture
                );
                buffer.SetGlobalTexture(
                    bloomTexture2Id,
                    Texture2D.blackTexture
                );
                buffer.SetGlobalTexture(
                    bloomTexture3Id,
                    Texture2D.blackTexture
                );
            }

            // 继续兼容当前 Camera 的最终混合模式。
            buffer.SetGlobalFloat(
                srcBlendId,
                (float)finalBlendMode.source
            );
            buffer.SetGlobalFloat(
                dstBlendId,
                (float)finalBlendMode.destination
            );

            // 最终后处理直接输出到 CameraTarget。
            buffer.SetRenderTarget(
                BuiltinRenderTextureType.CameraTarget,
                finalBlendMode.destination == BlendMode.Zero &&
                camera.rect == fullViewRect
                    ? RenderBufferLoadAction.DontCare
                    : RenderBufferLoadAction.Load,
                RenderBufferStoreAction.Store
            );

            // Render Scale 生效时，内部纹理尺寸与最终视口不同，
            // 因此最终输出仍然使用 Camera 的真实 pixelRect。
            buffer.SetViewport(camera.pixelRect);

            // 三角形数量为 3，生成覆盖全屏的单个大三角形。
            buffer.DrawProcedural(
                Matrix4x4.identity,
                material,
                finalPostFXPass,
                MeshTopology.Triangles,
                3
            );

            // 恢复默认覆盖混合，避免影响后续相机。
            buffer.SetGlobalFloat(
                srcBlendId,
                (float)BlendMode.One
            );
            buffer.SetGlobalFloat(
                dstBlendId,
                (float)BlendMode.Zero
            );

            buffer.EndSample(finalPostFXSampleName);
        }
        
    }
}
