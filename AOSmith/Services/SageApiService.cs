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
        private const string SageApiUrl = "https://sagetest.aosmith.in/Sage300.WebAPI2024/api/TransferEntry";
        private const string SageUserId = "ADMIN";
        private const string SagePassword = "Sage@123$";
        private const string SageCompanyId = "SMDAT";

        private static readonly HttpClient _httpClient;

        static SageApiService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
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
                var httpResponse = await _httpClient.PostAsync(SageApiUrl, content);

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
    }
}
