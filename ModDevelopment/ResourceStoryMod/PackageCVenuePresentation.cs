using System;
using System.Linq;

namespace ResourceStoryMod.Story
{
    // A shot is reconstructed from the persisted scene/node, never from the
    // current speaker. Speaking within a shot therefore cannot exchange slots.
    public sealed class VenueShot
    {
        public string Key {get;init;}
        public string Background {get;init;}
        public string[] Actors {get;init;}
        public bool Wide {get;init;}
        public float X(int slot)=>Actors.Length==1?0:Wide?(slot-(Actors.Length-1)/2f)*215:Actors.Length==2?(slot==0?-245:245):(slot-1)*365;
    }
    public static class VenuePresentation
    {
        private static readonly string[] Order={"ms_nagi","ms_natsume","ms_mahoro","ms_hotaru","ms_houka"};
        public static bool IsVenue(string scene)=>scene is "ms_scene_007" or "ms_scene_008" or "ms_scene_010" or "ms_scene_016" or "ms_scene_025" or "ms_scene_030";
        public static string Background(string scene,string node=null)=>scene=="ms_scene_031"||scene=="ms_scene_025"&&node!=null&&node!="line_001"||scene=="ms_scene_030"&&(node is "line_004_app_chase_unwarned" or "line_005")?"greenroom":"stage";
        public static string Title(string scene,string node=null)=>scene switch{
            "ms_scene_007"=>"水道街Livehouse · 合奏比试",
            "ms_scene_008"=>"水道街Livehouse · 最后一场比试",
            "ms_scene_010"=>"水道Livehouse · 第一次舞台",
            "ms_scene_016"=>"中层区星环回廊 · 选拔赛舞台",
            "ms_scene_025"=>node!=null&&node!="line_001"?"上层区星环大剧院 · 赛后后台":"上层区星环大剧院 · 首轮晋级赛",
            "ms_scene_030"=>Background(scene,node)=="greenroom"?"上层区星环大厅 · 后台":"上层区星环大厅 · 总决赛加冕",
            "ms_scene_031"=>"星环大厅 · 后台休息室",
            _=>throw new ArgumentOutOfRangeException(nameof(scene))};
        public static VenueShot Shot(StoryScene scene,StoryNode node,string[] available)
        {
            if(!IsVenue(scene.Id))throw new ArgumentOutOfRangeException(nameof(scene));
            var actors=Order.Where(available.Contains).ToArray();
            bool wide=(scene.Id is "ms_scene_025" or "ms_scene_030")&&node.Id=="line_001";
            var named=(node.DialogueParts?.Select(p=>p.Speaker)??new[]{node.Speaker}).Where(actors.Contains).Distinct().ToList();
            if(wide)named=actors.ToList();
            else{
                if(named.Count==0)named.AddRange(actors.Where(a=>a is "ms_natsume" or "ms_mahoro" or "ms_hotaru").Take(3));
                if(named.Count==0)named.AddRange(actors.Take(2));
                if(named.Count==1){
                    string mate=named[0] switch{"ms_nagi"=>scene.Id=="ms_scene_031"?"ms_mahoro":"ms_natsume","ms_natsume"=>"ms_nagi","ms_mahoro"=>scene.Id=="ms_scene_031"?"ms_nagi":"ms_hotaru",_=>"ms_mahoro"};
                    if(actors.Contains(mate)&&!named.Contains(mate))named.Add(mate);
                }
                if(named.Count>3)throw new InvalidOperationException("Venue node needs an authored shot split");
            }
            var ordered=Order.Where(named.Contains).ToArray();
            return new(){Key=scene.Id+"/"+Background(scene.Id,node.Id)+"/"+string.Join("+",ordered)+(wide?"/wide":"/medium"),Background=Background(scene.Id,node.Id),Actors=ordered,Wide=wide};
        }
    }
}
