using Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AzureBillingV2.Models
{

    public class CostDetailsReportStatus
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("manifest")]
        public Manifest Manifest { get; set; }

        [JsonPropertyName("validTill")]
        public DateTime ValidTill { get; set; }
    }
    public class Blob
    {
        [JsonPropertyName("blobLink")]
        public string BlobLink { get; set; }

        [JsonPropertyName("byteCount")]
        public int ByteCount { get; set; }
    }

    public class Manifest
    {
        [JsonPropertyName("manifestVersion")]
        public string ManifestVersion { get; set; }

        [JsonPropertyName("dataFormat")]
        public string DataFormat { get; set; }

        [JsonPropertyName("byteCount")]
        public int ByteCount { get; set; }

        [JsonPropertyName("blobCount")]
        public int BlobCount { get; set; }

        [JsonPropertyName("compressData")]
        public bool CompressData { get; set; }

        [JsonPropertyName("requestContext")]
        public RequestContext RequestContext { get; set; }

        [JsonPropertyName("blobs")]
        public List<Blob> Blobs { get; set; }
    }

    public class RequestBody
    {
        [JsonPropertyName("metric")]
        public string Metric { get; set; }

        [JsonPropertyName("timePeriod")]
        public TimePeriod TimePeriod { get; set; }

        [JsonPropertyName("invoiceId")]
        public object InvoiceId { get; set; }

        [JsonPropertyName("billingPeriod")]
        public object BillingPeriod { get; set; }
    }

    public class RequestContext
    {
        [JsonPropertyName("requestScope")]
        public string RequestScope { get; set; }

        [JsonPropertyName("requestBody")]
        public RequestBody RequestBody { get; set; }
    }
        


}
