using System;

namespace AOSmith.Models
{
    public class ApprovalViewModel
    {
        public int Id { get; set; }
        public string DocumentReference { get; set; }
        public DateTime Date { get; set; }
        public string Department { get; set; }
        public string ItemName { get; set; }
        public string FromLocation { get; set; }
        public string ToLocation { get; set; }
        public decimal Quantity { get; set; }
        public string Reason { get; set; }
        public int ApprovalLevel { get; set; }
        public int ApprovalStatus { get; set; }
        public string ApprovalStatusName { get; set; }

        // For composite key
        public int FinYear { get; set; }
        public int RecType { get; set; }
        public int RecNumber { get; set; }
    }
}
