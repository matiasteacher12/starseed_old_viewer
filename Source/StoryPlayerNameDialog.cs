#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Local preview preference. The game's story and character names remain unchanged.
public sealed class StoryPlayerNameDialog : MonoBehaviour
{
    public const string PreferenceKey="Starseed.Story.PlayerName";
    public const string DefaultName="아키텍트";
    public string PlayerName { get; private set; }
    public bool IsOpen => panel && panel.activeSelf;
    public bool CapturesInput => IsOpen || closedFrame==Time.frameCount;
    public event Action<string> Changed;
    GameObject panel;
    InputField input;
    Text error;
    Font font;
    int closedFrame=-1;

    public void Initialize(Transform stage,Transform logWindow)
    {
        PlayerName=Normalize(PlayerPrefs.GetString(PreferenceKey,DefaultName));
        if(PlayerName.Length==0)PlayerName=DefaultName;
        font=StoryContent.Load<Font>("Original/MFont_ko");
        var edit=ButtonAt(logWindow,"플레이어 이름 · F2",new Vector2(130,-38),new Vector2(205,38),Open);
        edit.anchorMin=edit.anchorMax=new Vector2(0,1);
        edit.GetComponent<Image>().color=new Color(.88f,.91f,.93f,1);
        panel=new GameObject("Player name settings",typeof(RectTransform),typeof(Image));
        panel.transform.SetParent(stage,false);
        var screen=(RectTransform)panel.transform;screen.anchorMin=Vector2.zero;screen.anchorMax=Vector2.one;screen.offsetMin=screen.offsetMax=Vector2.zero;
        panel.GetComponent<Image>().color=new Color(0,0,0,.6f);
        var card=Rect(screen,"Name card",Vector2.zero,new Vector2(560,280));
        card.gameObject.AddComponent<Image>().color=new Color(.96f,.97f,.97f,1);
        Label(card,"플레이어 이름",new Vector2(0,100),new Vector2(480,36),26);
        Label(card,"대화 로그에 표시할 이름 · 최대 20자",new Vector2(0,58),new Vector2(490,30),18);
        var field=Rect(card,"Name input",new Vector2(0,4),new Vector2(480,48));
        field.gameObject.AddComponent<Image>().color=Color.white;
        input=field.gameObject.AddComponent<InputField>();input.characterLimit=40;input.lineType=InputField.LineType.SingleLine;
        var value=Label(field,"",Vector2.zero,new Vector2(452,40),23);value.alignment=TextAnchor.MiddleLeft;value.supportRichText=false;
        input.textComponent=value;input.targetGraphic=field.GetComponent<Image>();
        input.onValueChanged.AddListener(_=>error.text="");
        error=Label(card,"",new Vector2(0,-43),new Vector2(490,26),17);error.color=new Color(.7f,.16f,.12f);
        ButtonAt(card,"취소",new Vector2(-125,-100),new Vector2(220,42),Close);
        ButtonAt(card,"저장",new Vector2(125,-100),new Vector2(220,42),Save);
        panel.SetActive(false);
    }

    public static string Normalize(string value)
    {
        string clean=new string((value??"").Where(c=>!char.IsControl(c)).ToArray()).Trim();
        var elements=new StringInfo(clean);
        return elements.LengthInTextElements>20?elements.SubstringByTextElements(0,20):clean;
    }
    public void Open()
    {
        panel.transform.SetAsLastSibling();panel.SetActive(true);
        input.text=PlayerName;error.text="";input.Select();input.ActivateInputField();
    }
    public void Close()
    {
        panel.SetActive(false);closedFrame=Time.frameCount;
        if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
    }
    public bool SetPlayerName(string value)
    {
        string name=Normalize(value);
        if(name.Length==0)return false;
        PlayerName=name;PlayerPrefs.SetString(PreferenceKey,name);PlayerPrefs.Save();Changed?.Invoke(name);
        return true;
    }
    void Save()
    {
        if(!SetPlayerName(input.text)){error.text="이름을 입력해 주세요.";input.ActivateInputField();return;}
        Close();
    }
    void Update()
    {
        if(!IsOpen)return;
        if(Input.GetKeyDown(KeyCode.Escape)){Close();return;}
        // Enter confirms an IME composition before it can save the whole name.
        if((Input.GetKeyDown(KeyCode.Return)||Input.GetKeyDown(KeyCode.KeypadEnter)) && string.IsNullOrEmpty(Input.compositionString))Save();
    }
    RectTransform Rect(Transform parent,string name,Vector2 position,Vector2 size)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=position;rect.sizeDelta=size;return rect;
    }
    Text Label(Transform parent,string value,Vector2 position,Vector2 size,int fontSize)
    {
        var rect=Rect(parent,value,position,size);var label=rect.gameObject.AddComponent<Text>();
        label.text=value;label.font=font;label.fontSize=fontSize;label.color=new Color(.12f,.16f,.19f);label.alignment=TextAnchor.MiddleCenter;label.raycastTarget=false;return label;
    }
    RectTransform ButtonAt(Transform parent,string text,Vector2 position,Vector2 size,UnityEngine.Events.UnityAction action)
    {
        var rect=Rect(parent,text,position,size);var image=rect.gameObject.AddComponent<Image>();image.color=new Color(.85f,.9f,.94f);
        var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(action);
        Label(rect,text,Vector2.zero,size,18);return rect;
    }
}

#endif
