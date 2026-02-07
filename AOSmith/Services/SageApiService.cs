using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AOSmith.Models;
using Newtonsoft.Json;

namespace AOSmith.Services
{
    public class SageApiService
    {
        private const string SageTransferApiUrl = "https://sagetest.aosmith.in/Sage300.WebAPI2024/api/TransferEntry";
        private const string SageAdjustmentApiUrl = "https://sagetest.aosmith.in/Sage300.WebAPI2024/api/AdjustmentEntry";
        private const string SageItemsApiUrl = "https://sagetest.aosmith.in/Sage300.WebAPI2024/api/icitems";
        private const string SageLocationsApiUrl = "https://sagetest.aosmith.in/Sage300.WebAPI2024/api/Locations";
        private const string SageUserId = "ADMIN";
        private const string SagePassword = "Sage@123$";
        private const string SageCompanyId = "SMDAT";

        /// <summary>
        /// Set to true to cache Items and Locations after the first API call.
        /// Subsequent requests will return data from memory cache.
        /// </summary>
        private const bool ENABLE_CACHE = true;

        private static readonly HttpClient _httpClient;

        // In-memory cache for items and locations
        private static SageItemResponse _cachedItems;
        private static SageLocationResponse _cachedLocations;
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

        public async Task<SageTransferEntryResponse> SendTransferEntryAsync(
            List<StockAdjustmentLineItem> lineItems,
            DateTime transactionDate,
            int recNumber,
            int recType)
        {
            string jsonPayload = "";
            try
            {
                var request = BuildRequest(lineItems, transactionDate, recNumber, recType);
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

                if (!httpResponse.IsSuccessStatusCode && string.IsNullOrEmpty(sageResponse.Status))
                {
                    sageResponse.Status = "Error";
                    sageResponse.Message = $"HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}";
                }

                return sageResponse;
            }
            catch (TaskCanceledException)
            {
                return new SageTransferEntryResponse
                {
                    Status = "Error",
                    Message = "Sage API request timed out. Please try again.",
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
                    RawResponse = ex.ToString(),
                    RawRequest = jsonPayload
                };
            }
        }

        private SageTransferEntryRequest BuildRequest(
            List<StockAdjustmentLineItem> lineItems,
            DateTime transactionDate,
            int recNumber,
            int recType)
        {
            var transDateStr = transactionDate.ToString("yyyy-MM-ddTHH:mm:ss");
            var expArDateStr = transactionDate.AddMonths(1).ToString("yyyy-MM-ddTHH:mm:ss");
            var recTypeName = recType == 12 ? "INCREASE" : "DECREASE";

            var request = new SageTransferEntryRequest
            {
                UserId = SageUserId,
                Password = SagePassword,
                CompanyId = SageCompanyId,
                DocNum = $"TRN{recNumber.ToString().PadLeft(6, '0')}",
                Reference = $"INV-ADJ-{transactionDate:yyyy}-{recNumber.ToString().PadLeft(2, '0')}",
                TransDate = transDateStr,
                ExpArDate = expArDateStr,
                HdrDesc = $"Inventory transfer - Stock {recTypeName} #{recNumber}",
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
            List<ApprovalLineItem> lineItems,
            DateTime transactionDate,
            int recNumber,
            string location)
        {
            string jsonPayload = "";
            try
            {
                var request = BuildAdjustmentRequest(lineItems, transactionDate, recNumber, location);
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

                if (!httpResponse.IsSuccessStatusCode && string.IsNullOrEmpty(sageResponse.Status))
                {
                    sageResponse.Status = "Error";
                    sageResponse.Message = $"HTTP {(int)httpResponse.StatusCode}: {httpResponse.ReasonPhrase}";
                }

                return sageResponse;
            }
            catch (TaskCanceledException)
            {
                return new SageAdjustmentEntryResponse
                {
                    Status = "Error",
                    Message = "Sage API request timed out. Please try again.",
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
                    RawResponse = ex.ToString(),
                    RawRequest = jsonPayload
                };
            }
        }

        private SageAdjustmentEntryRequest BuildAdjustmentRequest(
            List<ApprovalLineItem> lineItems,
            DateTime transactionDate,
            int recNumber,
            string location)
        {
            var transDateStr = transactionDate.ToString("yyyy-MM-ddTHH:mm:ss");

            var request = new SageAdjustmentEntryRequest
            {
                UserId = SageUserId,
                Password = SagePassword,
                CompanyId = SageCompanyId,
                DocNum = $"ADJ{recNumber.ToString().PadLeft(6, '0')}",
                Reference = $"INV-ADJ-{transactionDate:MMM-yyyy}".ToUpper(),
                Location = location?.Trim(),
                TransDate = transDateStr,
                HdrDesc = $"Stock Increase adjustment #{recNumber} - Approved",
                TransType = 1,
                AdjHeaderOptFields = new List<SageOptField>(),
                Items = lineItems.Select(item => new SageAdjustmentItem
                {
                    ItemNo = item.ItemCode?.Trim(),
                    Quantity = item.Quantity,
                    ExtCost = 0,
                    AdjDetailOptFields = new List<SageOptField>()
                }).ToList()
            };

            return request;
        }

        // ========== Sage Master Data APIs ==========

        /// <summary>
        /// Fetch all active items from Sage300 ICITEM API.
        /// When ENABLE_CACHE is true, the first call fetches from API and caches the result;
        /// subsequent calls return from cache.
        /// </summary>
        public async Task<SageItemResponse> GetItemsAsync()
        {
            // Return cached data if available
            if (ENABLE_CACHE && _cachedItems != null)
            {
                return _cachedItems;
            }

            try
            {
                var url = $"{SageItemsApiUrl}?userid={Uri.EscapeDataString(SageUserId)}&password={Uri.EscapeDataString(SagePassword)}&companyid={Uri.EscapeDataString(SageCompanyId)}&ActiveItems=1";

                var response = await _httpClient.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                var result = JsonConvert.DeserializeObject<SageItemResponse>(responseBody);
                result = result ?? new SageItemResponse { icitems = new List<SageItem>(), status = -1 };

                // Store in cache if enabled and response is valid
                if (ENABLE_CACHE && result.icitems != null && result.icitems.Count > 0)
                {
                    lock (_itemsCacheLock)
                    {
                        _cachedItems = result;
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
        /// Fetch all locations from Sage300 ICLOCATION API.
        /// When ENABLE_CACHE is true, the first call fetches from API and caches the result;
        /// subsequent calls return from cache.
        /// </summary>
        public async Task<SageLocationResponse> GetLocationsAsync()
        {
            // Return cached data if available
            if (ENABLE_CACHE && _cachedLocations != null)
            {
                return _cachedLocations;
            }

            try
            {
                var url = $"{SageLocationsApiUrl}?userid={Uri.EscapeDataString(SageUserId)}&password={Uri.EscapeDataString(SagePassword)}&companyid={Uri.EscapeDataString(SageCompanyId)}";

                var response = await _httpClient.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                var result = JsonConvert.DeserializeObject<SageLocationResponse>(responseBody);
                result = result ?? new SageLocationResponse { locations = new List<SageLocation>(), status = -1 };

                // Store in cache if enabled and response is valid
                if (ENABLE_CACHE && result.locations != null && result.locations.Count > 0)
                {
                    lock (_locationsCacheLock)
                    {
                        _cachedLocations = result;
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

        /// <summary>
        /// Clears the cached items and locations, forcing the next call to fetch fresh data from the API.
        /// </summary>
        public static void ClearCache()
        {
            lock (_itemsCacheLock) { _cachedItems = null; }
            lock (_locationsCacheLock) { _cachedLocations = null; }
        }
    }
}
