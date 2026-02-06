using System.Threading.Tasks;
using System.Web.Mvc;
using AOSmith.Helpers;
using AOSmith.Models;
using AOSmith.Services;

namespace AOSmith.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly AuthService _authService = new AuthService();

        // GET: Account/Login
        public ActionResult Login()
        {
            // Redirect to home if already logged in
            if (SessionHelper.IsUserLoggedIn())
            {
                return RedirectToAction("Index", "Home");
            }

            // TempData will automatically be available in the view
            return View();
        }

        // POST: Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginRequest model)
        {
            if (!ModelState.IsValid)
            {
                // Store error in TempData and redirect to avoid form resubmission
                TempData["Error"] = "Please enter both username and password.";
                return RedirectToAction("Login");
            }

            try
            {
                // Call AuthService with LoginRequest model
                var result = await _authService.LoginAsync(model);

                if (result.IsSuccess && result.UserSession != null)
                {
                    // Set Session using SessionHelper
                    SessionHelper.SetUserSession(result.UserSession);

                    TempData["Success"] = "Login successful! Welcome " + result.UserSession.Name;
                    return RedirectToAction("Index", "Home");
                }

                // Login failed - use TempData and redirect (PRG pattern)
                TempData["Error"] = result.Message ?? "Invalid username or password.";
                return RedirectToAction("Login");
            }
            catch (System.Exception ex)
            {
                // Use TempData and redirect (PRG pattern)
                TempData["Error"] = "Login Error: " + ex.Message;
                return RedirectToAction("Login");
            }
        }

        public ActionResult Logout()
        {
            SessionHelper.ClearSession();

            // Clear authentication cookie
            if (Request.Cookies["ASP.NET_SessionId"] != null)
            {
                Response.Cookies["ASP.NET_SessionId"].Expires = System.DateTime.Now.AddDays(-1);
            }

            // Prevent back-button showing cached pages
            Response.Cache.SetExpires(System.DateTime.UtcNow.AddMinutes(-1));
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            Response.Cache.SetNoStore();

            return RedirectToAction("Login");
        }
    }
}
