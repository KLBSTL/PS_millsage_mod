using System;

namespace ResourceStoryMod.Story
{
    // Advisory arithmetic only; no cash lock, grant or native price override.
    public static class NormalBusinessBudget
    {
        public const int Turnover=80,PlanningDailyCost=5;
        public static int Reserve(int rent,int days=7,int rentCycles=1)
        {
            if(rent<0||days<0||rentCycles<0)throw new ArgumentOutOfRangeException();
            return checked(rent*rentCycles+days*PlanningDailyCost+Turnover);
        }
        public static int Spendable(int cash,int rent)=>Math.Max(0,checked(cash-Reserve(rent)));
        public static int OpeningTarget(int rent)=>checked(480+30+Reserve(rent,14,2));
        public static int GrowthTarget(int rent)=>checked(1800+110+Reserve(rent,14,2));
    }
}
