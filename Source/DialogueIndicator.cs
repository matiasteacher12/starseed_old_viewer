#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Linq;
using UnityEngine;

// The four animated bars use the original Dialogue_Voice keyframes.
// The three trailing dots stay still. Playback follows text reveal, not audio.
public sealed class DialogueIndicator : MonoBehaviour
{
    [Serializable] class Data {public float duration;public Curve[] curves;}
    [Serializable] class Curve {public int index;public Key[] keys;}
    [Serializable] class Key {public float time,value,inTangent,outTangent;}
    RectTransform[] bars;
    AnimationCurve[] curves;
    float elapsed,duration;
    bool revealing,changed;
    readonly float[] rest={16,10,6,12,2,2,2};

    public void Initialize(RectTransform[] targets) {
        bars=targets;curves=new AnimationCurve[bars.Length];
        var data=BackendJson.FromJson<Data>(StoryContent.Load<TextAsset>("dialogue-indicator").text);duration=data.duration;
        foreach(var c in data.curves)curves[c.index]=new AnimationCurve(c.keys.Select(k=>new Keyframe(k.time,k.value,k.inTangent,k.outTangent)).ToArray());
        ResetBars();
    }
    public void SetRevealing(bool value) {
        if(value==revealing)return;
        revealing=value;
        if(value){elapsed=0;changed=false;Debug.Log("STORY_INDICATOR_START");}
        else {ResetBars();Debug.Log("STORY_INDICATOR_STOP animated="+changed);}
    }
    void Update() {
        if(!revealing)return;
        elapsed+=Time.deltaTime;
        for(int i=0;i<bars.Length;i++)if(curves[i]!=null){
            float height=curves[i].Evaluate(elapsed%duration);
            if(Mathf.Abs(height-rest[i])>.1f)changed=true;
            SetHeight(i,height);
        }
    }
    void SetHeight(int i,float height){bars[i].sizeDelta=new Vector2(2,height);bars[i].anchoredPosition=new Vector2(102+i*5,-575+height/2);}
    void ResetBars(){if(bars!=null)for(int i=0;i<bars.Length;i++)SetHeight(i,rest[i]);}
    void OnDisable(){SetRevealing(false);}
}

#endif
