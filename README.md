# Azure Billing v2

Sample Azure Function to retrieve  the daily billing data for all subscriptions in an Azure Management Group. The resulting CSV file is stored in a single storage account you define. 
The information is retieved via the Azure Cost Management APIs to:

1. [Request report generation](https://learn.microsoft.com/en-us/rest/api/cost-management/generate-cost-details-report/create-operation?tabs=HTTP) for all of the subscriptions in your managment group. The Management group is set Guid value set in the `ManagementGroupId` key in the configuration. The report is generated for the previous day by default or via a `startDate` parameter in the query string.
2. Check the status of the report
3. Once complete, copy the file from the default storage account to the storage account and container you designate. The storage account and container are set in the `StorageConnectionString` and `ContainerName` keys in the configuration.