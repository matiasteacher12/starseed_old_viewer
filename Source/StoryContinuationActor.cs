#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class StoryContinuationActor : MonoBehaviour
{
    readonly List<Material> materials=new List<Material>();
    Transform head;
    SkinnedMeshRenderer face;
    int expressionIndex=-1;
    float weight, brightness;
    Vector3 home;
    Quaternion facing;
    AnimationCurve struggle;
    float entered;
    [Serializable] class CurveKey {public float time,value,inTangent,outTangent;}
    [Serializable] class CurveData {public CurveKey[] keys;}

    public void Initialize(StoryScene.ContinuationActor config, Action<Material,Transform> setDefaults)
    {
        home=config.position;facing=Quaternion.Euler(0,config.rotationY,0);
        transform.SetPositionAndRotation(home,facing);
        transform.localScale=Vector3.one;
        foreach(var tr in GetComponentsInChildren<Transform>(true)) tr.gameObject.layer=17;
        foreach(var a in GetComponentsInChildren<AudioSource>(true)) a.enabled=false;
        foreach(var renderer in GetComponentsInChildren<Renderer>(true)) {
            if(renderer.name.ToLowerInvariant().Contains("shadow")) {renderer.enabled=false;continue;}
            foreach(var material in renderer.materials) {setDefaults(material,transform);materials.Add(material);}
        }
        head=GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Exp_Bip001 Head_Lookat");
        foreach(var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
            if(!renderer.sharedMesh)continue;
            int index=renderer.sharedMesh.GetBlendShapeIndex(config.expression);
            if(index>=0){face=renderer;expressionIndex=index;break;}
        }
        weight=config.expressionWeight;brightness=config.brightness;
        if(config.asset.Contains("mana")) {
            var curve=BackendJson.FromJson<CurveData>(StoryContent.Load<TextAsset>("dialogue-character-struggle").text);
            struggle=new AnimationCurve(curve.keys.Select(k=>new Keyframe(k.time,k.value,k.inTangent,k.outTangent)).ToArray());
        }
        var animator=GetComponentInChildren<Animator>();
        animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        animator.Play("idle_inven",0,0);animator.Update(0);
    }

    void OnEnable(){entered=Time.time;}

    void LateUpdate()
    {
        if(struggle!=null)transform.position=home+facing*new Vector3(struggle.Evaluate(Time.time-entered),0,0);
        foreach(var material in materials) {
            if(head){material.SetVector("_FaceForwardDirection",head.up);material.SetVector("_FaceUpDirection",-head.right);}
            material.SetColor("_PerCharacterBaseColorTint",new Color(brightness,brightness,brightness,1));
        }
        if(face && expressionIndex>=0)face.SetBlendShapeWeight(expressionIndex,weight);
    }
}

#endif
