namespace AOSmith.Models
{
    /// <summary>
    /// Model for the GetApprovalLevels endpoint (approvers from Login_Master)
    /// </summary>
    public class ApproverLevel
    {
        public int ApprovalLevel { get; set; }
        public string ApproverName { get; set; }
        public int LoginId { get; set; }
    }

    /// <summary>
    /// Model for displaying threshold data (includes approver name from JOIN)
    /// </summary>
    public class ApprovalAmountThreshold
    {
        public int ThresholdId { get; set; }
        public int ThresholdLevel { get; set; }
        public decimal ThresholdMinAmount { get; set; }
        public decimal ThresholdMaxAmount { get; set; }

        // Display-only: current approver at this level (from Login_Master JOIN)
        public string ApproverName { get; set; }
    }

    /// <summary>
    /// Line item for saving thresholds via TVP (level + amounts only)
    /// </summary>
    public class ApprovalAmountThresholdLineItem
    {
        public int ThresholdLevel { get; set; }
        public decimal ThresholdMinAmount { get; set; }
        public decimal ThresholdMaxAmount { get; set; }
    }
}
