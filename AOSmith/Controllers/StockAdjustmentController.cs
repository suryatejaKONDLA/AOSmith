using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using AOSmith.Helpers;
using AOSmith.Models;
using AOSmith.Filters;

namespace AOSmith.Controllers
{
    [AuthFilter]
    public class StockAdjustmentController : Controller
    {
        private readonly IDatabaseHelper _dbHelper;

        public StockAdjustmentController()
        {
            _dbHelper = new DatabaseHelper();
        }

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

            // Locations
            var locations = await _dbHelper.QueryAsync<LocationMaster>(
                "SELECT LOCATION, [DESC] FROM Location_Master WHERE INACTIVE = 0 ORDER BY [DESC]");
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
        public async Task<JsonResult> SaveStockAdjustment(StockAdjustmentSaveModel model)
        {
            try
            {
                // This will be implemented later
                // For now, just return success for frontend testing
                return Json(new { success = true, message = "Stock adjustment saved successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
