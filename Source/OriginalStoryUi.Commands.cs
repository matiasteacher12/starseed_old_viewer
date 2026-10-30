#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D;

public sealed partial class OriginalStoryUi {
    readonly System.Collections.Generic.Dictionary<Transform,Vector3> dialoguePivotPositions=new System.Collections.Generic.Dictionary<Transform,Vector3>();
    Transform ActiveDialogueRoot => Find("Center/DialogueCenter").gameObject.activeSelf ? Find("Center/DialogueCenter") : Find("Center/Dialogue_Portrait").gameObject.activeSelf ? Find("Center/Dialogue_Portrait") : Dialogue.transform;
    public void HideAllDialogue() {
        Dialogue.SetActive(false);HideCenteredDialogue();Find("Center/Dialogue_Portrait").gameObject.SetActive(false);
    }
    public TMP_Text PrepareDialogue(string speaker,string portrait,int mode,float fontSize,bool keep) {
        talking.Stop();
        var indicators=Find(mode==2?"Center/Dialogue_Portrait/Pivot/Dialogue":"Center/Dialogue/Pivot/Dialogue");
        next=indicators.Find("Btn_Next").gameObject;
        talking=indicators.Find("Pivot/Name/Icon").GetComponent<Animation>();
        next.SetActive(!revealing);if(voicePlaying)talking.Play();
        if(mode==1) {
            HideAllDialogue();var text=ShowCenteredDialogue();
            if(fontSize>0)text.fontSize=fontSize;
            if(!keep)ResetTextAnimation();return text;
        }
        if(mode==2) {
            HideAllDialogue();Choices.SetActive(false);
            var root=Find("Center/Dialogue_Portrait");root.gameObject.SetActive(true);
            root.Find("Pivot/Dialogue/Pivot/Name/Layout/Text_Name").GetComponent<Text>().text=speaker;
            root.Find("Pivot/Dialogue/Pivot/Name/Layout/Text_Part").GetComponent<Text>().text="";
            var image=root.Find("Pivot/Dialogue/Pivot_Portrait/Portrait/Mask/Img_Character").GetComponent<Image>();
            image.overrideSprite=null;image.sprite=OriginalPortrait(portrait);
            var text=root.Find("Pivot/Dialogue/Pivot/Dialogue/Text_Dialogue_1").GetComponent<TMP_Text>();
            if(fontSize>0)text.fontSize=fontSize;
            root.Find("Pivot/Dialogue/Pivot/Name").GetComponent<Animation>().Play("Dialogue_Name_In");
            root.Find("Pivot/Dialogue/Pivot/Dialogue").GetComponent<Animation>().Play("Dialogue_In");
            ResetTextAnimation();return text;
        }
        HideAllDialogue();ShowDialogue(speaker);ResetTextAnimation();
        if(fontSize>0)Body.fontSize=fontSize;
        return Body;
    }
    void ResetTextAnimation() {
        StopGlyphWave();
        var root=ActiveDialogueRoot;var animation=root.GetComponent<Animation>();if(animation)animation.Stop();
        foreach(var group in root.GetComponentsInChildren<CanvasGroup>(true))group.alpha=1;
        var pivot=root.Find("Pivot")??root;
        if(!dialoguePivotPositions.ContainsKey(pivot))dialoguePivotPositions[pivot]=pivot.localPosition;
        pivot.localPosition=dialoguePivotPositions[pivot];
    }
    public float PlayTextEffect(string name) {
        var animation=ActiveDialogueRoot.GetComponent<Animation>();
        if(string.IsNullOrEmpty(name)){animation.Stop();return 0;}
        var state=animation[name];
        // Old normal-dialogue scripts request Shake_02, while the preserved UI
        // attaches that exact clip only to the portrait dialogue. Its two curves
        // target Pivot/Dialogue/Pivot, shared by both. Bind the unchanged source
        // clip here as viewer compatibility, without inventing a shake curve.
        if(state==null && name=="Dialogue_Shake_02" && ActiveDialogueRoot.Find("Pivot/Dialogue/Pivot")) {
            var clip=Root.GetComponentsInChildren<Animation>(true).SelectMany(a=>a.Cast<AnimationState>()).FirstOrDefault(s=>s.name==name)?.clip;
            if(clip){animation.AddClip(clip,name);state=animation[name];Debug.Log("STORY_DIALOGUE_CLIP_COMPAT "+name);}
        }
        if(state==null)throw new InvalidOperationException("Missing original dialogue clip "+name);
        animation.Play(name);Debug.Log("STORY_TEXT_EFFECT "+name+" duration="+state.length);return state.length;
    }
}

#endif
