using System;

namespace AOSmith.Models
{
    /// <summary>
    /// Model for the last record details per REC type
    /// </summary>
    public class LastRecordNumber
    {
        public int REC_Type { get; set; }
        public string REC_Name { get; set; }
        public string REC_Name2 { get; set; }
        public int LastRecNumber { get; set; }
        public DateTime? LastDate { get; set; }
        public DateTime? LastTime { get; set; }
    }
}
