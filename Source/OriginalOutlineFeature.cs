using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine;

// The preserved shader has a separate NiloToonOutline pass.
public sealed class OriginalOutlineFeature : ScriptableRendererFeature
{
    // NiloToon's opaque pass samples depth for its rim light. A depth copy
    // AFTER opaques is too late and can leave the previous UI camera's depth
    // bound, lighting the entire character. Request this camera's depth first.
    sealed class CharacterDepthPass : ScriptableRenderPass {
        public CharacterDepthPass(){renderPassEvent=RenderPassEvent.BeforeRenderingOpaques;ConfigureInput(ScriptableRenderPassInput.Depth);}
        public override void Execute(ScriptableRenderContext context,ref RenderingData data){}
    }
    sealed class OutlinePass : ScriptableRenderPass {
        public OutlinePass(){renderPassEvent=RenderPassEvent.AfterRenderingOpaques;}
        public override void Execute(ScriptableRenderContext context,ref RenderingData data){
            if(data.cameraData.camera.cullingMask!=(1<<17) && !data.cameraData.camera.GetComponent<StoryCinematicCamera>())return;
            var settings=CreateDrawingSettings(new ShaderTagId("NiloToonOutline"),ref data,SortingCriteria.CommonOpaque);
            var filtering=new FilteringSettings(RenderQueueRange.opaque,data.cameraData.camera.cullingMask);
            context.DrawRenderers(data.cullResults,ref settings,ref filtering);
        }
    }
    OutlinePass pass;
    CharacterDepthPass depth;
    public override void Create(){pass=new OutlinePass();depth=new CharacterDepthPass();}
    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data){
        if(data.cameraData.camera.cullingMask==(1<<17) || data.cameraData.camera.GetComponent<StoryCinematicCamera>())renderer.EnqueuePass(depth);
        renderer.EnqueuePass(pass);
    }
}
