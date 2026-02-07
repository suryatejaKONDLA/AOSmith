using System.Collections.Generic;

namespace AOSmith.Models
{
    /// <summary>
    /// Response from Sage300 ICITEM API
    /// </summary>
    public class SageItemResponse
    {
        public List<SageItem> icitems { get; set; }
        public int status { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Warnings { get; set; }
        public List<string> Messages { get; set; }
        public string dbMessages { get; set; }
    }

    public class SageItem
    {
        public string itemno { get; set; }
        public string desc { get; set; }
        public string stockunit { get; set; }
        public decimal stdcost { get; set; }
        public bool inactive { get; set; }
        public string datelastmn { get; set; }
        public string fmtitemno { get; set; }
        public string category { get; set; }
        public bool stockitem { get; set; }
        public string comment1 { get; set; }
        public string comment2 { get; set; }
        public string comment3 { get; set; }
        public string comment4 { get; set; }
    }
}
