using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Web.Mvc;
using AOSmith.Helpers;

namespace AOSmith.Controllers
{
    public class HomeController : Controller
    {
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

        public ActionResult ManageUsers()
        {
            ViewBag.Message = "Manage Users Page";
            return View();
        }
    }
}
