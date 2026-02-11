using System.Collections.Generic;
using Newtonsoft.Json;

namespace AOSmith.Models
{
    public class SageAdjustmentEntryResponse
    {
        [JsonProperty("status")]
        public object Status { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("Errors")]
        public List<string> Errors { get; set; }

        [JsonProperty("Warnings")]
        public List<string> Warnings { get; set; }

        [JsonProperty("Messages")]
        public List<string> Messages { get; set; }

        [JsonProperty("docnum")]
        public string DocNum { get; set; }

        [JsonIgnore]
        public string RawResponse { get; set; }

        [JsonIgnore]
        public string RawRequest { get; set; }

        [JsonIgnore]
        public bool IsSuccess =>
            (Errors == null || Errors.Count == 0) && !string.IsNullOrEmpty(DocNum);
    }
}
