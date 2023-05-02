using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AzureBillingV2.Models
{
    public class ReportTracking
    {
        [JsonPropertyName("subscriptionName")]
        public string SubscriptionName { get; set; }

        [JsonPropertyName("subscriptionId")]
        public string SubscriptionId { get; set; }

        [JsonPropertyName("reportStatusUrl")]
        public string ReportStatusUrl { get; set; }

        [JsonPropertyName("reportBlobSas")]
        public string ReportBlobSas { get; set; }

        [JsonPropertyName("destinationBlobName")]
        public string DestinationBlobName { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; } = true;

        [JsonPropertyName("statusMessage")]
        public string StatusMessage { get; set; }
    }
}
