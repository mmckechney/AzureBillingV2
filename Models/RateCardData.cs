using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AzureBillingV2.Models
{
    public class RateCardData
    {
        [JsonPropertyName("OfferTerms")]
        public List<object> OfferTerms { get; set; }

        [JsonPropertyName("Meters")]
        public List<Meter> Meters { get; set; }

        [JsonPropertyName("Currency")]
        public string Currency { get; set; }

        [JsonPropertyName("Locale")]
        public string Locale { get; set; }

        [JsonPropertyName("IsTaxIncluded")]
        public bool IsTaxIncluded { get; set; }
    }

    public class Meter
    {
        [JsonPropertyName("EffectiveDate")]
        public DateTime EffectiveDate { get; set; }

        [JsonPropertyName("IncludedQuantity")]
        public double IncludedQuantity { get; set; }

        [JsonPropertyName("MeterCategory")]
        public string MeterCategory { get; set; }

        [JsonPropertyName("MeterId")]
        public string MeterId { get; set; }

        [JsonPropertyName("MeterName")]
        public string MeterName { get; set; }

        [JsonPropertyName("MeterRates")]
        public MeterRates MeterRates { get; set; }

        [JsonPropertyName("MeterRegion")]
        public string MeterRegion { get; set; }

        [JsonPropertyName("MeterSubCategory")]
        public string MeterSubCategory { get; set; }

        [JsonPropertyName("MeterTags")]
        public List<object> MeterTags { get; set; }

        [JsonPropertyName("Unit")]
        public string Unit { get; set; }
    }

    public class MeterRates
    {
        [JsonPropertyName("0")]
        public double BaseRate { get; set; }

        [JsonPropertyName("51200.00")]
        public double? _5120000 { get; set; }

        [JsonPropertyName("512000.00")]
        public double? _51200000 { get; set; }
    }
}
