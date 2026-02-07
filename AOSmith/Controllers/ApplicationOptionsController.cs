using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using AOSmith.Filters;
using AOSmith.Helpers;
using AOSmith.Models;
using AOSmith.Services;

namespace AOSmith.Controllers
{
    [AuthFilter]
    public class ApplicationOptionsController : Controller
    {
        private readonly IDatabaseHelper _dbHelper = new DatabaseHelper();
        private readonly SageApiService _sageService = new SageApiService();

        public async Task<ActionResult> Index()
        {
            // Check if user is admin (Login_ID = 1)
            var userId = SessionHelper.GetUserId();
            if (userId != 1)
            {
                TempData["Error"] = "Access denied. Only administrators can access this page.";
                return RedirectToAction("Index", "Home");
            }

            // Load existing options (single record)
            var options = await LoadApplicationOptions();

            // Load locations for dropdown
            await LoadLocationsDropdown();

            return View(options);
        }

        private async Task<ApplicationOptions> LoadApplicationOptions()
        {
            const string sql = @"SELECT TOP 1
                            APP_ID as AppId,
                            RTRIM(APP_Default_Location) as AppDefaultLocation,
                            APP_Tran_Doc_Series as AppTranDocSeries,
                            APP_Created_ID as AppCreatedId,
                            APP_Created_DateTime as AppCreatedDateTime,
                            APP_Modified_ID as AppModifiedId,
                            APP_Modified_DateTime as AppModifiedDateTime,
                            APP_Approved_ID as AppApprovedId,
                            APP_Approved_DateTime as AppApprovedDateTime
                        FROM APP_Options
                        ORDER BY APP_ID";

            var result = await _dbHelper.QueryAsync<ApplicationOptions>(sql);
            return result.FirstOrDefault() ?? new ApplicationOptions();
        }

        private async Task LoadLocationsDropdown()
        {
            try
            {
                var response = await _sageService.GetLocationsAsync();
                if (response?.locations != null)
                {
                    var locations = response.locations.Select(l => new LocationMaster
                    {
                        LOCATION = l.location?.Trim(),
                        DESC = l.desc?.Trim()
                    }).OrderBy(l => l.DESC).ToList();

                    ViewBag.Locations = new SelectList(locations, "LOCATION", "DESC");
                    return;
                }
            }
            catch { }

            // Fallback: empty list
            ViewBag.Locations = new SelectList(new List<LocationMaster>(), "LOCATION", "DESC");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> Save(ApplicationOptions model)
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
                    new SqlParameter("@APP_ID", SqlDbType.Int) { Value = model.AppId },
                    new SqlParameter("@APP_Default_Location", SqlDbType.Char, 6) { Value = (object)model.AppDefaultLocation ?? DBNull.Value },
                    new SqlParameter("@APP_Tran_Doc_Series", SqlDbType.VarChar, 10) { Value = (object)model.AppTranDocSeries ?? DBNull.Value },
                    new SqlParameter("@Session_ID", SqlDbType.Int) { Value = userId }
                };

                var result = await _dbHelper.ExecuteStoredProcedureWithOutputsAsync("APP_Options_Insert", parameters);

                if (result.IsSuccess)
                {
                    return Json(new { success = true, message = result.ResultMessage });
                }
                else
                {
                    return Json(new { success = false, message = result.ResultMessage });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
