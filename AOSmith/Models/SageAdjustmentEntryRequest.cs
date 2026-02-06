using System.Collections.Generic;
using Newtonsoft.Json;

namespace AOSmith.Models
{
    public class SageAdjustmentEntryRequest
    {
        [JsonProperty("userid")]
        public string UserId { get; set; }

        [JsonProperty("password")]
        public string Password { get; set; }

        [JsonProperty("companyid")]
        public string CompanyId { get; set; }

        [JsonProperty("docnum")]
        public string DocNum { get; set; }

        [JsonProperty("reference")]
        public string Reference { get; set; }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("transdate")]
        public string TransDate { get; set; }

        [JsonProperty("hdrdesc")]
        public string HdrDesc { get; set; }

        [JsonProperty("transtype")]
        public int TransType { get; set; }

        [JsonProperty("adjHeaderOptFields")]
        public List<SageOptField> AdjHeaderOptFields { get; set; } = new List<SageOptField>();

        [JsonProperty("items")]
        public List<SageAdjustmentItem> Items { get; set; } = new List<SageAdjustmentItem>();
    }

    public class SageAdjustmentItem
    {
        [JsonProperty("itemno")]
        public string ItemNo { get; set; }

        [JsonProperty("quantity")]
        public decimal Quantity { get; set; }

        [JsonProperty("extcost")]
        public decimal ExtCost { get; set; }

        [JsonProperty("woffacct")]
        public string WoffAcct { get; set; }

        [JsonProperty("adjDetailOptFields")]
        public List<SageOptField> AdjDetailOptFields { get; set; } = new List<SageOptField>();
    }
}
