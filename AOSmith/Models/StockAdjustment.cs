using System;

namespace AOSmith.Models
{
    public class StockAdjustment
    {
        // Composite Primary Key
        public int StockFinYear { get; set; }
        public int StockRecType { get; set; }
        public int StockRecNumber { get; set; }
        public int StockRecSno { get; set; }

        // Transaction Fields
        public DateTime StockDate { get; set; }
        public string StockFromLocation { get; set; }
        public string StockToLocation { get; set; }
        public string StockItemCode { get; set; }
        public decimal StockQty { get; set; }
        public decimal StockCost { get; set; }

        // Audit Fields
        public int StockCreatedId { get; set; }
        public DateTime StockCreatedDate { get; set; }
        public int? StockModifiedId { get; set; }
        public DateTime? StockModifiedDate { get; set; }

        // Sage Integration Fields
        public bool StockSageDataSent { get; set; }
        public DateTime? StockSageSentDate { get; set; }
    }
}
