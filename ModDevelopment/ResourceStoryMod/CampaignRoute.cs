using System;
using System.Linq;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    // Shared test route selection. No flags are injected: each route is reached through authored choices.
    internal sealed class CampaignRoute
    {
        internal string Diary { get; }
        internal bool Commercial { get; }
        internal bool Heirloom { get; }
        internal bool Warn { get; }
        internal bool Alternate { get; }
        internal string Ending => Commercial ? "commercial" : Diary == "raw" ? "apparatus" : Diary == "filter" ? "rainbow" : "shared";
        internal string Id => $"{Diary}_{(Commercial ? "contract" : "autonomy")}_{(Heirloom ? "heirloom" : "reclaimed")}_{(Warn ? "warned" : "quiet")}_{(Alternate ? "last" : "first")}";
        internal CampaignRoute(string diary="raw", bool commercial=false, bool heirloom=true, bool warn=true, bool alternate=false)
        { Diary=diary;Commercial=commercial;Heirloom=heirloom;Warn=warn;Alternate=alternate; }
        internal StoryChoice Select(StoryFrame frame)
        {
            string preferred=frame.Scene.Id switch
            {
                "ms_scene_012" => Heirloom ? "opt_advise_heirloom" : "opt_advise_commercial",
                "ms_scene_019" => Diary=="raw" ? "opt_decode_solo_raw" : Diary=="filter" ? "opt_decode_shop_filter" : "opt_decode_group_together",
                "ms_scene_022" => Commercial ? "opt_sign_agency_contract" : "opt_reject_agency_keep_autonomy",
                "ms_scene_028" when frame.Node.Id=="choice_002" => Warn ? "opt_threaten_call_mahoro" : "opt_refuse_long_term_deposit",
                _ => null
            };
            if(frame.Choices.Count==0)throw new InvalidOperationException("No eligible option: "+frame.Scene.Id+"/"+frame.Node.Id);
            return preferred==null ? (Alternate ? frame.Choices.Last() : frame.Choices.First()) :
                frame.Choices.Single(c=>c.Id==preferred);
        }
    }
}
