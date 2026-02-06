using Newtonsoft.Json;

namespace AOSmith.Models
{
    public class SageTransferEntryResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("docnum")]
        public string DocNum { get; set; }

        [JsonProperty("transferNumber")]
        public string TransferNumber { get; set; }

        [JsonIgnore]
        public string RawResponse { get; set; }

        [JsonIgnore]
        public string RawRequest { get; set; }
    }
}
