#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections.Generic;
using UnityEngine;

// Exact old-script bank-list omissions. All five cues are present in preserved
// Windows-matched ACBs and already packaged for other connected episodes.
// Evidence: Data/remaining091/audio_gap_search.json and exact_voice_repair_candidates.json.
public static class StoryAudioRepairs {
    static bool MissingSound(string episode,string group,string bank,string cue) {
        if(bank!="Story")return false;
        if(cue=="fx_story_birdchirping_01")return episode=="rachel01_01"&&group=="rachel_1_15";
        if(cue!="001_fx_story_echidnasnake01")return false;
        return (episode=="echidna01_01"&&(group=="echidna_1_01"||group=="echidna_1_04"||group=="echidna_1_06"))
            ||(episode=="echidna01_02"&&(group=="echidna_2_08"||group=="echidna_2_09"))
            ||(episode=="echidna01_03"&&group=="echidna_3_17")
            ||(episode=="echidna01_04"&&(group=="echidna_4_01"||group=="echidna_4_08"));
    }
    static string Resource(string episode,string group,string cue) {
        if(episode=="ambrosia01_04"&&group=="ambrosia_4_04"&&cue=="vo_klairs@shame01")return "e7071843dba9ca3dc957c4296b390371dcb26051b390c08b8a8686329146e477";
        if(episode=="ambrosia01_04"&&group=="ambrosia_4_05"&&cue=="vo_klairs@think01")return "96ae2b6e08420384f2c2f353b21e15bb3fc73917a84b735e27cdaa1dd8aa26d7";
        if(episode=="erica01_01"&&group=="erica_1_11"&&cue=="vo_crimson@positive05_2")return "9ce064749d650a2aa213a2173b924aa51abc89be04471533baad308d4a6f99bd";
        if(episode=="erica01_04"&&group=="erica_4_11"&&cue=="vo_rayhou@laugh02_2")return "4c150508602faf96977f8052d6a300851b9efe339417cd8120163c7a6e16d210";
        if(episode=="Event_Story_Fuchsia_05"&&group=="Event_fuchsia_5_01"&&cue=="vo_kanata@positive02")return "0f8c6aabdd18c7389443ae0134d743a17179ac14eda98e55ce8a5eaeb28c6e12";
        return null;
    }
    public static string[] Apply(StoryEpisode episode) {
        var resources=new HashSet<string>();
        foreach(var group in episode.groups)foreach(var step in group.steps)foreach(var command in step.commands){
            if(command.name=="SE"&&MissingSound(episode.id,group.key,command.args[0],command.args[1])){
                if(command.audio.Length!=0)throw new InvalidOperationException("Missing sound source changed: "+episode.id);
                command.audioMode="missing-source-cue";command.sourceGap="exact original sound cue absent from all 408 preserved ACB copies";
            }
            if(command.name!="Voice")continue;
            string hash=Resource(episode.id,group.key,command.args[1]);if(hash==null)continue;
            if(command.audio.Length!=0||command.audioMode!="missing-source-cue")throw new InvalidOperationException("Voice repair source changed: "+episode.id+" "+command.args[1]);
            string resource="Original/Episode/"+hash;command.audio=new[]{resource};command.audioMode="declared-bank-repaired";resources.Add(resource);
            Debug.Log("STORY_DECLARED_BANK_REPAIR "+episode.id+" "+group.key+" "+command.args[1]+" resource="+resource);
        }
        return new List<string>(resources).ToArray();
    }
}
#endif
