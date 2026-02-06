using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using AOSmith.Filters;
using AOSmith.Helpers;
using AOSmith.Models;
using Newtonsoft.Json;

namespace AOSmith.Controllers
{
    [AuthFilter]
    public class StockAdjustmentController : Controller
    {
        private readonly IDatabaseHelper _dbHelper = new DatabaseHelper();

        public async Task<ActionResult> Create()
        {
            var model = new StockAdjustmentViewModel
            {
                TransactionDate = DateTime.Today,
                CreatedBy = SessionHelper.GetUserName() ?? "",
                LineItems = new List<StockAdjustment>()
            };

            await LoadDropdowns();
            await LoadFileTypes();
            return View(model);
        }

        private async Task LoadDropdowns()
        {
            // REC Types (only 10 and 12)
            var recTypes = await _dbHelper.QueryAsync<RecTypeMaster>(
                "SELECT * FROM REC_Type_Master WHERE REC_Type IN (10, 12) ORDER BY REC_Order");
            ViewBag.RecTypes = new SelectList(recTypes, "REC_Type", "REC_Name2");

            // Locations (RTRIM to remove trailing spaces from CHAR(6))
            var locations = await _dbHelper.QueryAsync<LocationMaster>(
                "SELECT RTRIM(LOCATION) as LOCATION, [DESC] FROM Location_Master WHERE INACTIVE = 0 ORDER BY [DESC]");
            ViewBag.Locations = new SelectList(locations, "LOCATION", "DESC");

            // Items
            var items = await _dbHelper.QueryAsync<ItemMaster>(
                "SELECT ITEMNO, [DESC] AS SDESCRIPT FROM Item_Master ORDER BY [DESC]");
            ViewBag.Items = new SelectList(items, "ITEMNO", "SDESCRIPT");
        }

        private async Task LoadFileTypes()
        {
            var fileTypes = await _dbHelper.QueryAsync<FileTypeMaster>(
                "SELECT * FROM [dbo].[File_Type_Master] ORDER BY [File_Type_ID]");
            ViewBag.FileTypes = fileTypes;
        }

        [HttpPost]
        public async Task<JsonResult> GetItemDetails(string itemCode)
        {
            var item = await _dbHelper.QuerySingleAsync<ItemMaster>(
                "SELECT ITEMNO, [DESC] AS SDESCRIPT FROM Item_Master WHERE ITEMNO = @itemCode",
                new Dictionary<string, object> { { "@itemCode", itemCode } });

            if (item != null)
            {
                return Json(new { success = true, description = item.SDESCRIPT });
            }

            return Json(new { success = false });
        }

        [HttpPost]
        public async Task<JsonResult> GetDefaultLocation()
        {
            try
            {
                var sql = @"SELECT TOP 1 RTRIM(APP_Default_Location) as AppDefaultLocation
                           FROM APP_Options
                           ORDER BY APP_ID";

                var result = await _dbHelper.QueryAsync<ApplicationOptions>(sql);
                var options = result.FirstOrDefault();

                if (options != null && !string.IsNullOrWhiteSpace(options.AppDefaultLocation))
                {
                    return Json(new
                    {
                        success = true,
                        location = options.AppDefaultLocation.Trim()
                    });
                }

                return Json(new
                {
                    success = false,
                    message = "Default location not configured. Please configure Application Options first."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<JsonResult> SaveStockAdjustment()
        {
            try
            {
                // Parse FormData manually (JS sends lineItemsJson as a JSON string inside FormData)
                var transactionDateStr = Request.Form["transactionDate"];
                var lineItemsJson = Request.Form["lineItemsJson"];

                if (string.IsNullOrWhiteSpace(lineItemsJson))
                {
                    return Json(new { success = false, message = "No line items provided." });
                }

                // Parse the date
                if (!DateTime.TryParse(transactionDateStr, out var transactionDate))
                {
                    transactionDate = DateTime.Today;
                }

                // Deserialize line items from JSON
                var lineItems = JsonConvert.DeserializeObject<List<StockAdjustmentLineItem>>(lineItemsJson);

                if (lineItems == null || !lineItems.Any())
                {
                    return Json(new { success = false, message = "No line items provided." });
                }

                // Get Session User ID
                var sessionId = SessionHelper.GetUserId() ?? 1;

                // Group line items by RecType (since database structure requires separate transactions per RecType)
                var groupedByRecType = lineItems.GroupBy(x => x.RecType);

                var successMessages = new List<string>();
                var errorMessages = new List<string>();

                // Process each RecType group as a separate transaction
                foreach (var group in groupedByRecType)
                {
                    var recType = group.Key;
                    var items = group.ToList();

                    // Re-number the line items for this specific transaction (1, 2, 3, ...)
                    for (var i = 0; i < items.Count; i++)
                    {
                        items[i].StockRecSno = i + 1;
                    }

                    // Build the table-valued parameters for this RecType group
                    var stockDetailsTable = CreateStockDetailsDataTable(items);
                    var stockFilesTable = CreateStockFilesDataTable(); // Empty for now

                    // Prepare stored procedure parameters
                    var parameters = new List<System.Data.SqlClient.SqlParameter>
                    {
                        new System.Data.SqlClient.SqlParameter("@StockFinYear", System.Data.SqlDbType.Int)
                        {
                            Value = 0 // Auto-generate
                        },
                        new System.Data.SqlClient.SqlParameter("@StockRecType", System.Data.SqlDbType.Int)
                        {
                            Value = recType
                        },
                        new System.Data.SqlClient.SqlParameter("@StockRecNumber", System.Data.SqlDbType.Int)
                        {
                            Value = 0 // Insert mode (0 = new, >0 = edit)
                        },
                        new System.Data.SqlClient.SqlParameter("@StockDate", System.Data.SqlDbType.Date)
                        {
                            Value = transactionDate
                        },
                        new System.Data.SqlClient.SqlParameter("@StockDetails", System.Data.SqlDbType.Structured)
                        {
                            TypeName = "dbo.StockAdjustment_TBType",
                            Value = stockDetailsTable
                        },
                        new System.Data.SqlClient.SqlParameter("@StockFiles", System.Data.SqlDbType.Structured)
                        {
                            TypeName = "dbo.StockAdjustmentFile_TBType",
                            Value = stockFilesTable
                        },
                        new System.Data.SqlClient.SqlParameter("@Session_ID", System.Data.SqlDbType.Int)
                        {
                            Value = sessionId
                        }
                    };

                    // Execute stored procedure for this RecType group
                    var result = await _dbHelper.ExecuteStoredProcedureWithOutputsAsync(
                        "dbo.StockAdjustment_Insert",
                        parameters
                    );

                    if (result.ResultVal == 1)
                    {
                        successMessages.Add(result.ResultMessage);
                    }
                    else
                    {
                        errorMessages.Add($"RecType {recType}: {result.ResultMessage}");
                    }
                }

                // Return consolidated result
                if (errorMessages.Any())
                {
                    return Json(new
                    {
                        success = false,
                        message = "Some transactions failed:\n" + string.Join("\n", errorMessages),
                        successCount = successMessages.Count,
                        errorCount = errorMessages.Count,
                        resultType = "error"
                    });
                }
                else
                {
                    var consolidatedMessage = successMessages.Count == 1
                        ? successMessages[0]
                        : $"{successMessages.Count} transactions created successfully:\n" + string.Join("\n", successMessages);

                    return Json(new
                    {
                        success = true,
                        message = consolidatedMessage,
                        transactionCount = successMessages.Count,
                        resultType = "success"
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "An error occurred: " + ex.Message,
                    resultType = "error"
                });
            }
        }

        private System.Data.DataTable CreateStockDetailsDataTable(List<StockAdjustmentLineItem> lineItems)
        {
            var table = new System.Data.DataTable();
            table.Columns.Add("StockRecSno", typeof(int));
            table.Columns.Add("StockFromLocation", typeof(string));
            table.Columns.Add("StockToLocation", typeof(string));
            table.Columns.Add("StockItemCode", typeof(string));
            table.Columns.Add("StockQty", typeof(decimal));

            foreach (var item in lineItems)
            {
                table.Rows.Add(
                    item.StockRecSno,
                    item.FromLocation?.Trim() ?? string.Empty,
                    item.ToLocation?.Trim() ?? string.Empty,
                    item.ItemCode?.Trim() ?? string.Empty,
                    item.Qty
                );
            }

            return table;
        }

        private System.Data.DataTable CreateStockFilesDataTable()
        {
            var table = new System.Data.DataTable();
            table.Columns.Add("FileType", typeof(int));
            table.Columns.Add("FileName", typeof(string));
            table.Columns.Add("FilePath", typeof(string));
            table.Columns.Add("FileSizeKb", typeof(decimal));
            table.Columns.Add("FileExtension", typeof(string));

            // Return empty table for now (files handled separately)
            return table;
        }
    }
}
