using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureBilling.Test
{
    public class ServiceProvider
    {
        public static T GetRequiredService<T>()
        {
            var provider = Provider();
            return provider.GetRequiredService<T>();
        }
        public static IServiceProvider Provider()
        {
            var configMan = new ConfigurationManager()
                .AddJsonFile("appsettings.json", true)
                .AddJsonFile("local.settings.json", true);


            var services = new ServiceCollection();
            services.AddScoped<IConfiguration>(c =>
            {
                return configMan.Build();
            })
                .AddScoped<ILoggerFactory, LoggerFactory>()
                .AddSingleton<AzureBillingV2.Apis>();


            return services.BuildServiceProvider();
        }
    }
}
