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

        internal async Task<string> GetTokenString()
        {

            if (_tokenCred == null)
            {
                _tokenCred = new DefaultAzureCredential();
            }

            if (_accessToken == null)
            {
                _accessToken = await _tokenCred.GetTokenAsync(new TokenRequestContext(new string[] { "https://management.azure.com/.default" }), CancelSource.Token);
            }

            return _accessToken.Value.Token;

        }
        public async Task<AuthenticationHeaderValue> GetAuthHeader()
        {
            var tokenString = await GetTokenString();
            return new AuthenticationHeaderValue("Bearer", tokenString);
        }


        public async Task<ReportTracking> RequestCostDetailsReport(ReportTracking tracker, DateTime start, DateTime end, int iteration = 0)
        {
            try
            {
                var apiUrl = generateCostDetails.Replace("{subscriptionId}", tracker.SubscriptionId);
                httpClient.DefaultRequestHeaders.Authorization = await GetAuthHeader();

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
                        return await RequestCostDetailsReport(tracker, start, end, iteration++);
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

        public async Task<ReportTracking> GetReportStatusBlobUrl(ReportTracking tracker, int iteration = 0)
        {
            try
            {
                httpClient.DefaultRequestHeaders.Authorization = await GetAuthHeader();
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
                        Thread.Sleep(2000);
                        return await GetReportStatusBlobUrl(tracker, iteration++);
                    }
                }
            }
            catch (Exception exe)
            {
                if (iteration < 10)
                {
                    Thread.Sleep(2000);
                    return await GetReportStatusBlobUrl(tracker, iteration++);
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

        public async Task<(List<ReportTracking>, string)> GetListOfSubscriptions(string managementGroupId)
        {
            string failureMessage = "";
            try
            {


                var url = $"https://management.azure.com/providers/Microsoft.Management/getEntities?api-version=2020-05-01&%24filter=name%20eq%20%27{managementGroupId}%27";
                httpClient.DefaultRequestHeaders.Authorization = await GetAuthHeader();
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
    }
}
