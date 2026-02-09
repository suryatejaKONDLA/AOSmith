using System;

namespace AOSmith.Models
{
    /// <summary>
    /// Model for listing users in the grid
    /// </summary>
    public class UserListItem
    {
        public int LoginId { get; set; }
        public string LoginUser { get; set; }
        public string LoginName { get; set; }
        public string LoginPassword { get; set; }
        public string LoginDesignation { get; set; }
        public string LoginMobileNo { get; set; }
        public string LoginEmailId { get; set; }
        public DateTime? LoginDob { get; set; }
        public string LoginGender { get; set; }
        public int LoginDeptCode { get; set; }
        public string DepartmentName { get; set; }
        public bool LoginIsApprover { get; set; }
        public int LoginApprovalLevel { get; set; }
        public bool LoginActiveFlag { get; set; }
        public DateTime? LoginCreatedDate { get; set; }
        public DateTime? LoginModifiedDate { get; set; }
    }

    /// <summary>
    /// Model for saving a user (Add/Edit)
    /// </summary>
    public class UserSaveRequest
    {
        public int LoginId { get; set; }
        public string LoginUser { get; set; }
        public string LoginName { get; set; }
        public string LoginPassword { get; set; }
        public string LoginDesignation { get; set; }
        public string LoginMobileNo { get; set; }
        public string LoginEmailId { get; set; }
        public DateTime? LoginDob { get; set; }
        public string LoginGender { get; set; }
        public int LoginDeptCode { get; set; }
        public bool LoginIsApprover { get; set; }
        public int LoginApprovalLevel { get; set; }
        public bool LoginActiveFlag { get; set; }
    }

    /// <summary>
    /// Department dropdown item
    /// </summary>
    public class DepartmentItem
    {
        public int DeptCode { get; set; }
        public string DeptName { get; set; }
    }
}
