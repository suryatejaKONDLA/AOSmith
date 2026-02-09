using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Web.Mvc;
using AOSmith.Filters;
using AOSmith.Helpers;
using AOSmith.Models;

namespace AOSmith.Controllers
{
    [AuthFilter]
    public class HomeController : Controller
    {
        private readonly IDatabaseHelper _dbHelper = new DatabaseHelper();

        public ActionResult Index()
        {
            ViewBag.Username = SessionHelper.GetUsername();
            ViewBag.UserName = SessionHelper.GetUserName();
            ViewBag.UserRole = SessionHelper.GetUserRole();

            try
            {
                string connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("SELECT GETDATE()", conn))
                    {
                        ViewBag.ServerDate = cmd.ExecuteScalar().ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Database Connection Error: " + ex.Message;
            }

            return View();
        }

        #region Manage Users

        public ActionResult ManageUsers()
        {
            // Only admin (Login_ID = 1) can access
            var userId = SessionHelper.GetUserId();
            if (userId != 1)
            {
                TempData["Error"] = "Access denied. Only administrators can access this page.";
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetAllUsers()
        {
            try
            {
                var userId = SessionHelper.GetUserId();
                if (userId != 1)
                {
                    return Json(new { success = false, message = "Access denied" }, JsonRequestBehavior.AllowGet);
                }

                const string sql = @"SELECT
                    lm.Login_ID AS LoginId,
                    lm.Login_User AS LoginUser,
                    RTRIM(lm.Login_Name) AS LoginName,
                    lm.Login_Password AS LoginPassword,
                    RTRIM(lm.Login_Designation) AS LoginDesignation,
                    RTRIM(lm.Login_Mobile_No) AS LoginMobileNo,
                    RTRIM(lm.Login_Email_ID) AS LoginEmailId,
                    lm.Login_DOB AS LoginDob,
                    RTRIM(lm.Login_Gender) AS LoginGender,
                    lm.Login_DEPT_Code AS LoginDeptCode,
                    ISNULL(dm.DEPT_Name, '') AS DepartmentName,
                    lm.Login_Is_Approver AS LoginIsApprover,
                    lm.Login_Approval_Level AS LoginApprovalLevel,
                    lm.Login_Active_Flag AS LoginActiveFlag,
                    lm.Login_Created_Date AS LoginCreatedDate,
                    lm.Login_Modified_Date AS LoginModifiedDate
                FROM Login_Master lm
                LEFT JOIN DEPT_Master dm ON lm.Login_DEPT_Code = dm.DEPT_Code
                ORDER BY lm.Login_ID";

                var users = await _dbHelper.QueryAsync<UserListItem>(sql);

                return Json(new { success = true, data = users }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetUserById(int id)
        {
            try
            {
                var userId = SessionHelper.GetUserId();
                if (userId != 1)
                {
                    return Json(new { success = false, message = "Access denied" }, JsonRequestBehavior.AllowGet);
                }

                const string sql = @"SELECT
                    Login_ID AS LoginId,
                    Login_User AS LoginUser,
                    RTRIM(Login_Name) AS LoginName,
                    Login_Password AS LoginPassword,
                    RTRIM(Login_Designation) AS LoginDesignation,
                    RTRIM(Login_Mobile_No) AS LoginMobileNo,
                    RTRIM(Login_Email_ID) AS LoginEmailId,
                    Login_DOB AS LoginDob,
                    RTRIM(Login_Gender) AS LoginGender,
                    Login_DEPT_Code AS LoginDeptCode,
                    Login_Is_Approver AS LoginIsApprover,
                    Login_Approval_Level AS LoginApprovalLevel,
                    Login_Active_Flag AS LoginActiveFlag
                FROM Login_Master
                WHERE Login_ID = @Id";

                var parameters = new Dictionary<string, object> { { "@Id", id } };
                var user = await _dbHelper.QuerySingleAsync<UserListItem>(sql, parameters);

                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" }, JsonRequestBehavior.AllowGet);
                }

                return Json(new { success = true, data = user }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetDepartments()
        {
            try
            {
                const string sql = "SELECT DEPT_Code AS DeptCode, RTRIM(DEPT_Name) AS DeptName FROM DEPT_Master ORDER BY DEPT_Order";
                var depts = await _dbHelper.QueryAsync<DepartmentItem>(sql);
                return Json(new { success = true, data = depts }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public async Task<JsonResult> SaveUser(UserSaveRequest model)
        {
            try
            {
                var userId = SessionHelper.GetUserId();
                if (userId != 1)
                {
                    return Json(new { success = false, message = "Access denied" });
                }

                var parameters = new List<SqlParameter>
                {
                    new SqlParameter("@Login_ID", SqlDbType.Int) { Value = model.LoginId },
                    new SqlParameter("@Login_User", SqlDbType.VarChar, 100) { Value = (object)model.LoginUser ?? DBNull.Value },
                    new SqlParameter("@Login_Name", SqlDbType.VarChar, 40) { Value = (object)model.LoginName ?? DBNull.Value },
                    new SqlParameter("@Login_Password", SqlDbType.VarChar, 40) { Value = (object)model.LoginPassword ?? DBNull.Value },
                    new SqlParameter("@Login_Designation", SqlDbType.VarChar, 40) { Value = (object)model.LoginDesignation ?? DBNull.Value },
                    new SqlParameter("@Login_Mobile_No", SqlDbType.VarChar, 15) { Value = (object)model.LoginMobileNo ?? DBNull.Value },
                    new SqlParameter("@Login_Email_ID", SqlDbType.VarChar, 100) { Value = (object)model.LoginEmailId ?? DBNull.Value },
                    new SqlParameter("@Login_DOB", SqlDbType.Date) { Value = (object)model.LoginDob ?? DBNull.Value },
                    new SqlParameter("@Login_Gender", SqlDbType.Char, 1) { Value = (object)model.LoginGender ?? DBNull.Value },
                    new SqlParameter("@Login_DEPT_Code", SqlDbType.Int) { Value = model.LoginDeptCode },
                    new SqlParameter("@Login_Is_Approver", SqlDbType.Bit) { Value = model.LoginIsApprover },
                    new SqlParameter("@Login_Approval_Level", SqlDbType.Int) { Value = model.LoginApprovalLevel },
                    new SqlParameter("@Login_Active_Flag", SqlDbType.Bit) { Value = model.LoginActiveFlag },
                    new SqlParameter("@Session_ID", SqlDbType.Int) { Value = userId }
                };

                var result = await _dbHelper.ExecuteStoredProcedureWithOutputsAsync("Login_Insert", parameters);

                return Json(new { success = result.IsSuccess, message = result.ResultMessage });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<JsonResult> DeleteUser(int id)
        {
            try
            {
                var userId = SessionHelper.GetUserId();
                if (userId != 1)
                {
                    return Json(new { success = false, message = "Access denied" });
                }

                var parameters = new List<SqlParameter>
                {
                    new SqlParameter("@Login_ID", SqlDbType.Int) { Value = id },
                    new SqlParameter("@Session_ID", SqlDbType.Int) { Value = userId }
                };

                var result = await _dbHelper.ExecuteStoredProcedureWithOutputsAsync("Login_Delete", parameters);

                return Json(new { success = result.IsSuccess, message = result.ResultMessage });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        #endregion
    }
}
