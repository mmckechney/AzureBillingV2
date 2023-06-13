using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureBillingV2.Models
{
    public class BillingData //: ClassMap<BillingData>
    {
        public BillingData()
        {
            //Map(m => m.invoiceId);
            //Map(m => m.previousInvoiceId);
            //Map(m => m.billingAccountId);
            //Map(m => m.billingAccountName);
            //Map(m => m.billingProfileId);
            //Map(m => m.billingProfileName);
            //Map(m => m.invoiceSectionId);
            //Map(m => m.invoiceSectionName);
            //Map(m => m.resellerName);
            //Map(m => m.resellerMpnId);
            //Map(m => m.costCenter);
            //Map(m => m.billingPeriodEndDate);
            //Map(m => m.billingPeriodStartDate);
            //Map(m => m.servicePeriodEndDate);
            //Map(m => m.servicePeriodStartDate);
            //Map(m => m.date);
            //Map(m => m.serviceFamily);
            //Map(m => m.productOrderId);
            //Map(m => m.productOrderName);
            //Map(m => m.consumedService);
            //Map(m => m.meterId);
            //Map(m => m.meterName);
            //Map(m => m.meterCategory);
            //Map(m => m.meterSubCategory);
            //Map(m => m.meterRegion);
            //Map(m => m.ProductId);
            //Map(m => m.ProductName);
            //Map(m => m.SubscriptionId);
            //Map(m => m.subscriptionName);
            //Map(m => m.publisherType);
            //Map(m => m.publisherId);
            //Map(m => m.publisherName);
            //Map(m => m.resourceGroupName);
            //Map(m => m.ResourceId);
            //Map(m => m.resourceLocation);
            //Map(m => m.location);
            //Map(m => m.effectivePrice);
            //Map(m => m.quantity);
            //Map(m => m.unitOfMeasure);
            //Map(m => m.chargeType);
            //Map(m => m.billingCurrency);
            //Map(m => m.pricingCurrency);
            //Map(m => m.costInBillingCurrency);
            //Map(m => m.costInPricingCurrency);
            //Map(m => m.costInUsd);
            //Map(m => m.paygCostInBillingCurrency);
            //Map(m => m.paygCostInUsd);
            //Map(m => m.exchangeRatePricingToBilling);
            //Map(m => m.exchangeRateDate);
            //Map(m => m.isAzureCreditEligible);
            //Map(m => m.serviceInfo1);
            //Map(m => m.serviceInfo2);
            //Map(m => m.additionalInfo);
            //Map(m => m.tags);
            //Map(m => m.PayGPrice);
            //Map(m => m.frequency);
            //Map(m => m.term);
            //Map(m => m.reservationId);
            //Map(m => m.reservationName);
            //Map(m => m.pricingModel);
            //Map(m => m.unitPrice);
            //Map(m => m.costAllocationRuleName);
            //Map(m => m.benefitId);

        }
        public string InvoiceSectionName { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string AccountOwnerId { get; set; } = "";
        public string SubscriptionId { get; set; } = "";
        public string SubscriptionName { get; set; } = "";
        public string ResourceGroup { get; set; } = "";
        public string ResourceLocation { get; set; } = "";
        public string Date { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string MeterCategory { get; set; } = "";
        public string MeterSubCategory { get; set; } = "";
        public string MeterId { get; set; } = "";
        public string MeterName { get; set; } = "";
        public string MeterRegion { get; set; } = "";
        public string UnitOfMeasure { get; set; } = "";
        public double Quantity { get; set; } = 0.0;
        public double EffectivePrice { get; set; } = 0.0;
        public double CostInBillingCurrency { get; set; } = 0.0;
        public string CostCenter { get; set; } = "";
        public string ConsumedService { get; set; } = "";
        public string ResourceId { get; set; } = "";
        public string Tags { get; set; } = "";
        public string OfferId { get; set; } = "";
        public string AdditionalInfo { get; set; } = "";
        public string ServiceInfo1 { get; set; } = "";
        public string ServiceInfo2 { get; set; } = "";
        public string ResourceName { get; set; } = "";
        public string ReservationId { get; set; } = "";
        public string ReservationName { get; set; } = "";
        public double UnitPrice { get; set; } = 0.0;
        public string ProductOrderId { get; set; } = "";
        public string ProductOrderName { get; set; } = "";
        public string Term { get; set; } = "";
        public string PublisherType { get; set; } = "";
        public string PublisherName { get; set; } = "";
        public string ChargeType { get; set; } = "";
        public string Frequency { get; set; } = "";
        public string PricingModel { get; set; } = "";
        public string AvailabilityZone { get; set; } = "";
        public string BillingAccountId { get; set; } = "";
        public string BillingAccountName { get; set; } = "";
        public string BillingCurrencyCode { get; set; } = "";
        public string BillingPeriodStartDate { get; set; } = "";
        public string BillingPeriodEndDate { get; set; } = "";
        public string BillingProfileId { get; set; } = "";
        public string BillingProfileName { get; set; } = "";
        public string InvoiceSectionId { get; set; } = "";
        public string IsAzureCreditEligible { get; set; } = "";
        public string PartNumber { get; set; } = "";
        public double PayGPrice { get; set; } = 0.0;
        public string PlanName { get; set; } = "";
        public string ServiceFamily { get; set; } = "";
        public string CostAllocationRuleName { get; set; } = "";
        public string benefitId { get; set; } = "";
        public string benefitName { get; set; } = "";



    }

    public enum BillingFileType
    {
        Raw, 
        Billing
    }
}
