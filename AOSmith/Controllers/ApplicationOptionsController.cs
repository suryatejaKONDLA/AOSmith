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
using Newtonsoft.Json;

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
                var companyName = SessionHelper.GetCompanyName();
                var response = await _sageService.GetLocationsAsync(companyName);
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

        // ==================== LAST RECORD NUMBERS ====================

        /// <summary>
        /// Get last record number, date, time per REC type
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> GetLastRecordNumbers()
        {
            try
            {
                const string sql = "EXEC LastRecordNumber_Select";
                var records = (await _dbHelper.QueryAsync<LastRecordNumber>(sql)).ToList();
                return Json(new { success = true, data = records }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // ==================== APPROVAL AMOUNT THRESHOLD ====================

        /// <summary>
        /// Get all approval levels with the current approver name at each level
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> GetApprovalLevels()
        {
            try
            {
                const string sql = @"SELECT Login_Approval_Level AS ApprovalLevel,
                                            Login_Name AS ApproverName,
                                            Login_ID AS LoginId
                                     FROM Login_Master
                                     WHERE Login_Is_Approver = 1 AND Login_Active_Flag = 1
                                     ORDER BY Login_Approval_Level";

                var approvers = (await _dbHelper.QueryAsync<ApproverLevel>(sql)).ToList();

                return Json(new { success = true, data = approvers }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Get existing saved thresholds (joined with current approver names)
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> GetThresholds()
        {
            try
            {
                const string sql = @"SELECT t.Threshold_ID AS ThresholdId,
                                            t.Threshold_Level AS ThresholdLevel,
                                            t.Threshold_Min_Amount AS ThresholdMinAmount,
                                            t.Threshold_Max_Amount AS ThresholdMaxAmount,
                                            ISNULL(lm.Login_Name, '') AS ApproverName
                                     FROM Approval_Amount_Threshold t
                                     LEFT JOIN Login_Master lm ON lm.Login_Approval_Level = t.Threshold_Level
                                                                  AND lm.Login_Is_Approver = 1
                                     ORDER BY t.Threshold_Level";

                var thresholds = (await _dbHelper.QueryAsync<ApprovalAmountThreshold>(sql)).ToList();

                return Json(new { success = true, data = thresholds }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Save approval amount thresholds via MERGE SP
        /// </summary>
        [HttpPost]
        public async Task<JsonResult> SaveThresholds(string thresholdsJson)
        {
            try
            {
                var userId = SessionHelper.GetUserId();
                if (userId != 1)
                {
                    return Json(new { success = false, message = "Access denied" });
                }

                var lineItems = JsonConvert.DeserializeObject<List<ApprovalAmountThresholdLineItem>>(thresholdsJson);

                if (lineItems == null || !lineItems.Any())
                {
                    return Json(new { success = false, message = "No threshold data provided." });
                }

                // Build DataTable for TVP
                var table = new DataTable();
                table.Columns.Add("ThresholdLevel", typeof(int));
                table.Columns.Add("ThresholdMinAmount", typeof(decimal));
                table.Columns.Add("ThresholdMaxAmount", typeof(decimal));

                foreach (var item in lineItems)
                {
                    table.Rows.Add(item.ThresholdLevel, item.ThresholdMinAmount, item.ThresholdMaxAmount);
                }

                var parameters = new List<SqlParameter>
                {
                    new SqlParameter("@ThresholdDetails", SqlDbType.Structured)
                    {
                        TypeName = "dbo.ApprovalAmountThreshold_TBType",
                        Value = table
                    },
                    new SqlParameter("@Session_ID", SqlDbType.Int) { Value = userId }
                };

                var result = await _dbHelper.ExecuteStoredProcedureWithOutputsAsync("ApprovalAmountThreshold_Insert", parameters);

                return Json(new { success = result.IsSuccess, message = result.ResultMessage });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
