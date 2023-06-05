using AzureBillingV2.Models;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

namespace AzureBilling.Test
{
    [TestClass]
    public class ApiTests
    {
        private static string reportStatusUrl = string.Empty;
        private static string reportBlobSas = string.Empty;
        private static IConfiguration _config;
        private AzureBillingV2.Apis _apis = null;
        [TestInitialize]
        public void Initialize()
        {
            _apis = ServiceProvider.GetRequiredService<AzureBillingV2.Apis>() ?? throw new ArgumentNullException(nameof(AzureBillingV2.Apis));
            _config = ServiceProvider.GetRequiredService<IConfiguration>() ?? throw new ArgumentNullException(nameof(IConfiguration));
        }
        [DataTestMethod]
        [DataRow("0d901325-d643-4db7-ae90-58b4e3834629", "16b3c013-d300-468d-ac64-7eda0820b6d3", "MS-AZR-0003P")]
        public async Task GetRateCard_Test(string subscriptionId, string tenantId, string offerDurableId)
        {
            var rateCard = await _apis.GetRateCardInformation(subscriptionId, tenantId, offerDurableId);
            Assert.IsNotNull(rateCard);
        }

        [DataTestMethod]
        [DataRow("0d901325-d643-4db7-ae90-58b4e3834629", "16b3c013-d300-468d-ac64-7eda0820b6d3")]
        public async Task A_RequestCostDetailReport_Test(string subscriptionId,string tenantId)
        {
            var tracker = new ReportTracking();
            tracker.SubscriptionId = subscriptionId;
            var start = DateTime.Now.AddDays(-3);
            var end = DateTime.Now.AddDays(-2);                
            var report = await _apis.RequestCostDetailsReport(tracker, start,end, tenantId);
            Assert.IsNotNull(report);
            Assert.IsTrue(report.Success);
            reportStatusUrl = report.ReportStatusUrl;

        }
        [DataTestMethod]
        [DataRow("0d901325-d643-4db7-ae90-58b4e3834629", "16b3c013-d300-468d-ac64-7eda0820b6d3",null)]
        public async Task B_GetReportGenerationStatus(string subscriptionId, string tenantId, string? reportStatusUrl)
        {
            var tracker = new ReportTracking();
            tracker.SubscriptionId = subscriptionId;
            tracker.ReportStatusUrl = reportStatusUrl ?? ApiTests.reportStatusUrl;
            var results = await _apis.GetReportStatusBlobUrl(tracker, tenantId);
            Assert.IsNotNull(results);
            Assert.IsTrue(results.Success);
            reportBlobSas = results.ReportBlobSas;
        }

        [DataTestMethod]
        [DataRow("0d901325-d643-4db7-ae90-58b4e3834629", "16b3c013-d300-468d-ac64-7eda0820b6d3")]
        public async Task C_ReadReportBlobFile(string subscriptionId, string tenantId)
        {
            var reportBlobSas = _config["ReportBlobSas"] ?? ApiTests.reportBlobSas;
            var results = await _apis.GetReportCSVContents(reportBlobSas);
            Assert.IsNotNull(results);

        }

        [DataTestMethod]
        [DataRow("0d901325-d643-4db7-ae90-58b4e3834629", "16b3c013-d300-468d-ac64-7eda0820b6d3")]
        public async Task D_MatchRateToReportUsageQuantity(string subscriptionId, string tenantId)
        {
            var rateCard = await _apis.GetRateCardInformation(subscriptionId, tenantId);
            Assert.IsNotNull(rateCard);
            var reportBlobSas = _config["ReportBlobSas"] ?? ApiTests.reportBlobSas;
            
            var costReport = await _apis.GetReportCSVContents(reportBlobSas);
            Assert.IsNotNull(costReport);

            foreach(var record in costReport)
            {

                var rate = rateCard.Item1.Meters.Where(m => m.MeterId == record.meterId).FirstOrDefault();
                if(rate != null)
                {
                    var reportCost = record.costInUsd;
                    var calcCost = rate.MeterRates.BaseRate * record.quantity;
                    double delta = 0;
                    if (calcCost != 0 && reportCost != 0)
                    {
                        delta = reportCost / calcCost;
                    }
                  
                    Assert.IsTrue(delta < 2 || delta > 0.7, $"Calculated delta for {record.meterName} is {delta}. Report Value: {reportCost}; Calculated Value: {calcCost}");

                }
           
            }

        }

        [DataTestMethod]
        [DataRow("0d901325-d643-4db7-ae90-58b4e3834629", "16b3c013-d300-468d-ac64-7eda0820b6d3")]
        public async Task E_MatchRateToReportUsageQuantity(string subscriptionId, string tenantId)
        {
            var rateCard = await _apis.GetRateCardInformation(subscriptionId, tenantId);
            Assert.IsNotNull(rateCard);
            var reportBlobSas = _config["ReportBlobSas"] ?? ApiTests.reportBlobSas;

            var costReport = await _apis.GetReportCSVContents(reportBlobSas);
            Assert.IsNotNull(costReport);

            foreach (var record in costReport)
            {

                var rate = rateCard.Item1.Meters.Where(m => m.MeterId == record.meterId).FirstOrDefault();
                if (rate != null)
                {
                    var reportCost = record.costInUsd;
                    var calcCost = rate.MeterRates.BaseRate * record.quantity;
                    double delta = 0;
                    if (calcCost != 0 && reportCost != 0)
                    {
                        delta = reportCost / calcCost;
                    }

                    Assert.IsTrue(delta < 2 || delta > 0.7, $"Calculated delta for {record.meterName} is {delta}. Report Value: {reportCost}; Calculated Value: {calcCost}");

                }

            }

        }
    }
}