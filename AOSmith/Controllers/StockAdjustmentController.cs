using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using AOSmith.Filters;
using AOSmith.Helpers;
using AOSmith.Models;
using AOSmith.Services;
using Newtonsoft.Json;
using OfficeOpenXml;
using OfficeOpenXml.DataValidation;
using OfficeOpenXml.Style;

namespace AOSmith.Controllers
{
    [AuthFilter]
    public class StockAdjustmentController : Controller
    {
        private readonly IDatabaseHelper _dbHelper = new DatabaseHelper();
        private readonly SageApiService _sageService = new SageApiService();
        private readonly EmailService _emailService = new EmailService();

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

        /// <summary>
        /// Fetch item cost (stdcost) from Sage ItemSearch API
        /// </summary>
        [HttpPost]
        public async Task<JsonResult> GetItemCost(string itemCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(itemCode))
                {
                    return Json(new { success = false, message = "Item code is required" });
                }

                var response = await _sageService.SearchItemAsync(itemCode.Trim());
                if (response?.icitems != null && response.icitems.Any())
                {
                    var item = response.icitems.FirstOrDefault();
                    if (item != null)
                    {
                        return Json(new { success = true, cost = item.stdcost });
                    }
                }

                return Json(new { success = false, message = "Item not found" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Export an Excel template with data validation dropdowns populated from Sage API and DB
        /// </summary>
        [HttpGet]
        public async Task<ActionResult> ExportTemplate()
        {
            try
            {
                // Fetch REC Types from DB
                var recTypes = await _dbHelper.QueryAsync<RecTypeMaster>(
                    "SELECT * FROM REC_Type_Master WHERE REC_Type IN (10, 12) ORDER BY REC_Order");
                var recTypeList = recTypes.ToList();

                // Fetch Items from Sage API
                var itemsResponse = await _sageService.GetItemsAsync();
                var sageItems = itemsResponse?.icitems?.Where(i => !i.inactive).ToList() ?? new List<SageItem>();

                // Fetch Locations from Sage API
                var locationsResponse = await _sageService.GetLocationsAsync();
                var sageLocations = locationsResponse?.locations?.ToList() ?? new List<SageLocation>();

                using (var package = new ExcelPackage())
                {
                    // ===== Main data entry sheet =====
                    var ws = package.Workbook.Worksheets.Add("StockAdjustment");

                    // Headers (4 columns: Adj Type, Item Code, Location, Quantity)
                    var headers = new[] { "Adjustment Type", "Item Code", "Location", "Quantity" };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        ws.Cells[1, i + 1].Value = headers[i];
                        ws.Cells[1, i + 1].Style.Font.Bold = true;
                        ws.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        ws.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(79, 129, 189));
                        ws.Cells[1, i + 1].Style.Font.Color.SetColor(System.Drawing.Color.White);
                        ws.Cells[1, i + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    }

                    // ===== Hidden "Lists" sheet for validation data =====
                    var listSheet = package.Workbook.Worksheets.Add("Lists");
                    listSheet.Hidden = eWorkSheetHidden.VeryHidden;

                    // Column A: Adjustment Types (REC_Type - REC_Name2)
                    listSheet.Cells[1, 1].Value = "AdjustmentType";
                    for (int i = 0; i < recTypeList.Count; i++)
                    {
                        listSheet.Cells[i + 2, 1].Value = $"{recTypeList[i].REC_Type} - {recTypeList[i].REC_Name2}";
                    }

                    // Column B: Item Codes
                    listSheet.Cells[1, 2].Value = "ItemCode";
                    for (int i = 0; i < sageItems.Count; i++)
                    {
                        listSheet.Cells[i + 2, 2].Value = sageItems[i].itemno?.Trim();
                    }

                    // Column C: Locations
                    listSheet.Cells[1, 3].Value = "Location";
                    for (int i = 0; i < sageLocations.Count; i++)
                    {
                        listSheet.Cells[i + 2, 3].Value = sageLocations[i].location?.Trim();
                    }

                    // ===== Data Validation on main sheet (rows 2-1000) =====
                    int dataRows = 1000;

                    // Adjustment Type dropdown (Column A)
                    if (recTypeList.Count > 0)
                    {
                        var adjValidation = ws.DataValidations.AddListValidation(
                            ExcelCellBase.GetAddress(2, 1, dataRows, 1));
                        adjValidation.ShowErrorMessage = true;
                        adjValidation.ErrorTitle = "Invalid Adjustment Type";
                        adjValidation.Error = "Please select a valid Adjustment Type from the dropdown.";
                        adjValidation.Formula.ExcelFormula =
                            $"Lists!$A$2:$A${recTypeList.Count + 1}";
                    }

                    // Item Code dropdown (Column B)
                    if (sageItems.Count > 0)
                    {
                        var itemValidation = ws.DataValidations.AddListValidation(
                            ExcelCellBase.GetAddress(2, 2, dataRows, 2));
                        itemValidation.ShowErrorMessage = true;
                        itemValidation.ErrorTitle = "Invalid Item Code";
                        itemValidation.Error = "Please select a valid Item Code from the dropdown.";
                        itemValidation.Formula.ExcelFormula =
                            $"Lists!$B$2:$B${sageItems.Count + 1}";
                    }

                    // Location dropdown (Column C)
                    if (sageLocations.Count > 0)
                    {
                        var locValidation = ws.DataValidations.AddListValidation(
                            ExcelCellBase.GetAddress(2, 3, dataRows, 3));
                        locValidation.ShowErrorMessage = true;
                        locValidation.ErrorTitle = "Invalid Location";
                        locValidation.Error = "Please select a valid Location from the dropdown.";
                        locValidation.Formula.ExcelFormula =
                            $"Lists!$C$2:$C${sageLocations.Count + 1}";
                    }

                    // Quantity - number validation (Column D)
                    var qtyValidation = ws.DataValidations.AddDecimalValidation(
                        ExcelCellBase.GetAddress(2, 4, dataRows, 4));
                    qtyValidation.ShowErrorMessage = true;
                    qtyValidation.ErrorTitle = "Invalid Quantity";
                    qtyValidation.Error = "Quantity must be a positive number.";
                    qtyValidation.Operator = ExcelDataValidationOperator.greaterThan;
                    qtyValidation.Formula.Value = 0;

                    // Auto-fit columns
                    ws.Column(1).Width = 25;
                    ws.Column(2).Width = 20;
                    ws.Column(3).Width = 20;
                    ws.Column(4).Width = 15;

                    // Freeze header row
                    ws.View.FreezePanes(2, 1);

                    var fileBytes = package.GetAsByteArray();
                    return File(fileBytes,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "StockAdjustment_Template.xlsx");
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Import an Excel file, validate each cell against Sage data, return validated rows or errors.
        /// Template has 4 columns: Adjustment Type, Item Code, Location, Quantity.
        /// From/To locations are derived based on the Adjustment Type.
        /// Cost is fetched from Sage ItemSearch API for each valid item.
        /// </summary>
        [HttpPost]
        public async Task<JsonResult> ImportExcel()
        {
            try
            {
                if (Request.Files.Count == 0 || Request.Files[0] == null || Request.Files[0].ContentLength == 0)
                {
                    return Json(new { success = false, message = "No file uploaded." });
                }

                var file = Request.Files[0];
                var ext = Path.GetExtension(file.FileName)?.ToLower();
                if (ext != ".xlsx" && ext != ".xls")
                {
                    return Json(new { success = false, message = "Only Excel files (.xlsx) are supported." });
                }

                // Load master data for validation
                var recTypes = (await _dbHelper.QueryAsync<RecTypeMaster>(
                    "SELECT * FROM REC_Type_Master WHERE REC_Type IN (10, 12) ORDER BY REC_Order")).ToList();

                var itemsResponse = await _sageService.GetItemsAsync();
                var sageItems = itemsResponse?.icitems?.Where(i => !i.inactive).ToList() ?? new List<SageItem>();

                var locationsResponse = await _sageService.GetLocationsAsync();
                var sageLocations = locationsResponse?.locations?.ToList() ?? new List<SageLocation>();

                // Get default location from App Options (for Stock Decrease To Location)
                var appOptionsSql = @"SELECT TOP 1 RTRIM(APP_Default_Location) as AppDefaultLocation FROM APP_Options ORDER BY APP_ID";
                var appOptions = (await _dbHelper.QueryAsync<ApplicationOptions>(appOptionsSql)).FirstOrDefault();
                var defaultLocation = appOptions?.AppDefaultLocation?.Trim() ?? "";

                // Build lookup sets (case-insensitive)
                var validItemCodes = new HashSet<string>(
                    sageItems.Select(i => i.itemno?.Trim().ToUpper()).Where(x => x != null),
                    StringComparer.OrdinalIgnoreCase);

                var validLocationCodes = new HashSet<string>(
                    sageLocations.Select(l => l.location?.Trim().ToUpper()).Where(x => x != null),
                    StringComparer.OrdinalIgnoreCase);

                var recTypeMap = recTypes.ToDictionary(
                    r => r.REC_Type,
                    r => r.REC_Name2);

                var errors = new List<object>();
                var validRows = new List<object>();

                using (var stream = file.InputStream)
                using (var package = new ExcelPackage(stream))
                {
                    var ws = package.Workbook.Worksheets.FirstOrDefault();
                    if (ws == null)
                    {
                        return Json(new { success = false, message = "Excel file has no worksheets." });
                    }

                    int totalRows = ws.Dimension?.End.Row ?? 0;
                    if (totalRows < 2)
                    {
                        return Json(new { success = false, message = "Excel file has no data rows. Please fill in data starting from row 2." });
                    }

                    for (int row = 2; row <= totalRows; row++)
                    {
                        var adjTypeRaw = ws.Cells[row, 1].Text?.Trim(); // Column A
                        var itemCode = ws.Cells[row, 2].Text?.Trim();   // Column B
                        var location = ws.Cells[row, 3].Text?.Trim();   // Column C
                        var qtyRaw = ws.Cells[row, 4].Text?.Trim();     // Column D

                        // Skip completely empty rows
                        if (string.IsNullOrWhiteSpace(adjTypeRaw) &&
                            string.IsNullOrWhiteSpace(itemCode) &&
                            string.IsNullOrWhiteSpace(location) &&
                            string.IsNullOrWhiteSpace(qtyRaw))
                        {
                            continue;
                        }

                        bool rowHasError = false;

                        // Validate Adjustment Type (expecting "10 - STOCK DECREASE" or "12 - STOCK INCREASE")
                        int parsedRecType = 0;
                        string recTypeName = "";
                        if (string.IsNullOrWhiteSpace(adjTypeRaw))
                        {
                            errors.Add(new { row, column = "A", cell = $"A{row}", field = "Adjustment Type", message = "Adjustment Type is required." });
                            rowHasError = true;
                        }
                        else
                        {
                            var parts = adjTypeRaw.Split(new[] { " - " }, StringSplitOptions.None);
                            if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out parsedRecType) && recTypeMap.ContainsKey(parsedRecType))
                            {
                                recTypeName = recTypeMap[parsedRecType];
                            }
                            else
                            {
                                errors.Add(new { row, column = "A", cell = $"A{row}", field = "Adjustment Type", message = $"Invalid Adjustment Type '{adjTypeRaw}'. Use the dropdown values." });
                                rowHasError = true;
                            }
                        }

                        // Validate Item Code
                        string itemDescription = "";
                        decimal itemCost = 0;
                        if (string.IsNullOrWhiteSpace(itemCode))
                        {
                            errors.Add(new { row, column = "B", cell = $"B{row}", field = "Item Code", message = "Item Code is required." });
                            rowHasError = true;
                        }
                        else if (!validItemCodes.Contains(itemCode.ToUpper()))
                        {
                            errors.Add(new { row, column = "B", cell = $"B{row}", field = "Item Code", message = $"Item Code '{itemCode}' does not exist in Sage." });
                            rowHasError = true;
                        }
                        else
                        {
                            var foundItem = sageItems.FirstOrDefault(i =>
                                string.Equals(i.itemno?.Trim(), itemCode, StringComparison.OrdinalIgnoreCase));
                            itemDescription = foundItem?.desc?.Trim() ?? "";
                            itemCost = foundItem?.stdcost ?? 0;
                        }

                        // Validate Location
                        string locationName = "";
                        if (string.IsNullOrWhiteSpace(location))
                        {
                            errors.Add(new { row, column = "C", cell = $"C{row}", field = "Location", message = "Location is required." });
                            rowHasError = true;
                        }
                        else if (!validLocationCodes.Contains(location.ToUpper()))
                        {
                            errors.Add(new { row, column = "C", cell = $"C{row}", field = "Location", message = $"Location '{location}' does not exist in Sage." });
                            rowHasError = true;
                        }
                        else
                        {
                            var foundLoc = sageLocations.FirstOrDefault(l =>
                                string.Equals(l.location?.Trim(), location, StringComparison.OrdinalIgnoreCase));
                            locationName = $"{foundLoc?.location?.Trim()} - {foundLoc?.desc?.Trim()}";
                        }

                        // Validate Quantity
                        decimal parsedQty = 0;
                        if (string.IsNullOrWhiteSpace(qtyRaw))
                        {
                            errors.Add(new { row, column = "D", cell = $"D{row}", field = "Quantity", message = "Quantity is required." });
                            rowHasError = true;
                        }
                        else if (!decimal.TryParse(qtyRaw, out parsedQty) || parsedQty <= 0)
                        {
                            errors.Add(new { row, column = "D", cell = $"D{row}", field = "Quantity", message = $"Quantity '{qtyRaw}' must be a positive number." });
                            rowHasError = true;
                        }

                        if (!rowHasError)
                        {
                            // Derive From/To based on Adjustment Type
                            string fromLoc, toLoc, fromLocName, toLocName;
                            if (parsedRecType == 12)
                            {
                                // Stock Increase: both From and To = selected Location
                                fromLoc = location.Trim();
                                toLoc = location.Trim();
                                fromLocName = locationName;
                                toLocName = locationName;
                            }
                            else
                            {
                                // Stock Decrease (10): From = selected Location, To = default from App Options
                                fromLoc = location.Trim();
                                toLoc = defaultLocation;
                                fromLocName = locationName;
                                var defLocObj = sageLocations.FirstOrDefault(l =>
                                    string.Equals(l.location?.Trim(), defaultLocation, StringComparison.OrdinalIgnoreCase));
                                toLocName = defLocObj != null
                                    ? $"{defLocObj.location?.Trim()} - {defLocObj.desc?.Trim()}"
                                    : defaultLocation;
                            }

                            validRows.Add(new
                            {
                                recType = parsedRecType,
                                recTypeName = $"{parsedRecType} - {recTypeName}",
                                itemCode = itemCode?.Trim(),
                                itemDescription,
                                location = location?.Trim(),
                                locationName,
                                fromLocation = fromLoc,
                                fromLocationName = fromLocName,
                                toLocation = toLoc,
                                toLocationName = toLocName,
                                qty = parsedQty,
                                cost = itemCost
                            });
                        }
                    }
                }

                if (errors.Any())
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Found {errors.Count} error(s) in the uploaded file.",
                        errors,
                        validRowCount = validRows.Count,
                        totalErrorCount = errors.Count
                    });
                }

                if (!validRows.Any())
                {
                    return Json(new { success = false, message = "No valid data rows found in the file." });
                }

                return Json(new
                {
                    success = true,
                    message = $"{validRows.Count} row(s) imported successfully.",
                    data = validRows
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error processing Excel file: " + ex.Message });
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

                        // Extract document info for email
                        var recNumber = ExtractRecNumber(result.ResultMessage);
                        var documentReference = ExtractDocumentReference(result.ResultMessage);

                        // Send email notification to L1 approver (non-blocking)
                        try
                        {
                            var finYear = 0;
                            var finYearMatch = System.Text.RegularExpressions.Regex.Match(
                                result.ResultMessage, @"(\d{6})/",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (finYearMatch.Success) int.TryParse(finYearMatch.Groups[1].Value, out finYear);

                            await _emailService.SendRecordCreatedEmailAsync(finYear, recType, recNumber, documentReference);
                        }
                        catch { /* email failure should not break save */ }

                        // RecType 10 (Stock Decrease) → send to Sage Transfer Entry immediately
                        if (recType == 10)
                        {
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
            table.Columns.Add("StockCost", typeof(decimal));

            foreach (var item in lineItems)
            {
                table.Rows.Add(
                    item.StockRecSno,
                    item.FromLocation?.Trim() ?? string.Empty,
                    item.ToLocation?.Trim() ?? string.Empty,
                    item.ItemCode?.Trim() ?? string.Empty,
                    item.Qty,
                    item.Cost
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
