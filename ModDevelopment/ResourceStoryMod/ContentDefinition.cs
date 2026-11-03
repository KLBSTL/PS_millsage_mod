namespace ResourceStoryMod
{
    public sealed class ContentDefinition
    {
        public string CustomerId { get; set; } = "rs_test_trader";
        public string CustomerName { get; set; } = "试作行商·林舟";
        public string ItemId { get; set; } = "rs_water_sample";
        public string ItemName { get; set; } = "桶装饮用水样品";
        public int UnitPrice { get; set; } = 40;
        public string Intro { get; set; } = "我正在为附近街区寻找稳定的饮用水货源。先做一笔小额试单，看看这条补给线能不能顺利运转。";
        public string Offer { get; set; } = "一份饮用水样品，报价四十元。你愿意先交付一份吗？";
        public string Accepted { get; set; } = "把样品放在交易柜台上，按正常流程结算就好。我会记下这次交付。";
        public string Declined { get; set; } = "没关系，你可以再考虑一下。这份试单暂时仍然有效。";
        public string Completed { get; set; } = "样品收到了。这是第一步；等检测结果出来，我们再谈下一次补给。";
    }
}
