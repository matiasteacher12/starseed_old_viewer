#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Adapts original controller/material/slot data to the standalone player.
public sealed class StoryEpisodeActor : MonoBehaviour {
    void OnDestroy(){foreach(var material in materials)if(material)Destroy(material);}
    public StoryEpisode.Actor Definition {get;private set;}
    public Animator Animator {get;private set;}
    public string Motion {get;private set;}
    public string Expression {get;private set;}
    public AudioSource Voice;
    public bool Speaking, Paused;
    public float Brightness=1;
    [Serializable] class MissingMotion {public string actor,motion;}
    [Serializable] class MissingMotionList {public MissingMotion[] entries;}
    static MissingMotionList missingMotions;
    Transform head;
    SkinnedMeshRenderer face;
    readonly List<Material> materials=new List<Material>();
    readonly Dictionary<Material,float> originalRimIntensity=new Dictionary<Material,float>();
    float pivotSpeed=1;
    readonly Dictionary<int,float> weights=new Dictionary<int,float>();
    Coroutine emotionRoutine,makeupRoutine;
    Animation pivotAnimation;
    Transform model;
    float blinkAt,blinkStart=-10;
    float[] samples=new float[256];

    public void Initialize(StoryEpisode.Actor definition,GameObject instance,Action<Material,Transform> defaults) {
        Definition=definition;model=instance.transform;model.SetParent(transform,false);
        foreach(var tr in instance.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=17;
        foreach(var a in instance.GetComponentsInChildren<AudioSource>(true))a.enabled=false;
        foreach(var r in instance.GetComponentsInChildren<Renderer>(true)) {
            if(r.name.ToLowerInvariant().Contains("shadow")){r.enabled=false;continue;}
            foreach(var m in r.materials){defaults(m,model);materials.Add(m);if(m.HasProperty("_DepthTexRimLightIntensity"))originalRimIntensity[m]=m.GetFloat("_DepthTexRimLightIntensity");}
        }
        head=instance.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Exp_Bip001 Head_Lookat");
        face=instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.sharedMesh&&r.sharedMesh.GetBlendShapeIndex("blink")>=0);
        Animator=instance.GetComponentInChildren<Animator>();Animator.applyRootMotion=false;Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        pivotAnimation=gameObject.AddComponent<Animation>();pivotAnimation.playAutomatically=false;
        ResetActor();
    }
    public void Place(StoryEpisode.Slot slot,Vector3 offset) {
        pivotAnimation.Stop();
        transform.parent.SetPositionAndRotation(slot.position,slot.rotation);
        transform.localPosition=Vector3.zero;transform.localRotation=Quaternion.identity;transform.localScale=Vector3.one;
        model.localPosition=new Vector3(offset.x,offset.y,0);model.localRotation=Quaternion.identity;model.localScale=Vector3.one*offset.z;
    }
    public void SetScanline(bool enabled) {
        foreach(var material in materials) {
            material.SetFloat("_StencilRef",enabled?127:0);material.SetFloat("_StencilComp",enabled?8:0);material.SetFloat("_StencilPass",enabled?2:0);
            if(originalRimIntensity.TryGetValue(material,out var rim))material.SetFloat("_DepthTexRimLightIntensity",enabled?1:rim);
        }
    }
    public void ResetActor() {
        foreach(var receiver in GetComponentsInChildren<StoryAnimationEffects>(true))receiver.ClearEffects();
        SetScanline(false);
        StopAllCoroutines();weights.Clear();Expression="normal";Speaking=false;Brightness=1;
        if(face)for(int i=0;i<face.sharedMesh.blendShapeCount;i++)face.SetBlendShapeWeight(i,0);
        if(pivotAnimation)pivotAnimation.Stop();
        foreach(var m in materials)if(m.HasProperty("_BaseMapStackingLayer1TintColor")) {var color=m.GetColor("_BaseMapStackingLayer1TintColor");color.a=0;m.SetColor("_BaseMapStackingLayer1TintColor",color);}
        foreach(var parameter in Animator.parameters) {
            if(parameter.type==AnimatorControllerParameterType.Bool)Animator.SetBool(parameter.name,false);
            if(parameter.type==AnimatorControllerParameterType.Trigger)Animator.ResetTrigger(parameter.name);
        }
        Motion="idle_inven";Animator.Play(Motion,0,0);Animator.Update(0);
    }
    AnimationClip MotionClip(string state) => Animator.runtimeAnimatorController.animationClips.FirstOrDefault(c=>c.name==state || c.name.EndsWith("_"+state,StringComparison.Ordinal));
    float ClipDuration(string state) {var clip=MotionClip(state);return clip?clip.length:0;}

    public float PlayMotion(string name) {
        bool missing=!Animator.HasState(0,Animator.StringToHash(name));
        if(missingMotions==null)missingMotions=BackendJson.FromJson<MissingMotionList>(StoryContent.Load<TextAsset>("story-missing-motions").text);
        if(missing&&!missingMotions.entries.Any(e=>e.actor==Definition.key&&e.motion==name))throw new InvalidOperationException("Missing original motion: "+Definition.key+" / "+name);
        var parameters=Animator.parameters;
        string previous=Motion;
        // TalkScriptEngine.Process maps idle_inven to null before calling
        // LobbyCharacter.PlayAnimation(new, previous). Clearing the old loop bool
        // lets its authored *_end run; triggering idle_inven would interrupt it.
        if(parameters.Any(p=>p.name==previous+"_b"))Animator.SetBool(previous+"_b",false);
        if(parameters.Any(p=>p.name==previous && p.type==AnimatorControllerParameterType.Trigger))Animator.ResetTrigger(previous);
        if(missing) {
            Motion="idle_inven";
            Debug.LogWarning("STORY_SOURCE_MOTION_MISSING "+Definition.key+" / "+name+" previousReleased="+previous);
            return ClipDuration(previous+"_end");
        }
        Motion=name;
        float duration=previous=="idle_inven"?0:ClipDuration(previous+"_end");
        if(name!="idle_inven") {
            bool trigger=parameters.Any(p=>p.name==name && p.type==AnimatorControllerParameterType.Trigger);
            // Explicit *_end commands use the loop's authored exit after its
            // bool was cleared above; they do not need a separate trigger.
            bool authoredExit=name==previous+"_end" && parameters.Any(p=>p.name==previous+"_b");
            if(!trigger && !authoredExit)
                throw new InvalidOperationException("Story motion has no trigger in loaded controller: "+Definition.asset+" / "+name);
            if(trigger)Animator.SetTrigger(name);
            if(parameters.Any(p=>p.name==name+"_b"))Animator.SetBool(name+"_b",true);
            duration+=ClipDuration(name+"_start");
            var clip=MotionClip(name);if(clip && !clip.isLooping)duration+=clip.length;
        }
        Debug.Log("EPISODE_MOTION "+Definition.key+" "+previous+" -> "+name+" wait="+duration);
        return duration;
    }
    public float FaceWeight(string shape) {
        int index=face.sharedMesh.GetBlendShapeIndex(shape);return index<0?0:face.GetBlendShapeWeight(index);
    }
    public void SetEmotion(string name,float to,float duration) {
        // Script EmotionType.Normal is the neutral face (reset all expression
        // weights); it is not a mesh blendshape named "normal".
        string[] names={"Close","Smile","Angry","Sad","Skill1","Skill2","Skill3","Skill4","Surprise","Shy","Dislike","Tough","Bliss","Charm","Hunt","Sly","Pitful"};
        int slot=Array.IndexOf(names,name);
        if(slot<0 && name!="Normal")throw new InvalidOperationException("Unknown script emotion: "+name);
        // The script enum is translated to EmotionController's authored index
        // table. Names such as Bliss need not match the mesh's shape names.
        int index=slot<0?-1:Definition.emotionIndices[slot];
        if(index>=face.sharedMesh.blendShapeCount) {
            // The preserved Frederica prefab requests index 20 for Skill3,
            // but its face has exactly 20 shapes (0..19) and no skill3 shape.
            // Release the preceding expression without substituting another.
            if(Definition.key!="Character/Prefabs/c_attacker_frederica_1"||name!="Skill3"||index!=20||face.sharedMesh.blendShapeCount!=20)
                throw new InvalidOperationException("Invalid original emotion index: "+Definition.key+" / "+name);
            Debug.LogWarning("STORY_SOURCE_EMOTION_MISSING "+Definition.key+" / "+name+" index="+index);
            index=-1;name="Normal";
        }
        Expression=name;
        if(emotionRoutine!=null)StopCoroutine(emotionRoutine);
        emotionRoutine=StartCoroutine(BlendEmotion(index,to,duration));
    }
    IEnumerator BlendEmotion(int index,float target,float duration) {
        var start=new Dictionary<int,float>(weights);if(index>=0 && !start.ContainsKey(index))start[index]=0;
        float elapsed=0;
        do {
            if(!Paused)elapsed+=Time.deltaTime;
            float t=duration<=0?1:Mathf.Clamp01(elapsed/duration);
            foreach(var pair in start)weights[pair.Key]=Mathf.Lerp(pair.Value,pair.Key==index?target:0,t);
            yield return null;
        }while(elapsed<duration);
    }
    public void SetMakeup(int layer,bool on,float duration) {
        if(layer<1||layer>Definition.makeup.Length)throw new InvalidOperationException("Unknown makeup layer "+layer);
        if(makeupRoutine!=null)StopCoroutine(makeupRoutine);
        makeupRoutine=StartCoroutine(BlendMakeup(Definition.makeup[layer-1],on,duration));
    }
    IEnumerator BlendMakeup(StoryEpisode.Makeup entry,bool on,float duration) {
        // FaceMatUpdater::.cctor resolves these exact two shader properties.
        var material=face.material;const string property="_BaseMapStackingLayer1TintColor";
        var from=material.GetColor(property);var target=entry.color;target.a=on?entry.color.a:0;
        if(on)material.SetTexture("_BaseMapStackingLayer1Tex",StoryContent.Load<Texture2D>(entry.resource));
        float elapsed=0;
        do {if(!Paused)elapsed+=Time.deltaTime;material.SetColor(property,Color.Lerp(from,target,duration<=0?1:elapsed/duration));yield return null;}while(elapsed<duration);
    }
    public void PlayPivot(StoryEpisode.Pivot data,float speed=1) {
        if(!pivotAnimation.GetClip(data.key)) {
            var clip=new AnimationClip {legacy=true,wrapMode=WrapMode.Once};
            foreach(var c in data.curves)clip.SetCurve(c.path,typeof(Transform),c.property,new AnimationCurve(c.keys.Select(k=>new Keyframe(k.time,k.value,k.inTangent,k.outTangent)).ToArray()));
            pivotAnimation.AddClip(clip,data.key);
        }
        // The original clip animates a zero-based pivot below the slot transform.
        // Use a separate child so its local curves cannot erase slot positioning.
        if(!pivotAnimation.enabled)pivotAnimation.enabled=true;
        pivotSpeed=speed;pivotAnimation[data.key].speed=speed;pivotAnimation.Play(data.key);
    }
    public void SetPivotHost(Transform slotParent) {
        transform.SetParent(slotParent,false);
    }
    public Transform Bone(string name) {
        // Old Mood rows use the DummyCenter attachment token; the referenced
        // preserved actor hierarchies name the corresponding bone Dummy_Center.
        if(name=="DummyCenter")name="Dummy_Center";
        return model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
    }
    void LateUpdate() {
        Animator.speed=Paused?0:1;
        foreach(AnimationState state in pivotAnimation)state.speed=Paused?0:pivotSpeed;
        foreach(var material in materials) {
            if(head){material.SetVector("_FaceForwardDirection",head.up);material.SetVector("_FaceUpDirection",-head.right);}
            material.SetColor("_PerCharacterBaseColorTint",new Color(Brightness,Brightness,Brightness,1));
        }
        foreach(var pair in weights)face.SetBlendShapeWeight(pair.Key,pair.Value);
        if(Paused)return;
        if(Time.time>blinkAt){blinkStart=Time.time;blinkAt=Time.time+UnityEngine.Random.Range(2,4);}
        int blink=face.sharedMesh.GetBlendShapeIndex("blink");
        if(blink>=0){weights.TryGetValue(blink,out var authored);face.SetBlendShapeWeight(blink,Mathf.Max(authored,Mathf.Max(0,1-Mathf.Abs((Time.time-blinkStart)/.1f-1))*100));}
        int mouth=face.sharedMesh.GetBlendShapeIndex("a_open");
        if(mouth>=0){float strength=0;if(Speaking&&Voice&&Voice.isPlaying){if(Voice.clip.loadState==AudioDataLoadState.Loaded)Voice.clip.GetData(samples,Mathf.Min(Voice.timeSamples,Voice.clip.samples-1));else Array.Clear(samples,0,samples.Length);strength=Mathf.Clamp(samples.Sum(x=>Mathf.Abs(x))/samples.Length*900,0,75);}face.SetBlendShapeWeight(mouth,strength);}
    }
}

#endif
