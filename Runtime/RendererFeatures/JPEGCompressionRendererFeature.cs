using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


[Serializable]
class JPEGCompressionSettings
{
    [SerializeField, Range(1, 8)]
    private int _DownsampleRatio = 1;
    public int DownsampleRatio { get => _DownsampleRatio; }
    [SerializeField]
    private FilterMode _DownsampleFilterMode = FilterMode.Point;
    public FilterMode DownsampleFilterMode { get => _DownsampleFilterMode; }
    [SerializeField, Range(1, 8)]
    private int _ChromaSubsampleRatio = 2;
    public int ChromaSubsampleRatio { get => _ChromaSubsampleRatio; }
    [SerializeField]
    private FilterMode _SubsampleFilterMode = FilterMode.Point;
    public FilterMode SubsampleFilterMode { get => _SubsampleFilterMode; }
    [SerializeField, Range(0.0f, 12.0f)]
    private float _QualityFactor = 1;
    public float QualityFactor { get => Mathf.Pow(_QualityFactor, 2); }

    [SerializeField]
    private string _ProfilerTag = "JPEG Compression Renderer Feature";
    public string ProfilerTag { get => _ProfilerTag; }
    [SerializeField]
    private RenderPassEvent _RenderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    public RenderPassEvent RenderPassEvent { get => _RenderPassEvent; }
}

[DisallowMultipleRendererFeature]
[Tooltip("Applies compression to the rendered image based on the JPEG standard")]
class JPEGCompressionRendererFeature : ScriptableRendererFeature
{
    // Serialized Fields
    [SerializeField, HideInInspector]
    private ComputeShader m_JPEGCompute;
    [SerializeField, HideInInspector]
    private ComputeShader m_YCbCrCompute;
    [SerializeField] 
    private JPEGCompressionSettings m_Settings = new JPEGCompressionSettings();
    [SerializeField]
    private CameraType m_CameraType = CameraType.SceneView | CameraType.Game;

    // Private Fields
    private JPEGCompressionPass m_CompressionPass = null;
    private bool m_Initialized = false;

    // Constants
    private const string k_ComputePath = "Compute/";
    private const string k_CompressionComputeName = "JPEGCompression";
    private const string k_YCbCrComputeComputeName = "YCbCrCalculations";

    public override void Create()
    {
        if (!RendererFeatureHelper.ValidUniversalPipeline(GraphicsSettings.defaultRenderPipeline, true, false)) return;

        m_Initialized = Initialize();
        
        if(m_Initialized) 
            if (m_CompressionPass == null) 
                m_CompressionPass = new JPEGCompressionPass(m_JPEGCompute, m_YCbCrCompute);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!m_Initialized) return;

        if (!RendererFeatureHelper.CameraTypeMatches(m_CameraType, renderingData.cameraData.cameraType)) return;

        bool shouldAdd = m_CompressionPass.Setup(m_Settings, renderer);
        if (shouldAdd)
        {
            renderer.EnqueuePass(m_CompressionPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        m_CompressionPass?.Dispose();
        base.Dispose(disposing);
    }

    private bool Initialize()
    {
        if (!RendererFeatureHelper.LoadComputeShader(ref m_JPEGCompute, k_ComputePath, k_CompressionComputeName)) return false;
        if (!RendererFeatureHelper.LoadComputeShader(ref m_YCbCrCompute, k_ComputePath, k_YCbCrComputeComputeName)) return false;
        return true;
    }

    class JPEGCompressionPass : ScriptableRenderPass
    {
        // Private Variables
        private ComputeShader m_JPEGCompute;
        private ComputeShader m_YCbCrCompute;
        private Dictionary<JPEGKernels, Vector3Int> m_JPEGComputeDict= new Dictionary<JPEGKernels, Vector3Int>();
        private Dictionary<YCbCrKernels, Vector3Int> m_YCbCrComputeDict = new Dictionary<YCbCrKernels, Vector3Int>();
        private Vector2Int m_FullTexSize = new Vector2Int();
        private Vector2Int m_SubsampleTexSize = new Vector2Int();
        private Vector2Int m_NumberOfBlocksFull = new Vector2Int();
        private Vector2Int m_NumberOfBlocksSubsample = new Vector2Int();
        private ProfilingSampler m_ProfilingSampler = null;
        private ScriptableRenderer m_Renderer = null;
        private RenderTargetIdentifier m_RGBTextureTarget;
        private RenderTargetIdentifier m_YTextureTarget;
        private RenderTargetIdentifier m_CbTextureTarget;
        private RenderTargetIdentifier m_CrTextureTarget;
        private ComputeBuffer m_LumaQuantizeBuffer;
        private ComputeBuffer m_ChromaQuantizeBuffer;
        private JPEGCompressionSettings m_CurrentSettings = new JPEGCompressionSettings();

        // Statics
        private static readonly int s_RGBTextureId = Shader.PropertyToID("_JPEG_RGBTex");
        private static readonly int s_YTextureId = Shader.PropertyToID("_JPEG_YTex");
        private static readonly int s_CbTextureId = Shader.PropertyToID("_JPEG_CbTex");
        private static readonly int s_CrTextureId = Shader.PropertyToID("_JPEG_CrTex");
        private static readonly int[] s_LumaQuantizeTable = new int[] // 64 vals
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

        private static readonly int[] s_ChromaQuantizeTable = new int[] // 64 vals
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

        private enum JPEGKernels
        {
            CDT_Horizontal = 0,
            CDT_Vertical = 1,
            ICDT_Horizontal = 2,
            ICDT_Vertical = 3,
            CenterValues = 4,
            DecenterValues = 5,
            Quantize = 6
        }

        private enum YCbCrKernels
        {
            ToYCbCr = 0,
            ToRGB = 1
        }

        public JPEGCompressionPass(ComputeShader jpegCompute, ComputeShader ycbcrCompute)
        {
            m_CurrentSettings = new JPEGCompressionSettings();
            m_JPEGCompute = jpegCompute;
            m_YCbCrCompute = ycbcrCompute;

            if(m_JPEGCompute != null)
            {
                foreach(JPEGKernels kernel in Enum.GetValues(typeof(JPEGKernels))) 
                {
                    m_JPEGComputeDict.Add(kernel, RenderPassHelper.GetKernelThredGroupVector(m_JPEGCompute, (int)kernel));
                }
            }
            if(m_YCbCrCompute != null)
            {
                foreach (YCbCrKernels kernel in Enum.GetValues(typeof(YCbCrKernels)))
                {
                    m_YCbCrComputeDict.Add(kernel, RenderPassHelper.GetKernelThredGroupVector(m_YCbCrCompute, (int)kernel));
                }
            }
        }


        public bool Setup(JPEGCompressionSettings settings, ScriptableRenderer renderer)
        {
            m_CurrentSettings = settings;
            m_Renderer = renderer;

            m_ProfilingSampler = new ProfilingSampler(m_CurrentSettings.ProfilerTag);
            renderPassEvent = settings.RenderPassEvent;
            CreateBuffers();
            ConfigureInput(ScriptableRenderPassInput.Color);

            if(m_JPEGCompute == null || m_YCbCrCompute == null) return false;
            return true;
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            m_FullTexSize.x = Mathf.CeilToInt(cameraTargetDescriptor.width / (float)m_CurrentSettings.DownsampleRatio);
            m_FullTexSize.y = Mathf.CeilToInt(cameraTargetDescriptor.height / (float)m_CurrentSettings.DownsampleRatio);
            m_SubsampleTexSize.x = Mathf.CeilToInt(m_FullTexSize.x / (float)m_CurrentSettings.ChromaSubsampleRatio);
            m_SubsampleTexSize.y = Mathf.CeilToInt(m_FullTexSize.y / (float)m_CurrentSettings.ChromaSubsampleRatio);

            m_NumberOfBlocksFull.x = Mathf.CeilToInt(m_FullTexSize.x / 8.0f);
            m_NumberOfBlocksFull.y = Mathf.CeilToInt(m_FullTexSize.y / 8.0f);
            m_NumberOfBlocksSubsample.x = Mathf.CeilToInt(m_SubsampleTexSize.x / 8.0f);
            m_NumberOfBlocksSubsample.y = Mathf.CeilToInt(m_SubsampleTexSize.y / 8.0f);

            RenderTextureDescriptor rgbDescriptor = new RenderTextureDescriptor(
            m_FullTexSize.x,
            m_FullTexSize.y,
            cameraTargetDescriptor.colorFormat
            );
            rgbDescriptor.msaaSamples = 1;
            rgbDescriptor.enableRandomWrite = true;

            cmd.GetTemporaryRT(s_RGBTextureId, rgbDescriptor, m_CurrentSettings.DownsampleFilterMode);
            m_RGBTextureTarget = new RenderTargetIdentifier(s_RGBTextureId);
            ConfigureTarget(m_RGBTextureTarget);

            RenderTextureDescriptor lumaDescriptor = new RenderTextureDescriptor(
            m_FullTexSize.x,
            m_FullTexSize.y,
            RenderTextureFormat.RFloat
            );
            lumaDescriptor.msaaSamples = 1;
            lumaDescriptor.enableRandomWrite = true;

            cmd.GetTemporaryRT(s_YTextureId, lumaDescriptor, m_CurrentSettings.DownsampleFilterMode);
            m_YTextureTarget = new RenderTargetIdentifier(s_YTextureId);
            ConfigureTarget(m_YTextureTarget);

            RenderTextureDescriptor chromaDescriptor = new RenderTextureDescriptor(
            m_SubsampleTexSize.x,
            m_SubsampleTexSize.y,
            RenderTextureFormat.RFloat
            );
            chromaDescriptor.msaaSamples = 1;
            chromaDescriptor.enableRandomWrite = true;

            cmd.GetTemporaryRT(s_CbTextureId, chromaDescriptor, m_CurrentSettings.SubsampleFilterMode);
            m_CbTextureTarget = new RenderTargetIdentifier(s_CbTextureId);
            ConfigureTarget(m_CbTextureTarget);

            cmd.GetTemporaryRT(s_CrTextureId, chromaDescriptor, m_CurrentSettings.SubsampleFilterMode);
            m_CrTextureTarget = new RenderTargetIdentifier(s_CrTextureId);
            ConfigureTarget(m_CrTextureTarget);

            cmd.SetBufferData(m_LumaQuantizeBuffer, s_LumaQuantizeTable);
            cmd.SetBufferData(m_ChromaQuantizeBuffer, s_ChromaQuantizeTable);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // fetch a command buffer to use
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                // where the render pass does its work
                cmd.Blit(m_Renderer.cameraColorTarget, m_RGBTextureTarget);

                // fill Y Cb and Cr textures from RGB
                RGBToYCbCr(
                    cmd, 
                    m_RGBTextureTarget,
                    m_YTextureTarget, 
                    m_CbTextureTarget,
                    m_CrTextureTarget,
                    m_SubsampleTexSize,
                    m_CurrentSettings.ChromaSubsampleRatio);

                // compress the Y (luma) texture
                CompressTexture(cmd, 
                    m_YTextureTarget, 
                    m_LumaQuantizeBuffer,
                    m_FullTexSize,
                    m_NumberOfBlocksFull,
                    m_CurrentSettings.QualityFactor);
                // compress the Cb (chroma) texture
                CompressTexture(cmd,
                    m_CbTextureTarget,
                    m_ChromaQuantizeBuffer,
                    m_SubsampleTexSize,
                    m_NumberOfBlocksSubsample, 
                    m_CurrentSettings.QualityFactor);
                // compress the Cr (chroma) texture
                CompressTexture(cmd, 
                    m_CrTextureTarget, 
                    m_ChromaQuantizeBuffer, 
                    m_SubsampleTexSize,
                    m_NumberOfBlocksSubsample, 
                    m_CurrentSettings.QualityFactor);

                // fill the RGB texture from Y Cb and Cr
                YCbCrToRGB(
                    cmd,
                    m_RGBTextureTarget,
                    m_YTextureTarget, 
                    m_CbTextureTarget, 
                    m_CrTextureTarget,
                    m_FullTexSize);

                // then blit back into color target 
                cmd.Blit(m_RGBTextureTarget, m_Renderer.cameraColorTarget);
            }
            // don't forget to tell ScriptableRenderContext to actually execute the commands
            context.ExecuteCommandBuffer(cmd);

            // tidy up after ourselves
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        void RGBToYCbCr(CommandBuffer cmd, 
            RenderTargetIdentifier rgbTarget, 
            RenderTargetIdentifier yTarget, 
            RenderTargetIdentifier cbTarget, 
            RenderTargetIdentifier crTarget,
            Vector2Int subsampleTextureSize,
            int subsampleRatio)
        {
            RenderPassHelper.SetComputeIntParamsVector(cmd, m_YCbCrCompute, "_SubsampleTexSize", subsampleTextureSize);
            cmd.SetComputeIntParam(m_YCbCrCompute, "_CbCrSubsample", subsampleRatio);

            cmd.SetComputeTextureParam(m_YCbCrCompute, (int)YCbCrKernels.ToYCbCr, "_RBGTexIn", rgbTarget);
            cmd.SetComputeTextureParam(m_YCbCrCompute, (int)YCbCrKernels.ToYCbCr, "_YTexOut", yTarget);
            cmd.SetComputeTextureParam(m_YCbCrCompute, (int)YCbCrKernels.ToYCbCr, "_CbTexOut", cbTarget);
            cmd.SetComputeTextureParam(m_YCbCrCompute, (int)YCbCrKernels.ToYCbCr, "_CrTexOut", crTarget);

            RenderPassHelper.DispatchComputeAtSize(
                cmd,
                m_YCbCrCompute,
                (int)YCbCrKernels.ToYCbCr,
                m_YCbCrComputeDict[YCbCrKernels.ToYCbCr],
                subsampleTextureSize);
        }

        void YCbCrToRGB(CommandBuffer cmd, 
            RenderTargetIdentifier rgbTarget,
            RenderTargetIdentifier yTarget, 
            RenderTargetIdentifier cbTarget, 
            RenderTargetIdentifier crTarget,
            Vector2Int fullTextureSize)
        {
            //RenderPassHelper.SetComputeIntParamsVector(cmd, m_YCbCrCompute, "_SubsampleTexSize", subsampleTextureSize);
            RenderPassHelper.SetComputeIntParamsVector(cmd, m_YCbCrCompute, "_FullTexSize", fullTextureSize);

            cmd.SetComputeTextureParam(m_YCbCrCompute, (int)YCbCrKernels.ToRGB, "_RBGTexOut", rgbTarget);
            cmd.SetComputeTextureParam(m_YCbCrCompute, (int)YCbCrKernels.ToRGB, "_YTexIn", yTarget);
            cmd.SetComputeTextureParam(m_YCbCrCompute, (int)YCbCrKernels.ToRGB, "_CbTexIn", cbTarget);
            cmd.SetComputeTextureParam(m_YCbCrCompute, (int)YCbCrKernels.ToRGB, "_CrTexIn", crTarget);

            RenderPassHelper.DispatchComputeAtSize(
                cmd,
                m_YCbCrCompute,
                (int)YCbCrKernels.ToRGB,
                m_YCbCrComputeDict[YCbCrKernels.ToRGB],
                fullTextureSize);
        }

        void CompressTexture(CommandBuffer cmd, RenderTargetIdentifier renderTarget, ComputeBuffer quantizationBuffer, Vector2Int textureSize, Vector2Int numberOfBlocks, float qualityFactor)
        {
            // center values from 0 - 1 to -128 - 128
            RenderPassHelper.SetComputeIntParamsVector(cmd, m_JPEGCompute, "_TextureSize", textureSize);
            cmd.SetComputeTextureParam(m_JPEGCompute, (int)JPEGKernels.CenterValues, "_TransformTex", renderTarget);

            RenderPassHelper.DispatchComputeAtSize(
                cmd,
                m_JPEGCompute,
                (int)JPEGKernels.CenterValues,
                m_JPEGComputeDict[JPEGKernels.CenterValues],
                textureSize);

            // horizontal cosine discrete transform (CDT II)
            RenderPassHelper.SetComputeIntParamsVector(cmd, m_JPEGCompute, "_NumBlocks", numberOfBlocks);
            cmd.SetComputeTextureParam(m_JPEGCompute, (int)JPEGKernels.CDT_Horizontal, "_TransformTex", renderTarget);

            RenderPassHelper.DispatchComputeAtSize(
                cmd,
                m_JPEGCompute,
                (int)JPEGKernels.CDT_Horizontal,
                m_JPEGComputeDict[JPEGKernels.CDT_Horizontal],
                numberOfBlocks.x,
                textureSize.y);

            // vertical cosine discrete transform (CDT II)
            cmd.SetComputeTextureParam(m_JPEGCompute, (int)JPEGKernels.CDT_Vertical, "_TransformTex", renderTarget);

            RenderPassHelper.DispatchComputeAtSize(
                cmd,
                m_JPEGCompute,
                (int)JPEGKernels.CDT_Vertical,
                m_JPEGComputeDict[JPEGKernels.CDT_Vertical],
                textureSize.x,
                numberOfBlocks.y);

            // quantize
            cmd.SetComputeFloatParam(m_JPEGCompute, "_QualityFactor", qualityFactor);
            cmd.SetComputeTextureParam(m_JPEGCompute, (int)JPEGKernels.Quantize, "_TransformTex", renderTarget);
            cmd.SetComputeBufferParam(m_JPEGCompute, (int)JPEGKernels.Quantize, "_QuantizationTable", quantizationBuffer);

            RenderPassHelper.DispatchComputeAtSize(
                cmd,
                m_JPEGCompute,
                (int)JPEGKernels.Quantize,
                m_JPEGComputeDict[JPEGKernels.Quantize],
                textureSize);

            // horizontal inverse cosine discrete transform (CDT III)
            cmd.SetComputeTextureParam(m_JPEGCompute, (int)JPEGKernels.ICDT_Horizontal, "_TransformTex", renderTarget);

            RenderPassHelper.DispatchComputeAtSize(
                cmd,
                m_JPEGCompute,
                (int)JPEGKernels.ICDT_Horizontal,
                m_JPEGComputeDict[JPEGKernels.ICDT_Horizontal],
                numberOfBlocks.x,
                textureSize.y);

            // vertical inverse cosine discrete transform (CDT III)
            cmd.SetComputeTextureParam(m_JPEGCompute, (int)JPEGKernels.ICDT_Vertical, "_TransformTex", renderTarget);

            RenderPassHelper.DispatchComputeAtSize(
                cmd,
                m_JPEGCompute,
                (int)JPEGKernels.ICDT_Vertical,
                m_JPEGComputeDict[JPEGKernels.ICDT_Vertical],
                textureSize.x,
                numberOfBlocks.y);

            // decenter values from -128 - 128 to 0 - 1
            cmd.SetComputeTextureParam(m_JPEGCompute, (int)JPEGKernels.DecenterValues, "_TransformTex", renderTarget);

            RenderPassHelper.DispatchComputeAtSize(
                cmd,
                m_JPEGCompute,
                (int)JPEGKernels.DecenterValues,
                m_JPEGComputeDict[JPEGKernels.DecenterValues],
                textureSize);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // release temporary RT here
            cmd.ReleaseTemporaryRT(s_RGBTextureId);
            cmd.ReleaseTemporaryRT(s_YTextureId);
            cmd.ReleaseTemporaryRT(s_CbTextureId);
            cmd.ReleaseTemporaryRT(s_CrTextureId);
        }

        public void Dispose()
        {
            m_LumaQuantizeBuffer?.Dispose();
            m_ChromaQuantizeBuffer?.Dispose();
        }

        public void CreateBuffers()
        {
            RenderPassHelper.CreateBufferIfInvalid(ref m_LumaQuantizeBuffer, 64, sizeof(int));
            RenderPassHelper.CreateBufferIfInvalid(ref m_ChromaQuantizeBuffer, 64, sizeof(int));
        }
    }
}


