using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Contracts;
using Data.Framework.DataAccess.ProvidersInterface;
using DataAccess.Provider.Dapper.Repository;
using MailKit.Net.Imap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using Utilities;

namespace Email_Ticket_Fetcher
{
    public class Program
    {
        public static void Main(string[] args)
        {
            LogManager.LoadConfiguration(string.Concat(Directory.GetCurrentDirectory(), "/nlog.config"));
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                    services.AddTransient<IUnitOfWork, DapperUnitofWorkForThreading>();
                    services.AddTransient<ImapClient>();
                    services.AddHttpClient();
                    services.AddSignalR();
                    //services.AddDbContext<Email_Ticket_Fetcher.DataContext.AppContext>(options =>
                    //      options.UseSqlServer(
                    //          Configuration.GetConnectionString("ConnectionStrings")));
                    services.AddSingleton<ILoggerManager, LoggerManager>();
                });
    }
}
