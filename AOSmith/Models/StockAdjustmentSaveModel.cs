using System.Collections.Generic;

namespace AOSmith.Models
{
    public class StockAdjustmentSaveModel
    {
        public System.DateTime TransactionDate { get; set; }
        public List<StockAdjustmentLineItem> LineItems { get; set; }
    }
}
