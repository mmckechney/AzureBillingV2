using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AzureBillingV2.Models;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AzureBillingV2
{
    public class Orchestration
    {
        private Apis apis;
        private readonly ILogger _logger;
        public Orchestration(Apis apis, ILoggerFactory loggerFactory)
        {
            this.apis = apis;
            _logger = loggerFactory.CreateLogger<Orchestration>();
        }
        public async Task<(List<ReportTracking>, List<ReportTracking>)> GenerateCostDetailsReports(List<ReportTracking> subs, DateTime startDateTime, DateTime endDateTime)
        {
            var successfulReports = new List<ReportTracking>();
            var failedReports = new List<ReportTracking>();

            // Generate cost details report for each subscription
            _logger.LogInformation($"Generating cost detail reports for {subs.Count()} subscriptions");
            List<Task<ReportTracking>> generateTasks = new List<Task<ReportTracking>>();
            foreach (var sub in subs)
            {
                generateTasks.Add(apis.RequestCostDetailsReport(sub, startDateTime, endDateTime));
            }
            var generateResults = await Task.WhenAll(generateTasks.ToArray());

            //Update lists of success and failed.
            successfulReports = generateResults.Where(t => !string.IsNullOrEmpty(t.ReportStatusUrl)).ToList();
            failedReports.AddRange(generateResults.Where(t => !t.Success));

            _logger.LogInformation($"Found {successfulReports.Count()} successful generation tasks");

            return (successfulReports, failedReports);
        }

        public async Task<(List<ReportTracking>, List<ReportTracking>)> GetReportsStatusAndBlobUrls(List<ReportTracking> reports)
        {
            var successfulReports = new List<ReportTracking>();
            var failedReports = new List<ReportTracking>();

            // Get list of report URIs
            _logger.LogInformation($"Checking report status and getting report Sas URLs for {reports.Count()} reports");
            List <Task<ReportTracking>> reportTasks = new List<Task<ReportTracking>>();
            foreach (var task in reports)
            {
                reportTasks.Add(apis.GetReportStatusBlobUrl(task, task.TenantId));
            }
            var reportResults = await Task.WhenAll(reportTasks.ToArray());

            //Update lists of success and failed.
            successfulReports = reportResults.Where(t => t.Success).ToList();
            failedReports.AddRange(reportResults.Where(t => !t.Success));
            _logger.LogInformation($"Found {successfulReports.Count()} successful reports");

            return (successfulReports, failedReports);
        }

        internal async Task<(List<ReportTracking>,List<ReportTracking>)> GetLegacyRateCardsForSubs(List<ReportTracking> reports, string offerDurableId, bool rateCardPerSubscription)
        {
            var successfulReports = new List<ReportTracking>();
            var failedReports = new List<ReportTracking>();
            if (rateCardPerSubscription)
            {
                _logger.LogInformation($"Getting Legacy Rate Card information for {reports.Count()} subscriptions");
                var rateCardTasks = new List<Task<ReportTracking>>();
                foreach (var tracker in reports)
                {
                    tracker.OfferDurableId = offerDurableId;
                    rateCardTasks.Add(apis.GetRateCardInformation(tracker, tracker.TenantId));
                }
                var rateCardResults = await Task.WhenAll(rateCardTasks.ToArray());
                //Update lists of success and failed.
                successfulReports = rateCardResults.Where(t => t.Success).ToList();
                failedReports.AddRange(rateCardResults.Where(t => !t.Success));

                _logger.LogInformation($"Retrieved Legacy Rate Card information for {successfulReports.Count()} subscriptions");
                return (successfulReports, failedReports);
            }
            else
            {
                var tracker = reports.First();
                tracker.OfferDurableId = offerDurableId;
                _logger.LogInformation($"RateCardPerSubscription is set to `false`. Retrieving Legacy Rate Card information for {tracker.TenantId} which will be used for all subscriptions");
                tracker = await apis.GetRateCardInformation(tracker, tracker.TenantId);
                if (tracker.Success)
                {
                    foreach (var t in reports)
                    {
                        if (t.SubscriptionId != tracker.SubscriptionId)
                        {
                            t.OfferDurableId = offerDurableId;
                            t.RateCardUrl = tracker.RateCardUrl;
                        }
                    }
                    return (reports, failedReports);
                }
                else
                {
                    return (successfulReports, reports);
                }
            }
        }

        internal async Task<(List<ReportTracking>, List<ReportTracking>)> MapRateCardsToCostReports(List<ReportTracking> reports)
        {
            var successfulReports = new List<ReportTracking>();
            var failedReports = new List<ReportTracking>();

            _logger.LogInformation($"Mapping Legacy Rate Card information to billing data for {reports.Count()} subscriptions");
            var mappingTasks = new List<Task<ReportTracking>>();
            var defaultRateCard = reports.Where(r => r.RateCard != null).FirstOrDefault().RateCard;
            foreach (var tracker in reports)
            {
                mappingTasks.Add(apis.MapRateCardToCostReport(tracker, tracker.RateCard ?? defaultRateCard));
            }
            var mappingResults = await Task.WhenAll(mappingTasks.ToArray());
            //Update lists of success and failed.
            successfulReports = mappingResults.Where(t => t.Success).ToList();
            failedReports.AddRange(mappingResults.Where(t => !t.Success));

            _logger.LogInformation($"Successfully mapped Legacy Rate Card information to billing data for {successfulReports.Count()} subscriptions");
            return (successfulReports, failedReports);
        }

        internal async Task<(List<ReportTracking>, List<ReportTracking>)> SaveMappedReportsToStorage(List<ReportTracking> reports, string filePrefix, DateOnly startDate, string containerName, string targetConnectionString)
        {
            var successfulReports = new List<ReportTracking>();
            var failedReports = new List<ReportTracking>();
            
            _logger.LogInformation($"Saving mapped billing data for {reports.Count()} subscriptions");
            var writeTasks = new List<Task<ReportTracking>>();
            foreach (var tracker in reports)
            {
                tracker.CostDataBlobName = $"{startDate.ToString("yyyy-MM-dd")}/{filePrefix}-{tracker.SubscriptionId}.csv";

                writeTasks.Add(apis.SaveMappedDataToStorage(tracker, containerName, targetConnectionString));
            }
            var writeResults = await Task.WhenAll(writeTasks.ToArray());
            //Update lists of success and failed.
            successfulReports = writeResults.Where(t => t.Success).ToList();
            failedReports.AddRange(writeResults.Where(t => !t.Success));
            _logger.LogInformation($"Saved mapped billing data for {successfulReports.Count()} subscriptions");

            return (successfulReports, failedReports);
        }

        internal async Task<(List<ReportTracking>, List<ReportTracking>)> SaveSubscriptionsRateCardData(List<ReportTracking> reports, DateOnly startDate, string containerName, string targetConnectionString)
        {
            var successfulReports = new List<ReportTracking>();
            var failedReports = new List<ReportTracking>();
            
            _logger.LogInformation($"Saving rate card data for {reports.Count()} subscriptions");
            var rateCardBlobTasks = new List<Task<ReportTracking>>();
            foreach (var tracker in reports)
            {
                tracker.RateCardBlobName = $"{startDate.ToString("yyyy-MM-dd")}/RateCard-{tracker.SubscriptionId}.json";
                rateCardBlobTasks.Add(apis.SaveRateCardToStorage(tracker, containerName, targetConnectionString));
            }
            var rateCardBlobResults = await Task.WhenAll(rateCardBlobTasks.ToArray());
            //Update lists of success and failed.
            successfulReports = rateCardBlobResults.Where(t => t.Success).ToList();
            failedReports.AddRange(rateCardBlobResults.Where(t => !t.Success));

            _logger.LogInformation($"Saved rate card data for {successfulReports.Count()} subscriptions");

            return (successfulReports, failedReports);
        }

        internal async Task<(List<ReportTracking>, List<ReportTracking>)> CopyReportBlobsToTargetStorage(List<ReportTracking> reports, string filePrefix, DateOnly startDate, string containerName, string targetConnectionString)
        {
            var successfulReports = new List<ReportTracking>();
            var failedReports = new List<ReportTracking>();
            
            // Copy Reports to Blob Storage
            _logger.LogInformation($"Copying {reports.Count()} reports to destination Blob Container");
            var blobCopyTasks = new List<Task<ReportTracking>>();
            foreach (var tracker in reports)
            {
                tracker.CostDataBlobName = $"{startDate.ToString("yyyy-MM-dd")}/{filePrefix}-{tracker.SubscriptionId}.csv";
                blobCopyTasks.Add(apis.SaveBlobToStorage(tracker, containerName, targetConnectionString));
            }
            var blobCopyResults = await Task.WhenAll(blobCopyTasks.ToArray());
            _logger.LogInformation($"Copied {blobCopyResults.Count()} reports to blob storage");

            //Update lists of success and failed.
            successfulReports = blobCopyResults.Where(t => t.Success).ToList();
            failedReports.AddRange(blobCopyResults.Where(t => !t.Success));
            _logger.LogInformation($"Successfully copied {successfulReports.Count()} reports to destination Blob Container");

            return (successfulReports, failedReports);
        }
    }
}
