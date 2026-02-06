using System.Web.Mvc;
using AOSmith.Helpers;

namespace AOSmith.Filters
{
    public class AuthFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            // Skip if [AllowAnonymous] is present on the action or controller
            if (filterContext.ActionDescriptor.IsDefined(typeof(AllowAnonymousAttribute), true) ||
                filterContext.ActionDescriptor.ControllerDescriptor.IsDefined(typeof(AllowAnonymousAttribute), true))
            {
                base.OnActionExecuting(filterContext);
                return;
            }

            // Check if user is logged in
            if (!SessionHelper.IsUserLoggedIn())
            {
                filterContext.Result = new RedirectToRouteResult(
                    new System.Web.Routing.RouteValueDictionary
                    {
                        { "controller", "Account" },
                        { "action", "Login" }
                    });
                return;
            }

            // Add no-cache headers to prevent back-button access after logout
            var response = filterContext.HttpContext.Response;
            response.Cache.SetExpires(System.DateTime.UtcNow.AddMinutes(-1));
            response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            response.Cache.SetNoStore();

            base.OnActionExecuting(filterContext);
        }
    }
}
