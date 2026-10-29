using System;
using System.Collections.Generic;
using System.Linq;

// TalkScriptEngine.GetVideoIndexList (0xB495B0) remembers the most recent
// StartTalk/StartTalk_Portrait/SilentTalk/StartTalk_Center row at SkipBlock.
// GetNextSkip (0xB48420) selects the next strictly later anchor, not episode end.
public static class StorySkipPlan {
    public sealed class Landing {public string group;public int step,command,flatIndex,scene;}
    public static List<Landing> Build(StoryEpisode episode) {
        var result=new List<Landing>();
        var visited=new HashSet<string>();
        for(int scene=0;scene<episode.scenes.Length;scene++)Visit(episode,episode.scenes[scene].entry,scene,visited,result);
        return result;
    }
    static void Visit(StoryEpisode episode,string key,int scene,HashSet<string> visited,List<Landing> result) {
        if(string.IsNullOrEmpty(key)||!visited.Add(key))return;
        var group=episode.groups.First(g=>g.key==key);
        var anchor=new Landing{group=key,scene=scene};int index=0;
        for(int step=0;step<group.steps.Length;step++)for(int command=0;command<group.steps[step].commands.Length;command++,index++) {
            string name=group.steps[step].commands[command].name;
            // Native bit mask 0x808000003 has bits 0, 1, 27, 35 (not 31).
            if(name=="StartTalk" || name=="StartTalk_Portrait" || name=="SilentTalk" || name=="StartTalk_Center")
                anchor=new Landing{group=key,scene=scene,step=step,command=command,flatIndex=index};
            if(name=="SkipBlock" && !result.Any(p=>p.group==key&&p.flatIndex==anchor.flatIndex))result.Add(anchor);
        }
        foreach(var command in group.steps.SelectMany(s=>s.commands).Where(c=>c.name=="Select"||c.name=="Goto"))
            Visit(episode,command.args[command.name=="Goto"?0:1],scene,visited,result);
    }
    public static Landing Next(StoryEpisode episode,string group,int flatIndex) {
        // The current group may contain no anchor; retain its position in the
        // episode's authored reachable group order rather than sorting names.
        var order=new List<string>();var seen=new HashSet<string>();
        foreach(var scene in episode.scenes)Order(episode,scene.entry,seen,order);
        int current=order.IndexOf(group);
        return Build(episode).FirstOrDefault(p=>order.IndexOf(p.group)>current || (p.group==group&&p.flatIndex>flatIndex));
    }
    static void Order(StoryEpisode episode,string key,HashSet<string> seen,List<string> order) {
        if(string.IsNullOrEmpty(key)||!seen.Add(key))return;order.Add(key);
        foreach(var command in episode.groups.First(g=>g.key==key).steps.SelectMany(s=>s.commands).Where(c=>c.name=="Select"||c.name=="Goto"))
            Order(episode,command.args[command.name=="Goto"?0:1],seen,order);
    }
}
