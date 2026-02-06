using Newtonsoft.Json;

namespace AOSmith.Models
{
    public class SageAdjustmentEntryResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("docnum")]
        public string DocNum { get; set; }

        [JsonIgnore]
        public string RawResponse { get; set; }

        [JsonIgnore]
        public string RawRequest { get; set; }
    }
}
