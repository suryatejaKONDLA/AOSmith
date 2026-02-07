using System;
using System.Collections.Generic;
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
    public class StockAdjustmentController : Controller
    {
        private readonly IDatabaseHelper _dbHelper = new DatabaseHelper();
        private readonly SageApiService _sageService = new SageApiService();

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

            // Locations - fetched from Sage API at runtime via AJAX (no DB query needed)
            ViewBag.Locations = new SelectList(new List<LocationMaster>(), "LOCATION", "DESC");

            // Items - fetched from Sage API at runtime via AJAX (no DB query needed)
            ViewBag.Items = new SelectList(new List<ItemMaster>(), "ITEMNO", "SDESCRIPT");
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
            try
            {
                var response = await _sageService.GetItemsAsync();
                if (response?.icitems != null)
                {
                    var item = response.icitems.FirstOrDefault(i =>
                        string.Equals(i.itemno?.Trim(), itemCode?.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (item != null)
                    {
                        return Json(new { success = true, description = item.desc?.Trim() });
                    }
                }

                return Json(new { success = false });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Fetch items from Sage API with optional search term
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> SearchItems(string term)
        {
            try
            {
                var response = await _sageService.GetItemsAsync();
                if (response?.icitems == null || !response.icitems.Any())
                {
                    var errorMsg = response?.Errors != null && response.Errors.Any()
                        ? string.Join("; ", response.Errors)
                        : "Failed to load items from Sage API";
                    return Json(new { success = false, message = errorMsg }, JsonRequestBehavior.AllowGet);
                }

                var items = response.icitems
                    .Where(i => !i.inactive)
                    .Select(i => new
                    {
                        id = i.itemno?.Trim(),
                        text = $"{i.itemno?.Trim()} - {i.desc?.Trim()}"
                    });

                // Apply search filter if term provided
                if (!string.IsNullOrWhiteSpace(term))
                {
                    var searchTerm = term.Trim().ToLower();
                    items = items.Where(i =>
                        i.id.ToLower().Contains(searchTerm) ||
                        i.text.ToLower().Contains(searchTerm));
                }

                return Json(new { success = true, results = items.OrderBy(i => i.text).ToList() }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Fetch locations from Sage API with optional search term
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> SearchLocations(string term)
        {
            try
            {
                var response = await _sageService.GetLocationsAsync();
                if (response?.locations == null)
                {
                    return Json(new { success = false, message = "Failed to load locations from Sage API" }, JsonRequestBehavior.AllowGet);
                }

                var locations = response.locations
                    .Select(l => new
                    {
                        id = l.location?.Trim(),
                        text = $"{l.location?.Trim()} - {l.desc?.Trim()}"
                    });

                // Apply search filter if term provided
                if (!string.IsNullOrWhiteSpace(term))
                {
                    var searchTerm = term.Trim().ToLower();
                    locations = locations.Where(l =>
                        l.id.ToLower().Contains(searchTerm) ||
                        l.text.ToLower().Contains(searchTerm));
                }

                return Json(new { success = true, results = locations.OrderBy(l => l.text).ToList() }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
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
                var sageResponses = new List<object>();

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

                        // RecType 10 (Stock Decrease) → send to Sage Transfer Entry immediately
                        if (recType == 10)
                        {
                            var recNumber = ExtractRecNumber(result.ResultMessage);
                            var documentReference = ExtractDocumentReference(result.ResultMessage);

                            var sageResponse = await _sageService.SendTransferEntryAsync(
                                items, transactionDate, recNumber, recType);

                            sageResponses.Add(new
                            {
                                recType,
                                recNumber,
                                documentReference,
                                sageStatus = sageResponse.Status,
                                sageMessage = sageResponse.Message,
                                sageRawResponse = sageResponse.RawResponse,
                                sageRawRequest = sageResponse.RawRequest
                            });
                        }
                        // RecType 12 (Stock Increase) → DB only, Sage called after approval
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
                        resultType = "error",
                        sageResults = sageResponses
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
                        resultType = "success",
                        sageResults = sageResponses
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

        private int ExtractRecNumber(string resultMessage)
        {
            if (string.IsNullOrEmpty(resultMessage)) return 0;

            // Try to extract Document Reference format: 202526/STDL/1 or 202526/STIN/1
            var docRefMatch = System.Text.RegularExpressions.Regex.Match(resultMessage, @"(\d{6})/([A-Z]+)/(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (docRefMatch.Success && int.TryParse(docRefMatch.Groups[3].Value, out var recNum))
            {
                return recNum;
            }

            // Fallback: try to find any number in the message
            var numMatch = System.Text.RegularExpressions.Regex.Match(resultMessage, @"(\d+)");
            if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out var num))
            {
                return num;
            }

            return 0;
        }

        private string ExtractDocumentReference(string resultMessage)
        {
            if (string.IsNullOrEmpty(resultMessage)) return "";

            // Extract Document Reference format: 202526/STDL/1 or 202526/STIN/1
            var docRefMatch = System.Text.RegularExpressions.Regex.Match(resultMessage, @"(\d{6}/[A-Z]+/\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (docRefMatch.Success)
            {
                return docRefMatch.Groups[1].Value;
            }

            return "";
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
