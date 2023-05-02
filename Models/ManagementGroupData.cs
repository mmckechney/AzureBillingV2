using System.Text.Json.Serialization;
namespace AzureBillingV2.Models
{
     public class ManagementGroupData
    {

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("value")]
        public List<Value> Value { get; set; }
    }
   

    
   

    public class Value
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("properties")]
        public Properties Properties { get; set; }


    }

    public class Properties
    {
        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; }

        [JsonPropertyName("numberOfChildren")]
        public int NumberOfChildren { get; set; }

        [JsonPropertyName("numberOfChildGroups")]
        public int NumberOfChildGroups { get; set; }

        [JsonPropertyName("numberOfDescendants")]
        public int NumberOfDescendants { get; set; }

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; }

     
    }


}