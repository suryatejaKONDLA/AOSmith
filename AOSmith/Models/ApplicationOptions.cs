using System;

namespace AOSmith.Models
{
    public class ApplicationOptions
    {
        public int AppId { get; set; }
        public string AppDefaultLocation { get; set; }
        public string AppTranNumberPrefix { get; set; }
        public string AppAdjuNumberPrefix { get; set; }
        public string AppReveNumberPrefix { get; set; }
        public int? AppCreatedId { get; set; }
        public DateTime? AppCreatedDateTime { get; set; }
        public int? AppModifiedId { get; set; }
        public DateTime? AppModifiedDateTime { get; set; }
        public int? AppApprovedId { get; set; }
        public DateTime? AppApprovedDateTime { get; set; }
    }

    public class RowAmountThreshold
    {
        public int RowId { get; set; }
        public decimal RowAmount { get; set; }
        public int? RowCreatedId { get; set; }
        public DateTime? RowCreatedDate { get; set; }
        public int? RowModifiedId { get; set; }
        public DateTime? RowModifiedDate { get; set; }
    }
}
