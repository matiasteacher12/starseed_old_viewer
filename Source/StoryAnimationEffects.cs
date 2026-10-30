#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Receives the original clip events on the Animator's own GameObject.
// Only effects whose dependencies/materials have been audited are enabled.
public sealed class StoryAnimationEffects : MonoBehaviour {
    [Serializable] public class Manifest {public ActorEffects[] actors;}
    [Serializable] public class ActorEffects {public string actor;public Entry[] effects;public string[] deferredVoices,subAnimations;}
    [Serializable] public class Entry {public string key,asset;}
    [Serializable] public class EventData {public EffectData[] Params;}
    [Serializable] public class EffectData {
        public string Path,Parent;
        public Vector3 Position,Rotation,Scale;
        public bool FollowParent,UseHitLayer;
        public float Duration;
    }
    public StoryEpisodeActor Owner;
    public Func<string,GameObject> Load;
    public Action<GameObject> Configure;
    public Action<GameObject,EffectData> Spawned;
    public ActorEffects Data;
    readonly List<GameObject> effects=new List<GameObject>();
    readonly Dictionary<GameObject,float> remaining=new Dictionary<GameObject,float>();
    readonly HashSet<string> warned=new HashSet<string>();
    bool paused;
    public int SpawnCount {get;private set;}
    public IEnumerable<GameObject> ActiveEffects=>effects.Where(e=>e);

    public void PlaySelfEffect(string text) {
        var data=BackendJson.FromJson<EventData>(text);
        foreach(var item in data.Params) {
            var entry=Data.effects.FirstOrDefault(e=>e.key==item.Path);
            if(entry==null){if(warned.Add(item.Path))Debug.LogWarning("STORY_ANIM_EFFECT_UNREVIEWED "+item.Path);continue;}
            var bone=Owner.Bone(item.Parent);
            if(!bone)throw new InvalidOperationException("Missing animation effect parent "+item.Parent);
            var effect=Instantiate(Load(entry.asset),bone,false);
            effect.transform.localPosition=item.Position;
            effect.transform.localRotation=Quaternion.Euler(item.Rotation);
            effect.transform.localScale=item.Scale;
            if(!item.FollowParent)effect.transform.SetParent(null,true);
            foreach(var tr in effect.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=17;
            Configure(effect);
            // Old events all have UseHitLayer=false. Preserve the authored material depth test; old native setter is not verified.
            foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>())ps.Play(false);
            effects.Add(effect);SpawnCount++;
            // Compatibility interpretation of the old event's explicit duration.
            // Old native method mapping is unavailable; see Expansion091 audit.
            if(item.Duration>0)remaining[effect]=item.Duration;
            Spawned?.Invoke(effect,item);
            Debug.Log("STORY_ANIM_EFFECT actor="+Owner.Definition.key+" path="+item.Path+" bone="+item.Parent+" follow="+item.FollowParent+" hitLayer="+item.UseHitLayer+" clipEvent=true");
        }
    }
    // IgnoreAnimSound covers the audited PlaySound path. PlayVoice has separate
    // native context guards; extra lobby voice playback remains unverified.
    public void PlaySound(string cue) { }
    public void PlayAnimator(string value) {
        if(Data==null||!(Data.subAnimations??Array.Empty<string>()).Contains(value))throw new InvalidOperationException("Unreviewed sub-animation "+value);
        var parts=value.Split(',');var targets=Owner.GetComponentsInChildren<Animator>(true).Where(a=>a.name==parts[0]).ToArray();
        if(targets.Length!=1||!targets[0].HasState(0,Animator.StringToHash(parts[1])))throw new InvalidOperationException("Missing authored sub-animation "+value);
        targets[0].Play(parts[1]);Debug.Log("STORY_SUB_ANIMATOR "+Owner.Definition.key+" "+value);
    }
    public void PlayVoice(string cue) {if(warned.Add("voice:"+cue))Debug.LogWarning("STORY_ANIM_VOICE_DEFERRED "+Owner.Definition.key+" "+cue);}
    public void ClearEffects(){foreach(var effect in effects)if(effect)Destroy(effect);effects.Clear();remaining.Clear();paused=false;}
    void OnDisable(){ClearEffects();}
    void LateUpdate() {
        if(Owner.Paused!=paused) {
            paused=Owner.Paused;
            foreach(var effect in effects)if(effect)foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>()) {
                if(paused&&ps.isPlaying)ps.Pause(false);else if(!paused&&ps.isPaused)ps.Play(false);
            }
        }
        for(int i=effects.Count-1;i>=0;i--) {
            var effect=effects[i];
            if(!effect){remaining.Remove(effect);effects.RemoveAt(i);continue;}
            if(paused)continue;
            bool timed=remaining.TryGetValue(effect,out var duration);
            if(timed)remaining[effect]=duration-Time.deltaTime;
            if((timed&&remaining[effect]<=0)||(!timed&&!effect.GetComponentsInChildren<ParticleSystem>().Any(ps=>ps.IsAlive(true)))){remaining.Remove(effect);Destroy(effect);effects.RemoveAt(i);}
        }
    }
}

#endif
