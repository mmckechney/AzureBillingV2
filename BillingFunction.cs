using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using AzureBillingV2.Models;
using System.Threading.Tasks;
using System.Text.Json;
using System.Transactions;
using System;

namespace AzureBillingV2
{
    public class BillingFunction
    {
        private readonly ILogger _logger;
        private IConfiguration config;
        private Apis apis;

        public BillingFunction(ILoggerFactory loggerFactory, IConfiguration config, Apis apis)
        {
            _logger = loggerFactory.CreateLogger<BillingFunction>();
            this.config = config;
            this.apis = apis;
        }

        [Function("BillingFunction")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
        {
            var successfulReports = new List<ReportTracking>();
            var failedReports = new List<ReportTracking>();
            var finalResults = new FinalResults();
           

            var managementGroupId = config["ManagementGroupId"];
            var tenantId = config["TenantId"];
            bool useLegacyRateCard;
            bool.TryParse(config["UseLegacyRateCard"], out useLegacyRateCard);
            
            finalResults.ManagementGroupId = managementGroupId;


            //Set the start and end dates for the reports
            var start = req.Query["startDate"];
            DateTime endDateTime;
            if(!DateOnly.TryParse(start, out DateOnly startDate))
            {
                startDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-1));
                endDateTime = startDate.ToDateTime(new TimeOnly(23, 59, 59, 999));
            }
            else
            {
                endDateTime = startDate.ToDateTime(new TimeOnly(23, 59, 59, 999));
            }
           
            _logger.LogInformation($"Running Cost Management Report -- startDate: {startDate}, endDate: {endDateTime}");
            var startDateTime = startDate.ToDateTime(new TimeOnly(0, 0));
            finalResults.StartDate = startDateTime;
            finalResults.EndDate = endDateTime;
            

            // Get list of subscriptions
            (var subs, var failMsg) = await apis.GetListOfSubscriptions(managementGroupId, tenantId);
            _logger.LogInformation($"Found {subs.Count()} subscriptions");
            if(subs.Count == 0 && !string.IsNullOrWhiteSpace(failMsg))
            {
                finalResults.HasFailures = true;
                finalResults.FailureMessage = failMsg;
            }
            

            // Generate cost details report for each subscription
            List<Task<ReportTracking>> generateTasks = new List<Task<ReportTracking>>();
            foreach(var sub in subs)
            {
                generateTasks.Add(apis.RequestCostDetailsReport(sub, startDateTime, endDateTime));
            }
            var generateResults = await Task.WhenAll(generateTasks.ToArray());

            //Update lists of success and failed.
            successfulReports = generateResults.Where(t => !string.IsNullOrEmpty(t.ReportStatusUrl)).ToList();
            failedReports.AddRange(generateResults.Where(t => !t.Success));
            
            _logger.LogInformation($"Found {successfulReports.Count()} successful generation tasks");

            // Get list of report URIs
            List<Task<ReportTracking>> reportTasks = new List<Task<ReportTracking>>();
            foreach(var task in successfulReports)
            {
                reportTasks.Add(apis.GetReportStatusBlobUrl(task));
            }
            var reportResults = await Task.WhenAll(reportTasks.ToArray());

            //Update lists of success and failed.
            successfulReports = reportResults.Where(t => t.Success).ToList();
            failedReports.AddRange(reportResults.Where(t => !t.Success));

            var containerName = config["ContainerName"];
            var targetConnectionString = config["StorageConnectionString"];

            if (useLegacyRateCard)
            {
                _logger.LogInformation($"Getting Legacy Rate Card information for {successfulReports.Count()} subscriptions");
                var rateCardTasks = new List<Task<ReportTracking>>();
                foreach (var tracker in successfulReports)
                {
                    rateCardTasks.Add(apis.GetRateCardInformation(tracker, tenantId));
                }
                var rateCardResults = await Task.WhenAll(rateCardTasks.ToArray());
                //Update lists of success and failed.
                successfulReports = rateCardResults.Where(t => t.Success).ToList();
                failedReports.AddRange(rateCardResults.Where(t => !t.Success));

                _logger.LogInformation($"Mapping Legacy Rate Card information to billing data for {successfulReports.Count()} subscriptions");
                var mappingTasks = new List<Task<ReportTracking>>();
                foreach (var tracker in successfulReports)
                {
                    mappingTasks.Add(apis.MapRateCardToCostReport(tracker));
                }
                var mappingResults = await Task.WhenAll(mappingTasks.ToArray());
                //Update lists of success and failed.
                successfulReports = mappingResults.Where(t => t.Success).ToList();
                failedReports.AddRange(rateCardResults.Where(t => !t.Success));


                _logger.LogInformation($"Saving mapped billing data for {successfulReports.Count()} subscriptions");
                var writeTasks = new List<Task<ReportTracking>>();
                foreach (var tracker in successfulReports)
                {
                    tracker.DestinationBlobName = $"{startDate.ToString("yyyy-MM-dd")}/Billing-{tracker.SubscriptionId}.csv";
                    writeTasks.Add(apis.SaveMappedDataToStorage(tracker,containerName, targetConnectionString));
                }
                var writeResults = await Task.WhenAll(writeTasks.ToArray());
                //Update lists of success and failed.
                successfulReports = mappingResults.Where(t => t.Success).ToList();
                failedReports.AddRange(rateCardResults.Where(t => !t.Success));


            }
            else
            {

                
                // Copy Reports to Blob Storage
                var blobCopyTasks = new List<Task<ReportTracking>>();
                foreach (var tracker in successfulReports)
                {
                    tracker.DestinationBlobName = $"{startDate.ToString("yyyy-MM-dd")}/Billing-{tracker.SubscriptionId}.csv";
                    blobCopyTasks.Add(apis.SaveBlobToStorage(tracker, containerName, targetConnectionString));
                }
                var blobCopyResults = await Task.WhenAll(blobCopyTasks.ToArray());
                _logger.LogInformation($"Copied {blobCopyResults.Count()} reports to blob storage");

                //Update lists of success and failed.
                successfulReports = blobCopyResults.Where(t => t.Success).ToList();
                failedReports.AddRange(blobCopyResults.Where(t => !t.Success));
            }

            
            if (failedReports.Count == 0 && successfulReports.Count > 0)
            {
                finalResults.HasFailures = false;
            }
            finalResults.SubscriptionReports.AddRange(successfulReports);
            finalResults.SubscriptionReports.AddRange(failedReports);

            var final = JsonSerializer.Serialize<FinalResults> (finalResults, new JsonSerializerOptions() { WriteIndented = true});
            
            var response = req.CreateResponse(finalResults.HasFailures ? HttpStatusCode.FailedDependency : HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");

            response.WriteString(final);

            return response;
        }
    }
}
