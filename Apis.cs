﻿using System;
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
        private int maxConcurrency = 2;
        private static SemaphoreSlim semaphore;
        public Apis(ILoggerFactory loggerFactory, IConfiguration config)
        {
            this._logger = loggerFactory.CreateLogger<Apis>();
            this.httpClient = new HttpClient();
            this.config = config;
            if (int.TryParse(config["MaxConcurrency"], out int max))
            {
                maxConcurrency = max;
            }
            semaphore = new SemaphoreSlim(1,maxConcurrency);
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
            semaphore.Wait();
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
                        tracker.StatusMessage.Add($"Unable to request cost details for subscription {tracker.SubscriptionId}. Location header was empty");
                        _logger.LogError(tracker.StatusMessage.ToLines());
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
                        semaphore.Release();
                        return await RequestCostDetailsReport(tracker, start, end, tenantId, iteration++);
                    }
                    else
                    {
                        tracker.StatusMessage.Add($"Unable to request cost details for subscription {tracker.SubscriptionId}. {result.ReasonPhrase}");
                        _logger.LogError(tracker.StatusMessage.ToLines());
                    }
                }
            }
            catch (Exception exe)
            {
                tracker.StatusMessage.Add($"Failed to request cost report generation for subscription: {tracker.SubscriptionId} -- {exe.Message}");
                _logger.LogError(exe, tracker.StatusMessage.ToLines());
            }
            finally
            {
                try { semaphore.Release(); } catch { }
            }
            tracker.Success = false;
            return tracker;
        }

       
   

        public async Task<ReportTracking> GetReportStatusBlobUrl(ReportTracking tracker, string tenantId = "", int iteration = 0)
        {
            semaphore.Wait();
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
                    else if (status.Status.Trim().ToLower() == "nodatafound")
                    {
                        tracker.StatusMessage.Add($"Cost report for subscription {tracker.SubscriptionId} has no data. Skipping...");
                    }
                    else if (iteration < 10)
                    {
                        _logger.LogInformation($"Report status for subscription {tracker.SubscriptionId} is: {status.Status.Trim()}. Checking again...");
                        semaphore.Release();
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
                    semaphore.Release();
                    Thread.Sleep(3000);
                    return await GetReportStatusBlobUrl(tracker, tenantId, iteration);
                }
                else
                {
                    tracker.StatusMessage.Add($"Failed to get Report Status for subscription: {tracker.SubscriptionId} -- {exe.Message}");
                    _logger.LogError(exe, tracker.StatusMessage.ToLines());
                }
            }
            finally
            {
                semaphore.Release();
            }
            tracker.Success = false;
            return tracker;
        }

        public async Task<ReportTracking> SaveBlobToStorage(ReportTracking tracker, string containerName, string targetConnectionString, BillingFileType fileType)
        {
            semaphore.Wait();
            string blobName = "";
            try
            {
                switch(fileType)
                {
                    case BillingFileType.Raw:
                        blobName = tracker.RawCostDataBlobName;
                        break;
                    case BillingFileType.Billing:
                        blobName = tracker.CostDataBlobName;
                        break;
                }
                _logger.LogInformation($"Saving Cost Report for subscription {tracker.SubscriptionId} to {blobName}");
                containerName = containerName.ToLower();
                BlobContainerClient containerClient = new BlobContainerClient(targetConnectionString, containerName);
                containerClient.CreateIfNotExists();
                
                BlobClient targetClient = new BlobClient(targetConnectionString, containerName, blobName);
                var sourceUri = new Uri(tracker.ReportBlobSas);
                var copyOperation = await targetClient.StartCopyFromUriAsync(sourceUri);
                var result = await copyOperation.WaitForCompletionAsync();
                if (result.GetRawResponse().Status < 300)
                {

                    switch (fileType)
                    {
                        case BillingFileType.Raw:
                            tracker.RawCostDataBlobName = targetClient.Uri.ToString();
                            break;
                        case BillingFileType.Billing:
                            tracker.CostDataBlobName = targetClient.Uri.ToString();
                            break;
                    }
                    tracker.StatusMessage.Add("Successfully saved report to Blob storage.");

                    return tracker;
                }
                else
                {
                    tracker.StatusMessage.Add($"Failed to copy target blob file '{blobName}': {result.GetRawResponse().ReasonPhrase}");
                    _logger.LogError(tracker.StatusMessage.ToLines());
                }
            }
            catch (Exception exe)
            {
                tracker.StatusMessage.Add($"Failed to copy to target blob file: {blobName} -- {exe.Message}");
                _logger.LogError(exe, tracker.StatusMessage.ToLines());
            }
            finally
            {
                semaphore.Release();
            }
            tracker.Success = false;
            return tracker;

        }



        public async Task<(List<ReportTracking>, string)> GetListOfSubscriptions(string managementGroupId, string tenantId = "")
        {
            semaphore.Wait();
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
                    var mgSubscriptions = mg.Value.Where(v => v.Type == "/subscriptions").Select(s => new ReportTracking()
                    {
                        SubscriptionId = s.Name,
                        SubscriptionName = s.Properties.DisplayName,
                        TenantId = s.Properties.TenantId,
                        
                    }).ToList();
                    if (mgSubscriptions.Count == 0)
                    {
                        return (mgSubscriptions, $"No subscriptions found for Management Group {managementGroupId}");
                    }
                    else
                    {
                        mgSubscriptions.ForEach(s =>
                        {
                            s.TenantId = tenantId;
                        });
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
            finally
            {
                semaphore.Release();
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

                    var csv = new CsvReader(sr, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,
                        HeaderValidated = null,
                        MissingFieldFound = null,
                        PrepareHeaderForMatch = args => args.Header.FirstUpper()
                    });
                    //csv.Context.RegisterClassMap<BillingData>();
                    billData = csv.GetRecords<BillingData>().ToList();
                }

                return billData;
            }
            catch (Exception exe)
            {
                _logger.LogError(exe, $"Unable to read file at {blobSasUrl}");
                return null;
            }

        }


        public async Task<ReportTracking> GetRateCardInformation(ReportTracking tracker, string tenantId = "")
        {
            _logger.LogInformation($"Getting rate card information for subscription {tracker.SubscriptionId}");
            var result = await GetRateCardInformation(tracker.SubscriptionId, tenantId, tracker.OfferDurableId);
            tracker.RateCard = result.Item1;
            tracker.RateCardUrl = result.Item2;
            if(tracker.RateCard == null)
            {
                tracker.Success = false;
                tracker.StatusMessage.Add(result.Item3);
            }
            return tracker;

        }
        public async Task<(RateCardData, string, string)> GetRateCardInformation(string subscriptionId, string tenantId = "", string offerDurableId = "MS-AZR-0003P", string currency = "USD", string locale = "en-US", string regionInfo = "US", int iteration = 0, string redirectUrl = "")
        {
            semaphore.Wait();
            var statusCode = 0;
            var reasonPhrase = "";
            var failureMessage = "";
            string apiUrl = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.Commerce/RateCard?api-version=2015-06-01-preview&$filter=OfferDurableId eq '{offerDurableId}' and Currency eq '{currency}' and Locale eq '{locale}' and RegionInfo eq '{regionInfo}'";

            try
            {
                iteration = iteration + 1;
                if (!string.IsNullOrEmpty(redirectUrl))
                {
                    apiUrl = redirectUrl;
                }
                httpClient.DefaultRequestHeaders.Authorization = await GetAuthHeader(tenantId);
                var result = await httpClient.GetAsync(apiUrl);
                statusCode = (int)result.StatusCode;
                reasonPhrase = result.ReasonPhrase;
                if (result.IsSuccessStatusCode)
                {
                    var rateData = await result.Content.ReadFromJsonAsync<RateCardData>();
                    return (rateData, apiUrl, "");
                }
                else if (statusCode < 400)
                {
                    if (result.Headers.Location != null)
                    {
                        _logger.LogInformation($"Get Rate Card for {subscriptionId} returned a {statusCode} return code. Redirecting to Locaton header URL: {result.Headers.Location.ToString()}");
                        apiUrl = result.Headers.Location.ToString();
                        semaphore.Release();
                        return await GetRateCardInformation(subscriptionId, tenantId, offerDurableId, currency, locale, regionInfo, iteration, apiUrl);
                    }
                    else
                    {
                        failureMessage = $"Get Rate Card for {subscriptionId} returned a {statusCode} return code, but no Location Header found . {result.ReasonPhrase}";
                        _logger.LogError(failureMessage);
                    }
                }
                else
                {
                    string content = await result.Content.ReadAsStringAsync();
                    failureMessage = content;
                    _logger.LogError($"Unable to get rate card information for subscription {subscriptionId}. {result.ReasonPhrase}");
                }
            }
            catch (Exception exe)
            {
                _logger.LogError(exe, $"Unable to get rate card information for subscription {subscriptionId}. Return status code is: {statusCode}");
                if (statusCode < 400 && iteration < 3)
                {
                    _logger.LogInformation($"Retrying rate card information for subscription {subscriptionId}. Iteration {iteration}");
                    semaphore.Release();
                    await Task.Delay(5000);
                    return await GetRateCardInformation(subscriptionId, tenantId, offerDurableId, currency, locale, regionInfo, iteration);
                }
            }
            finally
            {
                semaphore.Release();
            }

            return (null, apiUrl,  $"Failed to get Legacy Rate Card for {subscriptionId}. Return status code is: {statusCode} ({reasonPhrase}). {failureMessage}");
        }

        public async Task<ReportTracking> MapRateCardToCostReport(ReportTracking tracker, RateCardData rateCard)
        {
            semaphore.Wait();
            List<BillingData> costReport = null;
            try
            {
                _logger.LogInformation($"Mapping rate card to cost report for subscription {tracker.SubscriptionId}");
                costReport = await GetReportCSVContents(tracker.ReportBlobSas);

                foreach (var record in costReport)
                {
                    try
                    {
                        var rate = rateCard.Meters.Where(m => m.MeterId == record.MeterId).FirstOrDefault();
                        if (rate != null)
                        {
                            var reportCost = record.CostInBillingCurrency;
                            var calcCost = rate.MeterRates.BaseRate * record.Quantity;
                            record.CostInBillingCurrency = calcCost;
                        }else
                        {
                            _logger.LogWarning($"Unable to match billing data MeterId '{record.MeterId}' ({record.ProductName})");
                        }
                    }
                    catch (Exception exe)
                    {
                        _logger.LogWarning($"Unable to map rate card to cost report for meterid {record.MeterId}. {exe.Message}");
                    }


                }
            }
            finally
            {
                semaphore.Release();
            }
            tracker.CostInfo = costReport;
            return tracker;
        }

        public async Task<ReportTracking> SaveMappedDataToStorage(ReportTracking tracker, string containerName, string targetConnectionString)
        {
            semaphore.Wait();
            try
            {
                _logger.LogInformation($"Saving mapped cost data for subscription {tracker.SubscriptionId} to {tracker.CostDataBlobName}");
                containerName = containerName.ToLower();
                BlobContainerClient containerClient = new BlobContainerClient(targetConnectionString, containerName);
                containerClient.CreateIfNotExists();

                BlobClient targetClient = new BlobClient(targetConnectionString, containerName, tracker.CostDataBlobName);


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
                    tracker.CostDataBlobName = targetClient.Uri.ToString();
                    tracker.StatusMessage.Add("Successfully saved rate card mapped report to Blob storage.");

                    return tracker;
                }
                else
                {
                    tracker.StatusMessage.Add($"Failed to copy target blob file '{tracker.CostDataBlobName}': {writeOperation.GetRawResponse().ReasonPhrase}");
                    _logger.LogError(tracker.StatusMessage.ToLines());
                }
            }
            catch (Exception exe)
            {
                tracker.StatusMessage.Add($"Failed to copy to target blob file: {tracker.CostDataBlobName} -- {exe.Message}");
                _logger.LogError(exe, tracker.StatusMessage.ToLines());
            }
            finally
            {
                semaphore.Release();
            }
            tracker.Success = false;
            return tracker;

        }

        public async Task<ReportTracking> SaveRateCardToStorage(ReportTracking tracker, string containerName, string targetConnectionString)
        {
            semaphore.Wait();
            try
            {
                _logger.LogInformation($"Saving rate card data for subscription {tracker.SubscriptionId} to {tracker.RateCardBlobName}");

                containerName = containerName.ToLower();
                BlobContainerClient containerClient = new BlobContainerClient(targetConnectionString, containerName);
                containerClient.CreateIfNotExists();

                var rateCard = JsonSerializer.Serialize<RateCardData>(tracker.RateCard, new JsonSerializerOptions() { WriteIndented = true});
                BlobClient rateCardClient = new BlobClient(targetConnectionString, containerName, tracker.RateCardBlobName);
                var rateCardClientWrite = await rateCardClient.UploadAsync(BinaryData.FromString(rateCard), true);
                if (rateCardClientWrite.GetRawResponse().Status < 300)
                {
                    tracker.RateCardBlobName = rateCardClient.Uri.ToString();
                    tracker.StatusMessage.Add("Successfully saved rate card to Blob storage.");
                    return tracker;
                }
                else
                {
                    tracker.StatusMessage.Add($"Failed to copy rate card blob file '{tracker.RateCardBlobName}': {rateCardClientWrite.GetRawResponse().ReasonPhrase}");
                    _logger.LogError(tracker.StatusMessage.ToLines());
                }
            }
            catch (Exception rateExe)
            {
                tracker.StatusMessage.Add($"Failed to copy rate card blob file: {tracker.RateCardBlobName} -- {rateExe.Message}");
                _logger.LogError(rateExe, tracker.StatusMessage.ToLines());
            }
            finally
            {
                semaphore.Release();
            }
            tracker.Success = false;
            return tracker;
        }

        #endregion
    }
}
