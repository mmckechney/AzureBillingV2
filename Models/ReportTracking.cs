using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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

        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; }

        [JsonPropertyName("reportStatusUrl")]
        public string ReportStatusUrl { get; set; }

        [JsonPropertyName("reportBlobSas")]
        public string ReportBlobSas { get; set; }

        [JsonPropertyName("offerDurableId")]
        public string OfferDurableId { get; set; }

        [JsonPropertyName("rateCardUrl")]
        public string RateCardUrl { get; set; }

        [JsonPropertyName("rateCardJsonBlobName")]
        public string RateCardBlobName { get; set; }

        [JsonPropertyName("rawCostDataBlobName")]
        public string RawCostDataBlobName { get; set; }

        [JsonPropertyName("costDataBlobName")]
        public string CostDataBlobName { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; } = true;

        [JsonPropertyName("statusMessage")]
        public List<string> StatusMessage { get; set; } = new List<string>();

        [JsonIgnore]
        public RateCardData RateCard { get; set; }

        [JsonIgnore]
        public List<BillingData> CostInfo { get; set; }

    }
    public static class Extensions
    {
        public static string ToLines(this List<string> lst)
        {
            return string.Join(Environment.NewLine, lst);
        }
    }
}
