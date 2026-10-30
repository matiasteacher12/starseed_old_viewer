#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class OriginalInstarseedUi {
    [Serializable] public class MessageCatalogue {public int schema;public Conversation[] conversations;}
    [Serializable] public class Conversation {public long characterId;public string source;public Message[] messages;}
    [Serializable] public class Message {public string id,episode,scene,title;public int order;public ChatLine[] lines;}
    [Serializable] public class ChatLine {public int order,kind;public string[] texts;}
    [Serializable] class ChatProgress {public int[] choices;}
    MessageCatalogue messages;
    readonly Dictionary<string,List<int>> chatChoices=new Dictionary<string,List<int>>();
    public Action<OriginalStoryMenu.Episode,Action> OpenDate;
    public Func<OriginalStoryMenu.Episode,bool> DateIsRead;
    const string ChoicePrefix="instarseed.chat.choices.";
    void EnsureMessages(){if(messages!=null)return;var asset=StoryContent.Load<TextAsset>("instarseed-messages");messages=asset?BackendJson.FromJson<MessageCatalogue>(asset.text):new MessageCatalogue{schema=1,conversations=Array.Empty<Conversation>()};if(messages.schema!=1)throw new InvalidOperationException("Unsupported message catalogue");}
    Conversation ConversationFor(InstarseedView.Profile person){EnsureMessages();return messages.conversations.FirstOrDefault(c=>c.characterId==person.characterId);}
    void ConfigureMessageButton(GameObject row,InstarseedView.Profile person){
        Off(row,"Buttons");if(ConversationFor(person)==null)return;
        Active(row,"Buttons",true);Manual(row.transform.Find("Buttons").gameObject);Place(row.transform.Find("Buttons"),620,95,620,76);
        foreach(Transform child in row.transform.Find("Buttons"))child.gameObject.SetActive(child.name=="Btn_Message");
        var button=row.transform.Find("Buttons/Btn_Message");Place(button,250,0,340,70);Set(row,"Buttons/Btn_Message/Text_On","메시지");Off(row,"Buttons/Btn_Message/New");Bind(row,"Buttons/Btn_Message",()=>ShowMessages(person));
    }
    public void ShowMessages(InstarseedView.Profile person){
        var conversation=ConversationFor(person);if(conversation==null)return;
        Show("Message",()=>ShowProfile(person),1);Bind(current,"Ani/Top/Btn_Back",Back);Set(current,"Ani/Top/Text_Title",person.name+" · 메시지");Off(current,"Ani/FollowerList");Scroller("Ani/ScrollView");
        foreach(var item in conversation.messages.OrderBy(m=>m.order)){var m=item;Add(240,parent=>QuestRow(parent,m.title,"메시지 보기",()=>ShowChat(person,m),false));}
        Refresh();Debug.Log("INSTARSEED_MESSAGES character="+person.characterId+" entries="+conversation.messages.Length);
    }
    List<int> Choices(Message message){
        if(chatChoices.TryGetValue(message.id,out var value))return value;
        value=new List<int>();
        if(!Application.isBatchMode){try{var saved=BackendJson.FromJson<ChatProgress>(PlayerPrefs.GetString(ChoicePrefix+message.id,"{}"));if(saved?.choices!=null)value.AddRange(saved.choices);}catch(Exception){Debug.LogWarning("INSTARSEED_CHAT_STATE_INVALID "+message.id);}}
        var options=message.lines.Where(l=>l.kind==1).ToArray();int valid=0;while(valid<value.Count&&valid<options.Length&&value[valid]>=0&&value[valid]<options[valid].texts.Length)valid++;if(valid<value.Count)value.RemoveRange(valid,value.Count-valid);
        chatChoices[message.id]=value;return value;
    }
    void SaveChoices(Message message){if(Application.isBatchMode)return;PlayerPrefs.SetString(ChoicePrefix+message.id,BackendJson.ToJson(new ChatProgress{choices=Choices(message).ToArray()}));PlayerPrefs.Save();}
    static void ManualTree(GameObject go){foreach(var l in go.GetComponentsInChildren<LayoutGroup>(true))l.enabled=false;foreach(var f in go.GetComponentsInChildren<ContentSizeFitter>(true))f.enabled=false;}
    string ChatText(string text)=>StoryTextDisplay.Format(StoryTextDisplay.PlayerText(text??"",PlayerPrefs.GetString(StoryPlayerNameDialog.PreferenceKey,StoryPlayerNameDialog.DefaultName)));
    void ChatLabel(GameObject row,string path,string value,float x,float y,float w,float h,float size){
        var target=row.transform.Find(path);if(!target)throw new InvalidOperationException("Missing chat text "+path);Place(target,x,y,w,h);target.gameObject.SetActive(true);
        var old=target.GetComponent<Text>();if(old)old.enabled=false;var tmp=target.GetComponent<TMP_Text>();
        if(!tmp){var child=target.Find("Viewer text");if(!child){child=new GameObject("Viewer text",typeof(RectTransform)).transform;child.SetParent(target,false);Stretch(child);}tmp=child.GetComponent<TMP_Text>()??child.gameObject.AddComponent<TextMeshProUGUI>();}
        tmp.font=font;tmp.fontSharedMaterial=font.material;tmp.spriteAsset=emoji;tmp.fontSize=size;tmp.enableAutoSizing=false;tmp.enableWordWrapping=true;tmp.color=new Color(.16f,.18f,.22f);tmp.raycastTarget=false;tmp.overflowMode=TextOverflowModes.Overflow;tmp.text=value;
    }
    public void ShowChat(InstarseedView.Profile person,Message message,bool bottom=false){
        Show("Chatting",()=>ShowMessages(person),1);ManualTree(current);Off(current,"Common_PopupBackground_Simple");Stretch(current.transform.Find("Window"));
        Off(current,"Window/ForSizeGetter_Message1");Off(current,"Window/ForSizeGetter_Message2");Off(current,"Window/Contents/Top/Rank");
        Bind(current,"Window/Contents/Top/Btn_back",Back);Set(current,"Window/Contents/Top/Btn_back/Text_Name",person.name);
        Stretch(current.transform.Find("Window/Contents/Center/Layout/ChatArea"));Scroller("Window/Contents/Center/Layout/ChatArea/Scroller");
        var answers=Choices(message);int answer=0;bool waiting=false;
        foreach(var line in message.lines){
            if(line.kind==0){foreach(var t in line.texts)AddChatBubble(person,ChatText(t),false);continue;}
            if(answer<answers.Count){AddChatBubble(person,ChatText(line.texts[answers[answer++]]),true);continue;}
            AddReplyOptions(line.texts.Select(ChatText).ToArray(),option=>{answers.Add(option);SaveChoices(message);ShowChat(person,message,true);});waiting=true;break;
        }
        if(!waiting){
            if(!string.IsNullOrEmpty(message.episode)){var ep=EpisodeFor(message);bool read=DateIsRead?.Invoke(ep)??false;Add(250,parent=>QuestRow(parent,message.title,read?"지난 만남을 회상":"만남 장소로 이동",()=>OpenDate?.Invoke(ep,()=>ShowChat(person,message,true)),read));}
            AddReplyOptions(new[]{"대화 다시 보기"},_=>{answers.Clear();SaveChoices(message);ShowChat(person,message);});
        }
        Refresh();Canvas.ForceUpdateCanvases();if(bottom){scroll.verticalNormalizedPosition=0;Refresh();}
        Debug.Log("INSTARSEED_CHAT id="+message.id+" answers="+answers.Count+" complete="+(!waiting)+" episode="+message.episode);
    }
    static OriginalStoryMenu.Episode EpisodeFor(Message m)=>new OriginalStoryMenu.Episode{id=m.episode,title=m.title,scene=m.scene,playable=true};
    void AddChatBubble(InstarseedView.Profile person,string text,bool mine){
        float height=Mathf.Max(124,TextHeight(text,890,36)+90);Add(height,parent=>{
            var row=Make(mine?"Favor_MyChatItem":"Favor_OtherChatItem",parent);ManualTree(row);Off(row,"Emoticon");Place(row.transform.Find("Bubble"),mine?280:160,0,950,height-16);
            var bubble=row.transform.Find("Bubble/Image_Bubble");Place(bubble,0,mine?12:45,950,height-(mine?28:61));
            ChatLabel(row,"Bubble/Image_Bubble/Text",text,24,14,900,height-50,36);
            if(!mine){Place(row.transform.Find("Bubble/Portrait"),-130,8,105,105);Picture(row,"Bubble/Portrait/Mask/Img_Portrait",person.icon);ChatLabel(row,"Bubble/Text_UserName",person.name,0,0,880,38,27);}
            return row;
        });
    }
    void AddReplyOptions(string[] options,Action<int> choose){
        var heights=options.Select(t=>Mathf.Max(80,TextHeight(t,900,36)+30)).ToArray();float height=heights.Sum()+30;
        Add(height,parent=>{var row=Make("Favor_ChatSelectItem",parent);ManualTree(row);Off(row,"Favor_Btn_Reply");Place(row.transform.Find("Bubble"),240,10,1000,height-20);Place(row.transform.Find("Bubble/Select"),0,0,1000,height-20);float y=0;
            for(int i=0;i<2;i++){string path="Bubble/Select/Favor_Btn_Reply"+(i==0?"":"_1");Active(row,path,i<options.Length);if(i>=options.Length)continue;int selected=i;Place(row.transform.Find(path),0,y,1000,heights[i]);ChatLabel(row,path+"/Text_Reply",options[i],80,14,880,heights[i]-20,36);Bind(row,path,()=>choose(selected));y+=heights[i];}return row;});
    }
    GameObject QuestRow(Transform parent,string title,string button,Action action,bool read){
        var row=Make("Favor_QuestChatItem",parent);ManualTree(row);Place(row.transform.Find("Bubble"),160,12,1000,220);Place(row.transform.Find("Bubble/Portrait"),-130,8,105,105);Place(row.transform.Find("Bubble/Bg"),0,10,1000,190);
        ChatLabel(row,"Bubble/Bg/Text_Quest",title,55,18,880,75,36);Place(row.transform.Find("Bubble/Bg/Button"),0,100,1000,80);Place(row.transform.Find("Bubble/Bg/Button/Btn_Go"),180,5,640,70);
        string root="Bubble/Bg/Button/Btn_Go/";Active(row,root+"Normal",!read);Active(row,root+"Repeat",read);foreach(var state in new[]{"Normal","Repeat"}){Off(row,root+state+"/Clear");ChatLabel(row,root+state+"/Text",button,20,12,600,55,32);}Bind(row,"Bubble/Bg/Button/Btn_Go",action);return row;
    }
    public void PrepareDateCheck(string episodeId){
        EnsureMessages();var conversation=messages.conversations.Single(c=>c.messages.Any(m=>m.episode==episodeId));var person=data.profiles.Single(p=>p.characterId==conversation.characterId);var message=conversation.messages.Single(m=>m.episode==episodeId);
        ShowChat(person,message);CompleteChatCheck();scroll.verticalNormalizedPosition=0;Refresh();var quest=rows.Select(r=>r.go).First(g=>g&&g.name=="Favor_QuestChatItem");quest.transform.Find("Bubble/Bg/Button/Btn_Go").GetComponent<Button>().onClick.Invoke();
    }
    void CompleteChatCheck(){
        int guard=0;
        while(guard++<100){scroll.verticalNormalizedPosition=0;Refresh();var choice=rows.Select(r=>r.go).FirstOrDefault(g=>g&&g.name=="Favor_ChatSelectItem");if(!choice)throw new Exception("Chat input missing");var button=choice.transform.Find("Bubble/Select/Favor_Btn_Reply").GetComponent<Button>();var text=button.transform.Find("Text_Reply").GetComponentInChildren<TMP_Text>().text;if(text=="대화 다시 보기")return;button.onClick.Invoke();}
        throw new Exception("Chat did not terminate");
    }
    public System.Collections.IEnumerator CheckMessages(Action<string> capture){
        EnsureMessages();if(messages.conversations.Length!=74||messages.conversations.Sum(c=>c.messages.Length)!=370)throw new Exception("Message scope mismatch");
        int count=0;
        foreach(var conversation in messages.conversations){var person=data.profiles.Single(p=>p.characterId==conversation.characterId);ShowProfile(person);yield return null;
            var header=rows.First().go;var control=header.transform.Find("Buttons/Btn_Message").GetComponent<Button>();if(!control.interactable||!control.gameObject.activeInHierarchy)throw new Exception("Message entry disabled");control.onClick.Invoke();if(current!=pages["Message"]||rows.Count!=5)throw new Exception("Message list mismatch");
            if(count==0)capture("messages");
            foreach(var message in conversation.messages){ShowChat(person,message);yield return null;CompleteChatCheck();yield return null;CheckScroll(0);
                if(count<3)capture("chat-"+count);if(Choices(message).Count!=message.lines.Count(l=>l.kind==1))throw new Exception("Chat choices incomplete");Back();if(current!=pages["Message"])throw new Exception("Chat back mismatch");count++;}
            Back();if(current!=pages["Profile"])throw new Exception("Message back mismatch");
        }
        Debug.Log("INSTARSEED_MESSAGES_CHECK_OK characters=74 messages="+count+" choicesInteractive=true back=profile");
    }
}
#endif
