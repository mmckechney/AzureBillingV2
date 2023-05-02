using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace AzureBillingV2.Models
{
    public class FinalResults
    {
        [JsonPropertyName("managementGroupId")]
        public string ManagementGroupId { get; set; }

        [JsonPropertyName("startDate")]
        public DateTime StartDate { get; set; }
        
        [JsonPropertyName("endDate")]
        public DateTime EndDate { get; set; }

        [JsonPropertyName("hasFailures")]
        public bool HasFailures { get; set; } = true;
       
        [JsonPropertyName("failureMessage")]
        public string FailureMessage { get; set; }

        public List<ReportTracking> SubscriptionReports { get; set; } = new List<ReportTracking>();


    }
}
