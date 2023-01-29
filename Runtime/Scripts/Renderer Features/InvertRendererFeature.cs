using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

class InvertRendererFeature : ScriptableRendererFeature
{
    #region Public properties and methods
    [SerializeField, Range(0, 1)]
    float _invertStrength = 1;
    public float invertStrength
    {
        get { return _invertStrength; }
        set { _invertStrength = value; }
    }
    #endregion

    #region Private properties
    [SerializeField]
    string _profilerTag = "Invert Renderer Feature";

    [SerializeField]
    private CameraType _visibleFrom = CameraType.SceneView | CameraType.Game;

    [SerializeField]
    private RenderPassEvent _renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

    [SerializeField]
    private Shader _shader;

    private Material _material;

    private InvertPass _renderPass = null;

    private bool _initialized = false;
    #endregion

    class InvertPass : ScriptableRenderPass
    {
        // used to label this pass in Unity's Frame Debug utility
        string profilerTag;

        Material material;
        float invertStrength;

        RenderTargetIdentifier cameraColorTarget;

        int tempTextureID;
        RenderTargetIdentifier tempTexture;

        public InvertPass(
            string profilerTag,
            RenderPassEvent renderPassEvent,
            Material material,
            float invertStrength
            )
        {
            this.profilerTag = profilerTag;
            this.renderPassEvent = renderPassEvent;
            this.material = material;
            this.invertStrength = invertStrength;

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

            material.SetFloat("_InvertStrength", invertStrength);

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

        _renderPass = new InvertPass(
            _profilerTag,
            _renderPassEvent,
            _material,
            _invertStrength);

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


