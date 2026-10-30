using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Fixed home layout. Backend supplies navigation callbacks only.
public static class ViewerHomeMenu {
    public static void Draw(RectTransform root,TMP_FontAsset font,Action story,Action instarseed){
        var ink=new Color(.89f,.94f,.97f);var muted=new Color(.58f,.68f,.75f);var accent=new Color(.36f,.87f,.89f);
        Label(root,font,"STARSEED",64,48,900,42,22,accent);Label(root,font,"기록 보관소",64,112,1000,72,46,ink);
        Label(root,font,"구 한국판의 이야기를 다시 만나보세요.",67,195,1000,38,21,muted);
        var a=Button(root,font,"스토리",64,294,556,278,story,new Color(.09f,.14f,.19f),ink);
        Label(a,font,"STORY ARCHIVE",32,100,460,28,15,accent);Label(a,font,"메인 1–6장 · 인터루드\n94개 에피소드",32,151,460,75,23,muted);
        var b=Button(root,font,"인스타시드",644,294,572,278,instarseed,new Color(.10f,.23f,.27f),ink);
        Label(b,font,"INSTARSEED",32,100,470,28,15,accent);Label(b,font,"프록시안들의 일상\n게시물 · 영상 · 댓글",32,151,470,75,23,muted);
        Label(root,font,"LOCAL ARCHIVE  /  "+ViewerShell.BackendVersion,66,650,720,30,14,muted);Label(root,font,"M  음소거 전환    - / +  음량",884,650,330,30,15,muted);Debug.Log("VIEWER_HOME_SHOWN");
    }
    static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h){var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rt.SetParent(parent,false);rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(w,h);return rt;}
    static TMP_Text Label(Transform p,TMP_FontAsset font,string value,float x,float y,float w,float h,float size,Color color){var rt=Rect(p,"Label",x,y,w,h);rt.gameObject.SetActive(false);var t=rt.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSize=size;t.color=color;t.text=value;t.alignment=TextAlignmentOptions.TopLeft;t.raycastTarget=false;t.overflowMode=TextOverflowModes.Ellipsis;rt.gameObject.SetActive(true);return t;}
    static RectTransform Button(Transform p,TMP_FontAsset font,string value,float x,float y,float w,float h,Action action,Color color,Color ink){var rt=Rect(p,"Button "+value,x,y,w,h);var img=rt.gameObject.AddComponent<Image>();img.color=color;var b=rt.gameObject.AddComponent<Button>();b.targetGraphic=img;b.navigation=new Navigation{mode=Navigation.Mode.None};b.onClick.AddListener(()=>action());var t=Label(rt,font,value,12,0,w-24,h,36,ink);t.margin=new Vector4(32,35,24,20);return rt;}
}
