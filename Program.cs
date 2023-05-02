using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

 var config = new ConfigurationBuilder()
              .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)  // common settings go here.
              .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT")}.json", optional: true, reloadOnChange: false)  // environment specific settings go here
              .AddJsonFile("local.settings.json", optional: true, reloadOnChange: false)  // secrets go here. This file is excluded from source control.
              .AddEnvironmentVariables()
              .Build();

var builder = new HostBuilder()
    .ConfigureLogging(
                logging =>
                {
                    logging.AddConsole();
                })
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton<AzureBillingV2.Apis>();
        services.AddSingleton<IConfigurationRoot>(config);
    });


var host = builder.Build();
host.Run();
