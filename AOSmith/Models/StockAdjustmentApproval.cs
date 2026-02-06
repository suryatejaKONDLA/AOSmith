using System;

namespace AOSmith.Models
{
    public class StockAdjustmentApproval
    {
        // Composite Primary Key / Foreign Key
        public int ApprovalFinYear { get; set; }
        public int ApprovalRecType { get; set; }
        public int ApprovalRecNumber { get; set; }
        public int ApprovalLevel { get; set; }

        // Approval Details
        public int ApprovalStatus { get; set; }
        public int? ApprovalUserId { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public string ApprovalComments { get; set; }

        // Audit Fields
        public DateTime ApprovalCreatedDate { get; set; }
    }
}
