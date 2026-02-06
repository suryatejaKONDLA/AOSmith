namespace AOSmith.Models
{
    /// <summary>
    /// Constants for Approval Status codes
    /// </summary>
    public static class ApprovalStatusConstants
    {
        public const int Pending = 1;
        public const int Approved = 2;
        public const int Rejected = 3;

        public static string GetStatusName(int statusCode)
        {
            switch (statusCode)
            {
                case Pending:
                    return "Pending Approval";
                case Approved:
                    return "Approved";
                case Rejected:
                    return "Rejected";
                default:
                    return "Unknown";
            }
        }
    }
}
