using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Dapper;

namespace AOSmith
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            // Dapper: Auto-map Login_Name -> LoginName, Login_ID -> LoginId, etc.
            DefaultTypeMap.MatchNamesWithUnderscores = true;
        }
    }
}
