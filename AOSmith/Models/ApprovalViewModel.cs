using System;
using System.Collections.Generic;

namespace AOSmith.Models
{
    /// <summary>
    /// Represents a single stock adjustment document with all its approval level info
    /// </summary>
    public class ApprovalDocumentViewModel
    {
        public int FinYear { get; set; }
        public int RecType { get; set; }
        public int RecNumber { get; set; }
        public string DocumentReference { get; set; }
        public DateTime Date { get; set; }
        public string CreatedBy { get; set; }
        public string Department { get; set; }
        public int TotalLevels { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public int NextPendingLevel { get; set; } // 0 = fully approved, -1 = rejected
        public bool CanApprove { get; set; }
        public List<ApprovalLevelInfo> Levels { get; set; }
        public List<ApprovalLineItem> LineItems { get; set; }
    }

    /// <summary>
    /// Approval status for a single level of a document
    /// </summary>
    public class ApprovalLevelInfo
    {
        public int FinYear { get; set; }
        public int RecType { get; set; }
        public int RecNumber { get; set; }
        public int Level { get; set; }
        public int StatusCode { get; set; }
        public string StatusName { get; set; }
        public int? ApproverUserId { get; set; }
        public string ApproverName { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public string Comments { get; set; }
    }

    /// <summary>
    /// Line item detail for a stock adjustment document
    /// </summary>
    public class ApprovalLineItem
    {
        public int FinYear { get; set; }
        public int RecType { get; set; }
        public int RecNumber { get; set; }
        public int Sno { get; set; }
        public string ItemCode { get; set; }
        public string ItemDesc { get; set; }
        public string FromLocation { get; set; }
        public string FromLocationName { get; set; }
        public string ToLocation { get; set; }
        public string ToLocationName { get; set; }
        public decimal Quantity { get; set; }
        public decimal Cost { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// Flat row for document-level query (before grouping in C#)
    /// </summary>
    public class ApprovalDocumentRow
    {
        public int FinYear { get; set; }
        public int RecType { get; set; }
        public int RecNumber { get; set; }
        public string DocumentReference { get; set; }
        public DateTime Date { get; set; }
        public string CreatedBy { get; set; }
        public string Department { get; set; }
        public int TotalLevels { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public int NextPendingLevel { get; set; }
        public string RecTypeName { get; set; }
    }
}
