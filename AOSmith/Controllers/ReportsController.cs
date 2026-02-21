using System;
using System.Collections.Generic;
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
    public class ReportsController : Controller
    {
        private readonly IDatabaseHelper _dbHelper;
        private readonly SageApiService _sageService;

        public ReportsController()
        {
            _dbHelper = new DatabaseHelper();
            _sageService = new SageApiService();
        }

        // GET: Reports
        public ActionResult Index()
        {
            return View();
        }

        // GET: Reports/GetReportData
        [HttpGet]
        public async Task<JsonResult> GetReportData(string fromDate, string toDate)
        {
            try
            {
                var userId = SessionHelper.GetUserId();
                var companyName = SessionHelper.GetCompanyName();

                if (!userId.HasValue)
                {
                    return Json(new { success = false, message = "User session expired. Please login again." }, JsonRequestBehavior.AllowGet);
                }

                DateTime dtFrom, dtTo;
                if (!DateTime.TryParse(fromDate, out dtFrom) || !DateTime.TryParse(toDate, out dtTo))
                {
                    return Json(new { success = false, message = "Please provide valid From and To dates." }, JsonRequestBehavior.AllowGet);
                }

                // Ensure from <= to
                if (dtFrom > dtTo)
                {
                    return Json(new { success = false, message = "From Date cannot be after To Date." }, JsonRequestBehavior.AllowGet);
                }

                var isApprover = SessionHelper.IsApprover();
                var userApprovalLevel = SessionHelper.GetApprovalLevel();

                var queryParams = new Dictionary<string, object>
                {
                    { "@CompanyName", companyName },
                    { "@FromDate", dtFrom },
                    { "@ToDate", dtTo }
                };

                // For approvers: show records up to their approval level
                // For non-approvers: show only records they created
                string userFilter;
                if (isApprover && userApprovalLevel > 0)
                {
                    userFilter = @"AND EXISTS (
                        SELECT 1 FROM Stock_Adjustment_Approval al
                        WHERE al.Approval_Company_Name = sa.Stock_Company_Name
                        AND al.Approval_FIN_Year = sa.Stock_FIN_Year
                        AND al.Approval_REC_Type = sa.Stock_REC_Type
                        AND al.Approval_REC_Number = sa.Stock_REC_Number
                        AND al.Approval_Level <= @UserLevel
                    )";
                    queryParams.Add("@UserLevel", userApprovalLevel);
                }
                else
                {
                    userFilter = "AND sa.Stock_Created_ID = @CreatedBy";
                    queryParams.Add("@CreatedBy", userId.Value);
                }

                // Get documents within date range (RecType 10, 12)
                var documentsQuery = @"
                    SELECT DISTINCT
                        sa.Stock_FIN_Year AS FinYear,
                        sa.Stock_REC_Type AS RecType,
                        sa.Stock_REC_Number AS RecNumber,
                        CAST(sa.Stock_FIN_Year AS VARCHAR(10)) + '/' + sa.Stock_Company_Name + '/' + rm.REC_Name + '/' + CAST(sa.Stock_REC_Number AS VARCHAR(10)) AS DocumentReference,
                        sa.Stock_Date AS Date,
                        lm.Login_Name AS CreatedBy,
                        ISNULL(dm.DEPT_Name, '') AS Department,
                        rm.REC_Name2 AS RecTypeName,
                        (SELECT COUNT(*) FROM Stock_Adjustment_Approval a
                         WHERE a.Approval_Company_Name = sa.Stock_Company_Name
                         AND a.Approval_FIN_Year = sa.Stock_FIN_Year
                         AND a.Approval_REC_Type = sa.Stock_REC_Type
                         AND a.Approval_REC_Number = sa.Stock_REC_Number) AS TotalLevels,
                        (SELECT COUNT(*) FROM Stock_Adjustment_Approval a
                         WHERE a.Approval_Company_Name = sa.Stock_Company_Name
                         AND a.Approval_FIN_Year = sa.Stock_FIN_Year
                         AND a.Approval_REC_Type = sa.Stock_REC_Type
                         AND a.Approval_REC_Number = sa.Stock_REC_Number
                         AND a.Approval_Status = 2) AS ApprovedCount,
                        (SELECT COUNT(*) FROM Stock_Adjustment_Approval a
                         WHERE a.Approval_Company_Name = sa.Stock_Company_Name
                         AND a.Approval_FIN_Year = sa.Stock_FIN_Year
                         AND a.Approval_REC_Type = sa.Stock_REC_Type
                         AND a.Approval_REC_Number = sa.Stock_REC_Number
                         AND a.Approval_Status = 3) AS RejectedCount,
                        CASE
                            WHEN EXISTS (SELECT 1 FROM Stock_Adjustment_Approval a
                                WHERE a.Approval_Company_Name = sa.Stock_Company_Name
                                AND a.Approval_FIN_Year = sa.Stock_FIN_Year
                                AND a.Approval_REC_Type = sa.Stock_REC_Type
                                AND a.Approval_REC_Number = sa.Stock_REC_Number
                                AND a.Approval_Status = 3) THEN -1
                            ELSE
                                ISNULL((SELECT MIN(a.Approval_Level)
                                FROM Stock_Adjustment_Approval a
                                WHERE a.Approval_Company_Name = sa.Stock_Company_Name
                                AND a.Approval_FIN_Year = sa.Stock_FIN_Year
                                AND a.Approval_REC_Type = sa.Stock_REC_Type
                                AND a.Approval_REC_Number = sa.Stock_REC_Number
                                AND a.Approval_Status = 1
                                AND NOT EXISTS (
                                    SELECT 1 FROM Stock_Adjustment_Approval prev
                                    WHERE prev.Approval_Company_Name = a.Approval_Company_Name
                                    AND prev.Approval_FIN_Year = a.Approval_FIN_Year
                                    AND prev.Approval_REC_Type = a.Approval_REC_Type
                                    AND prev.Approval_REC_Number = a.Approval_REC_Number
                                    AND prev.Approval_Level < a.Approval_Level
                                    AND prev.Approval_Status != 2
                                )), 0)
                        END AS NextPendingLevel
                    FROM Stock_Adjustment sa
                    INNER JOIN Login_Master lm ON sa.Stock_Created_ID = lm.Login_ID
                    LEFT JOIN DEPT_Master dm ON lm.Login_DEPT_Code = dm.DEPT_Code
                    INNER JOIN REC_Type_Master rm ON sa.Stock_REC_Type = rm.REC_Type
                    WHERE sa.Stock_REC_Type IN (10, 12)
                    AND sa.Stock_Company_Name = @CompanyName
                    AND sa.Stock_Date >= @FromDate
                    AND sa.Stock_Date <= @ToDate
                    " + userFilter + @"
                    GROUP BY sa.Stock_Company_Name, sa.Stock_FIN_Year, sa.Stock_REC_Type, sa.Stock_REC_Number,
                             sa.Stock_Date, lm.Login_Name, dm.DEPT_Name, rm.REC_Name, rm.REC_Name2
                    ORDER BY sa.Stock_Date DESC, sa.Stock_REC_Number DESC";

                var documents = await _dbHelper.QueryAsync<ApprovalDocumentRow>(documentsQuery, queryParams);

                if (documents == null || !documents.Any())
                {
                    return Json(new { success = true, data = new List<object>() }, JsonRequestBehavior.AllowGet);
                }

                // Get approval levels for these documents
                var levelsQuery = @"
                    SELECT
                        a.Approval_FIN_Year AS FinYear,
                        a.Approval_REC_Type AS RecType,
                        a.Approval_REC_Number AS RecNumber,
                        a.Approval_Level AS Level,
                        a.Approval_Status AS StatusCode,
                        asm.Approval_Status_Name AS StatusName,
                        a.Approval_User_ID AS ApproverUserId,
                        lm.Login_Name AS ApproverName,
                        a.Approval_Date AS ApprovalDate,
                        a.Approval_Comments AS Comments
                    FROM Stock_Adjustment_Approval a
                    INNER JOIN Approval_Status_Master asm ON a.Approval_Status = asm.Approval_Status_Code
                    LEFT JOIN Login_Master lm ON a.Approval_User_ID = lm.Login_ID
                    WHERE a.Approval_Company_Name = @CompanyName
                    AND a.Approval_REC_Type IN (10, 12)
                    ORDER BY a.Approval_FIN_Year, a.Approval_REC_Number, a.Approval_Level";

                var companyParams = new Dictionary<string, object> { { "@CompanyName", companyName } };
                var levels = await _dbHelper.QueryAsync<ApprovalLevelInfo>(levelsQuery, companyParams);

                // Get line items
                var lineItemsQuery = @"
                    SELECT
                        sa.Stock_FIN_Year AS FinYear,
                        sa.Stock_REC_Type AS RecType,
                        sa.Stock_REC_Number AS RecNumber,
                        sa.Stock_REC_SNO AS Sno,
                        RTRIM(sa.Stock_Item_Code) AS ItemCode,
                        '' AS ItemDesc,
                        RTRIM(sa.Stock_From_Location) AS FromLocation,
                        '' AS FromLocationName,
                        RTRIM(sa.Stock_To_Location) AS ToLocation,
                        '' AS ToLocationName,
                        sa.Stock_Qty AS Quantity,
                        ISNULL(sa.Stock_Cost, 0) AS Cost,
                        ISNULL(sa.Stock_Qty * sa.Stock_Cost, 0) AS Amount
                    FROM Stock_Adjustment sa
                    WHERE sa.Stock_Company_Name = @CompanyName
                    AND sa.Stock_REC_Type IN (10, 12)
                    AND sa.Stock_Date >= @FromDate
                    AND sa.Stock_Date <= @ToDate
                    " + userFilter + @"
                    ORDER BY sa.Stock_FIN_Year, sa.Stock_REC_Number, sa.Stock_REC_SNO";

                var lineItems = await _dbHelper.QueryAsync<ApprovalLineItem>(lineItemsQuery, queryParams);

                // Resolve item and location names from Sage API
                await ResolveNamesFromSageApi(companyName, lineItems);

                // Group by (FinYear, RecNumber) to merge RecType 10 and 12 into one row (same as Approval)
                var result = documents
                    .GroupBy(doc => new { doc.FinYear, doc.RecNumber })
                    .Select(g =>
                    {
                        var first = g.First();
                        var allRecTypes = g.Select(d => d.RecType).Distinct().OrderBy(r => r).ToList();

                        // Use Max (not Sum) because both RecTypes share the same approval levels
                        var totalLevels = g.Max(d => d.TotalLevels);
                        var approvedCount = g.Max(d => d.ApprovedCount);
                        var rejectedCount = g.Max(d => d.RejectedCount);

                        // NextPendingLevel: if any rejected -> -1; if all fully approved -> 0; else min pending
                        int nextPending;
                        if (g.Any(d => d.NextPendingLevel == -1))
                            nextPending = -1;
                        else if (g.All(d => d.NextPendingLevel == 0))
                            nextPending = 0;
                        else
                            nextPending = g.Where(d => d.NextPendingLevel > 0).Any()
                                ? g.Where(d => d.NextPendingLevel > 0).Min(d => d.NextPendingLevel)
                                : 0;

                        // Document reference without RecType name
                        var docRef = $"{first.FinYear}/{companyName}/{first.RecNumber}";

                        return new
                        {
                            first.FinYear,
                            first.RecNumber,
                            DocumentReference = docRef,
                            first.Date,
                            first.CreatedBy,
                            first.Department,
                            TotalLevels = totalLevels,
                            ApprovedCount = approvedCount,
                            RejectedCount = rejectedCount,
                            NextPendingLevel = nextPending,
                            RecTypes = allRecTypes,
                            Levels = levels?.Where(l => l.FinYear == first.FinYear && l.RecType == first.RecType && l.RecNumber == first.RecNumber)
                                .OrderBy(l => l.Level)
                                .Select(l => new
                                {
                                    l.Level,
                                    l.StatusCode,
                                    l.StatusName,
                                    l.ApproverName,
                                    l.ApprovalDate,
                                    l.Comments
                                }).ToList(),
                            LineItems = lineItems?.Where(li => li.FinYear == first.FinYear && li.RecNumber == first.RecNumber)
                                .OrderBy(li => li.RecType).ThenBy(li => li.Sno)
                                .Select(li => new
                                {
                                    li.RecType,
                                    RecTypeName = li.RecType == 10 ? "Decrease" : "Increase",
                                    li.Sno,
                                    li.ItemCode,
                                    li.ItemDesc,
                                    li.FromLocation,
                                    li.FromLocationName,
                                    li.ToLocation,
                                    li.ToLocationName,
                                    li.Quantity,
                                    li.Cost,
                                    li.Amount
                                }).ToList()
                        };
                    }).ToList();

                return Json(new { success = true, data = result }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Resolves item descriptions and location names from Sage API
        /// </summary>
        private async Task ResolveNamesFromSageApi(string companyName, List<ApprovalLineItem> lineItems)
        {
            if (lineItems == null || !lineItems.Any()) return;

            try
            {
                var itemsTask = _sageService.GetItemsAsync(companyName);
                var locationsTask = _sageService.GetLocationsAsync(companyName);
                await Task.WhenAll(itemsTask, locationsTask);

                var itemsResponse = await itemsTask;
                var locationsResponse = await locationsTask;

                var itemLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (itemsResponse?.icitems != null)
                {
                    foreach (var item in itemsResponse.icitems)
                    {
                        var key = item.itemno?.Trim();
                        if (!string.IsNullOrEmpty(key) && !itemLookup.ContainsKey(key))
                        {
                            itemLookup[key] = item.desc?.Trim() ?? "";
                        }
                    }
                }

                var locationLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (locationsResponse?.locations != null)
                {
                    foreach (var loc in locationsResponse.locations)
                    {
                        var key = loc.location?.Trim();
                        if (!string.IsNullOrEmpty(key) && !locationLookup.ContainsKey(key))
                        {
                            locationLookup[key] = loc.desc?.Trim() ?? "";
                        }
                    }
                }

                foreach (var li in lineItems)
                {
                    if (!string.IsNullOrEmpty(li.ItemCode) && itemLookup.TryGetValue(li.ItemCode.Trim(), out var itemDesc))
                    {
                        li.ItemDesc = itemDesc;
                    }
                    if (!string.IsNullOrEmpty(li.FromLocation) && locationLookup.TryGetValue(li.FromLocation.Trim(), out var fromLocName))
                    {
                        li.FromLocationName = fromLocName;
                    }
                    if (!string.IsNullOrEmpty(li.ToLocation) && locationLookup.TryGetValue(li.ToLocation.Trim(), out var toLocName))
                    {
                        li.ToLocationName = toLocName;
                    }
                }
            }
            catch
            {
                // If API fails, leave names empty - codes are still shown
            }
        }
    }
}
