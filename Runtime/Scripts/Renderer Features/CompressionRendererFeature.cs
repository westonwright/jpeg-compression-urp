using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

class CompressionRendererFeature : ScriptableRendererFeature
{
    #region Public properties and methods
    [SerializeField, Range(1, 32)]
    int _chromaSubsampleRatio = 2;
    public int chromaSubsampleRatio
    {
        get { return _chromaSubsampleRatio; }
        set { _chromaSubsampleRatio = value; }
    }
    
    [SerializeField, Range(0.1f, 32)]
    float _qualityFactor = 1;
    public float qualityFactor
    {
        get { return _qualityFactor; }
        set { _qualityFactor = value; }
    }
    #endregion

    #region Private properties
    [SerializeField]
    string _profilerTag = "Compression Renderer Feature";

    [SerializeField]
    private CameraType _visibleFrom = CameraType.SceneView | CameraType.Game;

    [SerializeField]
    private RenderPassEvent _renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

    [SerializeField]
    private ComputeShader _compressionCompute;
    [SerializeField]
    private ComputeShader _YCbCrCompute;

    private CompressionPass _renderPass = null;

    private bool _initialized = false;
    #endregion

    class CompressionPass : ScriptableRenderPass
    {
        // used to label this pass in Unity's Frame Debug utility
        string profilerTag;

        ComputeShader compressionCompute;
        ComputeShader YCbCrCompute;

        int chromaSubsampleRatio;
        float qualityFactor;

        static readonly int[] lumaTable = new int[] // 64 vals
        {
            16, 11, 10, 16, 24, 40, 51, 61,
            12, 12, 14, 19, 26, 58, 60, 55,
            14, 13, 16, 24, 40, 57, 69, 56,
            14, 17, 22, 29, 51, 87, 80, 62,
            18, 22, 37, 56, 68, 109, 103, 77,
            24, 35, 55, 64, 81, 104, 113, 92,
            49, 64, 78, 87, 103, 121, 120, 101,
            72, 92, 95, 98, 112, 100, 103, 99
        };

        static readonly int[] chromaTable = new int[] // 64 vals
        {
            17, 18, 24, 47, 99, 99, 99, 99,
            18, 21, 26, 66, 99, 99, 99, 99,
            24, 26, 56, 99, 99, 99, 99, 99,
            47, 66, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99
        };

        ComputeBuffer lumaQuantizeBuffer;
        ComputeBuffer chromaQuantizeBuffer;

        RenderTargetIdentifier cameraColorTarget;

        int rgbTextureId;
        RenderTargetIdentifier rgbTexture;
        int yTextureId;
        RenderTargetIdentifier yTexture;
        int cbTextureId;
        RenderTargetIdentifier cbTexture;
        int crTextureId;
        RenderTargetIdentifier crTexture;

        Vector2Int fullTexSize;
        Vector2Int downsampleTexSize;

        public CompressionPass(
            string profilerTag,
            RenderPassEvent renderPassEvent,
            ComputeShader compressionCompute,
            ComputeShader YCbCrCompute,
            int chromaSubsampleRatio,
            float qualityFactor
            )
        {
            this.profilerTag = profilerTag;
            this.renderPassEvent = renderPassEvent;
            this.compressionCompute = compressionCompute;
            this.YCbCrCompute = YCbCrCompute;
            this.chromaSubsampleRatio = chromaSubsampleRatio;
            this.qualityFactor = qualityFactor;

            rgbTextureId = Shader.PropertyToID("_RGBTex");
            yTextureId = Shader.PropertyToID("_YTex");
            cbTextureId = Shader.PropertyToID("_CbTex");
            crTextureId = Shader.PropertyToID("_CrTex");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            cameraColorTarget = renderingData.cameraData.renderer.cameraColorTarget;
        }

        // called each frame before Execute, use it to set up things the pass will need
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            fullTexSize = new Vector2Int(cameraTextureDescriptor.width, cameraTextureDescriptor.height);
            downsampleTexSize = new Vector2Int(cameraTextureDescriptor.width / chromaSubsampleRatio, cameraTextureDescriptor.height / chromaSubsampleRatio);

            RenderTextureDescriptor camCopyDescriptor = new RenderTextureDescriptor(
                cameraTextureDescriptor.width,
                cameraTextureDescriptor.height,
                cameraTextureDescriptor.colorFormat
                );
            camCopyDescriptor.enableRandomWrite = true;

            cmd.GetTemporaryRT(rgbTextureId, camCopyDescriptor);
            rgbTexture = new RenderTargetIdentifier(rgbTextureId);
            ConfigureTarget(rgbTexture);

            RenderTextureDescriptor fullTexDescriptor = new RenderTextureDescriptor(
                fullTexSize.x,
                fullTexSize.y,
                RenderTextureFormat.RFloat
                );
            fullTexDescriptor.enableRandomWrite = true;
            cmd.GetTemporaryRT(yTextureId, fullTexDescriptor);
            yTexture = new RenderTargetIdentifier(yTextureId);
            ConfigureTarget(yTexture);

            RenderTextureDescriptor downasmpleTexDescriptor = new RenderTextureDescriptor(
                downsampleTexSize.x,
                downsampleTexSize.y,
                RenderTextureFormat.RFloat
                );
            downasmpleTexDescriptor.enableRandomWrite = true;
            cmd.GetTemporaryRT(cbTextureId, downasmpleTexDescriptor);
            cbTexture = new RenderTargetIdentifier(cbTextureId);
            ConfigureTarget(cbTexture);
            
            cmd.GetTemporaryRT(crTextureId, downasmpleTexDescriptor);
            crTexture = new RenderTargetIdentifier(crTextureId);
            ConfigureTarget(crTexture);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {

            DisposeBuffers();

            // fetch a command buffer to use
            CommandBuffer cmd = CommandBufferPool.Get(profilerTag);
            //cmd.Clear();

            lumaQuantizeBuffer = new ComputeBuffer(64, sizeof(int));
            chromaQuantizeBuffer = new ComputeBuffer(64, sizeof(int));

            cmd.SetBufferData(lumaQuantizeBuffer, lumaTable);
            cmd.SetBufferData(chromaQuantizeBuffer, chromaTable);

            // where the render pass does its work
            cmd.Blit(cameraColorTarget, rgbTexture);

            uint xGroupSize;
            uint yGroupSize;
            uint zGroupSize;

            int toYCbCrKernel = YCbCrCompute.FindKernel("ToYCbCr");
            int toRGBKernel = YCbCrCompute.FindKernel("ToRGB");

            YCbCrCompute.GetKernelThreadGroupSizes(toYCbCrKernel, out xGroupSize, out yGroupSize, out zGroupSize);

            cmd.SetComputeIntParams(YCbCrCompute, "_DownsampleTexSize", new int[] { downsampleTexSize.x, downsampleTexSize.y });
            cmd.SetComputeIntParam(YCbCrCompute, "_CbCrSubsample", chromaSubsampleRatio);

            cmd.SetComputeTextureParam(YCbCrCompute, toYCbCrKernel, "_RBGTex", rgbTexture);
            cmd.SetComputeTextureParam(YCbCrCompute, toYCbCrKernel, "_YTex", yTexture);
            cmd.SetComputeTextureParam(YCbCrCompute, toYCbCrKernel, "_CbTex", cbTexture);
            cmd.SetComputeTextureParam(YCbCrCompute, toYCbCrKernel, "_CrTex", crTexture);

            cmd.DispatchCompute(YCbCrCompute, toYCbCrKernel,
                Mathf.CeilToInt(downsampleTexSize.x / (float)xGroupSize),
                Mathf.CeilToInt(downsampleTexSize.y / (float)yGroupSize),
                1);

            Vector2Int numBlocks = new Vector2Int(Mathf.CeilToInt(fullTexSize.x / 8.0f), Mathf.CeilToInt(fullTexSize.y / 8.0f));

            CompressTexture(cmd, yTexture, lumaQuantizeBuffer, fullTexSize, numBlocks);

            numBlocks = new Vector2Int(Mathf.CeilToInt(downsampleTexSize.x / 8.0f), Mathf.CeilToInt(downsampleTexSize.y / 8.0f));

            CompressTexture(cmd, cbTexture, chromaQuantizeBuffer, downsampleTexSize, numBlocks);
            CompressTexture(cmd, crTexture, chromaQuantizeBuffer, downsampleTexSize, numBlocks);

            YCbCrCompute.GetKernelThreadGroupSizes(toRGBKernel, out xGroupSize, out yGroupSize, out zGroupSize);

            cmd.SetComputeTextureParam(YCbCrCompute, toRGBKernel, "_RBGTex", rgbTexture);
            cmd.SetComputeTextureParam(YCbCrCompute, toRGBKernel, "_YTex", yTexture);
            cmd.SetComputeTextureParam(YCbCrCompute, toRGBKernel, "_CbTex", cbTexture);
            cmd.SetComputeTextureParam(YCbCrCompute, toRGBKernel, "_CrTex", crTexture);

            cmd.DispatchCompute(YCbCrCompute, toRGBKernel,
                Mathf.CeilToInt(downsampleTexSize.x / (float)xGroupSize),
                Mathf.CeilToInt(downsampleTexSize.y / (float)yGroupSize),
                1);

            // then blit back into color target 
            cmd.Blit(rgbTexture, cameraColorTarget);

            // don't forget to tell ScriptableRenderContext to actually execute the commands
            context.ExecuteCommandBuffer(cmd);

            // tidy up after ourselves
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        void CompressTexture(CommandBuffer cmd, RenderTargetIdentifier renderTarget, ComputeBuffer quantizationBuffer, Vector2Int textureSize, Vector2Int numBlocks)
        {
            uint xGroupSize;
            uint yGroupSize;
            uint zGroupSize;

            int centerValuesKernel = compressionCompute.FindKernel("CenterValues");
            int decenterValuesKernel = compressionCompute.FindKernel("DecenterValues");
            int cdt_HorizontalKernel = compressionCompute.FindKernel("CDT_Horizontal");
            int cdt_VerticalKernel = compressionCompute.FindKernel("CDT_Vertical");
            int icdt_HorizontalKernel = compressionCompute.FindKernel("ICDT_Horizontal");
            int icdt_VerticalKernel = compressionCompute.FindKernel("ICDT_Vertical");
            int quantizeKernel = compressionCompute.FindKernel("Quantize");

            compressionCompute.GetKernelThreadGroupSizes(centerValuesKernel, out xGroupSize, out yGroupSize, out zGroupSize);

            cmd.SetComputeIntParams(compressionCompute, "_textureSize", new int[] { textureSize.x, textureSize.y });
            cmd.SetComputeTextureParam(compressionCompute, centerValuesKernel, "_TransformTex", renderTarget);

            cmd.DispatchCompute(compressionCompute, centerValuesKernel,
                Mathf.CeilToInt(textureSize.x / (float)xGroupSize),
                Mathf.CeilToInt(textureSize.y / (float)yGroupSize),
                1);

            compressionCompute.GetKernelThreadGroupSizes(cdt_HorizontalKernel, out xGroupSize, out yGroupSize, out zGroupSize);

            cmd.SetComputeIntParams(compressionCompute, "_numBlocks", new int[] { numBlocks.x, numBlocks.y });
            cmd.SetComputeTextureParam(compressionCompute, cdt_HorizontalKernel, "_TransformTex", renderTarget);

            cmd.DispatchCompute(compressionCompute, cdt_HorizontalKernel,
                Mathf.CeilToInt(numBlocks.x / (float)xGroupSize),
                Mathf.CeilToInt(textureSize.y / (float)yGroupSize),
                1);

            compressionCompute.GetKernelThreadGroupSizes(cdt_VerticalKernel, out xGroupSize, out yGroupSize, out zGroupSize);

            cmd.SetComputeTextureParam(compressionCompute, cdt_VerticalKernel, "_TransformTex", renderTarget);

            cmd.DispatchCompute(compressionCompute, cdt_VerticalKernel,
                Mathf.CeilToInt(textureSize.x / (float)xGroupSize),
                Mathf.CeilToInt(numBlocks.y / (float)yGroupSize),
                1);

            compressionCompute.GetKernelThreadGroupSizes(quantizeKernel, out xGroupSize, out yGroupSize, out zGroupSize);

            cmd.SetComputeFloatParam(compressionCompute, "_QualityFactor", qualityFactor);
            cmd.SetComputeTextureParam(compressionCompute, quantizeKernel, "_TransformTex", renderTarget);
            cmd.SetComputeBufferParam(compressionCompute, quantizeKernel, "_QuantizationTable", quantizationBuffer);

            cmd.DispatchCompute(compressionCompute, quantizeKernel,
                Mathf.CeilToInt(textureSize.x / (float)xGroupSize),
                Mathf.CeilToInt(textureSize.y / (float)yGroupSize),
                1);
            compressionCompute.GetKernelThreadGroupSizes(icdt_VerticalKernel, out xGroupSize, out yGroupSize, out zGroupSize);

            cmd.SetComputeTextureParam(compressionCompute, icdt_VerticalKernel, "_TransformTex", renderTarget);

            cmd.DispatchCompute(compressionCompute, icdt_VerticalKernel,
                Mathf.CeilToInt(textureSize.x / (float)xGroupSize),
                Mathf.CeilToInt(numBlocks.y / (float)yGroupSize),
                1);

            compressionCompute.GetKernelThreadGroupSizes(icdt_HorizontalKernel, out xGroupSize, out yGroupSize, out zGroupSize);

            cmd.SetComputeTextureParam(compressionCompute, icdt_HorizontalKernel, "_TransformTex", renderTarget);

            cmd.DispatchCompute(compressionCompute, icdt_HorizontalKernel,
                Mathf.CeilToInt(numBlocks.x / (float)xGroupSize),
                Mathf.CeilToInt(textureSize.y / (float)yGroupSize),
                1);

            compressionCompute.GetKernelThreadGroupSizes(decenterValuesKernel, out xGroupSize, out yGroupSize, out zGroupSize);

            cmd.SetComputeTextureParam(compressionCompute, decenterValuesKernel, "_TransformTex", renderTarget);

            cmd.DispatchCompute(compressionCompute, decenterValuesKernel,
                Mathf.CeilToInt(textureSize.x / (float)xGroupSize),
                Mathf.CeilToInt(textureSize.y / (float)yGroupSize),
                1);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // Release Temporary RT here
            cmd.ReleaseTemporaryRT(rgbTextureId);
            cmd.ReleaseTemporaryRT(yTextureId);
            cmd.ReleaseTemporaryRT(cbTextureId);
            cmd.ReleaseTemporaryRT(crTextureId);
        }

        public void DisposeBuffers()
        {
            if (lumaQuantizeBuffer != null) lumaQuantizeBuffer.Dispose();
            if (chromaQuantizeBuffer != null) chromaQuantizeBuffer.Dispose();
        }
    }


    public override void Create()
    {
        _initialized = false;
        if (!Initialize()) return;

        if (!RendererFeatureFunctions.ValidUniversalPipeline(GraphicsSettings.defaultRenderPipeline, true, false)) return;

        _renderPass = new CompressionPass(
            _profilerTag,
            _renderPassEvent,
            _compressionCompute,
            _YCbCrCompute,
            _chromaSubsampleRatio,
            _qualityFactor);

        _initialized = true;
    }

    private bool Initialize()
    {
        if (_compressionCompute == null) return false;
        if (_YCbCrCompute == null) return false;
        //if (!RendererFeatureFunctions.CreateMaterial(_shader, ref _material)) return false;
        return true;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!_initialized) return;

        if (((int)_visibleFrom & (int)renderingData.cameraData.cameraType) == 0) return;

        renderer.EnqueuePass(_renderPass);
    }

    protected override void Dispose(bool disposing)
    {
        //RendererFeatureFunctions.DisposeMaterial(ref _material);
    }
    private void OnDisable()
    {
        if (_renderPass != null) _renderPass.DisposeBuffers();
    }
}


