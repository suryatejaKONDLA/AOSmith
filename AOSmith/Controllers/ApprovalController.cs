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

                if (!isApprover || userApprovalLevel <= 0)
                {
                    return Json(new { success = false, message = "You do not have permission to view approvals." }, JsonRequestBehavior.AllowGet);
                }

                // 1. Get all RecType 10 and 12 documents with approval summary
                var documentsQuery = @"
                    SELECT DISTINCT
                        sa.Stock_FIN_Year AS FinYear,
                        sa.Stock_REC_Type AS RecType,
                        sa.Stock_REC_Number AS RecNumber,
                        CAST(sa.Stock_FIN_Year AS VARCHAR(10)) + '/' + rm.REC_Name + '/' + CAST(sa.Stock_REC_Number AS VARCHAR(10)) AS DocumentReference,
                        sa.Stock_Date AS Date,
                        lm.Login_Name AS CreatedBy,
                        ISNULL(dm.DEPT_Name, '') AS Department,
                        rm.REC_Name2 AS RecTypeName,
                        (SELECT COUNT(*) FROM Stock_Adjustment_Approval a
                         WHERE a.Approval_FIN_Year = sa.Stock_FIN_Year
                         AND a.Approval_REC_Type = sa.Stock_REC_Type
                         AND a.Approval_REC_Number = sa.Stock_REC_Number) AS TotalLevels,
                        (SELECT COUNT(*) FROM Stock_Adjustment_Approval a
                         WHERE a.Approval_FIN_Year = sa.Stock_FIN_Year
                         AND a.Approval_REC_Type = sa.Stock_REC_Type
                         AND a.Approval_REC_Number = sa.Stock_REC_Number
                         AND a.Approval_Status = 2) AS ApprovedCount,
                        (SELECT COUNT(*) FROM Stock_Adjustment_Approval a
                         WHERE a.Approval_FIN_Year = sa.Stock_FIN_Year
                         AND a.Approval_REC_Type = sa.Stock_REC_Type
                         AND a.Approval_REC_Number = sa.Stock_REC_Number
                         AND a.Approval_Status = 3) AS RejectedCount,
                        CASE
                            WHEN EXISTS (SELECT 1 FROM Stock_Adjustment_Approval a
                                WHERE a.Approval_FIN_Year = sa.Stock_FIN_Year
                                AND a.Approval_REC_Type = sa.Stock_REC_Type
                                AND a.Approval_REC_Number = sa.Stock_REC_Number
                                AND a.Approval_Status = 3) THEN -1
                            ELSE
                                ISNULL((SELECT MIN(a.Approval_Level)
                                FROM Stock_Adjustment_Approval a
                                WHERE a.Approval_FIN_Year = sa.Stock_FIN_Year
                                AND a.Approval_REC_Type = sa.Stock_REC_Type
                                AND a.Approval_REC_Number = sa.Stock_REC_Number
                                AND a.Approval_Status = 1
                                AND NOT EXISTS (
                                    SELECT 1 FROM Stock_Adjustment_Approval prev
                                    WHERE prev.Approval_FIN_Year = a.Approval_FIN_Year
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
                    GROUP BY sa.Stock_FIN_Year, sa.Stock_REC_Type, sa.Stock_REC_Number,
                             sa.Stock_Date, lm.Login_Name, dm.DEPT_Name, rm.REC_Name, rm.REC_Name2
                    ORDER BY sa.Stock_Date DESC, sa.Stock_REC_Number DESC";

                var documents = await _databaseHelper.QueryAsync<ApprovalDocumentRow>(documentsQuery);

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
                    WHERE a.Approval_REC_Type IN (10, 12)
                    ORDER BY a.Approval_FIN_Year, a.Approval_REC_Number, a.Approval_Level";

                var levels = await _databaseHelper.QueryAsync<ApprovalLevelInfo>(levelsQuery);

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
                        sa.Stock_Qty AS Quantity
                    FROM Stock_Adjustment sa
                    WHERE sa.Stock_REC_Type IN (10, 12)
                    ORDER BY sa.Stock_FIN_Year, sa.Stock_REC_Number, sa.Stock_REC_SNO";

                var lineItems = await _databaseHelper.QueryAsync<ApprovalLineItem>(lineItemsQuery);

                // Resolve item and location names from Sage API
                await ResolveNamesFromSageApi(lineItems);

                // 4. Group everything together
                var result = documents.Select(doc => new
                {
                    doc.FinYear,
                    doc.RecType,
                    doc.RecNumber,
                    doc.DocumentReference,
                    doc.Date,
                    doc.CreatedBy,
                    doc.Department,
                    doc.TotalLevels,
                    doc.ApprovedCount,
                    doc.RejectedCount,
                    doc.NextPendingLevel,
                    // User can approve if: their level == next pending level
                    CanApprove = doc.NextPendingLevel > 0 && doc.NextPendingLevel == userApprovalLevel,
                    doc.RecTypeName,
                    Levels = levels?.Where(l => l.FinYear == doc.FinYear && l.RecType == doc.RecType && l.RecNumber == doc.RecNumber)
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
                    LineItems = lineItems?.Where(li => li.FinYear == doc.FinYear && li.RecType == doc.RecType && li.RecNumber == doc.RecNumber)
                        .OrderBy(li => li.Sno)
                        .Select(li => new
                        {
                            li.Sno,
                            li.ItemCode,
                            li.ItemDesc,
                            li.FromLocation,
                            li.FromLocationName,
                            li.ToLocation,
                            li.ToLocationName,
                            li.Quantity
                        }).ToList()
                }).ToList();

                return Json(new { success = true, data = result, userApprovalLevel = userApprovalLevel }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // POST: ProcessApproval
        [HttpPost]
        public async Task<JsonResult> ProcessApproval(int finYear, int recType, int recNumber, string action, string remarks)
        {
            try
            {
                var userId = SessionHelper.GetUserId();
                var isApprover = SessionHelper.IsApprover();
                var userApprovalLevel = SessionHelper.GetApprovalLevel();

                if (!isApprover || userApprovalLevel <= 0)
                {
                    return Json(new { success = false, message = "You do not have permission to process approvals." });
                }

                // Validate: check that all previous levels are Approved before this user's level
                if (userApprovalLevel > 1)
                {
                    var countQuery = @"
                        SELECT COUNT(*) AS Value
                        FROM Stock_Adjustment_Approval
                        WHERE Approval_FIN_Year = @FinYear
                        AND Approval_REC_Type = @RecType
                        AND Approval_REC_Number = @RecNumber
                        AND Approval_Level < @UserLevel
                        AND Approval_Status != @ApprovedStatus";

                    var checkParams = new Dictionary<string, object>
                    {
                        { "@FinYear", finYear },
                        { "@RecType", recType },
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

                // Validate: check the current level is still pending
                var statusCheckQuery = @"
                    SELECT Approval_Status AS Value
                    FROM Stock_Adjustment_Approval
                    WHERE Approval_FIN_Year = @FinYear
                    AND Approval_REC_Type = @RecType
                    AND Approval_REC_Number = @RecNumber
                    AND Approval_Level = @UserLevel";

                var statusCheckParams = new Dictionary<string, object>
                {
                    { "@FinYear", finYear },
                    { "@RecType", recType },
                    { "@RecNumber", recNumber },
                    { "@UserLevel", userApprovalLevel }
                };

                var currentStatus = await _databaseHelper.QuerySingleAsync<CountResult>(statusCheckQuery, statusCheckParams);

                if (currentStatus == null)
                {
                    return Json(new { success = false, message = "Approval record not found for your level." });
                }

                if (currentStatus.Value != ApprovalStatusConstants.Pending)
                {
                    return Json(new { success = false, message = "This approval level has already been processed." });
                }

                // Update the approval record
                int newStatus = action.ToLower() == "approve"
                    ? ApprovalStatusConstants.Approved
                    : ApprovalStatusConstants.Rejected;

                var updateQuery = @"
                    UPDATE Stock_Adjustment_Approval
                    SET Approval_Status = @ApprovalStatus,
                        Approval_User_ID = @UserId,
                        Approval_Date = @ApprovalDate,
                        Approval_Comments = @Comments
                    WHERE Approval_FIN_Year = @FinYear
                    AND Approval_REC_Type = @RecType
                    AND Approval_REC_Number = @RecNumber
                    AND Approval_Level = @UserLevel";

                var updateParams = new Dictionary<string, object>
                {
                    { "@ApprovalStatus", newStatus },
                    { "@UserId", userId },
                    { "@ApprovalDate", DateTime.Now },
                    { "@Comments", remarks ?? "" },
                    { "@FinYear", finYear },
                    { "@RecType", recType },
                    { "@RecNumber", recNumber },
                    { "@UserLevel", userApprovalLevel }
                };

                await _databaseHelper.ExecuteNonQueryAsync(updateQuery, updateParams);

                string levelLabel = "L" + userApprovalLevel;
                string message = action.ToLower() == "approve"
                    ? $"Document approved at {levelLabel} successfully."
                    : $"Document rejected at {levelLabel}.";

                // On rejection → create reversal records (RecType 14) with swapped locations via SP
                string reversalDocRef = null;
                SageTransferEntryResponse sageTransferResponse = null;
                if (action.ToLower() == "reject")
                {
                    var reversalParams = new List<System.Data.SqlClient.SqlParameter>
                    {
                        new System.Data.SqlClient.SqlParameter("@OriginalFinYear", System.Data.SqlDbType.Int) { Value = finYear },
                        new System.Data.SqlClient.SqlParameter("@OriginalRecType", System.Data.SqlDbType.Int) { Value = recType },
                        new System.Data.SqlClient.SqlParameter("@OriginalRecNumber", System.Data.SqlDbType.Int) { Value = recNumber },
                        new System.Data.SqlClient.SqlParameter("@Session_ID", System.Data.SqlDbType.Int) { Value = userId }
                    };

                    var reversalResult = await _databaseHelper.ExecuteStoredProcedureWithOutputsAsync(
                        "dbo.StockAdjustment_Reversal", reversalParams);

                    if (reversalResult.ResultVal == 1)
                    {
                        reversalDocRef = reversalResult.ResultMessage;
                        message += $" {reversalResult.ResultMessage}";

                        // Extract the new reversal RecNumber from the ResultMessage
                        // Format: "Reversal entry 202526/STRV/1 created for 202526/STIN/1."
                        int reversalRecNumber = ExtractReversalRecNumber(reversalResult.ResultMessage);

                        if (reversalRecNumber > 0)
                        {
                            // Fetch the reversal line items (RecType 14) from DB
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
                                WHERE sa.Stock_FIN_Year = @FinYear
                                AND sa.Stock_REC_Type = 14
                                AND sa.Stock_REC_Number = @RecNumber
                                ORDER BY sa.Stock_REC_SNO";

                            var reversalLineParams = new Dictionary<string, object>
                            {
                                { "@FinYear", finYear },
                                { "@RecNumber", reversalRecNumber }
                            };

                            var reversalLineItems = await _databaseHelper.QueryAsync<StockAdjustmentLineItem>(
                                reversalLineItemsQuery, reversalLineParams);

                            if (reversalLineItems != null && reversalLineItems.Any())
                            {
                                // Get the transaction date from the reversal records
                                var reversalDateQuery = @"
                                    SELECT TOP 1 Stock_Date AS Value
                                    FROM Stock_Adjustment
                                    WHERE Stock_FIN_Year = @FinYear
                                    AND Stock_REC_Type = 14
                                    AND Stock_REC_Number = @RecNumber";

                                var reversalDateRow = await _databaseHelper.QuerySingleAsync<DateResult>(
                                    reversalDateQuery, reversalLineParams);
                                var reversalTransDate = reversalDateRow?.Value ?? DateTime.Now;

                                // Send reversal data to Sage Transfer API
                                sageTransferResponse = await _sageService.SendTransferEntryAsync(
                                    reversalLineItems, reversalTransDate, reversalRecNumber, 14);

                                if (sageTransferResponse != null && sageTransferResponse.Status?.ToLower() != "error")
                                {
                                    // Mark reversal records as sent to Sage
                                    var reversalSageSentQuery = @"
                                        UPDATE Stock_Adjustment
                                        SET Stock_Sage_Data_Sent = 1,
                                            Stock_Sage_Sent_Date = GETDATE()
                                        WHERE Stock_FIN_Year = @FinYear
                                        AND Stock_REC_Type = 14
                                        AND Stock_REC_Number = @RecNumber";

                                    await _databaseHelper.ExecuteNonQueryAsync(reversalSageSentQuery, reversalLineParams);
                                    message += $" Sage Transfer API called successfully.";
                                }
                                else
                                {
                                    message += $" Sage Transfer API failed: {sageTransferResponse?.Message}";
                                }
                            }
                        }
                    }
                    else
                    {
                        message += $" Reversal creation failed: {reversalResult.ResultMessage}";
                    }

                    // Send rejection email to previous level approvers and creator
                    try
                    {
                        var approverName = SessionHelper.GetUserName() ?? "Unknown";
                        var docRefQuery = @"SELECT TOP 1
                            CAST(sa.Stock_FIN_Year AS VARCHAR(10)) + '/' + rm.REC_Name + '/' + CAST(sa.Stock_REC_Number AS VARCHAR(10)) AS DocumentReference
                            FROM Stock_Adjustment sa
                            INNER JOIN REC_Type_Master rm ON sa.Stock_REC_Type = rm.REC_Type
                            WHERE sa.Stock_FIN_Year = @FinYear AND sa.Stock_REC_Type = @RecType AND sa.Stock_REC_Number = @RecNumber";
                        var docRefParams = new Dictionary<string, object>
                        {
                            { "@FinYear", finYear }, { "@RecType", recType }, { "@RecNumber", recNumber }
                        };
                        var docRefResult = await _databaseHelper.QuerySingleAsync<DocRefResult>(docRefQuery, docRefParams);
                        var docRef = docRefResult?.DocumentReference ?? $"{finYear}/{recType}/{recNumber}";

                        await _emailService.SendRejectionEmailAsync(
                            finYear, recType, recNumber, docRef, userApprovalLevel, approverName, remarks);
                    }
                    catch { /* email failure should not break approval flow */ }
                }

                // Check if all levels are now fully approved → trigger Sage Adjustment Entry API
                SageAdjustmentEntryResponse sageAdjResponse = null;
                if (action.ToLower() == "approve")
                {
                    var fullyApprovedQuery = @"
                        SELECT COUNT(*) AS Value
                        FROM Stock_Adjustment_Approval
                        WHERE Approval_FIN_Year = @FinYear
                        AND Approval_REC_Type = @RecType
                        AND Approval_REC_Number = @RecNumber
                        AND Approval_Status != @ApprovedStatus";

                    var fullyApprovedParams = new Dictionary<string, object>
                    {
                        { "@FinYear", finYear },
                        { "@RecType", recType },
                        { "@RecNumber", recNumber },
                        { "@ApprovedStatus", ApprovalStatusConstants.Approved }
                    };

                    var remainingCount = await _databaseHelper.QuerySingleAsync<CountResult>(fullyApprovedQuery, fullyApprovedParams);

                    if (remainingCount != null && remainingCount.Value == 0)
                    {
                        // All levels approved! Send to Sage API based on RecType
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
                                sa.Stock_Qty AS Quantity
                            FROM Stock_Adjustment sa
                            WHERE sa.Stock_FIN_Year = @FinYear
                            AND sa.Stock_REC_Type = @RecType
                            AND sa.Stock_REC_Number = @RecNumber
                            ORDER BY sa.Stock_REC_SNO";

                        var lineItemParams = new Dictionary<string, object>
                        {
                            { "@FinYear", finYear },
                            { "@RecType", recType },
                            { "@RecNumber", recNumber }
                        };

                        var lineItems = await _databaseHelper.QueryAsync<ApprovalLineItem>(lineItemsQuery, lineItemParams);

                        if (lineItems != null && lineItems.Any())
                        {
                            // Get the transaction date
                            var dateQuery = @"
                                SELECT TOP 1 Stock_Date AS Value
                                FROM Stock_Adjustment
                                WHERE Stock_FIN_Year = @FinYear
                                AND Stock_REC_Type = @RecType
                                AND Stock_REC_Number = @RecNumber";

                            var dateRow = await _databaseHelper.QuerySingleAsync<DateResult>(dateQuery, lineItemParams);
                            var transDate = dateRow?.Value ?? DateTime.Now;

                            var sageService = new SageApiService();

                            // Both RecType 10 and 12 → Sage Adjustment Entry API after full approval
                            var location = lineItems.First().ToLocation;

                            sageAdjResponse = await sageService.SendAdjustmentEntryAsync(
                                lineItems, transDate, recNumber, location);

                            if (sageAdjResponse.Status?.ToLower() != "error")
                            {
                                var sageSentQuery = @"
                                    UPDATE Stock_Adjustment
                                    SET Stock_Sage_Data_Sent = 1,
                                        Stock_Sage_Sent_Date = GETDATE()
                                    WHERE Stock_FIN_Year = @FinYear
                                    AND Stock_REC_Type = @RecType
                                    AND Stock_REC_Number = @RecNumber";

                                await _databaseHelper.ExecuteNonQueryAsync(sageSentQuery, lineItemParams);
                            }

                            message = $"Document fully approved! Sage Adjustment Entry API called.";
                        }

                        // Send fully approved email to creator
                        try
                        {
                            var approverNameFull = SessionHelper.GetUserName() ?? "Unknown";
                            var docRefQueryFull = @"SELECT TOP 1
                                CAST(sa.Stock_FIN_Year AS VARCHAR(10)) + '/' + rm.REC_Name + '/' + CAST(sa.Stock_REC_Number AS VARCHAR(10)) AS DocumentReference
                                FROM Stock_Adjustment sa
                                INNER JOIN REC_Type_Master rm ON sa.Stock_REC_Type = rm.REC_Type
                                WHERE sa.Stock_FIN_Year = @FinYear AND sa.Stock_REC_Type = @RecType AND sa.Stock_REC_Number = @RecNumber";
                            var docRefResultFull = await _databaseHelper.QuerySingleAsync<DocRefResult>(docRefQueryFull, lineItemParams);
                            var docRefFull = docRefResultFull?.DocumentReference ?? $"{finYear}/{recType}/{recNumber}";

                            await _emailService.SendFullyApprovedEmailAsync(
                                finYear, recType, recNumber, docRefFull, approverNameFull);
                        }
                        catch { /* email failure should not break approval flow */ }
                    }
                    else
                    {
                        // Not fully approved yet → send email to next level approver
                        try
                        {
                            var approverNamePartial = SessionHelper.GetUserName() ?? "Unknown";
                            var docRefQueryPartial = @"SELECT TOP 1
                                CAST(sa.Stock_FIN_Year AS VARCHAR(10)) + '/' + rm.REC_Name + '/' + CAST(sa.Stock_REC_Number AS VARCHAR(10)) AS DocumentReference
                                FROM Stock_Adjustment sa
                                INNER JOIN REC_Type_Master rm ON sa.Stock_REC_Type = rm.REC_Type
                                WHERE sa.Stock_FIN_Year = @FinYear AND sa.Stock_REC_Type = @RecType AND sa.Stock_REC_Number = @RecNumber";
                            var docRefParamsPartial = new Dictionary<string, object>
                            {
                                { "@FinYear", finYear }, { "@RecType", recType }, { "@RecNumber", recNumber }
                            };
                            var docRefResultPartial = await _databaseHelper.QuerySingleAsync<DocRefResult>(docRefQueryPartial, docRefParamsPartial);
                            var docRefPartial = docRefResultPartial?.DocumentReference ?? $"{finYear}/{recType}/{recNumber}";

                            await _emailService.SendApprovalEmailToNextLevelAsync(
                                finYear, recType, recNumber, docRefPartial, userApprovalLevel, approverNamePartial);
                        }
                        catch { /* email failure should not break approval flow */ }
                    }
                }

                // Build Sage response object
                object sageResponseObj = null;
                bool isFullyApproved = false;

                if (sageAdjResponse != null)
                {
                    isFullyApproved = true;
                    sageResponseObj = new
                    {
                        status = sageAdjResponse.Status,
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
                        status = sageTransferResponse.Status,
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

            // Match the reversal document reference: "Reversal entry XXXXXX/XXXX/N"
            var match = System.Text.RegularExpressions.Regex.Match(
                resultMessage, @"Reversal entry \d+/[A-Z]+/(\d+)",
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
        private async Task ResolveNamesFromSageApi(List<ApprovalLineItem> lineItems)
        {
            if (lineItems == null || !lineItems.Any()) return;

            try
            {
                // Fetch items and locations from Sage API in parallel
                var itemsTask = _sageService.GetItemsAsync();
                var locationsTask = _sageService.GetLocationsAsync();
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
