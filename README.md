# Azure Billing v2

This repo contains the code for a sample Azure Function to retrieve the daily billing data for all subscriptions in an Azure Management Group. It uses an HTTP trigger to start the report generation.

The resulting CSV files (one per subscription found) are stored in an Azure Storage Account you define.
The information is retieved via the Azure Cost Management APIs using the following process:

1. [Request report generation](https://learn.microsoft.com/en-us/rest/api/cost-management/generate-cost-details-report/create-operation?tabs=HTTP) for all of the subscriptions in your managment group. The Management group set in the `ManagementGroupId` key in the configuration. The report is generated for the previous day by default or via a `startDate` parameter in the query string.
2. Check the status of each report generation and waits until they are all complete.
3. Once complete, copies the files from the default storage account to the storage account and container you designate. The storage account and container are set in the `StorageConnectionString` and `ContainerName` keys in the configuration.\
 The destination file will named in the following format with the date path prefixed: `yyyy-MM-dd/Billing-{subscription guid}.csv`

The HTTP request will return a JSON summary of the report generation as per the below example. You can easily determine if any reports failed to generate via the HTTP return code (200 for success, 424 for any failures) or from the `hasFailures` value. The subscriptions that had issues will be in the reports list array with a `"success": false` value and a `statusMessage` showing the failure reason.

``` json
{
  "managementGroupId": "XXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX",
  "startDate": "2023-05-03T00:00:00",
  "endDate": "2023-05-03T23:59:59.999",
  "hasFailures": false,
  "failureMessage": null,
  "SubscriptionReports": [
    {
      "subscriptionName": "My Sub name",
      "subscriptionId": "XXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX",
      "reportStatusUrl": "https://management.azure.com/subscriptions/XXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX/providers/Microsoft.CostManagement/costDetailsOperationResults/xxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx?api-version=2022-10-01",
      "reportBlobSas": "https://ccmreportstoragewestus3.blob.core.windows.net/armmusagedetailsreportdownloadcontainer/20230504/xxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx?sv=2018-03-28\u0026sr=b\u0026sig=6TFT5jBp1QRYIrApdrBd4vl%2FTNgeLCw0NViskYMXXl0%3D\u0026spr=https\u0026st=2023-05-04T14%3A30%3A21Z\u0026se=2023-05-05T02%3A35%3A21Z\u0026sp=r",
      "destinationBlobName": "https://XXXXXXX.blob.core.windows.net/billing/2023-05-03/Billing-XXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX.csv",
      "success": true,
      "statusMessage": "Successfully saved report to Blob storage"
    }
  ]
}
```

----

## Set-up Reqirements

- Azure Function App configured with the .NET 7 isolated runtime
  - Build and deploy this project to that Function App
- System Assigned Managed Identity for the Function with `Cost Management Reader` or `Cost Management Contributor` RBAC role
- Azure Storage Account with Blob Storage
- App Configuration Settings:
  - `ManagementGroupId` - the value need to be the Guid Id value of the mangement group
  - `StorageConnectionString` - connection string to the Azure storage account
  - `ContainerName` - name of the storage container to save the billing report CSV. If is does not exist, it will be created at runtime. The name must adhere to the [naming rules](https://learn.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata#container-names).

**NOTE:** If you have a large number of subscriptions in your management group, you may need to deploy the Function to something other than the "Consumption" tier. This is because the Consumption tier will timeout after 5 minutes.
