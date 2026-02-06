using System;
using System.Collections.Generic;

namespace AOSmith.Models
{
    public class StockAdjustmentViewModel
    {
        // Header Information
        public int FinYear { get; set; }
        public int RecType { get; set; }
        public string RecTypeName { get; set; }
        public int RecNumber { get; set; }
        public DateTime TransactionDate { get; set; }

        // Line Items
        public List<StockAdjustment> LineItems { get; set; } = new List<StockAdjustment>();

        // Approval Information
        public List<StockAdjustmentApproval> Approvals { get; set; } = new List<StockAdjustmentApproval>();
        public int ApprovedCount { get; set; }
        public int PendingCount { get; set; }
        public int RejectedCount { get; set; }
        public int CurrentApprovalLevel { get; set; }
        public bool IsFullyApproved { get; set; }

        // File Attachments
        public List<StockAdjustmentFile> Files { get; set; } = new List<StockAdjustmentFile>();
        public int FilesUploadedCount { get; set; }
        public bool AllFilesUploaded { get; set; }

        // Sage Integration Status
        public bool SageDataSent { get; set; }
        public DateTime? SageSentDate { get; set; }

        // Audit Information
        public string CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}
