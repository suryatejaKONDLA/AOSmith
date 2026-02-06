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

namespace AOSmith.Controllers
{
    [AuthFilter]
    public class ApprovalController : Controller
    {
        private readonly IDatabaseHelper _databaseHelper;

        public ApprovalController()
        {
            _databaseHelper = new DatabaseHelper();
        }

        public ApprovalController(IDatabaseHelper databaseHelper)
        {
            _databaseHelper = databaseHelper;
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

                if (!isApprover)
                {
                    return Json(new { success = false, message = "You do not have permission to view approvals." }, JsonRequestBehavior.AllowGet);
                }

                // Query to get pending approvals based on user's approval level
                var query = @"
                    SELECT
                        sa.Stock_Adj_Fin_Year AS FinYear,
                        sa.Stock_Adj_REC_Type AS RecType,
                        sa.Stock_Adj_REC_Number AS RecNumber,
                        saa.Approval_Level AS ApprovalLevel,
                        CONCAT(sa.Stock_Adj_Fin_Year, '/',
                               CASE WHEN sa.Stock_Adj_REC_Type = 10 THEN 'STDL'
                                    WHEN sa.Stock_Adj_REC_Type = 12 THEN 'STIN'
                                    ELSE 'UNKNOWN' END,
                               '/', sa.Stock_Adj_REC_Number) AS DocumentReference,
                        sa.Stock_Adj_Date AS Date,
                        d.Dept_Name AS Department,
                        i.Item_Code + ' - ' + i.Item_Desc AS ItemName,
                        l1.Loc_Code AS FromLocation,
                        l2.Loc_Code AS ToLocation,
                        sa.Stock_Adj_QTY AS Quantity,
                        sa.Stock_Adj_Reason AS Reason,
                        saa.Approval_Status AS ApprovalStatus,
                        asm.Approval_Status_Name AS ApprovalStatusName,
                        saa.Approval_Level * 1000000 + sa.Stock_Adj_Fin_Year * 10000 + sa.Stock_Adj_REC_Type * 1000 + sa.Stock_Adj_REC_Number AS Id
                    FROM Stock_Adjustment sa
                    INNER JOIN Stock_Adjustment_Approval saa
                        ON sa.Stock_Adj_Fin_Year = saa.Approval_Fin_Year
                        AND sa.Stock_Adj_REC_Type = saa.Approval_REC_Type
                        AND sa.Stock_Adj_REC_Number = saa.Approval_REC_Number
                    INNER JOIN Item_Master i ON sa.Stock_Adj_Item_Code = i.Item_Code
                    INNER JOIN Location_Master l1 ON sa.Stock_Adj_From_Location = l1.Loc_Code
                    INNER JOIN Location_Master l2 ON sa.Stock_Adj_To_Location = l2.Loc_Code
                    INNER JOIN DEPT_Master d ON sa.Stock_Adj_Dept_Code = d.Dept_Code
                    INNER JOIN Approval_Status_Master asm ON saa.Approval_Status = asm.Approval_Status_Code
                    WHERE saa.Approval_Status = @PendingStatus
                    AND sa.Stock_Adj_REC_Type = 12  -- Only Stock Increase (STIN) requires approval
                    ORDER BY sa.Stock_Adj_Date DESC, sa.Stock_Adj_REC_Number DESC";

                var parameters = new Dictionary<string, object>
                {
                    { "@PendingStatus", ApprovalStatusConstants.Pending }
                };

                var approvals = await _databaseHelper.QueryAsync<ApprovalViewModel>(query, parameters);

                return Json(new { success = true, data = approvals }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // POST: ProcessApproval
        [HttpPost]
        public async Task<JsonResult> ProcessApproval(int id, string action, string remarks)
        {
            try
            {
                var userId = SessionHelper.GetUserId();
                var isApprover = SessionHelper.IsApprover();

                if (!isApprover)
                {
                    return Json(new { success = false, message = "You do not have permission to process approvals." });
                }

                // Extract composite key from id
                // Id format: ApprovalLevel * 1000000 + FinYear * 10000 + RecType * 1000 + RecNumber
                int approvalLevel = id / 1000000;
                int remainder = id % 1000000;
                int finYear = remainder / 10000;
                remainder = remainder % 10000;
                int recType = remainder / 1000;
                int recNumber = remainder % 1000;

                int newStatus = action.ToLower() == "approve"
                    ? ApprovalStatusConstants.Approved
                    : ApprovalStatusConstants.Rejected;

                var query = @"
                    UPDATE Stock_Adjustment_Approval
                    SET Approval_Status = @ApprovalStatus,
                        Approval_User_ID = @UserId,
                        Approval_Date = @ApprovalDate,
                        Approval_Comments = @Comments
                    WHERE Approval_Fin_Year = @FinYear
                    AND Approval_REC_Type = @RecType
                    AND Approval_REC_Number = @RecNumber
                    AND Approval_Level = @ApprovalLevel";

                var parameters = new Dictionary<string, object>
                {
                    { "@ApprovalStatus", newStatus },
                    { "@UserId", userId },
                    { "@ApprovalDate", DateTime.Now },
                    { "@Comments", remarks },
                    { "@FinYear", finYear },
                    { "@RecType", recType },
                    { "@RecNumber", recNumber },
                    { "@ApprovalLevel", approvalLevel }
                };

                await _databaseHelper.ExecuteNonQueryAsync(query, parameters);

                string message = action.ToLower() == "approve"
                    ? "Stock adjustment approved successfully."
                    : "Stock adjustment rejected successfully.";

                return Json(new { success = true, message = message });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
