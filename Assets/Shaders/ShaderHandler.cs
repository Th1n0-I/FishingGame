using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace GrayWolf.GPUInstancing.Domain
{
    public class PersistentDepthFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// Returns the last cached camera depth texture. (It can be this frame or one of previous frames)
        /// </summary>
        public static RTHandle PersistentDepthTexture => _persistentDepthTexture;

        private DepthCopyPass _depthPass;
        private static RTHandle _persistentDepthTexture;
        private static readonly int k_PersistentCameraDepthID = Shader.PropertyToID("_PersistentCameraDepth");

        [SerializeField] private RenderPassEvent renderPassEvent;

        public override void Create()
        {
            _depthPass = new DepthCopyPass(renderPassEvent);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if(renderingData.cameraData.cameraType != CameraType.Game)
                return;
        
            // Ensure our persistent depth RT is allocated with current camera size
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;                           // no depth buffer (we're storing depth as color)
            desc.msaaSamples = 1;                               // no MSAA for the depth texture
            desc.stencilFormat = GraphicsFormat.None;           // no stencil
            desc.graphicsFormat = GraphicsFormat.R32_SFloat;    // 32-bit float single channel
        
            RenderingUtils.ReAllocateHandleIfNeeded(ref _persistentDepthTexture, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_PersistentDepthTexture");
                
            if (PersistentDepthTexture == null || PersistentDepthTexture.rt == null)
            {
                Debug.LogError("Persistent Depth RT is null.");
            }
        
            // Assign the RTHandle to our pass so it can import it
            _depthPass.Setup(PersistentDepthTexture);
        
            renderer.EnqueuePass(_depthPass);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (_persistentDepthTexture != null)
            {
                _persistentDepthTexture.Release();
                _persistentDepthTexture = null;
            }
        }
    
        class DepthCopyPass : ScriptableRenderPass
        {
            private RTHandle _persistentDepthHandle;

            public DepthCopyPass(RenderPassEvent renderPassEvent)
            {
                this.renderPassEvent = renderPassEvent;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public void Setup(RTHandle persistentDepthHandle)
            {
                this._persistentDepthHandle = persistentDepthHandle;
            }

            // RecordRenderGraph is called each frame to build the render graph pass
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
            {
                // Import the persistent RTHandle into the render graph (for writing)
                TextureHandle depthTarget = renderGraph.ImportTexture(_persistentDepthHandle);
                // Get the current camera depth (frame data) texture handle
                UniversalResourceData frameData = frameContext.Get<UniversalResourceData>();
                TextureHandle cameraDepth = frameData.cameraDepthTexture;
            
                if (!cameraDepth.IsValid())
                {
                    Debug.LogError("Camera depth texture is not valid!");
                    return;
                }
            
                renderGraph.AddBlitPass(cameraDepth, depthTarget, Vector2.one, Vector2.zero, passName: "Copy Depth To Persistent Texture");
                Shader.SetGlobalTexture(k_PersistentCameraDepthID, _persistentDepthTexture.rt);
            }
        }
    }
}