namespace AOSmith.Models
{
    public class FileTypeMaster
    {
        public int FileTypeId { get; set; }
        public string FileTypeName { get; set; }
        public bool FileTypeRequired { get; set; }
        public int? FileTypeMaxSizeMb { get; set; }
    }
}
