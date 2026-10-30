#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.Linq;
using Coffee.UIExtensions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.U2D;

// The visual tree, sprite slicing, layout groups, legacy Animation clips and
// particle modules belong to the preserved prefabs. This class only binds state
// and input in the standalone excerpt; it does not run the original game code.
public sealed partial class OriginalStoryUi : MonoBehaviour
{
    [Serializable] class ExpansionFonts {public string[] resources;}
    public GameObject Root { get; private set; }
    public GameObject Dialogue { get; private set; }
    public GameObject Choices { get; private set; }
    public TMP_Text Body { get; private set; }
    public bool ModalOpen => log.activeSelf || summary.activeSelf || nameDialog.IsOpen;
    public bool NameInputCaptured => nameDialog.CapturesInput;
    public string PlayerName => nameDialog.PlayerName;
    public bool Busy { get; private set; }
    public int AutoMode { get; private set; }
    public float PlaybackSpeed => AutoMode == 2 ? 2 : 1;
    public Action<int> Selected;
    public UnityAction Advance, Hide, SkipConfirmed, MovieSkipConfirmed;
    public UnityAction LogOpened;
    bool movieMode, movieSkipAllowed, scriptInteractive=true;
    Transform stage, hud, autoRoot;
    GameObject log, summary, restore, next;
    Animation talking;
    Button[] rows;
    bool revealing;
    bool voicePlaying;
    public bool VoiceIndicatorPlaying => voicePlaying;
    TMP_Text logBody;
    GameObject playerLog, followupLog;
    Coroutine modalRoutine;
    ScrollRect logScroll;
    Coroutine logScrollRoutine;
    UnityAction modalClosed;
    StoryPlayerNameDialog nameDialog;

    public void Initialize(Transform parent, AssetBundle bundle, StoryScene.Config config)
    {
        stage = parent;
        var holder = new GameObject("UI preparation"); holder.SetActive(false);
        Root = Instantiate(bundle.LoadAsset<GameObject>(config.uiAsset), holder.transform, false);
        log = Instantiate(bundle.LoadAsset<GameObject>(config.logAsset), holder.transform, false);
        summary = Instantiate(bundle.LoadAsset<GameObject>(config.summaryAsset), holder.transform, false);
        foreach (var go in new[] {Root, log, summary}) {
            // Source prefabs include invisible emitters with no material. The
            // newer UIParticle renderer otherwise indexes an empty material list
            // and leaves a default, solid-colour quad in the middle of a choice.
            foreach (var effect in go.GetComponentsInChildren<UIParticle>(true)) {
                effect.particles.RemoveAll(ps => !ps || !HasParticleMaterial(ps));
            }
            foreach (var animation in go.GetComponentsInChildren<Animation>(true)) { animation.playAutomatically = false; animation.Stop(); }
            foreach (var b in go.GetComponentsInChildren<Button>(true)) { b.onClick = new Button.ButtonClickedEvent(); b.navigation = new Navigation { mode = Navigation.Mode.None }; }
            foreach (var c in go.GetComponentsInChildren<Canvas>(true)) c.overrideSorting = false;
            var korean = StoryContent.Load<Font>("Original/MFont_ko");
            foreach (var text in go.GetComponentsInChildren<Text>(true)) if (!text.font || !text.font.name.Contains("Number")) text.font = korean;
            foreach (var graphic in go.GetComponentsInChildren<Graphic>(true)) {
                // Preserve original materials; only transparent collider graphics accept hits.
                if (graphic is UIParticle) graphic.raycastTarget = false;
            }
            go.SetActive(false); go.transform.SetParent(stage, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero; rt.localScale = Vector3.one;
        }
        Destroy(holder);
        Dialogue = Find("Center/Dialogue").gameObject;
        Choices = Find("Center/Select").gameObject;
        Body = Find("Center/Dialogue/Pivot/Dialogue/Pivot/Dialogue/Text_Dialogue_1").GetComponent<TMP_Text>();
        if (!Body) throw new InvalidOperationException("Original dialogue TMP component was not restored.");
        if(!Body.font) {
            var source=StoryContent.Load<Font>("Original/MFont_ko");
            if(!source)throw new InvalidOperationException("Missing preserved Korean font");
            var koreanSdf=TMP_FontAsset.CreateFontAsset(source);
            koreanSdf.name="Old Korean dialogue font";koreanSdf.isMultiAtlasTexturesEnabled=true;
            Body.font=koreanSdf;
        }
        var symbolFallback=StoryContent.Load<TMP_FontAsset>("Original/SymbolB1F8");
        if(Body.font.fallbackFontAssetTable==null)Body.font.fallbackFontAssetTable=new System.Collections.Generic.List<TMP_FontAsset>();
        if(symbolFallback&&!Body.font.fallbackFontAssetTable.Contains(symbolFallback))Body.font.fallbackFontAssetTable.Add(symbolFallback);
        var additionalFonts=StoryContent.Load<TextAsset>("story-expansion-fonts");
        if(additionalFonts)foreach(var key in BackendJson.FromJson<ExpansionFonts>(additionalFonts.text).resources){var extra=StoryContent.Load<TMP_FontAsset>(key);if(!extra)throw new InvalidOperationException("Missing story fallback font "+key);if(!Body.font.fallbackFontAssetTable.Contains(extra))Body.font.fallbackFontAssetTable.Add(extra);}
        // The Korean build still uses legacy Text for log paragraphs.
        // Bind those paragraphs to the same TMP text adapter as dialogue,
        // retaining the cloned prefab's RectTransform and authored font size.
        foreach(var legacy in log.GetComponentsInChildren<Text>(true).Where(t=>t.name=="Text_Desc").ToArray()) {
            var node=legacy.gameObject;var value=legacy.text;var size=legacy.fontSize;var color=legacy.color;
            UnityEngine.Object.DestroyImmediate(legacy);
            var replacement=node.AddComponent<TextMeshProUGUI>();replacement.font=Body.font;
            replacement.text=value;replacement.fontSize=size;replacement.color=color;
            replacement.enableWordWrapping=true;replacement.raycastTarget=false;
        }
        // The JP prefab keeps a separate Japanese SDF on portrait dialogue.
        // Reuse the already restored original Korean SDF for every dialogue/log
        // text, including initially inactive modes.
        foreach(var root in new[]{Root,log,summary})
            foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))
                if(!text.font || text.font.name.StartsWith("MFont",StringComparison.Ordinal))text.font=Body.font;
        hud = Find("Center/Top/Hud");
        autoRoot = hud.Find("Btn_Right/Btn_Auto");
        restore = Find("Btn_HideBack").gameObject;
        next = Find("Center/Dialogue/Pivot/Dialogue/Btn_Next").gameObject;
        talking = Find("Center/Dialogue/Pivot/Dialogue/Pivot/Name/Icon").GetComponent<Animation>();
        foreach (var path in new[] {"DimBase", "Bg_Vignette_White", "Bg_Vignette_Black", "Center/Img_Slot", "Center/CenterImage", "Center/DialogueCenter", "Center/Dialogue_Portrait", "Center/Title", "Center/Top/IntroduceParent", "ScreenFade_Background", "TalkAmbienceSoundRoot"}) Find(path).gameObject.SetActive(false);
        Find("Center/Dialogue/Pivot/Dialogue/Pivot/Name/Text_Name").GetComponent<Text>().text = "아이렌";
        Bind(Find("Center/Btn_Background"), () => Advance?.Invoke());
        Bind(restore.transform, () => Hide?.Invoke());
        Bind(hud.Find("Btn_Hide"), () => Hide?.Invoke());
        Bind(hud.Find("Btn_Log"), ToggleLog);
        Bind(hud.Find("Btn_Right/Btn_Auto"), CycleAuto);
        Bind(hud.Find("Btn_Right/Btn_Skip"), ToggleSummary);
        rows = new Button[config.choices.Length];
        var list = Find("Center/Select/Select");
        for (int i = 0; i < list.childCount; i++) {
            var row = list.GetChild(i); row.gameObject.SetActive(i < rows.Length);
            if (i >= rows.Length) continue;
            int choice = i;
            row.Find("Text").GetComponent<Text>().text = config.choices[i];
            // Only Select1 contains the optional automatic-choice countdown.
            // Select2/Select3 intentionally omit it in the original prefab.
            var countdown = row.Find("Time");
            if (countdown) countdown.gameObject.SetActive(false);
            row.Find("Select").gameObject.SetActive(false);
            rows[i] = Bind(row, () => Select(choice));
        }
        PrepareLog(config);
        PrepareSummary(config);
        nameDialog=gameObject.AddComponent<StoryPlayerNameDialog>();
        nameDialog.Initialize(stage,log.transform.Find("Window/Contents"));
        nameDialog.Changed+=SetPlayerLogName;
        SetPlayerLogName(nameDialog.PlayerName);
        Dialogue.SetActive(false); Choices.SetActive(false); restore.SetActive(false);
        SetAuto(0); Root.SetActive(true);
        Debug.Log("STORY_ORIGINAL_UI_READY images=" + Root.GetComponentsInChildren<MImage>(true).Length);
    }

    Transform Find(string path) => Root.transform.Find(path) ?? throw new InvalidOperationException("Missing original UI path: " + path);
    static bool HasParticleMaterial(ParticleSystem ps) {
        var renderer=ps.GetComponent<ParticleSystemRenderer>();
        if(renderer && renderer.sharedMaterials.Length>0 && renderer.sharedMaterials[0]) return true;
        if(renderer) renderer.enabled=false;
        return false;
    }
    static Button Bind(Transform tr, UnityAction action) {
        var b = tr.GetComponent<Button>();
        if (!b) throw new InvalidOperationException("Missing original button: " + tr.name);
        b.onClick.AddListener(action); return b;
    }
    static void TextAt(GameObject root, string path, string value) => root.transform.Find(path).GetComponent<Text>().text = value;

    public void ResetView()
    {
        ResetCenterImage();
        StopGlyphWave();
        scriptInteractive=true;RefreshHud();
        StopLogVoice();
        HideIntroduce();
        ResetEndFade();
        StopAllCoroutines(); Busy = false; modalRoutine = null; modalClosed = null;
        ResetSceneFade();
        nameDialog.Close();log.SetActive(false); summary.SetActive(false); playerLog.SetActive(false); followupLog.SetActive(false);
          SetHidden(false); Choices.SetActive(false); HideAllDialogue();
        foreach(var row in rows) { row.interactable = true; row.GetComponent<Animation>().Stop(); row.transform.Find("Select").gameObject.SetActive(false); }
        SetRevealing(false);
        SetVoicePlaying(false);
        Find("Bg_Vignette_White").gameObject.SetActive(false);Find("Bg_Vignette_Black").gameObject.SetActive(false);
    }
    public void ShowDialogue(string speaker = "아이렌")
    {
        HideCenteredDialogue();Choices.SetActive(false);
        Find("Center/Dialogue/Pivot/Dialogue/Pivot/Name/Text_Name").GetComponent<Text>().text = speaker;
        Dialogue.SetActive(true);
        // The root's default clip is Dialogue_Shake_01, not an entrance clip.
        // Only explicit script commands should trigger that shake.
        Find("Center/Dialogue/Pivot/Dialogue/Pivot/Name").GetComponent<Animation>().Play("Dialogue_Name_In");
        Find("Center/Dialogue/Pivot/Dialogue/Pivot/Dialogue").GetComponent<Animation>().Play("Dialogue_In");
    }
    public void AddFollowupToLog() => followupLog.SetActive(true);
    public void ShowChoices()
    {
        Dialogue.SetActive(false); SetRevealing(false);
        foreach(var row in rows) { row.interactable = true; row.transform.Find("Select").gameObject.SetActive(false); }
        Choices.SetActive(true); Choices.GetComponent<Animation>().Play("Dialogue_Choice");
    }
    public void Select(int index)
    {
        if (Busy || ModalOpen || !Choices.activeInHierarchy || index < 0 || index >= rows.Length) return;
        StartCoroutine(Selection(index));
    }
    IEnumerator Selection(int index)
    {
        Busy = true;
        foreach (var row in rows) row.interactable = false;
        var animation = rows[index].GetComponent<Animation>();
        animation.Play("TalkScene_Select");
        Debug.Log("STORY_SELECTION_EFFECT " + index + " duration=" + animation["TalkScene_Select"].length);
        yield return new WaitForSeconds(animation["TalkScene_Select"].length);
        Busy = false;
        if (episodeLog) AppendLog(PlayerName, rows[index].transform.Find("Text").GetComponent<Text>().text, "", true);
        else {
            playerLog.SetActive(true);
            playerLog.transform.Find("Player/Story/Desc/Text_Desc").GetComponent<TMP_Text>().text = rows[index].transform.Find("Text").GetComponent<Text>().text;
        }
        // Close this selection after its original effect, before the callback
        // starts a movie or synchronously opens another set of choices.
        Choices.SetActive(false);
        Selected?.Invoke(index);
    }
    public void SetRevealing(bool value)
    {
        if (revealing == value) return;
        revealing = value;
        next.SetActive(!value);
        if (!value && next.activeInHierarchy) next.GetComponent<Animation>().Play();
    }
    public void SetVoicePlaying(bool value) {
        if(voicePlaying==value)return;
        voicePlaying=value;
        if(value)talking.Play();else talking.Stop();
        Debug.Log("STORY_INDICATOR_" + (value ? "START" : "STOP") + " time=" + Time.time.ToString("F3"));
    }
    public void SetHidden(bool value)
    {
        Find("Center").gameObject.SetActive(!value); restore.SetActive(value);
        Debug.Log("STORY_UI_HIDDEN " + value);
    }
    public void CycleAuto() { if (!movieMode && scriptInteractive && !ModalOpen && !Busy) SetAuto((AutoMode + 1) % 3); }
    void SetAuto(int value)
    {
        AutoMode = value;
        autoRoot.Find("Auto_Off").gameObject.SetActive(value == 0);
        autoRoot.Find("Auto_On_Speed1").gameObject.SetActive(value == 1);
        autoRoot.Find("Auto_On_Speed2").gameObject.SetActive(value == 2);
        var a = autoRoot.GetComponentsInChildren<Animation>().FirstOrDefault();
        if (a) { foreach(AnimationState s in a) s.speed = PlaybackSpeed; a.Play(); }
        Debug.Log("STORY_AUTO " + value);
    }

    void PrepareLog(StoryScene.Config config)
    {
        TextAt(log, "Window/Contents/Text_Title", "로그");
        Bind(log.transform.Find("Window/Contents/Btn_Close"), ToggleLog);
        Bind(log.transform.Find("Common_PopupBackground"), ToggleLog);
        var content = log.transform.Find("Window/Contents/Log/Scroll View/Viewport/Content");
        var scrollView = content.parent.parent;
        logScroll = scrollView.GetComponent<ScrollRect>();
        if(!logScroll) logScroll = scrollView.gameObject.AddComponent<ScrollRect>();
        logScroll.content = (RectTransform)content;
        logScroll.viewport = (RectTransform)content.parent;
        logScroll.horizontal = false;
        logScroll.vertical = true;
        var row = content.GetChild(0);
        row.Find("Char/Story/Title/Layout/Text_Title").GetComponent<Text>().text = "아이렌";
        row.Find("Char/Story/Title/Layout/Text_Part").GetComponent<Text>().text = "";
        SetLogPortrait(row,"character_airen_01");
        logBody = row.Find("Char/Story/Desc/Text_Desc").GetComponent<TMP_Text>(); logBody.text = StoryTextDisplay.Format(config.firstLine);
        // The original loop places the row; here the excerpt has one actor line
        // and, after selection, one player line.
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true; layout.childControlHeight = false; layout.childForceExpandHeight = false; layout.spacing = 18;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        playerLog = Instantiate(row.gameObject, content, false);
        playerLog.transform.Find("Char").gameObject.SetActive(false);
        playerLog.transform.Find("Player").gameObject.SetActive(true);
        playerLog.transform.Find("Player/Story/Title/Text_Title").GetComponent<Text>().supportRichText=false;
        playerLog.SetActive(false);
        followupLog = Instantiate(row.gameObject, content, false);
        followupLog.transform.Find("Char/Story/Title/Layout/Text_Title").GetComponent<Text>().text = config.followupSpeaker;
        followupLog.transform.Find("Char/Story/Desc/Text_Desc").GetComponent<TMP_Text>().text = StoryTextDisplay.Format(config.followupLine);
        SetLogPortrait(followupLog.transform,"character_mana_01");
        followupLog.SetActive(false);
    }
    static void SetLogPortrait(Transform row,string spriteName)
    {
        var sprite=OriginalPortrait(spriteName);
        var image=row.Find("Char/Img/Mask/Img").GetComponent<Image>();image.overrideSprite=null;image.sprite=sprite;image.enabled=sprite;
    }
    public static Sprite OriginalPortrait(string spriteName) {
        var atlas=AssetBundle.GetAllLoadedAssetBundles()
            .SelectMany(b=>b.GetAllAssetNames().Where(n=>n.EndsWith("/icon_portrait.spriteatlas",StringComparison.OrdinalIgnoreCase)).Select(n=>b.LoadAsset<SpriteAtlas>(n))).FirstOrDefault();
        var sprite=atlas?atlas.GetSprite(spriteName):null;
        // Exact old table reference absent from its preserved 611-sprite atlas.
        // Keep the original frame/name and omit this image; never guess a face.
        if(!sprite&&(spriteName=="character_pharah_01"||spriteName=="character_shiyuan_01")){Debug.LogWarning("STORY_SOURCE_PORTRAIT_GAP "+spriteName);return null;}
        if(!sprite)throw new InvalidOperationException("Original log portrait missing: "+spriteName);
        return sprite;
    }
    void SetPlayerLogName(string name) {
        playerLog.transform.Find("Player/Story/Title/Text_Title").GetComponent<Text>().text=name;
        foreach(var row in episodePlayerRows) if(row) row.text=name;
    }
    public void OpenNameSettings(){if(!movieMode && scriptInteractive && !Busy && !summary.activeSelf && !nameDialog.IsOpen)nameDialog.Open();}
    void PrepareSummary(StoryScene.Config config)
    {
        TextAt(summary, "Window/Contents/Text_Title", "줄거리");
        TextAt(summary, "Window/Contents/Text_StoryTitle", config.storyTitle);
        TextAt(summary, "Window/Contents/Summary/Scroll View/Viewport/Content/Text_Summary", config.storySummary);
        TextAt(summary, "Window/Contents/Skip/Layout/Text_Skip", "이 스토리를 스킵하시겠습니까?");
        foreach(var name in new[] {"Btn_Cancel", "Btn_Ok"}) {
            var b = summary.transform.Find("Window/Button/" + name);
            b.Find("Normal").gameObject.SetActive(true); b.Find("Disable").gameObject.SetActive(false);
            b.Find("Normal/Text").GetComponent<Text>().text = name == "Btn_Ok" ? "확인" : "취소";
        }
        Bind(summary.transform.Find("Window/Contents/Btn_Close"), ToggleSummary);
        Bind(summary.transform.Find("Common_PopupBackground"), ToggleSummary);
        Bind(summary.transform.Find("Window/Button/Btn_Cancel"), ToggleSummary);
        Bind(summary.transform.Find("Window/Button/Btn_Ok"), () => {
            var callback=movieMode?MovieSkipConfirmed:SkipConfirmed;
            ClosePopup(summary, () => callback?.Invoke());
        });
    }
    public void SetEpisodeSummary(string title,string text) {
        TextAt(summary,"Window/Contents/Text_StoryTitle",title);
        TextAt(summary,"Window/Contents/Summary/Scroll View/Viewport/Content/Text_Summary",StoryTextDisplay.Format(text));
    }
    public void SetMovieMode(bool enabled,bool allowSkip=false) {
        movieMode=enabled;movieSkipAllowed=enabled&&allowSkip;
        // A fullscreen movie owns input while a held scene fade remains below it.
        if(enabled)Busy=false;
        RefreshHud();
    }
    public void SetScriptInteraction(bool enabled){scriptInteractive=enabled;RefreshHud();}
    void RefreshHud(){hud.gameObject.SetActive(!movieMode&&scriptInteractive);}
    public void ToggleLog() { if(movieMode || (!scriptInteractive&&!log.activeSelf) || Busy || summary.activeSelf || nameDialog.IsOpen) return; if(log.activeSelf) ClosePopup(log); else OpenPopup(log); }
    public void ToggleSummary() { if((movieMode?!movieSkipAllowed:!scriptInteractive) || Busy || log.activeSelf || nameDialog.IsOpen) return; if(summary.activeSelf) ClosePopup(summary); else OpenPopup(summary); }
    public void Escape() { if(log.activeSelf) ToggleLog(); else ToggleSummary(); }
    void OpenPopup(GameObject popup) {
        if(popup==log) {StopLogVoice();LogOpened?.Invoke();}
        popup.transform.SetAsLastSibling(); popup.SetActive(true); popup.GetComponent<Animation>().Play("Popup_In");
        if(popup == log) ScrollLogToLatest();
        Debug.Log("STORY_POPUP_OPEN " + popup.name);
    }
    void ScrollLogToLatest() {
        if(!log.activeInHierarchy || !logScroll) return;
        if(logScrollRoutine != null) StopCoroutine(logScrollRoutine);
        SnapLogToBottom();
        logScrollRoutine = StartCoroutine(ScrollLogAfterLayout());
    }
    void SnapLogToBottom() {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(logScroll.content);
        Canvas.ForceUpdateCanvases();
        logScroll.StopMovement();
        logScroll.verticalNormalizedPosition = 0f;
    }
    IEnumerator ScrollLogAfterLayout() {
        // Newly enabled rows and TMP text can resize in the next layout pass.
        yield return null;
        if(log.activeInHierarchy) SnapLogToBottom();
        logScrollRoutine = null;
    }
    void ClosePopup(GameObject popup, UnityAction callback = null) {
        if(modalRoutine != null) return;
        if(popup==log)StopLogVoice();
        modalClosed = callback; modalRoutine = StartCoroutine(CloseAnimation(popup));
    }
    IEnumerator CloseAnimation(GameObject popup) {
        var a = popup.GetComponent<Animation>(); a.Play("Popup_Out");
        yield return new WaitForSeconds(a["Popup_Out"].length);
        popup.SetActive(false); modalRoutine = null;
        var callback = modalClosed; modalClosed = null; callback?.Invoke();
        Debug.Log("STORY_POPUP_CLOSE " + popup.name);
    }
}

#endif
