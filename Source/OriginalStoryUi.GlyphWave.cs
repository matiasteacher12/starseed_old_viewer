using System;
using TMPro;
using UnityEngine;

public sealed partial class OriginalStoryUi {
    StoryGlyphWave glyphWave;
    public void PlayGlyphWave(TMP_Text target,string mode,string values) {
        if(!((mode=="Wave"&&values=="5&3")||(mode=="Shake"&&values=="2")))throw new InvalidOperationException("Unreviewed glyph animation "+mode+" "+values);
        StopGlyphWave();
        glyphWave=target.GetComponent<StoryGlyphWave>()??target.gameObject.AddComponent<StoryGlyphWave>();
        glyphWave.Begin(mode=="Shake");
        Debug.Log("STORY_GLYPH_ANIMATION_BEGIN "+mode+" "+values);
    }
    void StopGlyphWave(){if(glyphWave){glyphWave.Stop();glyphWave=null;}}
}

// Compatibility port from the newer reference, used for the exact Shake 2 and
// Wave 5&3 commands found in old tables. Old native implementation is unmapped.
public sealed class StoryGlyphWave : MonoBehaviour {
    TMP_Text text;float elapsed;bool running,shake;
    public float MaximumOffset {get;private set;}
    public void Begin(bool isShake=false){shake=isShake;text=GetComponent<TMP_Text>();elapsed=0;running=true;enabled=true;}
    public void Stop(){running=false;if(text)text.ForceMeshUpdate();enabled=false;}
    void LateUpdate() {
        if(!running||!text||!text.gameObject.activeInHierarchy)return;
        elapsed+=Time.deltaTime;if(elapsed>Mathf.PI*2)elapsed-=Mathf.PI*2;
        text.ForceMeshUpdate();var info=text.textInfo;float phase=0;MaximumOffset=0;
        for(int i=0;i<info.characterCount;i++) {
            var c=info.characterInfo[i];if(!c.isVisible)continue;
            var offset=shake?UnityEngine.Random.insideUnitSphere*2:Vector3.up*Mathf.Sin((elapsed+phase)*5)*3;phase+=.1f;
            MaximumOffset=Mathf.Max(MaximumOffset,offset.magnitude);
            var vertices=info.meshInfo[c.materialReferenceIndex].vertices;
            for(int v=0;v<4;v++)vertices[c.vertexIndex+v]+=offset;
        }
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }
}
