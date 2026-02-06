namespace AOSmith.Models
{
    public class LoginResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public UserSession UserSession { get; set; }

        public static LoginResponse Success(UserSession session)
        {
            return new LoginResponse
            {
                IsSuccess = true,
                Message = "Login successful",
                UserSession = session
            };
        }

        public static LoginResponse Failure(string message)
        {
            return new LoginResponse
            {
                IsSuccess = false,
                Message = message,
                UserSession = null
            };
        }
    }

    public class UserSession
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
        public string Email { get; set; }
        public string Department { get; set; }
        public bool IsApprover { get; set; }
        public int ApprovalLevel { get; set; }
    }
}
