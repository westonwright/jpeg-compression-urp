using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

class SharpenRenderFeature : ScriptableRendererFeature
{
    #region Constant properties
    #endregion

    #region Public properties and methods
    [SerializeField, Range(0, 10)]
    float _amount = 1f;
    public float amount
    {
        get { return _amount; }
        set { _amount = value; }
    }

    [SerializeField, Range(0, 1)]
    float _threshold = 0;
    public float threshold
    {
        get { return _threshold; }
        set { _threshold = value; }
    }

    [SerializeField, Range(0, 1)]
    float _thresholdRange = .1f;
    public float thresholdRange
    {
        get { return _thresholdRange; }
        set { _thresholdRange = value; }
    }

    [SerializeField, Range(2, 12)]
    int _diameter = 2;
    public int diameter
    {
        get { return _diameter; }
        set { _diameter = value; }
    }


    [SerializeField, Range(.01f, 10)]
    float _detail = 2;
    public float detail
    {
        get { return _detail; }
        set { _detail = value; }
    }
    #endregion

    #region Private properties

    [SerializeField]
    string _profilerTag = "Sharpen Renderer Feature";

    [SerializeField]
    private CameraType _visibleFrom = CameraType.SceneView | CameraType.Game;

    [SerializeField]
    RenderPassEvent _renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

    [SerializeField]
    private Shader _shader;

    private Material _material;

    private SharpenPass _renderPass = null;

    private bool _initialized = false;

    #endregion

    class SharpenPass : ScriptableRenderPass
    {
        // used to label this pass in Unity's Frame Debug utility
        private string profilerTag;

        private Material material;
        private float amount;
        private float threshold;
        private float thresholdRange;
        private int diameter;
        private float detail;

        RenderTargetIdentifier cameraColorTarget;

        int tempTextureID;
        RenderTargetIdentifier tempTexture;

        public SharpenPass(
            string profilerTag,
            RenderPassEvent renderPassEvent,
            Material material,
            float amount,
            float threshold,
            float thresholdRange,
            int diameter,
            float detail
            )
        {
            this.profilerTag = profilerTag;
            this.renderPassEvent = renderPassEvent;
            this.material = material;
            this.amount = amount;
            this.threshold = threshold;
            this.thresholdRange = thresholdRange;
            this.diameter = diameter;
            this.detail = detail;

            tempTextureID = Shader.PropertyToID("_TempTexture");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            cameraColorTarget = renderingData.cameraData.renderer.cameraColorTarget;
        }

        // called each frame before Execute, use it to set up things the pass will need
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            cmd.GetTemporaryRT(tempTextureID, cameraTextureDescriptor);
            tempTexture = new RenderTargetIdentifier(tempTextureID);
            ConfigureTarget(tempTexture);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // fetch a command buffer to use
            CommandBuffer cmd = CommandBufferPool.Get(profilerTag);
            cmd.Clear();

            material.SetFloat("_Amount", amount);
            material.SetFloat("_Threshold", threshold);
            material.SetFloat("_ThresholdRange", thresholdRange);
            material.SetInt("_Diameter", diameter);
            material.SetFloat("_Detail", detail);

            // where the render pass does its work
            cmd.Blit(cameraColorTarget, tempTexture, material, 0);

            // then blit back into color target 
            cmd.Blit(tempTexture, cameraColorTarget);

            // don't forget to tell ScriptableRenderContext to actually execute the commands
            context.ExecuteCommandBuffer(cmd);

            // tidy up after ourselves
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // Release Temporary RT here
            cmd.ReleaseTemporaryRT(tempTextureID);
        }
    }

    public override void Create()
    {
        _initialized = false;
        if (!Initialize()) return;

        if (!RendererFeatureFunctions.ValidUniversalPipeline(GraphicsSettings.defaultRenderPipeline, true, false)) return;

        _renderPass = new SharpenPass(
            _profilerTag,
            _renderPassEvent,
            _material,
            _amount,
            _threshold,
            _thresholdRange,
            _diameter,
            _detail);

        _initialized = true;
    }

    private bool Initialize()
    {
        if (!RendererFeatureFunctions.CreateMaterial(_shader, ref _material)) return false;
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
        RendererFeatureFunctions.DisposeMaterial(ref _material);
    }
}
