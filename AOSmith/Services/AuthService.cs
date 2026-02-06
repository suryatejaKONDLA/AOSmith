using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using AOSmith.Helpers;
using AOSmith.Models;

namespace AOSmith.Services
{
    public class AuthService
    {
        private readonly IDatabaseHelper _dbHelper;

        public AuthService(IDatabaseHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        public AuthService()
        {
            _dbHelper = new DatabaseHelper();
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest loginRequest)
        {
            // Call Login_Check stored procedure
            var dbResult = await ValidateLoginAsync(loginRequest.Username, loginRequest.Password);

            if (!dbResult.IsSuccess)
            {
                return LoginResponse.Failure(dbResult.ResultMessage);
            }

            // Fetch user details after successful login
            var user = await GetUserByUsernameAsync(loginRequest.Username);

            if (user == null)
            {
                return LoginResponse.Failure("User not found.");
            }

            // Create user session
            var userSession = new UserSession
            {
                UserId = user.LoginId,
                Username = user.LoginUser,
                Name = user.LoginName,
                Role = user.LoginDesignation,
                Email = user.LoginEmailId,
                IsApprover = user.LoginIsApprover,
                ApprovalLevel = user.LoginApprovalLevel ?? 0
            };

            return LoginResponse.Success(userSession);
        }

        public async Task<DbReturnResult> ValidateLoginAsync(string username, string password)
        {
            var parameters = new System.Collections.Generic.List<SqlParameter>
            {
                new SqlParameter("@Login_User", SqlDbType.VarChar, 100) { Value = username },
                new SqlParameter("@Login_Password", SqlDbType.VarChar, 40) { Value = password }
            };

            return await _dbHelper.ExecuteStoredProcedureWithOutputsAsync("Login_Check", parameters);
        }

        public async Task<User> GetUserByUsernameAsync(string username)
        {
            var parameters = new System.Collections.Generic.Dictionary<string, object>
            {
                { "@Username", username }
            };

            return await _dbHelper.QuerySingleAsync<User>(
                "SELECT * FROM Login_Master WHERE Login_User = @Username",
                parameters
            );
        }

        public async Task<User> GetUserByIdAsync(int userId)
        {
            var parameters = new System.Collections.Generic.Dictionary<string, object>
            {
                { "@UserId", userId }
            };

            return await _dbHelper.QuerySingleAsync<User>(
                "SELECT * FROM Login_Master WHERE Login_ID = @UserId",
                parameters
            );
        }
    }
}
