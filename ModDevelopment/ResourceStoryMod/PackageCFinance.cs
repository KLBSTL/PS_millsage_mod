using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ResourceStoryMod.Story
{
    public sealed class CampaignFinanceLine
    {
        public string Source {get;set;}
        public string Original {get;set;}
        public Dictionary<string,string> Variants {get;set;}=new();
        public Dictionary<string,List<StoryDialoguePart>> VariantParts {get;set;}=new();
    }
    public sealed class CampaignFinanceText
    {
        public int Version {get;set;}=1;
        public string Status {get;set;}
        public List<CampaignFinanceLine> Lines {get;set;}=new();
        public static readonly string[] Anchors={"ms_scene_018/line_002","ms_scene_021/line_001","ms_scene_022/line_001","ms_scene_022/line_007a","ms_scene_031/line_002_shar"};
        public static CampaignFinanceText Load(string path,StoryPackage package)
        {
            var text=JsonSerializer.Deserialize<CampaignFinanceText>(File.ReadAllText(path),StoryContent.Json);text?.Validate(package);
            return text??throw new InvalidDataException("资金文字补丁为空");
        }
        public void Validate(StoryPackage package)
        {
            bool solo=package.CampaignProfile==PackageC.ReturnProfile;var anchors=solo?Anchors.Select(x=>x=="ms_scene_031/line_002_shar"?"ms_scene_031/line_002_app":x):Anchors;
            if(Version!=1 || Status!=(solo?"solo_return":"minimal_revision") || Lines==null || Lines.Count!=5 || !Lines.Select(x=>x.Source).OrderBy(x=>x).SequenceEqual(anchors.OrderBy(x=>x)))throw new InvalidDataException("资金文字补丁范围不符");
            foreach(var row in Lines){
                var key=row.Source.Split('/');var node=package.Scenes.Single(s=>s.Id==key[0]).Nodes.Single(n=>n.Id==key[1]);
                if(node.Text!=row.Original || row.Variants==null || row.Variants.Count!=2 || !row.Variants.ContainsKey("independent_wallet_coordination") || !row.Variants.ContainsKey("funds_available_contract_context") || row.Variants.Values.Any(x=>string.IsNullOrWhiteSpace(x) || x.Length>(solo?4000:240)))throw new InvalidDataException("资金文字补丁与原文或变体不符："+row.Source);
            }
        }
        public string Resolve(StoryState state,string scene,StoryNode node)
        {
            if(state.CampaignItems?.Stage!=2)return node.Text;
            string key=scene+"/"+node.Id;
            if(!state.CampaignItems.FinanceContexts.TryGetValue(key,out var context)){
                string old=key=="ms_scene_018/line_002"?"ms_scene_018/line_001":key=="ms_scene_022/line_007a"?"ms_scene_022/line_001":null;
                if(old==null || !state.CampaignItems.FinanceContexts.TryGetValue(old,out context))return node.Text;
            }
            var row=Lines.FirstOrDefault(x=>x.Source==key);
            return row?.Variants.GetValueOrDefault(context.TextVariant)??node.Text;
        }
        public List<StoryDialoguePart> ResolveParts(StoryState state,string scene,StoryNode node)
        {
            string key=scene+"/"+node.Id;if(state.CampaignItems?.FinanceContexts.TryGetValue(key,out var c)!=true)return null;
            return Lines.FirstOrDefault(x=>x.Source==key)?.VariantParts?.GetValueOrDefault(c.TextVariant);
        }
    }
}
