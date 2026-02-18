using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AOSmith.Helpers;
using AOSmith.Models;
using Newtonsoft.Json;

namespace AOSmith.Services
{
    public class SageApiService
    {
        private const string SageTransferApiUrl = "https://sagetest.aosmith.in/Sage300.WebAPI2024/api/TransferEntry";
        private const string SageAdjustmentApiUrl = "https://sagetest.aosmith.in/Sage300.WebAPI2024/api/MultiLineAdjustmentEntry";
        private const string SageItemsApiUrl = "https://sagetest.aosmith.in/Sage300.WebAPI2024/api/icitems";
        private const string SageItemSearchApiUrl = "https://sagetest.aosmith.in/Sage300.WebAPI2024/api/ItemSearch";
        private const string SageLocationsApiUrl = "https://sagetest.aosmith.in/Sage300.WebAPI2024/api/Locations";
        private const string SageICStockApiUrl = "https://sagetest.aosmith.in/Sage300.WebAPI2024/api/GetICStock";

        private readonly IDatabaseHelper _dbHelper;

        public SageApiService()
        {
            _dbHelper = new DatabaseHelper();
        }

        public SageApiService(IDatabaseHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        private const bool ENABLE_CACHE = true;

        private static readonly HttpClient _httpClient;

        // In-memory cache keyed by companyName
        private static readonly Dictionary<string, SageItemResponse> _cachedItemsByCompany = new Dictionary<string, SageItemResponse>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, SageLocationResponse> _cachedLocationsByCompany = new Dictionary<string, SageLocationResponse>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _itemsCacheLock = new object();
        private static readonly object _locationsCacheLock = new object();

        static SageApiService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(120)
            };
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }

        // ========== Credential & RecType helpers ==========

        private async Task<SageCredentials> GetSageCredentialsAsync(string companyName)
        {
            var sql = @"SELECT TOP 1
                RTRIM(Company_Name) AS CompanyId,
                RTRIM(Company_Login_ID) AS UserId,
                RTRIM(Company_Password) AS Password
                FROM Company_Master WHERE Company_Name = @CompanyName";

            var parameters = new Dictionary<string, object> { { "@CompanyName", companyName } };
            var cred = await _dbHelper.QuerySingleAsync<SageCredentials>(sql, parameters);

            if (cred == null)
                throw new Exception($"Company '{companyName}' not found in Company_Master.");

            return cred;
        }

        private async Task<string> GetRecTypeNameAsync(int recType)
        {
            var sql = "SELECT TOP 1 RTRIM(REC_Name) AS Value FROM REC_Type_Master WHERE REC_Type = @RecType";
            var parameters = new Dictionary<string, object> { { "@RecType", recType } };
            var result = await _dbHelper.QuerySingleAsync<StringResult>(sql, parameters);
            return result?.Value ?? "UNK";
        }

        private async Task<string> GetTranNumberPrefixAsync()
        {
            var sql = "SELECT TOP 1 RTRIM(APP_TranNumber_Prefix) AS Value FROM APP_Options ORDER BY APP_ID";
            var result = await _dbHelper.QuerySingleAsync<StringResult>(sql);
            return result?.Value ?? "SAGE";
        }

        private async Task<string> GetAdjuNumberPrefixAsync()
        {
            var sql = "SELECT TOP 1 RTRIM(APP_AdjuNumber_Prefix) AS Value FROM APP_Options ORDER BY APP_ID";
            var result = await _dbHelper.QuerySingleAsync<StringResult>(sql);
            return result?.Value ?? "ADJ";
        }

        private async Task<string> GetReveNumberPrefixAsync()
        {
            var sql = "SELECT TOP 1 RTRIM(APP_ReveNumber_Prefix) AS Value FROM APP_Options ORDER BY APP_ID";
            var result = await _dbHelper.QuerySingleAsync<StringResult>(sql);
            return result?.Value ?? "REV";
        }

        // ========== Transfer Entry ==========

        public async Task<SageTransferEntryResponse> SendTransferEntryAsync(
            string companyName,
            int finYear,
            List<StockAdjustmentLineItem> lineItems,
            DateTime transactionDate,
            int recNumber,
            int recType)
        {
            string jsonPayload = "";
            try
            {
                var creds = await GetSageCredentialsAsync(companyName);
                var recName = await GetRecTypeNameAsync(recType);
                var tranPrefix = await GetTranNumberPrefixAsync();
                var revePrefix = await GetReveNumberPrefixAsync();

                var request = BuildRequest(creds, companyName, finYear, recName, tranPrefix, revePrefix, lineItems, transactionDate, recNumber, recType);
                jsonPayload = JsonConvert.SerializeObject(request, Formatting.Indented);

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var httpResponse = await _httpClient.PostAsync(SageTransferApiUrl, content);

                var responseBody = await httpResponse.Content.ReadAsStringAsync();

                SageTransferEntryResponse sageResponse;
                try
                {
                    sageResponse = JsonConvert.DeserializeObject<SageTransferEntryResponse>(responseBody);
                }
                catch
                {
                    sageResponse = new SageTransferEntryResponse
                    {
                        Status = httpResponse.IsSuccessStatusCode ? "Success" : "Error",
                        Message = responseBody
                    };
                }

                sageResponse.RawResponse = responseBody;
                sageResponse.RawRequest = jsonPayload;

                if (!httpResponse.IsSuccessStatusCode && sageResponse.Errors == null)
                {
                    sageResponse.Errors = new System.Collections.Generic.List<string>
                    {
                        $"HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}"
                    };
                }

                return sageResponse;
            }
            catch (TaskCanceledException)
            {
                return new SageTransferEntryResponse
                {
                    Status = "Error",
                    Message = "Sage API request timed out. Please try again.",
                    Errors = new List<string> { "Sage API request timed out." },
                    RawResponse = "Request timed out after 60 seconds.",
                    RawRequest = jsonPayload
                };
            }
            catch (Exception ex)
            {
                return new SageTransferEntryResponse
                {
                    Status = "Error",
                    Message = $"Failed to connect to Sage API: {ex.Message}",
                    Errors = new List<string> { $"Failed to connect to Sage API: {ex.Message}" },
                    RawResponse = ex.ToString(),
                    RawRequest = jsonPayload
                };
            }
        }

        private SageTransferEntryRequest BuildRequest(
            SageCredentials creds,
            string companyName,
            int finYear,
            string recName,
            string tranPrefix,
            string revePrefix,
            List<StockAdjustmentLineItem> lineItems,
            DateTime transactionDate,
            int recNumber,
            int recType)
        {
            var transDateStr = transactionDate.ToString("yyyy-MM-ddTHH:mm:ss");
            var expArDateStr = transactionDate.AddMonths(1).ToString("yyyy-MM-ddTHH:mm:ss");
            var recTypeName2 = recType == 12 ? "INCREASE" : recType == 14 ? "REVERSAL" : "DECREASE";
            var docNumPrefix = recType == 14 ? revePrefix : tranPrefix;
            var docReference = $"{finYear}/{companyName}/{recName}/{recNumber}";

            var request = new SageTransferEntryRequest
            {
                UserId = creds.UserId,
                Password = creds.Password,
                CompanyId = creds.CompanyId,
                DocNum = $"{docNumPrefix}{companyName}{recNumber.ToString().PadLeft(6, '0')}",
                Reference = docReference,
                TransDate = transDateStr,
                ExpArDate = expArDateStr,
                HdrDesc = $"Inventory transfer - Stock {recTypeName2} #{recNumber}",
                TransType = 6,
                TransHeaderOptFields = new List<SageOptField>(),
                Items = lineItems.Select(item => new SageTransferItem
                {
                    FromLoc = item.FromLocation?.Trim(),
                    ToLoc = item.ToLocation?.Trim(),
                    ItemNo = item.ItemCode?.Trim(),
                    Quantity = item.Qty,
                    Comments = $"Stock moved to {item.ToLocation?.Trim()} location for adjustment",
                    TransDetailOptFields = new List<SageOptField>()
                }).ToList()
            };

            return request;
        }

        // ========== Adjustment Entry (RecType 12 - Stock Increase) ==========

        public async Task<SageAdjustmentEntryResponse> SendAdjustmentEntryAsync(
            string companyName,
            int finYear,
            List<ApprovalLineItem> lineItems,
            DateTime transactionDate,
            int recNumber)
        {
            string jsonPayload = "";
            try
            {
                var creds = await GetSageCredentialsAsync(companyName);
                var adjuPrefix = await GetAdjuNumberPrefixAsync();

                var request = BuildAdjustmentRequest(creds, companyName, finYear, adjuPrefix, lineItems, transactionDate, recNumber);
                jsonPayload = JsonConvert.SerializeObject(request, Formatting.Indented);

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var httpResponse = await _httpClient.PostAsync(SageAdjustmentApiUrl, content);

                var responseBody = await httpResponse.Content.ReadAsStringAsync();

                SageAdjustmentEntryResponse sageResponse;
                try
                {
                    sageResponse = JsonConvert.DeserializeObject<SageAdjustmentEntryResponse>(responseBody);
                }
                catch
                {
                    sageResponse = new SageAdjustmentEntryResponse
                    {
                        Status = httpResponse.IsSuccessStatusCode ? "Success" : "Error",
                        Message = responseBody
                    };
                }

                sageResponse.RawResponse = responseBody;
                sageResponse.RawRequest = jsonPayload;

                if (!httpResponse.IsSuccessStatusCode && sageResponse.Errors == null)
                {
                    sageResponse.Errors = new System.Collections.Generic.List<string>
                    {
                        $"HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}"
                    };
                }

                return sageResponse;
            }
            catch (TaskCanceledException)
            {
                return new SageAdjustmentEntryResponse
                {
                    Status = "Error",
                    Message = "Sage API request timed out. Please try again.",
                    Errors = new List<string> { "Sage API request timed out." },
                    RawResponse = "Request timed out after 60 seconds.",
                    RawRequest = jsonPayload
                };
            }
            catch (Exception ex)
            {
                return new SageAdjustmentEntryResponse
                {
                    Status = "Error",
                    Message = $"Failed to connect to Sage API: {ex.Message}",
                    Errors = new List<string> { $"Failed to connect to Sage API: {ex.Message}" },
                    RawResponse = ex.ToString(),
                    RawRequest = jsonPayload
                };
            }
        }

        private SageAdjustmentEntryRequest BuildAdjustmentRequest(
            SageCredentials creds,
            string companyName,
            int finYear,
            string adjuPrefix,
            List<ApprovalLineItem> lineItems,
            DateTime transactionDate,
            int recNumber)
        {
            var transDateStr = transactionDate.ToString("yyyy-MM-ddTHH:mm:ss");
            var docReference = $"{finYear}/{companyName}/{adjuPrefix}/{recNumber}";

            var request = new SageAdjustmentEntryRequest
            {
                UserId = creds.UserId,
                Password = creds.Password,
                CompanyId = creds.CompanyId,
                DocNum = $"{adjuPrefix}{companyName}{recNumber.ToString().PadLeft(6, '0')}",
                Reference = docReference,
                TransDate = transDateStr,
                HdrDesc = $"Stock adjustment #{recNumber} - Approved",
                AdjustmentHeaderOptFields = new List<SageOptField>(),
                Items = lineItems.Select(item => new SageAdjustmentItem
                {
                    AdjDetailOptFields = new List<SageOptField>(),
                    ItemNo = item.ItemCode?.Trim(),
                    Location = item.RecType == 12 ? item.ToLocation?.Trim() : item.FromLocation?.Trim(),
                    WoffAcct = "",
                    Quantity = item.Quantity,
                    ExtCost = item.Cost * item.Quantity,
                    TransType = item.RecType == 12 ? 5 : 6
                }).ToList()
            };

            return request;
        }

        // ========== Sage Master Data APIs ==========

        /// <summary>
        /// Fetch all active items from Sage300 ICITEM API (per-company cache).
        /// </summary>
        public async Task<SageItemResponse> GetItemsAsync(string companyName)
        {
            if (ENABLE_CACHE)
            {
                lock (_itemsCacheLock)
                {
                    if (_cachedItemsByCompany.TryGetValue(companyName, out var cached))
                        return cached;
                }
            }

            try
            {
                var creds = await GetSageCredentialsAsync(companyName);
                var url = $"{SageItemsApiUrl}?userid={Uri.EscapeDataString(creds.UserId)}&password={Uri.EscapeDataString(creds.Password)}&companyid={Uri.EscapeDataString(creds.CompanyId)}&ActiveItems=1";

                var response = await _httpClient.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                var result = JsonConvert.DeserializeObject<SageItemResponse>(responseBody);
                result = result ?? new SageItemResponse { icitems = new List<SageItem>(), status = -1 };

                if (ENABLE_CACHE && result.icitems != null && result.icitems.Count > 0)
                {
                    lock (_itemsCacheLock)
                    {
                        _cachedItemsByCompany[companyName] = result;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return new SageItemResponse
                {
                    icitems = new List<SageItem>(),
                    status = -1,
                    Errors = new List<string> { $"Failed to fetch items: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// Fetch all locations from Sage300 ICLOCATION API (per-company cache).
        /// </summary>
        public async Task<SageLocationResponse> GetLocationsAsync(string companyName)
        {
            if (ENABLE_CACHE)
            {
                lock (_locationsCacheLock)
                {
                    if (_cachedLocationsByCompany.TryGetValue(companyName, out var cached))
                        return cached;
                }
            }

            try
            {
                var creds = await GetSageCredentialsAsync(companyName);
                var url = $"{SageLocationsApiUrl}?userid={Uri.EscapeDataString(creds.UserId)}&password={Uri.EscapeDataString(creds.Password)}&companyid={Uri.EscapeDataString(creds.CompanyId)}";

                var response = await _httpClient.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                var result = JsonConvert.DeserializeObject<SageLocationResponse>(responseBody);
                result = result ?? new SageLocationResponse { locations = new List<SageLocation>(), status = -1 };

                if (ENABLE_CACHE && result.locations != null && result.locations.Count > 0)
                {
                    lock (_locationsCacheLock)
                    {
                        _cachedLocationsByCompany[companyName] = result;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return new SageLocationResponse
                {
                    locations = new List<SageLocation>(),
                    status = -1,
                    Errors = new List<string> { $"Failed to fetch locations: {ex.Message}" }
                };
            }
        }

        // ========== Item Search API (for stdcost) ==========

        /// <summary>
        /// Search a specific item from Sage300 ItemSearch API to get stdcost and optional fields.
        /// </summary>
        public async Task<SageItemResponse> SearchItemAsync(string companyName, string itemNo)
        {
            try
            {
                var creds = await GetSageCredentialsAsync(companyName);
                var url = $"{SageItemSearchApiUrl}?userid={Uri.EscapeDataString(creds.UserId)}&password={Uri.EscapeDataString(creds.Password)}&companyid={Uri.EscapeDataString(creds.CompanyId)}&itemno={Uri.EscapeDataString(itemNo)}&optfield=HSNCODE";

                var response = await _httpClient.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                var result = JsonConvert.DeserializeObject<SageItemResponse>(responseBody);
                return result ?? new SageItemResponse { icitems = new List<SageItem>(), status = -1 };
            }
            catch (Exception ex)
            {
                return new SageItemResponse
                {
                    icitems = new List<SageItem>(),
                    status = -1,
                    Errors = new List<string> { $"Failed to search item: {ex.Message}" }
                };
            }
        }

        // ========== IC Stock API (for qtonhand) ==========

        /// <summary>
        /// Fetch stock quantity on hand from Sage300 GetICStock API for a specific item and location.
        /// </summary>
        public async Task<SageICStockResponse> GetICStockAsync(string companyName, string itemNo, string location)
        {
            try
            {
                var creds = await GetSageCredentialsAsync(companyName);
                var url = $"{SageICStockApiUrl}?userid={Uri.EscapeDataString(creds.UserId)}&password={Uri.EscapeDataString(creds.Password)}&companyid={Uri.EscapeDataString(creds.CompanyId)}&itemno={Uri.EscapeDataString(itemNo)}&location={Uri.EscapeDataString(location)}";

                var response = await _httpClient.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                var result = JsonConvert.DeserializeObject<SageICStockResponse>(responseBody);
                return result ?? new SageICStockResponse { itemstock = new List<SageICStockItem>(), status = -1 };
            }
            catch (Exception ex)
            {
                return new SageICStockResponse
                {
                    itemstock = new List<SageICStockItem>(),
                    status = -1,
                    Errors = new List<string> { $"Failed to fetch stock quantity: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// Clears the cached items and locations for all companies.
        /// </summary>
        public static void ClearCache()
        {
            lock (_itemsCacheLock) { _cachedItemsByCompany.Clear(); }
            lock (_locationsCacheLock) { _cachedLocationsByCompany.Clear(); }
        }
    }

    /// <summary>
    /// Sage credentials loaded from Company_Master
    /// </summary>
    public class SageCredentials
    {
        public string CompanyId { get; set; }
        public string UserId { get; set; }
        public string Password { get; set; }
    }

    /// <summary>
    /// Helper for scalar string queries
    /// </summary>
    public class StringResult
    {
        public string Value { get; set; }
    }
}
