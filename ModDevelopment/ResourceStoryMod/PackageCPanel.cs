using System;
using System.Collections.Generic;
using System.Linq;

namespace ResourceStoryMod.Story
{
    public static class CampaignPanel
    {
        public static CampaignWork[] Jobs(CampaignItemsState c)=>c.Jobs.Values.Reverse()
            .OrderBy(j=>j.Ready?(j.Delivered?2:1):0).ToArray();
        public static string Status(CampaignWork j)=>j.Delivered?"已交付":j.Ready?"已完工":j.Status=="running"?"办理中":j.Mode=="paused"?"已暂停":"待办理";
        public static string Station(CampaignWork j)=>j.Ready?null:j.Steps[j.Step].Station;
    }
}
