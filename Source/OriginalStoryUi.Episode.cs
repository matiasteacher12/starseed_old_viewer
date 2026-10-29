using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class OriginalStoryUi
{
    public TMP_Text ShowCenteredDialogue() {
        Dialogue.SetActive(false);Choices.SetActive(false);SetVoicePlaying(false);
        var root=Find("Center/DialogueCenter");root.gameObject.SetActive(true);
        var effects=root.Find("Pivot/Dialogue/Pivot_Fx");if(effects)effects.gameObject.SetActive(false);
        var node=root.Find("Pivot/Dialogue/Pivot/Text_Center")??root.Find("Text_Center_1");
        var text=node.GetComponent<TMP_Text>();
        text.richText=false; // <지난 이야기> is visible text, not a TMP tag.
        text.fontSize=24; // StartTalk_Center default in Root_TalkScene.
        return text;
    }
    public void HideCenteredDialogue(){Find("Center/DialogueCenter").gameObject.SetActive(false);}
    public void SetPastScene(bool enabled,int index) {
        var root=Find(index==1?"Bg_Vignette_Black":"Bg_Vignette_White");
        root.gameObject.SetActive(enabled);
        var animation=root.GetComponent<Animation>();
        if(enabled && animation)animation.Play("Vignette_Fade_In");
    }
    bool episodeLog;
    readonly List<GameObject> episodeRows = new List<GameObject>();
    readonly List<Text> episodePlayerRows = new List<Text>();
    public int EpisodeLogCount => episodeRows.Count;
    Coroutine introduceRoutine;
    public float EndFadeOpacity => Find("ScreenFade_Background/Background").GetComponent<Image>().canvasRenderer.GetAlpha();
    public IEnumerator PlayEndFade() {
        Busy=true;HideIntroduce();
        Find("Center/Top/Hud").gameObject.SetActive(false);
        var parent=Find("ScreenFade_Background");
        foreach(Transform child in parent)child.gameObject.SetActive(child.name=="Background");
        parent.gameObject.SetActive(true);
        var background=parent.Find("Background").GetComponent<Image>();
        background.color=Color.black;
        // Root_TalkScene.EndScene calls PlayFadeOutAsync(1.5), whose
        // implementation uses Graphic.CrossFadeAlpha, then waits 1500 ms.
        background.CrossFadeAlpha(0,0,true);
        background.CrossFadeAlpha(1,1.5f,false);
        Debug.Log("STORY_END_FADE_BEGIN duration=1.5");
        yield return new WaitForSeconds(1.5f);
        background.canvasRenderer.SetAlpha(1);
        Debug.Log("STORY_END_FADE_END");
    }
    void ResetEndFade() {
        Find("Center/Top/Hud").gameObject.SetActive(true);
        var parent=Find("ScreenFade_Background");
        parent.Find("Background").GetComponent<Image>().CrossFadeAlpha(0,0,true);
        parent.gameObject.SetActive(false);
    }

    public IEnumerator PlayEpisodeTitle(string title,int number) {
        var root=Find("Center/Title");
        root.Find("Title/Text_Title").GetComponent<Text>().text=title;
        root.Find("Title/Line/Text_Number").GetComponent<Text>().text="EPISODE "+number.ToString("00");
        root.gameObject.SetActive(true);
        var animation=root.GetComponent<Animation>();
        var state=animation["Dialogue_Title_In"];
        if(state==null)throw new System.InvalidOperationException("Missing original title animation");
        animation.Play(state.name);
        Debug.Log("EPISODE_TITLE "+title+" number="+number+" clip="+state.name+" duration="+state.length);
        yield return new WaitForSeconds(state.length);
        root.gameObject.SetActive(false);
    }
    public void ResetEpisodeTitle() {
        var title=Find("Center/Title");title.GetComponent<Animation>().Stop();title.gameObject.SetActive(false);
    }

    public void BeginEpisodeLog() {
        StopLogVoice();
        episodeLog=true;
        foreach(var row in episodeRows) {row.SetActive(false);Destroy(row);}
        episodeRows.Clear();episodePlayerRows.Clear();
        logBody.transform.parent.parent.parent.parent.gameObject.SetActive(false);
        playerLog.SetActive(false);followupLog.SetActive(false);
    }
    public void AppendLog(string speaker,string text,string portrait,bool player=false,AudioClip voiceClip=null) {
        text=StoryTextDisplay.Format(text);
        var template=followupLog;
        var row=Instantiate(template,template.transform.parent,false);
        row.name="Story log "+episodeRows.Count;
        row.transform.Find("Char").gameObject.SetActive(!player);
        row.transform.Find("Player").gameObject.SetActive(player);
        if(player) {
            var label=row.transform.Find("Player/Story/Title/Text_Title").GetComponent<Text>();
            label.supportRichText=false;label.text=PlayerName;episodePlayerRows.Add(label);
            row.transform.Find("Player/Story/Desc/Text_Desc").GetComponent<TMP_Text>().text=text;
        } else {
            row.transform.Find("Char/Story/Title/Layout/Text_Title").GetComponent<Text>().text=speaker;
            row.transform.Find("Char/Story/Desc/Text_Desc").GetComponent<TMP_Text>().text=text;
            if(string.IsNullOrEmpty(portrait)) {
                row.transform.Find("Char/Img").gameObject.SetActive(false);
                row.transform.Find("Char/Story/Title").gameObject.SetActive(false);
                row.transform.Find("Char/Story/Desc/Text_Desc").GetComponent<TMP_Text>().richText=false;
            } else SetLogPortrait(row.transform,portrait);
            if(voiceClip)AddLogVoiceButton(row.transform,voiceClip);
        }
        row.SetActive(true);episodeRows.Add(row);
        ScrollLogToLatest();
    }
    public void ConfigureChoices(string[] labels) {
        var list=Find("Center/Select/Select");
        rows=new Button[labels.Length];
        for(int i=0;i<list.childCount;i++) {
            var row=list.GetChild(i);row.gameObject.SetActive(i<labels.Length);
            if(i>=labels.Length)continue;
            int index=i;row.Find("Text").GetComponent<Text>().text=StoryTextDisplay.PlayerText(StoryTextDisplay.Format(labels[i]),PlayerName);
            var button=row.GetComponent<Button>();button.onClick.RemoveAllListeners();button.onClick.AddListener(()=>Select(index));rows[i]=button;
        }
    }
    public void ShowIntroduce(string text) {
        HideIntroduce();
        introduceRoutine=StartCoroutine(PlayIntroduce(text));
    }
    IEnumerator PlayIntroduce(string text) {
        var parent=Find("Center/Top/IntroduceParent");
        parent.Find("Introduce/Text_Introduce").GetComponent<Text>().text=text;
        var animation=parent.GetComponent<Animation>();
        var state=animation["Dialogue_Introduce_In"];
        if(state==null)throw new System.InvalidOperationException("Missing original location introduction animation");
        parent.gameObject.SetActive(true);state.time=0;animation.Play(state.name);
        Debug.Log("EPISODE_INTRO_BEGIN "+text+" duration="+state.length);
        // This clip contains its entrance, readable hold and exit. Keep that
        // authored lifetime independently of the removed scene click gate.
        yield return new WaitForSeconds(state.length);
        introduceRoutine=null;HideIntroduce();
        Debug.Log("EPISODE_INTRO_END "+text);
    }
    public void HideIntroduce() {
        if(introduceRoutine!=null){StopCoroutine(introduceRoutine);introduceRoutine=null;}
        var parent=Find("Center/Top/IntroduceParent");
        parent.GetComponent<Animation>().Stop();parent.gameObject.SetActive(false);
    }
}
