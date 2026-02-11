using System.Web;
using AOSmith.Models;

namespace AOSmith.Helpers
{
    public static class SessionHelper
    {
        private const string UserIdKey = "UserID";
        private const string UsernameKey = "Username";
        private const string NameKey = "Name";
        private const string RoleKey = "Role";
        private const string EmailKey = "Email";
        private const string IsApproverKey = "IsApprover";
        private const string ApprovalLevelKey = "ApprovalLevel";
        private const string CompanyNameKey = "CompanyName";

        public static void SetUserSession(UserSession userSession)
        {
            HttpContext.Current.Session[UserIdKey] = userSession.UserId;
            HttpContext.Current.Session[UsernameKey] = userSession.Username;
            HttpContext.Current.Session[NameKey] = userSession.Name;
            HttpContext.Current.Session[RoleKey] = userSession.Role;
            HttpContext.Current.Session[EmailKey] = userSession.Email;
            HttpContext.Current.Session[IsApproverKey] = userSession.IsApprover;
            HttpContext.Current.Session[ApprovalLevelKey] = userSession.ApprovalLevel;
            HttpContext.Current.Session[CompanyNameKey] = userSession.CompanyName;
        }

        public static UserSession GetUserSession()
        {
            if (HttpContext.Current.Session[UserIdKey] == null)
                return null;

            return new UserSession
            {
                UserId = (int)HttpContext.Current.Session[UserIdKey],
                Username = HttpContext.Current.Session[UsernameKey]?.ToString(),
                Name = HttpContext.Current.Session[NameKey]?.ToString(),
                Role = HttpContext.Current.Session[RoleKey]?.ToString(),
                Email = HttpContext.Current.Session[EmailKey]?.ToString(),
                IsApprover = HttpContext.Current.Session[IsApproverKey] as bool? ?? false,
                ApprovalLevel = HttpContext.Current.Session[ApprovalLevelKey] as int? ?? 0,
                CompanyName = HttpContext.Current.Session[CompanyNameKey]?.ToString()
            };
        }

        public static bool IsUserLoggedIn()
        {
            return HttpContext.Current.Session[UserIdKey] != null;
        }

        public static int? GetUserId()
        {
            return HttpContext.Current.Session[UserIdKey] as int?;
        }

        public static string GetUsername()
        {
            return HttpContext.Current.Session[UsernameKey]?.ToString();
        }

        public static string GetUserName()
        {
            return HttpContext.Current.Session[NameKey]?.ToString();
        }

        public static string GetUserRole()
        {
            return HttpContext.Current.Session[RoleKey]?.ToString();
        }

        public static bool IsApprover()
        {
            return HttpContext.Current.Session[IsApproverKey] as bool? ?? false;
        }

        public static int GetApprovalLevel()
        {
            return HttpContext.Current.Session[ApprovalLevelKey] as int? ?? 0;
        }

        public static string GetCompanyName()
        {
            return HttpContext.Current.Session[CompanyNameKey]?.ToString();
        }

        public static void ClearSession()
        {
            HttpContext.Current.Session.Clear();
            HttpContext.Current.Session.Abandon();
        }
    }
}
