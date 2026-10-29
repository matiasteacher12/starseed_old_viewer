using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// PP_nilo_talk uses URP Bloom, not NiloToonBloom. Reuse URP's own filter
// shader/pyramid. Illustrations share the actor target; transparent scenes still
// preserve coverage for the separately authored animated background prefabs.
public sealed class StoryBloomFeature : ScriptableRendererFeature
{
    public Shader bloomShader;
    BloomPass pass;
    BackgroundPass backgroundPass;
    public static bool Enabled = true;
    public static UnityEngine.UI.RawImage Background;
    public static Camera BackgroundCamera;
    public static RenderTexture AnimatedBackground;
    public static bool IncludeBackground = true;
    public override void Create(){pass?.Dispose();backgroundPass?.Dispose();pass=new BloomPass(bloomShader);backgroundPass=new BackgroundPass();}
    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data){
        if(data.cameraData.camera.cullingMask!=(1<<17))return;
        if(data.cameraData.camera==BackgroundCamera&&IncludeBackground&&Background&&Background.gameObject.activeInHierarchy&&(AnimatedBackground||Background.texture&&Background.texture!=Texture2D.blackTexture))
            renderer.EnqueuePass(backgroundPass);
        if(Enabled)renderer.EnqueuePass(pass);
    }
    protected override void Dispose(bool disposing){pass?.Dispose();backgroundPass?.Dispose();}
    // Original ChangeBackground reparents into CanvasTalk before postprocessing.
    // Draw the illustration before geometry so particles blend against it too.
    // Black placeholders leave existing animated background prefabs unobscured.
    sealed class BackgroundPass : ScriptableRenderPass {
        readonly Material material;
        public BackgroundPass(){renderPassEvent=RenderPassEvent.BeforeRenderingOpaques;material=CoreUtils.CreateEngineMaterial(StoryContent.Load<Shader>("StoryBackgroundCamera"));}
        public void Dispose(){CoreUtils.Destroy(material);}
        public override void Execute(ScriptableRenderContext context,ref RenderingData data){
            var image=Background;if(!image||!material)return;
            if(AnimatedBackground) {
                var copy=CommandBufferPool.Get("Animated talk background HDR");
                copy.Blit(AnimatedBackground,data.cameraData.renderer.cameraColorTarget);
                context.ExecuteCommandBuffer(copy);CommandBufferPool.Release(copy);return;
            }
            var uv=image.uvRect;
            material.SetVector("_UvRect",new Vector4(uv.x,uv.y,uv.width,uv.height));
            material.SetColor("_Color",image.color);
            material.SetFloat("_BlurFactor",image.material.GetFloat("_BlurFactor"));
            var cmd=CommandBufferPool.Get("Original talk background");
            cmd.Blit(image.texture,data.cameraData.renderer.cameraColorTarget,material,0);
            context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
        }
    }
    sealed class BloomPass : ScriptableRenderPass
    {
        readonly Material bloom,combine;
        readonly int source=Shader.PropertyToID("_StoryBloomSource");
        readonly int[] down=new int[16],up=new int[16];
        bool logged;
        public BloomPass(Shader shader){
            renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
            if(shader)bloom=CoreUtils.CreateEngineMaterial(shader);
            combine=CoreUtils.CreateEngineMaterial(StoryContent.Load<Shader>("StoryBloomCombine"));
            for(int i=0;i<16;i++){down[i]=Shader.PropertyToID("_StoryBloomDown"+i);up[i]=Shader.PropertyToID("_StoryBloomUp"+i);}
        }
        public void Dispose(){CoreUtils.Destroy(bloom);CoreUtils.Destroy(combine);}
        void Draw(CommandBuffer cmd,RenderTargetIdentifier src,RenderTargetIdentifier dst,int shaderPass){
            cmd.SetGlobalTexture("_SourceTex",src);cmd.Blit(src,dst,bloom,shaderPass);
        }
        public override void Execute(ScriptableRenderContext context,ref RenderingData data){
            if(!bloom||!combine)return;
            var target=data.cameraData.renderer.cameraColorTarget;
            var desc=data.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits=0;desc.msaaSamples=1;
            desc.colorFormat=RenderTextureFormat.ARGBHalf;
            var cmd=CommandBufferPool.Get("Original talk bloom");
            cmd.GetTemporaryRT(source,desc,FilterMode.Bilinear);cmd.Blit(target,source);
            int w=desc.width>>1,h=desc.height>>1;
            int count=Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(Mathf.Max(w,h),2)-1)-4,1,16);
            // Original profile: threshold 1, intensity 1, scatter .85, skip 4.
            bloom.SetVector("_Params",new Vector4(Mathf.Lerp(.05f,.95f,.85f),65472,1,.5f));
            desc.colorFormat=RenderTextureFormat.ARGBHalf;
            for(int i=0;i<count;i++){
                desc.width=w;desc.height=h;
                cmd.GetTemporaryRT(down[i],desc,FilterMode.Bilinear);cmd.GetTemporaryRT(up[i],desc,FilterMode.Bilinear);
                if(i==0)Draw(cmd,source,down[0],0);
                else {Draw(cmd,down[i-1],up[i],1);Draw(cmd,up[i],down[i],2);}
                w=Mathf.Max(1,w>>1);h=Mathf.Max(1,h>>1);
            }
            for(int i=count-2;i>=0;i--){
                cmd.SetGlobalTexture("_SourceTexLowMip",i==count-2?down[i+1]:up[i+1]);
                Draw(cmd,down[i],up[i],3);
            }
            var tint=new Color(1,.7294118f,.7294118f).linear;
            tint/=ColorUtils.Luminance(tint);
            combine.SetVector("_BloomTint",tint);
            cmd.SetGlobalTexture("_StoryBloomTexture",count==1?down[0]:up[0]);
            cmd.Blit(source,target,combine,0);
            cmd.ReleaseTemporaryRT(source);
            for(int i=0;i<count;i++){cmd.ReleaseTemporaryRT(down[i]);cmd.ReleaseTemporaryRT(up[i]);}
            context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
            if(!logged){logged=true;Debug.Log("STORY_BLOOM original tint=(1,.7294118,.7294118) intensity=1 scatter=.85 mipCount="+count);}
        }
    }
}
