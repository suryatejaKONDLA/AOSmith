using System.Collections.Generic;
using Newtonsoft.Json;

namespace AOSmith.Models
{
    public class SageTransferEntryRequest
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

        [JsonProperty("transdate")]
        public string TransDate { get; set; }

        [JsonProperty("expardate")]
        public string ExpArDate { get; set; }

        [JsonProperty("hdrdesc")]
        public string HdrDesc { get; set; }

        [JsonProperty("transtype")]
        public int TransType { get; set; }

        [JsonProperty("transHeaderOptFields")]
        public List<SageOptField> TransHeaderOptFields { get; set; } = new List<SageOptField>();

        [JsonProperty("items")]
        public List<SageTransferItem> Items { get; set; } = new List<SageTransferItem>();
    }

    public class SageTransferItem
    {
        [JsonProperty("fromloc")]
        public string FromLoc { get; set; }

        [JsonProperty("toloc")]
        public string ToLoc { get; set; }

        [JsonProperty("itemno")]
        public string ItemNo { get; set; }

        [JsonProperty("quantity")]
        public decimal Quantity { get; set; }

        [JsonProperty("comments")]
        public string Comments { get; set; }

        [JsonProperty("transDetailOptFields")]
        public List<SageOptField> TransDetailOptFields { get; set; } = new List<SageOptField>();
    }

    public class SageOptField
    {
        [JsonProperty("optfield")]
        public string OptField { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }
    }
}
