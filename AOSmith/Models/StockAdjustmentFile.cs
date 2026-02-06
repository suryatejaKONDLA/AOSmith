using System;

namespace AOSmith.Models
{
    public class StockAdjustmentFile
    {
        // Composite Primary Key / Foreign Key
        public int FileFinYear { get; set; }
        public int FileRecType { get; set; }
        public int FileRecNumber { get; set; }
        public int FileType { get; set; }

        // File Details
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public decimal? FileSizeKb { get; set; }
        public string FileExtension { get; set; }

        // Audit Fields
        public int? FileUploadedBy { get; set; }
        public DateTime? FileUploadedDate { get; set; }
    }
}
