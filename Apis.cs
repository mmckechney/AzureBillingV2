using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using AzureBillingV2.Models;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using System.Net.Http.Headers;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Grpc.Core;
using System.Net;
using System.ComponentModel.Design;
using Microsoft.Extensions.Primitives;
using System.Net.Http.Json;
using System.Diagnostics;
using CsvHelper;
using System.Globalization;
using Google.Protobuf.WellKnownTypes;

namespace AzureBillingV2
{
    public class Apis
    {
        private ILogger _logger;
        private HttpClient httpClient;
        private IConfiguration config;
        private Random randomGen = new Random();
        public Apis(ILoggerFactory loggerFactory, IConfiguration config)
        {
            this._logger = loggerFactory.CreateLogger<Apis>();
            this.httpClient = new HttpClient();
            this.config = config;
        }

        const string generateCostDetails = "https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/generateCostDetailsReport?api-version=2022-10-01";

        private TokenCredential? _tokenCred;
        internal AccessToken? _accessToken = null;
        private static CancellationTokenSource? _cancelSource;
        internal static CancellationTokenSource CancelSource
        {
            get
            {
                if (_cancelSource == null)
                {
                    _cancelSource = new CancellationTokenSource();

                }
                return _cancelSource;

            }
        }

        internal async Task<string> GetTokenString(string tenantId = "")
        {

            if (_tokenCred == null)
            {
                
                if (!string.IsNullOrEmpty(tenantId))
                {
                    _tokenCred = new DefaultAzureCredential(new DefaultAzureCredentialOptions() { TenantId = tenantId });
                }
                else
                {
                    _tokenCred = new DefaultAzureCredential();
                }
            }

            if (_accessToken == null)
            {
                _accessToken = await _tokenCred.GetTokenAsync(new TokenRequestContext(new string[] { "https://management.azure.com/.default" }), CancelSource.Token);
            }

            return _accessToken.Value.Token;

        }
        public async Task<AuthenticationHeaderValue> GetAuthHeader(string tenantId)
        {
            var tokenString = await GetTokenString(tenantId);
            return new AuthenticationHeaderValue("Bearer", tokenString);
        }


        public async Task<ReportTracking> RequestCostDetailsReport(ReportTracking tracker, DateTime start, DateTime end, string tenantId = "", int iteration = 0)
        {
            try
            {
                _logger.LogInformation($"Requesting cost report for subscription {tracker.SubscriptionId}");
                var apiUrl = generateCostDetails.Replace("{subscriptionId}", tracker.SubscriptionId);
                httpClient.DefaultRequestHeaders.Authorization = await GetAuthHeader(tenantId);

                var data = new CostDetailsRequestData()
                {
                    TimePeriod = new TimePeriod()
                    {
                        Start = start,
                        End = end
                    }
                };
                var content = JsonSerializer.Serialize<CostDetailsRequestData>(data);
                HttpContent body = new StringContent(content, System.Text.Encoding.UTF8, "application/json");

                var result = await httpClient.PostAsync(apiUrl, body);

                if (result.IsSuccessStatusCode)
                {
                    if (result.Headers.Contains("Location"))
                    {
                        var reportStatusUrl = result.Headers.GetValues("Location").First();
                        tracker.ReportStatusUrl = reportStatusUrl;
                        return tracker;
                    }
                    else
                    {
                        tracker.StatusMessage = $"Unable to request cost details for subscription {tracker.SubscriptionId}. Location header was empty";
                        _logger.LogError(tracker.StatusMessage);
                    }
                }
                else
                {

                    if(iteration < 10 && result.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var retry = result.Headers.RetryAfter.Delta;
                        if (retry.HasValue)
                        {
                            _logger.LogInformation($"Too Many Requests response from report request for subscription {tracker.SubscriptionId}. Waiting for retry period of {retry.Value.TotalSeconds} seconds");
                            Thread.Sleep((int)retry.Value.TotalMilliseconds);
                        }else
                        {
                            Thread.Sleep(randomGen.Next(4000,8000));
                        }
                        return await RequestCostDetailsReport(tracker, start, end, tenantId, iteration++);
                    }
                    else
                    {
                        tracker.StatusMessage = $"Unable to request cost details for subscription {tracker.SubscriptionId}. {result.ReasonPhrase}";
                        _logger.LogError(tracker.StatusMessage);
                    }
                }
            }
            catch (Exception exe)
            {
                tracker.StatusMessage = $"Failed to request cost report generation for subscription: {tracker.SubscriptionId} -- {exe.Message}";
                _logger.LogError(exe, tracker.StatusMessage);
            }
            tracker.Success = false;
            return tracker;
        }

       
   

        public async Task<ReportTracking> GetReportStatusBlobUrl(ReportTracking tracker, string tenantId = "", int iteration = 0)
        {
            try
            {
                iteration = iteration + 1;
                _logger.LogInformation($"Checking status of cost report for subscription {tracker.SubscriptionId}, Iteration: {iteration}");
                httpClient.DefaultRequestHeaders.Authorization = await GetAuthHeader(tenantId);
                var result = await httpClient.GetAsync(tracker.ReportStatusUrl);
                if (result.IsSuccessStatusCode)
                {
                    var contentString = await result.Content.ReadAsStringAsync();
                    var status = JsonSerializer.Deserialize<CostDetailsReportStatus>(contentString);
                    if (status.Status.Trim().ToLower() == "completed")
                    {
                        var blobUrl = status.Manifest.Blobs.First().BlobLink;
                        tracker.ReportBlobSas = blobUrl;
                        return tracker;
                    }
                    else if (iteration < 10)
                    {
                        _logger.LogInformation($"Report status for subscription {tracker.SubscriptionId} is: {status.Status.Trim()}. Checking again...");
                        Thread.Sleep(3000);
                        return await GetReportStatusBlobUrl(tracker, tenantId, iteration);
                    }
                }
            }
            catch (Exception exe)
            {
                if (iteration < 10)
                {
                    _logger.LogInformation($"Error checking status for subscription {tracker.SubscriptionId}: {exe.Message}. Checking again...");
                    Thread.Sleep(3000);
                    return await GetReportStatusBlobUrl(tracker, tenantId, iteration);
                }
                else
                {
                    tracker.StatusMessage = $"Failed to get Report Status for subscription: {tracker.SubscriptionId} -- {exe.Message}";
                    _logger.LogError(exe, tracker.StatusMessage);
                }
            }
            tracker.Success = false;
            return tracker;
        }

        public async Task<ReportTracking> SaveBlobToStorage(ReportTracking tracker, string containerName, string targetConnectionString)
        {
            try
            {
                _logger.LogInformation($"Saving Cost Report for subscription {tracker.SubscriptionId} to {tracker.DestinationBlobName}");
                containerName = containerName.ToLower();
                BlobContainerClient containerClient = new BlobContainerClient(targetConnectionString, containerName);
                containerClient.CreateIfNotExists();
                
                BlobClient targetClient = new BlobClient(targetConnectionString, containerName, tracker.DestinationBlobName);
                var sourceUri = new Uri(tracker.ReportBlobSas);
                var copyOperation = await targetClient.StartCopyFromUriAsync(sourceUri);
                var result = await copyOperation.WaitForCompletionAsync();
                if (result.GetRawResponse().Status < 300)
                {
                    tracker.DestinationBlobName = targetClient.Uri.ToString();
                    tracker.StatusMessage = "Successfully saved report to Blob storage";
                    return tracker;
                }
                else
                {
                    tracker.StatusMessage = $"Failed to copy target blob file '{tracker.DestinationBlobName}': {result.GetRawResponse().ReasonPhrase}";
                    _logger.LogError(tracker.StatusMessage);
                }
            }
            catch (Exception exe)
            {
                tracker.StatusMessage = $"Failed to copy to target blob file: {tracker.DestinationBlobName} -- {exe.Message}";
                _logger.LogError(exe, tracker.StatusMessage);
            }
            tracker.Success = false;
            return tracker;

        }



        public async Task<(List<ReportTracking>, string)> GetListOfSubscriptions(string managementGroupId, string tenantId = "")
        {
            string failureMessage = "";
            try
            {


                var url = $"https://management.azure.com/providers/Microsoft.Management/getEntities?api-version=2020-05-01&%24filter=name%20eq%20%27{managementGroupId}%27";
                httpClient.DefaultRequestHeaders.Authorization = await GetAuthHeader(tenantId);
                var result = await httpClient.PostAsync(url, null);
                if (result.IsSuccessStatusCode)
                {
                    var contentString = await result.Content.ReadAsStringAsync();
                    var mg = JsonSerializer.Deserialize<ManagementGroupData>(contentString);
                    var mgSubscriptions = mg.Value.Where(v => v.Type == "/subscriptions").Select(s => new ReportTracking() { SubscriptionId = s.Name, SubscriptionName = s.Properties.DisplayName }).ToList();
                    if (mgSubscriptions.Count == 0)
                    {
                        return (mgSubscriptions, $"No subscriptions found for Management Group {managementGroupId}");
                    }
                    return (mgSubscriptions, string.Empty);
                }
                else
                {
                    failureMessage = $"Failed to get list of subscriptions for management group: {managementGroupId} -- {result.ReasonPhrase}";
                    _logger.LogError(failureMessage);
                }
            }
            catch(Exception exe)
            {
                failureMessage = $"Failed to get list of subscriptions for management group: {managementGroupId} -- {exe.Message}";
                _logger.LogError(exe, failureMessage);
            }
            return (new List<ReportTracking>(), failureMessage);
        }


        #region Methods for Legacy Rate Card

        public async Task<List<BillingData>> GetReportCSVContents(string blobSasUrl)
        {
            try
            {
                List<BillingData> billData = new List<BillingData>();
                var sourceUri = new Uri(blobSasUrl);
                BlobClient targetClient = new BlobClient(sourceUri);
                var content = await targetClient.DownloadContentAsync();
                var csvContent = Encoding.UTF8.GetString(content.Value.Content);
                using (var sr = new StringReader(csvContent))
                {

                    var csv = new CsvReader(sr,new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, HeaderValidated = null, MissingFieldFound = null});
                    //csv.Context.RegisterClassMap<BillingData>();
                    billData = csv.GetRecords<BillingData>().ToList();
                }

                return billData;
            }
            catch(Exception exe)
            {
                _logger.LogError(exe, $"Unable to read file at {blobSasUrl}");
                return null;
            }
        }


        public async Task<ReportTracking> GetRateCardInformation(ReportTracking tracker, string tenantId = "")
        {
            _logger.LogInformation($"Getting rate card information for subscription {tracker.SubscriptionId}");
            var rateCard = await GetRateCardInformation(tracker.SubscriptionId, tenantId);
            tracker.RateCard = rateCard;
            return tracker;

        }
        public async Task<RateCardData> GetRateCardInformation(string subscriptionId, string tenantId = "", string offerDurableId = "MS-AZR-0003P", string currency = "USD", string locale = "en-US", string regionInfo = "US")
        {
            try
            {
                var apiUrl = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.Commerce/RateCard?api-version=2015-06-01-preview&$filter=OfferDurableId eq '{offerDurableId}' and Currency eq '{currency}' and Locale eq '{locale}' and RegionInfo eq '{regionInfo}'";
                httpClient.DefaultRequestHeaders.Authorization = await GetAuthHeader(tenantId);
                var result = await httpClient.GetAsync(apiUrl);
                if (result.IsSuccessStatusCode)
                {
                    var rateData = await result.Content.ReadFromJsonAsync<RateCardData>();
                    return rateData;
                }
                else
                {
                    _logger.LogError($"Unable to get rate card information for subscription {subscriptionId}. {result.ReasonPhrase}");
                }
            }
            catch (Exception exe)
            {
                _logger.LogError(exe, $"Unable to get rate card information for subscription {subscriptionId}");
            }
            return null;
        }

        public async Task<ReportTracking> MapRateCardToCostReport(ReportTracking tracker)
        {
            _logger.LogInformation($"Mapping rate card to cost report for subscription {tracker.SubscriptionId}");
            var costReport = await GetReportCSVContents(tracker.ReportBlobSas);

            foreach (var record in costReport)
            {

                var rate = tracker.RateCard.Meters.Where(m => m.MeterId == record.meterId).FirstOrDefault();
                if (rate != null)
                {
                    var reportCost = record.costInUsd;
                    var calcCost = rate.MeterRates.BaseRate * record.quantity;
                    record.costInUsd = calcCost;
                }

            }
            tracker.CostInfo = costReport;
            return tracker;
        }

        public async Task<ReportTracking> SaveMappedDataToStorage(ReportTracking tracker, string containerName, string targetConnectionString)
        {
            try
            {
                _logger.LogInformation($"Saving mapped cost data for subscription {tracker.SubscriptionId} to {tracker.DestinationBlobName}");
                containerName = containerName.ToLower();
                BlobContainerClient containerClient = new BlobContainerClient(targetConnectionString, containerName);
                containerClient.CreateIfNotExists();

                BlobClient targetClient = new BlobClient(targetConnectionString, containerName, tracker.DestinationBlobName);


                var sb = new StringBuilder();
                using (var writer = new StringWriter(sb))
                {
                    using (var csv = new CsvWriter(writer, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, HeaderValidated = null, MissingFieldFound = null, BadDataFound = null, }))
                    {
                        csv.WriteRecords<BillingData>(tracker.CostInfo.AsEnumerable());
                    }
                }

                var writeOperation = await targetClient.UploadAsync(BinaryData.FromString(sb.ToString()),true);

                if (writeOperation.GetRawResponse().Status < 300)
                {
                    tracker.DestinationBlobName = targetClient.Uri.ToString();
                    tracker.StatusMessage = "Successfully saved report to Blob storage";
                    return tracker;
                }
                else
                {
                    tracker.StatusMessage = $"Failed to copy target blob file '{tracker.DestinationBlobName}': {writeOperation.GetRawResponse().ReasonPhrase}";
                    _logger.LogError(tracker.StatusMessage);
                }

            }
            catch (Exception exe)
            {
                tracker.StatusMessage = $"Failed to copy to target blob file: {tracker.DestinationBlobName} -- {exe.Message}";
                _logger.LogError(exe, tracker.StatusMessage);
            }
            tracker.Success = false;
            return tracker;

        }

        #endregion
    }
}
