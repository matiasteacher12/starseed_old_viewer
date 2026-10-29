using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    Camera effectAuditCamera;
    RenderTexture effectAuditTarget;
    string effectAuditDirectory;
    readonly HashSet<string> auditedEffects=new HashSet<string>();
    void AuditEffectIfRequested(GameObject effect,string key) {
        if(effectAuditCamera && auditedEffects.Add(key))StartCoroutine(CheckOriginalEffect(effect,key));
    }
    IEnumerator CheckOriginalEffect(GameObject effect,string key) {
        var renderers=effect.GetComponentsInChildren<Renderer>(true);
        foreach(var r in renderers)foreach(var material in r.sharedMaterials)
            if(material && !material.shader.isSupported)throw new Exception("Unsupported original effect shader: "+key+" / "+material.shader.name);
        Debug.Log("STORY_EFFECT_AUDIT key="+key+" renderers="+renderers.Length+" particles="+effect.GetComponentsInChildren<ParticleSystem>(true).Length+
            " components="+string.Join(",",effect.GetComponentsInChildren<Component>(true).Where(c=>c).Select(c=>c.GetType().Name).Distinct()));
        for(int sample=0;sample<3;sample++) {
            yield return new WaitForSecondsRealtime(sample==0?.08f:sample==1?.25f:.55f);
            if(!effect)yield break;
            string prefix=Path.Combine(effectAuditDirectory,Path.GetFileName(key)+"-"+sample);
            var enabled=renderers.Select(r=>r&&r.enabled).ToArray();
            // Same simulation frame and same camera texture for old/new UI
            // composites. Disable only renderers for the no-effect reference;
            // do not restart particles or change original materials/curves.
            try {
                RenderTalkCamera();
                actorImage.material=cameraCompositeMaterial;Canvas.ForceUpdateCanvases();effectAuditCamera.Render();
                SaveStoryTarget(effectAuditTarget,prefix+"-new.png");
                actorImage.material=null;Canvas.ForceUpdateCanvases();effectAuditCamera.Render();
                SaveStoryTarget(effectAuditTarget,prefix+"-old.png");
                for(int i=0;i<renderers.Length;i++)if(renderers[i])renderers[i].enabled=false;
                RenderTalkCamera();
                actorImage.material=cameraCompositeMaterial;Canvas.ForceUpdateCanvases();effectAuditCamera.Render();
                SaveStoryTarget(effectAuditTarget,prefix+"-new-without.png");
                actorImage.material=null;Canvas.ForceUpdateCanvases();effectAuditCamera.Render();
                SaveStoryTarget(effectAuditTarget,prefix+"-old-without.png");
            } finally {
                for(int i=0;i<renderers.Length;i++)if(renderers[i])renderers[i].enabled=enabled[i];
                actorImage.material=cameraCompositeMaterial;
                RenderTalkCamera();
            }
            Debug.Log("STORY_EFFECT_CAPTURE key="+key+" sample="+sample);
        }
    }
}
