using System.Collections.Generic;

namespace AOSmith.Models
{
    /// <summary>
    /// Response from Sage300 GetICStock API
    /// </summary>
    public class SageICStockResponse
    {
        public List<SageICStockItem> itemstock { get; set; }
        public int status { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Warnings { get; set; }
        public List<string> Messages { get; set; }
        public string dbmessages { get; set; }
    }

    public class SageICStockItem
    {
        public string itemno { get; set; }
        public string location { get; set; }
        public decimal qtavail { get; set; }
        public decimal qtonhand { get; set; }
        public decimal qtonorder { get; set; }
        public decimal qtsalordr { get; set; }
        public decimal qtycommit { get; set; }
    }
}
