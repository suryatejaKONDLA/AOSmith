namespace AOSmith.Models
{
    public class StockAdjustmentLineItem
    {
        public int StockRecSno { get; set; }
        public int RecType { get; set; }
        public string FromLocation { get; set; }
        public string ToLocation { get; set; }
        public string ItemCode { get; set; }
        public string ItemDescription { get; set; }
        public decimal Qty { get; set; }
        public decimal Cost { get; set; }
        public string GLCode { get; set; }
    }
}
