#if UNITY_EDITOR || VIEWER_BACKEND
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

// Readability adjustment requested for display; source tables stay intact.
public static class StoryTextDisplay {
    public static string PlayerText(string value,string name) {
        value=(value??"").Replace("[nickname]",name).Replace("{PlayerName}",name);
        return Regex.Replace(value,@"([가-힣])\^(으로|이야|은|는|이|가|을|를|과|와|야|아|로)",m=> {
            int final=(m.Groups[1].Value[0]-0xAC00)%28;string suffix=m.Groups[2].Value;
            switch(suffix) {
                case "은":case "는":suffix=final!=0?"은":"는";break;
                case "이":case "가":suffix=final!=0?"이":"가";break;
                case "을":case "를":suffix=final!=0?"을":"를";break;
                case "과":case "와":suffix=final!=0?"과":"와";break;
                case "야":case "이야":suffix=final!=0?"이야":"야";break;
                case "아":suffix=final!=0?"아":"야";break;
                case "로":case "으로":suffix=final!=0&&final!=8?"으로":"로";break;
            }
            return m.Groups[1].Value+suffix;
        });
    }
    // Consume rich-text tags and addresses as whole tokens before punctuation.
    static readonly Regex Tokens=new Regex(@"<[^>]*>|(?:https?://|www\.)[^\s<>]+|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|[.,!?…。，！？]+",RegexOptions.IgnoreCase);
    public static string Format(string value) {
        if(string.IsNullOrEmpty(value))return value;
        var spaces=new HashSet<int>();
        foreach(Match match in Tokens.Matches(value)) {
            if(".,!?…。，！？".IndexOf(match.Value[0])<0)continue;
            int next=match.Index+match.Length;
            // Put the separator outside intervening closing tags, and never
            // alter a tag's attributes or interpret its punctuation as prose.
            while(next<value.Length && value[next]=='<') {
                int end=value.IndexOf('>',next);if(end<0)break;next=end+1;
            }
            if(next>=value.Length || char.IsWhiteSpace(value[next]) || ",.?!…。，！？:;：；)]}）］｝>\"'”’」』".IndexOf(value[next])>=0)continue;
            int previous=match.Index-1;
            while(previous>=0 && value[previous]=='>') {
                int start=value.LastIndexOf('<',previous);if(start<0)break;previous=start-1;
            }
            if(match.Length==1 && ".,，".IndexOf(match.Value[0])>=0 && previous>=0 && char.IsDigit(value[previous]) && char.IsDigit(value[next]))continue;
            bool ellipsis=match.Value.IndexOf('…')>=0 || match.Value.Contains("..");
            bool onlyEllipsis=ellipsis && match.Value.Trim('.','…').Length==0;
            // Opening hesitation belongs to the following phrase. Internal
            // pauses separate phrases, e.g. 응… 오로라; never split the dots.
            if(onlyEllipsis && (previous<0 || char.IsWhiteSpace(value[previous]) || "([{'\"“‘「『".IndexOf(value[previous])>=0))continue;
            spaces.Add(next);
        }
        if(spaces.Count==0)return value;
        var result=new StringBuilder(value.Length+spaces.Count);
        for(int i=0;i<value.Length;i++){if(spaces.Contains(i))result.Append(' ');result.Append(value[i]);}
        return result.ToString();
    }
}

#endif
