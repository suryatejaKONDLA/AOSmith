namespace AOSmith.Models
{
    public class User
    {
        public int LoginId { get; set; }
        public string LoginUser { get; set; }
        public string LoginName { get; set; }
        public string LoginPassword { get; set; }
        public string LoginDesignation { get; set; }
        public string LoginMobileNo { get; set; }
        public string LoginEmailId { get; set; }
        public System.DateTime? LoginDob { get; set; }
        public string LoginGender { get; set; }
        public int LoginDeptCode { get; set; }
        public System.DateTime? LoginLastLoginDate { get; set; }
        public System.DateTime? LoginLastLoginDate2 { get; set; }
        public System.DateTime? LoginLastPasswordChangeDate { get; set; }
        public bool LoginEmailVerifiedFlag { get; set; }
        public bool LoginIsApprover { get; set; }
        public int? LoginApprovalLevel { get; set; }
        public bool LoginActiveFlag { get; set; }
        public int LoginCreatedId { get; set; }
        public System.DateTime LoginCreatedDate { get; set; }
        public int? LoginModifiedId { get; set; }
        public System.DateTime? LoginModifiedDate { get; set; }
        public int? LoginApprovedId { get; set; }
        public System.DateTime? LoginApprovedDate { get; set; }
    }
}
