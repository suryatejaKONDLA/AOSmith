using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using AOSmith.Filters;
using AOSmith.Helpers;
using AOSmith.Models;
using AOSmith.Services;

namespace AOSmith.Controllers
{
    [AuthFilter]
    public class ApprovalController : Controller
    {
        private readonly IDatabaseHelper _databaseHelper;
        private readonly SageApiService _sageService;
        private readonly EmailService _emailService;

        public ApprovalController()
        {
            _databaseHelper = new DatabaseHelper();
            _sageService = new SageApiService();
            _emailService = new EmailService();
        }

        public ApprovalController(IDatabaseHelper databaseHelper)
        {
            _databaseHelper = databaseHelper;
            _sageService = new SageApiService();
            _emailService = new EmailService(databaseHelper);
        }

        // GET: Approval
        public ActionResult Index()
        {
            return View();
        }

        // GET: GetPendingApprovals
        [HttpGet]
        public async Task<JsonResult> GetPendingApprovals()
        {
            try
            {
                var userId = SessionHelper.GetUserId();
                var isApprover = SessionHelper.IsApprover();
                var userApprovalLevel = SessionHelper.GetApprovalLevel();
                var companyName = SessionHelper.GetCompanyName();

                if (!isApprover || userApprovalLevel <= 0)
                {
                    return Json(new { success = false, message = "You do not have permission to view approvals." }, JsonRequestBehavior.AllowGet);
                }

                // 1. Get RecType 10 and 12 documents with approval summary
                // Only show documents where an approval record exists at the user's level
                var companyParams = new Dictionary<string, object>
                {
                    { "@CompanyName", companyName },
                    { "@UserLevel", userApprovalLevel }
                };

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
                    AND EXISTS (
                        SELECT 1 FROM Stock_Adjustment_Approval al
                        WHERE al.Approval_Company_Name = sa.Stock_Company_Name
                        AND al.Approval_FIN_Year = sa.Stock_FIN_Year
                        AND al.Approval_REC_Type = sa.Stock_REC_Type
                        AND al.Approval_REC_Number = sa.Stock_REC_Number
                        AND al.Approval_Level <= @UserLevel
                    )
                    GROUP BY sa.Stock_Company_Name, sa.Stock_FIN_Year, sa.Stock_REC_Type, sa.Stock_REC_Number,
                             sa.Stock_Date, lm.Login_Name, dm.DEPT_Name, rm.REC_Name, rm.REC_Name2
                    ORDER BY sa.Stock_Date DESC, sa.Stock_REC_Number DESC";

                var documents = await _databaseHelper.QueryAsync<ApprovalDocumentRow>(documentsQuery, companyParams);

                if (documents == null || !documents.Any())
                {
                    return Json(new { success = true, data = new List<object>(), userApprovalLevel = userApprovalLevel }, JsonRequestBehavior.AllowGet);
                }

                // 2. Get all approval levels for RecType 10 and 12
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

                var levels = await _databaseHelper.QueryAsync<ApprovalLevelInfo>(levelsQuery, companyParams);

                // 3. Get all line items for RecType 10 and 12
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
                    ORDER BY sa.Stock_FIN_Year, sa.Stock_REC_Number, sa.Stock_REC_SNO";

                var lineItems = await _databaseHelper.QueryAsync<ApprovalLineItem>(lineItemsQuery, companyParams);

                // Resolve item and location names from Sage API
                await ResolveNamesFromSageApi(companyName, lineItems);

                // 4. Group by (FinYear, RecNumber) to merge RecType 10 and 12 into one row
                var result = documents
                    .GroupBy(doc => new { doc.FinYear, doc.RecNumber })
                    .Select(g =>
                    {
                        var first = g.First();
                        var allRecTypes = g.Select(d => d.RecType).Distinct().OrderBy(r => r).ToList();

                        // Merge approval counts across RecTypes
                        var totalLevels = g.Sum(d => d.TotalLevels);
                        var approvedCount = g.Sum(d => d.ApprovedCount);
                        var rejectedCount = g.Sum(d => d.RejectedCount);

                        // NextPendingLevel: if any rejected → -1; if all fully approved → 0; else min pending
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
                            CanApprove = nextPending > 0 && nextPending == userApprovalLevel,
                            RecTypes = allRecTypes,
                            // Show levels from first RecType (they are approved together so should be in sync)
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
                            // Combine line items from ALL RecTypes, include RecType for UI grouping
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

                return Json(new { success = true, data = result, userApprovalLevel = userApprovalLevel }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // POST: ProcessApproval (processes ALL RecTypes 10 & 12 for the given RecNumber)
        [HttpPost]
        public async Task<JsonResult> ProcessApproval(int finYear, int recNumber, string action, string remarks)
        {
            try
            {
                var userId = SessionHelper.GetUserId();
                var isApprover = SessionHelper.IsApprover();
                var userApprovalLevel = SessionHelper.GetApprovalLevel();
                var companyName = SessionHelper.GetCompanyName();

                if (!isApprover || userApprovalLevel <= 0)
                {
                    return Json(new { success = false, message = "You do not have permission to process approvals." });
                }

                // Validate: check that all previous levels are Approved before this user's level (across ALL RecTypes)
                if (userApprovalLevel > 1)
                {
                    var countQuery = @"
                        SELECT COUNT(*) AS Value
                        FROM Stock_Adjustment_Approval
                        WHERE Approval_Company_Name = @CompanyName
                        AND Approval_FIN_Year = @FinYear
                        AND Approval_REC_Type IN (10, 12)
                        AND Approval_REC_Number = @RecNumber
                        AND Approval_Level < @UserLevel
                        AND Approval_Status != @ApprovedStatus";

                    var checkParams = new Dictionary<string, object>
                    {
                        { "@CompanyName", companyName },
                        { "@FinYear", finYear },
                        { "@RecNumber", recNumber },
                        { "@UserLevel", userApprovalLevel },
                        { "@ApprovedStatus", ApprovalStatusConstants.Approved }
                    };

                    var pendingCount = await _databaseHelper.QuerySingleAsync<CountResult>(countQuery, checkParams);

                    if (pendingCount != null && pendingCount.Value > 0)
                    {
                        return Json(new { success = false, message = "Cannot approve: Previous approval level(s) are not yet approved." });
                    }
                }

                // Validate: check the current level is still pending (for ALL RecTypes)
                var statusCheckQuery = @"
                    SELECT COUNT(*) AS Value
                    FROM Stock_Adjustment_Approval
                    WHERE Approval_Company_Name = @CompanyName
                    AND Approval_FIN_Year = @FinYear
                    AND Approval_REC_Type IN (10, 12)
                    AND Approval_REC_Number = @RecNumber
                    AND Approval_Level = @UserLevel
                    AND Approval_Status != @PendingStatus";

                var statusCheckParams = new Dictionary<string, object>
                {
                    { "@CompanyName", companyName },
                    { "@FinYear", finYear },
                    { "@RecNumber", recNumber },
                    { "@UserLevel", userApprovalLevel },
                    { "@PendingStatus", ApprovalStatusConstants.Pending }
                };

                var alreadyProcessedCount = await _databaseHelper.QuerySingleAsync<CountResult>(statusCheckQuery, statusCheckParams);

                if (alreadyProcessedCount != null && alreadyProcessedCount.Value > 0)
                {
                    return Json(new { success = false, message = "This approval level has already been processed." });
                }

                // Update approval records for ALL RecTypes at this level
                int newStatus = action.ToLower() == "approve"
                    ? ApprovalStatusConstants.Approved
                    : ApprovalStatusConstants.Rejected;

                var updateQuery = @"
                    UPDATE Stock_Adjustment_Approval
                    SET Approval_Status = @ApprovalStatus,
                        Approval_User_ID = @UserId,
                        Approval_Date = @ApprovalDate,
                        Approval_Comments = @Comments
                    WHERE Approval_Company_Name = @CompanyName
                    AND Approval_FIN_Year = @FinYear
                    AND Approval_REC_Type IN (10, 12)
                    AND Approval_REC_Number = @RecNumber
                    AND Approval_Level = @UserLevel";

                var updateParams = new Dictionary<string, object>
                {
                    { "@ApprovalStatus", newStatus },
                    { "@UserId", userId },
                    { "@ApprovalDate", DateTime.Now },
                    { "@Comments", remarks ?? "" },
                    { "@CompanyName", companyName },
                    { "@FinYear", finYear },
                    { "@RecNumber", recNumber },
                    { "@UserLevel", userApprovalLevel }
                };

                await _databaseHelper.ExecuteNonQueryAsync(updateQuery, updateParams);

                string levelLabel = "L" + userApprovalLevel;
                string message = action.ToLower() == "approve"
                    ? $"Document approved at {levelLabel} successfully."
                    : $"Document rejected at {levelLabel}.";

                // On rejection → create reversal records (RecType 14) for RecType 10 (which was sent to Transfer API on save)
                string reversalDocRef = null;
                SageTransferEntryResponse sageTransferResponse = null;
                if (action.ToLower() == "reject")
                {
                    // Check if RecType 10 exists for this RecNumber
                    var hasRecType10Query = @"
                        SELECT COUNT(*) AS Value FROM Stock_Adjustment
                        WHERE Stock_Company_Name = @CompanyName AND Stock_FIN_Year = @FinYear
                        AND Stock_REC_Type = 10 AND Stock_REC_Number = @RecNumber";
                    var hasRecType10Params = new Dictionary<string, object>
                    {
                        { "@CompanyName", companyName }, { "@FinYear", finYear }, { "@RecNumber", recNumber }
                    };
                    var hasRecType10 = await _databaseHelper.QuerySingleAsync<CountResult>(hasRecType10Query, hasRecType10Params);

                    if (hasRecType10 != null && hasRecType10.Value > 0)
                    {
                        var reversalParams = new List<System.Data.SqlClient.SqlParameter>
                        {
                            new System.Data.SqlClient.SqlParameter("@CompanyName", System.Data.SqlDbType.VarChar, 10) { Value = companyName },
                            new System.Data.SqlClient.SqlParameter("@OriginalFinYear", System.Data.SqlDbType.Int) { Value = finYear },
                            new System.Data.SqlClient.SqlParameter("@OriginalRecType", System.Data.SqlDbType.Int) { Value = 10 },
                            new System.Data.SqlClient.SqlParameter("@OriginalRecNumber", System.Data.SqlDbType.Int) { Value = recNumber },
                            new System.Data.SqlClient.SqlParameter("@Session_ID", System.Data.SqlDbType.Int) { Value = userId }
                        };

                        var reversalResult = await _databaseHelper.ExecuteStoredProcedureWithOutputsAsync(
                            "dbo.StockAdjustment_Reversal", reversalParams);

                        if (reversalResult.ResultVal == 1)
                        {
                            reversalDocRef = reversalResult.ResultMessage;
                            message += $" {reversalResult.ResultMessage}";

                            int reversalRecNumber = ExtractReversalRecNumber(reversalResult.ResultMessage);

                            if (reversalRecNumber > 0)
                            {
                                var reversalLineItemsQuery = @"
                                    SELECT
                                        sa.Stock_REC_SNO AS StockRecSno,
                                        sa.Stock_REC_Type AS RecType,
                                        RTRIM(sa.Stock_From_Location) AS FromLocation,
                                        RTRIM(sa.Stock_To_Location) AS ToLocation,
                                        RTRIM(sa.Stock_Item_Code) AS ItemCode,
                                        '' AS ItemDescription,
                                        sa.Stock_Qty AS Qty
                                    FROM Stock_Adjustment sa
                                    WHERE sa.Stock_Company_Name = @CompanyName
                                    AND sa.Stock_FIN_Year = @FinYear
                                    AND sa.Stock_REC_Type = 14
                                    AND sa.Stock_REC_Number = @RecNumber
                                    ORDER BY sa.Stock_REC_SNO";

                                var reversalLineParams = new Dictionary<string, object>
                                {
                                    { "@CompanyName", companyName },
                                    { "@FinYear", finYear },
                                    { "@RecNumber", reversalRecNumber }
                                };

                                var reversalLineItems = await _databaseHelper.QueryAsync<StockAdjustmentLineItem>(
                                    reversalLineItemsQuery, reversalLineParams);

                                if (reversalLineItems != null && reversalLineItems.Any())
                                {
                                    var reversalDateQuery = @"
                                        SELECT TOP 1 Stock_Date AS Value
                                        FROM Stock_Adjustment
                                        WHERE Stock_Company_Name = @CompanyName
                                        AND Stock_FIN_Year = @FinYear
                                        AND Stock_REC_Type = 14
                                        AND Stock_REC_Number = @RecNumber";

                                    var reversalDateRow = await _databaseHelper.QuerySingleAsync<DateResult>(
                                        reversalDateQuery, reversalLineParams);
                                    var reversalTransDate = reversalDateRow?.Value ?? DateTime.Now;

                                    sageTransferResponse = await _sageService.SendTransferEntryAsync(
                                        companyName, finYear, reversalLineItems, reversalTransDate, reversalRecNumber, 14);

                                    if (sageTransferResponse != null && sageTransferResponse.IsSuccess)
                                    {
                                        var reversalSageSentQuery = @"
                                            UPDATE Stock_Adjustment
                                            SET Stock_Sage_Data_Sent = 1,
                                                Stock_Sage_Sent_Date = GETDATE(),
                                                Stock_Sage_Transaction_Number = @DocNum
                                            WHERE Stock_Company_Name = @CompanyName
                                            AND Stock_FIN_Year = @FinYear
                                            AND Stock_REC_Type = 14
                                            AND Stock_REC_Number = @RecNumber";

                                        var reversalSageParams = new Dictionary<string, object>
                                        {
                                            { "@DocNum", sageTransferResponse.DocNum ?? "" },
                                            { "@CompanyName", companyName },
                                            { "@FinYear", finYear },
                                            { "@RecNumber", reversalRecNumber }
                                        };
                                        await _databaseHelper.ExecuteNonQueryAsync(reversalSageSentQuery, reversalSageParams);
                                        message += $" Sage Transfer API called successfully.";
                                    }
                                    else
                                    {
                                        var rollbackReversalQuery = @"
                                            DELETE FROM Stock_Adjustment
                                            WHERE Stock_Company_Name = @CompanyName
                                            AND Stock_FIN_Year = @FinYear
                                            AND Stock_REC_Type = 14
                                            AND Stock_REC_Number = @RecNumber";

                                        var rollbackReversalParams = new Dictionary<string, object>
                                        {
                                            { "@CompanyName", companyName },
                                            { "@FinYear", finYear },
                                            { "@RecNumber", reversalRecNumber }
                                        };
                                        await _databaseHelper.ExecuteNonQueryAsync(rollbackReversalQuery, rollbackReversalParams);
                                        var transferErrors = sageTransferResponse?.Errors != null && sageTransferResponse.Errors.Count > 0
                                            ? string.Join("; ", sageTransferResponse.Errors)
                                            : sageTransferResponse?.Message ?? "Unknown Sage error";
                                        message += $" Sage Transfer API failed: {transferErrors}. Reversal records rolled back.";
                                    }
                                }
                            }
                        }
                        else
                        {
                            message += $" Reversal creation failed: {reversalResult.ResultMessage}";
                        }
                    }

                    // Send rejection email
                    try
                    {
                        var approverName = SessionHelper.GetUserName() ?? "Unknown";
                        var docRef = $"{finYear}/{companyName}/{recNumber}";

                        await _emailService.SendRejectionEmailAsync(
                            finYear, 10, recNumber, docRef, userApprovalLevel, approverName, remarks);
                    }
                    catch { /* email failure should not break approval flow */ }
                }

                // Check if ALL levels for ALL RecTypes are now fully approved → trigger Sage Adjustment Entry API
                SageAdjustmentEntryResponse sageAdjResponse = null;
                if (action.ToLower() == "approve")
                {
                    var fullyApprovedQuery = @"
                        SELECT COUNT(*) AS Value
                        FROM Stock_Adjustment_Approval
                        WHERE Approval_Company_Name = @CompanyName
                        AND Approval_FIN_Year = @FinYear
                        AND Approval_REC_Type IN (10, 12)
                        AND Approval_REC_Number = @RecNumber
                        AND Approval_Status != @ApprovedStatus";

                    var fullyApprovedParams = new Dictionary<string, object>
                    {
                        { "@CompanyName", companyName },
                        { "@FinYear", finYear },
                        { "@RecNumber", recNumber },
                        { "@ApprovedStatus", ApprovalStatusConstants.Approved }
                    };

                    var remainingCount = await _databaseHelper.QuerySingleAsync<CountResult>(fullyApprovedQuery, fullyApprovedParams);

                    if (remainingCount != null && remainingCount.Value == 0)
                    {
                        // All levels approved for ALL RecTypes! Send ONE merged Sage Adjustment Entry API call
                        var allLineItemsQuery = @"
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
                                ISNULL(sa.Stock_Cost, 0) AS Cost
                            FROM Stock_Adjustment sa
                            WHERE sa.Stock_Company_Name = @CompanyName
                            AND sa.Stock_FIN_Year = @FinYear
                            AND sa.Stock_REC_Type IN (10, 12)
                            AND sa.Stock_REC_Number = @RecNumber
                            ORDER BY sa.Stock_REC_Type, sa.Stock_REC_SNO";

                        var lineItemParams = new Dictionary<string, object>
                        {
                            { "@CompanyName", companyName },
                            { "@FinYear", finYear },
                            { "@RecNumber", recNumber }
                        };

                        var allLineItems = await _databaseHelper.QueryAsync<ApprovalLineItem>(allLineItemsQuery, lineItemParams);

                        if (allLineItems != null && allLineItems.Any())
                        {
                            // Get the transaction date
                            var dateQuery = @"
                                SELECT TOP 1 Stock_Date AS Value
                                FROM Stock_Adjustment
                                WHERE Stock_Company_Name = @CompanyName
                                AND Stock_FIN_Year = @FinYear
                                AND Stock_REC_Type IN (10, 12)
                                AND Stock_REC_Number = @RecNumber";

                            var dateRow = await _databaseHelper.QuerySingleAsync<DateResult>(dateQuery, lineItemParams);
                            var transDate = dateRow?.Value ?? DateTime.Now;

                            // Send ONE merged Sage call with both Increase (transtype 5) and Decrease (transtype 6) items
                            sageAdjResponse = await _sageService.SendAdjustmentEntryAsync(
                                companyName, finYear, allLineItems, transDate, recNumber);

                            if (sageAdjResponse.IsSuccess)
                            {
                                // Mark ALL RecType records as sent to Sage
                                var sageSentQuery = @"
                                    UPDATE Stock_Adjustment
                                    SET Stock_Sage_Data_Sent = 1,
                                        Stock_Sage_Sent_Date = GETDATE(),
                                        Stock_Sage_Transaction_Number = @DocNum
                                    WHERE Stock_Company_Name = @CompanyName
                                    AND Stock_FIN_Year = @FinYear
                                    AND Stock_REC_Type IN (10, 12)
                                    AND Stock_REC_Number = @RecNumber";

                                var sageSentParams = new Dictionary<string, object>
                                {
                                    { "@DocNum", sageAdjResponse.DocNum ?? "" },
                                    { "@CompanyName", companyName },
                                    { "@FinYear", finYear },
                                    { "@RecNumber", recNumber }
                                };
                                await _databaseHelper.ExecuteNonQueryAsync(sageSentQuery, sageSentParams);
                                message = $"Document fully approved! Sage Adjustment Entry API called successfully.";
                            }
                            else
                            {
                                // Rollback approval status for ALL RecTypes at this level
                                var rollbackQuery = @"
                                    UPDATE Stock_Adjustment_Approval
                                    SET Approval_Status = @PendingStatus,
                                        Approval_User_ID = NULL,
                                        Approval_Date = NULL,
                                        Approval_Comments = NULL
                                    WHERE Approval_Company_Name = @CompanyName
                                    AND Approval_FIN_Year = @FinYear
                                    AND Approval_REC_Type IN (10, 12)
                                    AND Approval_REC_Number = @RecNumber
                                    AND Approval_Level = @UserLevel";

                                var rollbackParams = new Dictionary<string, object>
                                {
                                    { "@PendingStatus", ApprovalStatusConstants.Pending },
                                    { "@CompanyName", companyName },
                                    { "@FinYear", finYear },
                                    { "@RecNumber", recNumber },
                                    { "@UserLevel", userApprovalLevel }
                                };
                                await _databaseHelper.ExecuteNonQueryAsync(rollbackQuery, rollbackParams);
                                var adjErrors = sageAdjResponse.Errors != null && sageAdjResponse.Errors.Count > 0
                                    ? string.Join("; ", sageAdjResponse.Errors)
                                    : sageAdjResponse.Message ?? "Unknown Sage error";
                                message = $"Sage Adjustment API failed: {adjErrors}. Approval has been rolled back.";
                            }
                        }

                        // Send fully approved email
                        if (sageAdjResponse != null && sageAdjResponse.IsSuccess)
                        {
                            try
                            {
                                var approverNameFull = SessionHelper.GetUserName() ?? "Unknown";
                                var docRefFull = $"{finYear}/{companyName}/{recNumber}";
                                await _emailService.SendFullyApprovedEmailAsync(
                                    finYear, 10, recNumber, docRefFull, approverNameFull);
                            }
                            catch { /* email failure should not break approval flow */ }
                        }
                    }
                    else
                    {
                        // Not fully approved yet → send email to next level approver
                        try
                        {
                            var approverNamePartial = SessionHelper.GetUserName() ?? "Unknown";
                            var docRefPartial = $"{finYear}/{companyName}/{recNumber}";
                            await _emailService.SendApprovalEmailToNextLevelAsync(
                                finYear, 10, recNumber, docRefPartial, userApprovalLevel, approverNamePartial);
                        }
                        catch { /* email failure should not break approval flow */ }
                    }
                }

                // Build response
                object sageResponseObj = null;
                bool isFullyApproved = false;

                if (sageAdjResponse != null)
                {
                    isFullyApproved = true;
                    sageResponseObj = new
                    {
                        status = sageAdjResponse.IsSuccess ? "Success" : "Error",
                        isSuccess = sageAdjResponse.IsSuccess,
                        message = sageAdjResponse.Message,
                        docNum = sageAdjResponse.DocNum,
                        rawRequest = sageAdjResponse.RawRequest,
                        rawResponse = sageAdjResponse.RawResponse
                    };
                }

                // Build Sage Transfer response for reversal
                object sageTransferResponseObj = null;
                if (sageTransferResponse != null)
                {
                    sageTransferResponseObj = new
                    {
                        status = sageTransferResponse.IsSuccess ? "Success" : "Error",
                        isSuccess = sageTransferResponse.IsSuccess,
                        message = sageTransferResponse.Message,
                        docNum = sageTransferResponse.DocNum,
                        transferNumber = sageTransferResponse.TransferNumber,
                        rawRequest = sageTransferResponse.RawRequest,
                        rawResponse = sageTransferResponse.RawResponse
                    };
                }

                return Json(new
                {
                    success = true,
                    message = message,
                    isFullyApproved = isFullyApproved,
                    sageResponse = sageResponseObj,
                    sageTransferResponse = sageTransferResponseObj
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Extracts the reversal RecNumber from the SP result message.
        /// Expected format: "Reversal entry 202526/STRV/3 created for 202526/STIN/1."
        /// </summary>
        private int ExtractReversalRecNumber(string resultMessage)
        {
            if (string.IsNullOrEmpty(resultMessage)) return 0;

            // Match the reversal document reference: "Reversal entry XXXXXX/COMPANY/XXXX/N"
            var match = System.Text.RegularExpressions.Regex.Match(
                resultMessage, @"Reversal entry \d+/[A-Z]+/[A-Z]+/(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success && int.TryParse(match.Groups[1].Value, out var recNum))
            {
                return recNum;
            }

            return 0;
        }

        /// <summary>
        /// Resolves item descriptions and location names from Sage API
        /// </summary>
        private async Task ResolveNamesFromSageApi(string companyName, List<ApprovalLineItem> lineItems)
        {
            if (lineItems == null || !lineItems.Any()) return;

            try
            {
                // Fetch items and locations from Sage API in parallel
                var itemsTask = _sageService.GetItemsAsync(companyName);
                var locationsTask = _sageService.GetLocationsAsync(companyName);
                await Task.WhenAll(itemsTask, locationsTask);

                var itemsResponse = await itemsTask;
                var locationsResponse = await locationsTask;

                // Build lookup dictionaries
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

                // Resolve names
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

    /// <summary>
    /// Helper class for scalar count queries
    /// </summary>
    public class CountResult
    {
        public int Value { get; set; }
    }

    /// <summary>
    /// Helper class for scalar date queries
    /// </summary>
    public class DateResult
    {
        public DateTime Value { get; set; }
    }

    /// <summary>
    /// Helper class for document reference queries
    /// </summary>
    public class DocRefResult
    {
        public string DocumentReference { get; set; }
    }
}
