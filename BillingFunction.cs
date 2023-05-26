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
using System.Diagnostics;

namespace AzureBillingV2
{
    public class BillingFunction
    {
        private readonly ILogger _logger;
        private IConfiguration config;
        private Apis apis;
        private Orchestration orchestration;

        public BillingFunction(ILoggerFactory loggerFactory, IConfiguration config, Apis apis, Orchestration orchestration)
        {
            _logger = loggerFactory.CreateLogger<BillingFunction>();
            this.config = config;
            this.apis = apis;
            this.orchestration = orchestration;
        }

        [Function("BillingFunction")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
        {
            var successfulReports = new List<ReportTracking>();
            var failedReports = new List<ReportTracking>();
            var stepFailedReports = new List<ReportTracking>();
            var finalResults = new FinalResults();
           

            //Set Values from App Settings
            var managementGroupId = config["ManagementGroupId"];
            var tenantId = config["TenantId"];
            bool useLegacyRateCard;
            bool.TryParse(config["UseLegacyRateCard"], out useLegacyRateCard);
            bool saveRateCardData;
            if(!bool.TryParse(config["SaveRateCardData"], out saveRateCardData))
            {
                saveRateCardData = true;
            }
            bool saveRawBillingReport;
            if (!bool.TryParse(config["SaveRawBillingReport"], out saveRawBillingReport))
            {
                saveRawBillingReport = true;
            }
            var containerName = config["ContainerName"];
            var targetConnectionString = config["StorageConnectionString"];


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
            
            //Set basic informtaion for final results 
            finalResults.StartDate = startDateTime;
            finalResults.EndDate = endDateTime;
            finalResults.ManagementGroupId = managementGroupId;

            // Get list of subscriptions
            (var subs, var failMsg) = await apis.GetListOfSubscriptions(managementGroupId, tenantId);
            _logger.LogInformation($"Found {subs.Count()} subscriptions");
            if(subs.Count == 0 && !string.IsNullOrWhiteSpace(failMsg))
            {
                finalResults.HasFailures = true;
                finalResults.FailureMessage = failMsg;
            }

            (successfulReports, stepFailedReports) = await orchestration.GenerateCostDetailsReports(subs, startDateTime, endDateTime);
            failedReports.AddRange(stepFailedReports);

            (successfulReports, stepFailedReports) = await orchestration.GetReportsStatusAndBlobUrls(successfulReports);
            failedReports.AddRange(stepFailedReports);

            if (useLegacyRateCard)
            {

                (successfulReports, stepFailedReports) = await orchestration.GetLegacyRateCardsForSubs(successfulReports);
                failedReports.AddRange(stepFailedReports);

               
                (successfulReports, stepFailedReports) = await orchestration.MapRateCardsToCostReports(successfulReports);
                failedReports.AddRange(stepFailedReports);

                if (saveRawBillingReport)
                {
                    (successfulReports, stepFailedReports) = await orchestration.CopyReportBlobsToTargetStorage(successfulReports, "Raw", startDate, containerName, targetConnectionString);
                    failedReports.AddRange(stepFailedReports);
                }

                (successfulReports, stepFailedReports) = await orchestration.SaveMappedReportsToStorage(successfulReports, "Billing", startDate,containerName, targetConnectionString);
                failedReports.AddRange(stepFailedReports);

                if (saveRateCardData)
                {
                    (successfulReports, stepFailedReports) = await orchestration.SaveSubscriptionsRateCardData(successfulReports, startDate, containerName, targetConnectionString);
                    failedReports.AddRange(stepFailedReports);
                }

              
            }
            else
            {
                (successfulReports, stepFailedReports) = await orchestration.CopyReportBlobsToTargetStorage(successfulReports, "Billing", startDate, containerName, targetConnectionString);
                failedReports.AddRange(stepFailedReports);
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
