using System;
using System.IO;
using System.Text.Json;
using ResourceStoryMod.Story;

namespace ResourceStoryMod
{
    internal sealed class NativeNormalPlay
    {
        private readonly Core core;
        internal NormalPlayConfig Config {get;}
        internal bool Active=>Config.Enabled&&(core.Test==null||core.Test.Request.NormalPlay);
        internal bool DeveloperPanels=>!Active||Config.DeveloperPanels;
        internal NativeNormalPlay(Core core)
        {
            this.core=core;string path=Path.Combine(core.DataRoot,"millsage.play.json");
            Config=File.Exists(path)?JsonSerializer.Deserialize<NormalPlayConfig>(File.ReadAllText(path),Core.Json):new NormalPlayConfig();
            if(Config==null)throw new InvalidDataException("普通游玩配置为空，保留原配置。");Config.Validate();
            if(core.Test==null&&!File.Exists(path))File.WriteAllText(path,JsonSerializer.Serialize(Config,Core.Json));
        }
        internal string PackagePath(string saved)=>Path.Combine(core.Root,"ModDevelopment","ResourceStoryMod","Content","Packs",NormalPlayPolicy.ProfileForSave(saved),"content","story.package.json");
    }
}
