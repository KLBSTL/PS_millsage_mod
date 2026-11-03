using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace ResourceStoryMod.Story
{
    public static class PackageBCatalog
    {
        private static readonly Dictionary<string,Dictionary<string,string>[]> tables=JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,string>[]>>(PackageBData.Json);
        private static readonly Dictionary<string,string> basicInspector=BasicInspector();
        private static Dictionary<string,string> BasicInspector()
        {
            var row=new Dictionary<string,string>(tables["items_package_b"].First(x=>x["id"]=="ms_machine_calibrator"));
            row["id"]=NormalPlayPolicy.BasicInspector;row["name"]="基础器材检测仪";row["reference_price"]=NormalPlayPolicy.BasicInspectorPrice.ToString(CultureInfo.InvariantCulture);row["outer_grid"]="3x2";row["source_zones"]="lower;middle";row["ordinary_retail_zones"]="lower;middle";row["native_item_type"]="TOOL";row["notes"]="用于乐队主线原件的即时检测，不提供专业参考自检、精密标定资格。";return row;
        }
        private static readonly Dictionary<string,string> basicSupply=new(){{"id",NormalPlayPolicy.BasicInspector},{"name","基础器材检测仪"},{"pool","parts"},{"source_zones","lower;middle"},{"ordinary_retail_zones","lower;middle"},{"used_allowed","False"}};
        public static IEnumerable<Dictionary<string,string>> Rows(string table)=>table=="items_package_b"?tables[table].Append(basicInspector):table=="supplier_candidates"?tables[table].Append(basicSupply):tables[table];
        public static Dictionary<string,string> Item(string id)=>Rows("items_package_b").FirstOrDefault(x=>x["id"]==id);
        public static string[] Split(string value)=>string.IsNullOrEmpty(value)?Array.Empty<string>():value.Split(';',StringSplitOptions.RemoveEmptyEntries);
        public static int Number(string value,int fallback=0)=>int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)?n:fallback;
        public static Dictionary<string,int> Quantities(string value)=>Split(value).ToDictionary(x=>x.Split('*')[0],x=>Number(x.Split('*')[1]));
        public static string Station(string value)=>value.Replace("st_","");
        public static readonly string[] Grades={"ordinary","selected","precision"};
        public static int Rank(string grade)=>Array.IndexOf(Grades,grade);
        public static string GradeName(string grade)=>grade=="precision"?"精密":grade=="selected"?"精选":"普通";
        public static decimal Multiplier(string grade)=>grade=="precision"?1.40m:grade=="selected"?1.18m:1m;
        public static bool Graded(string id)=>Item(id)?.GetValueOrDefault("graded")=="True" || LegacyMusic.Any(x=>x.Id==id);
        public static readonly string[] LegacyIds={"ms_module_quiet_power","ms_part_stabilizer_pickup","ms_module_tone_filter","ms_gear_drum_practice_pad","ms_gear_headphone_amp","ms_module_quick_patch","ms_part_stabilizer_pickup_commercial","ms_gear_commercial_guitar"};
        private static IEnumerable<MusicProduct> LegacyMusic=>MusicCatalog.Products.Where(p=>LegacyIds.Contains(p.Id));
        public static IEnumerable<string> AllIds=>LegacyIds.Concat(Rows("items_package_b").Select(x=>x["id"]));
        public static int Price(string sku)=>sku=="printer"?3200:Number(Item(sku)?.GetValueOrDefault("reference_price"),MusicCatalog.Find(sku)?.Price??0);
        public static WorkshopProduct[] WorkshopProducts()=>Rows("items_package_b").Select(x=>{var shape=(x["id"]=="ms_machine_calibrator"?"4x3":x["outer_grid"]).Split('x');return new WorkshopProduct{Id=x["id"],Name=x["name"],Kind=x["group"]=="raw_supply"?"material":x["group"],Price=Number(x["reference_price"]),Width=Number(shape[0]),Height=Number(shape[1])};}).ToArray();
        public static MusicProduct[] ExtraMusic()=>Rows("items_package_b").Where(x=>x["actor_id"]!="").Select(x=>{
            var shape=x["outer_grid"].Split('x');return new MusicProduct{Id=x["id"],Name=x["name"],Actor=x["actor_id"],Profile=x["compatibility_profile"],Slot=x["slot"]==""?null:x["slot"],Price=Number(x["reference_price"]),Willingness=x["id"]=="ms_gear_monitor_speaker"?3600:x["id"]=="ms_gear_custom_overdrive"?2600:Number(x["reference_price"])*5/4,Width=Number(shape[0]),Height=Number(shape[1]),NativeType=x["native_item_type"],UnlockScene=Number(x["unlock_scene"].Replace("ms_scene_","")),Zones=Split(x["source_zones"])};
        }).ToArray();
        public static WorkshopRecipe[] Recipes()=>Rows("recipes_package_b").Select(x=>new WorkshopRecipe{Id=x["recipe_id"],Name=(WorkshopProducts().FirstOrDefault(p=>p.Id==x["output_id"])?.Name??x["output_id"])+" ×"+x["output_quantity"],Station=Station(x["station"]),Output=x["output_id"],Quantity=Number(x["output_quantity"]),Seconds=Number(x["seconds_draft"]),Energy=Number(x["energy_draft"]),Inputs=Quantities(x["inputs"]),Tools=Quantities(x["tools"]),Qualification=x["qualification"],Critical=Split(x["critical_graded_inputs"])}).ToArray();
        public static WorkshopSpec Spec(string recipeId,string mode,IEnumerable<WorkshopEntity> inputs)
        {
            var recipe=WorkshopCatalog.Recipe(recipeId)??throw new InvalidOperationException("配方不存在");
            var row=Rows("recipe_modes_costs").FirstOrDefault(x=>x["recipe_id"]==recipeId && x["mode"]==mode)??throw new InvalidOperationException("此配方不支持该工艺");
            if(recipe.Station=="native_printer")throw new InvalidOperationException("打印由原版打印机执行");
            var all=new Dictionary<string,int>(recipe.Inputs);foreach(var p in Quantities(row["extra_inputs"]))all[p.Key]=all.GetValueOrDefault(p.Key)+p.Value;
            int cap=Rank(mode);foreach(var e in (inputs??Array.Empty<WorkshopEntity>()).Where(e=>recipe.Critical.Contains(e.Reference)))cap=Math.Min(cap,Rank(e.Grade));
            return new WorkshopSpec{Recipe=recipeId,Mode=mode,Station=recipe.Station,Inputs=all,Tools=new(recipe.Tools),Output=recipe.Output,Quantity=recipe.Quantity,DurationMs=Number(row["seconds"])*1000,Energy=Number(row["energy_E"]),Grade=Grades[Math.Max(0,cap)]};
        }
    }
    public sealed class WorkshopSpec
    {
        public string Recipe {get;set;}
        public string Mode {get;set;}="ordinary";
        public string Station {get;set;}
        public Dictionary<string,int> Inputs {get;set;}=new();
        public Dictionary<string,int> Tools {get;set;}=new();
        public string Output {get;set;}
        public int Quantity {get;set;}
        public int DurationMs {get;set;}
        public int Energy {get;set;}
        public string Grade {get;set;}="ordinary";
        public string Purpose {get;set;}="manufacture";
        public string Service {get;set;}
        public int MeasuringMachine {get;set;}
        [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string,int> OutputBatch {get;set;}
    }
}
