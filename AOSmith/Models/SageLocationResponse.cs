using System.Collections.Generic;

namespace AOSmith.Models
{
    /// <summary>
    /// Response from Sage300 ICLOCATION API
    /// </summary>
    public class SageLocationResponse
    {
        public List<SageLocation> locations { get; set; }
        public int status { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Warnings { get; set; }
        public List<string> Messages { get; set; }
        public string dbMessages { get; set; }
    }

    public class SageLocation
    {
        public string location { get; set; }
        public string desc { get; set; }
        public string address1 { get; set; }
        public string address2 { get; set; }
        public string address3 { get; set; }
        public string address4 { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string zip { get; set; }
        public string country { get; set; }
        public string phone { get; set; }
        public string fax { get; set; }
        public string contact { get; set; }
    }
}
