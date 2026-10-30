#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class StoryScene {
    [Serializable] class EffectShaderRow {public string source,asset;}
    [Serializable] class EffectShaderMap {public EffectShaderRow[] shaders;}
    Dictionary<string,Shader> effectCameraShaders;
    void ConfigureEffectComposite(GameObject effect) {
        if(effectCameraShaders==null) {
            effectCameraShaders=new Dictionary<string,Shader>();
            var manifest=StoryContent.Load<TextAsset>("story-effect-composite");
            if(!manifest)throw new InvalidOperationException("Run prepare_effect_composite.py before building story effects");
            foreach(var row in BackendJson.FromJson<EffectShaderMap>(manifest.text).shaders) {
                var shader=OriginalAsset<Shader>(row.asset);
                if(!shader.isSupported)throw new InvalidOperationException("Unsupported effect camera shader "+row.source);
                effectCameraShaders.Add(row.source,shader);
            }
        }
        foreach(var renderer in effect.GetComponentsInChildren<Renderer>(true))
            foreach(var material in renderer.materials)
                if(effectCameraShaders.TryGetValue(material.shader.name,out var shader))material.shader=shader;
    }
}

#endif
