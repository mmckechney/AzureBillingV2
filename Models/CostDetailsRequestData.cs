using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
namespace AzureBillingV2.Models
{
    public class CostDetailsRequestData
    {
        [JsonPropertyName("metric")]
        public string Metric { get; set; } = "ActualCost";

        [JsonPropertyName("timePeriod")]
        public TimePeriod TimePeriod { get; set; } = new TimePeriod();
    }

    public class TimePeriod
    {
        [JsonPropertyName("start")]
        public DateTime Start { get; set; }

        [JsonPropertyName("end")]
        public DateTime End { get; set; }
    }
}
