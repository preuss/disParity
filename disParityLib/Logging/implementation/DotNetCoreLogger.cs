using System;
/*
using Microsoft.Extensions.Logging;

namespace disParityLib.Infrastructure.Logging.LoggingCoreConcrete {
    public class DotNetCoreLogger<T> : disParityLib.Infrastructure.Logging.LoggingAbstractBase.ILogger {
        private readonly ILogger<T> concreteLogger;

        public DotNetCoreLogger(Microsoft.Extensions.Logging.ILogger<T> concreteLgr) {
            this.concreteLogger = concreteLgr ?? throw new ArgumentNullException("Microsoft.Extensions.Logging.ILogger is null");
        }

        public void Log(disParityLib.Infrastructure.Logging.LoggingAbstractBase.LogEntry entry) {
            if (null == entry) {
                throw new ArgumentNullException("LogEntry is null");
            } else {
                switch (entry.Severity) {
                    case LoggingAbstractBase.LoggingEventTypeEnum.Debug:
                        this.concreteLogger.LogDebug(entry.Message);
                        break;
                    case LoggingAbstractBase.LoggingEventTypeEnum.Information:
                        this.concreteLogger.LogInformation(entry.Message);
                        break;
                    case LoggingAbstractBase.LoggingEventTypeEnum.Warning:
                        this.concreteLogger.LogWarning(entry.Message);
                        break;
                    case LoggingAbstractBase.LoggingEventTypeEnum.Error:
                        this.concreteLogger.LogError(entry.Message, entry.Exception);
                        break;
                    case LoggingAbstractBase.LoggingEventTypeEnum.Fatal:
                        this.concreteLogger.LogCritical(entry.Message, entry.Exception);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(string.Format("LogEntry.Severity out of range. (Severity='{0}')", entry.Severity));
                }
            }
        }

        public void Log(string message) {
            this.concreteLogger.LogInformation(message);
        }

        public void Log(Exception exception) {
*/
            /* "Always pass exception as first parameter" from https://blog.rsuter.com/logging-with-ilogger-recommendations-and-best-practices/ */


            /* there is an issue with https://github.com/aspnet/Logging/blob/master/src/Microsoft.Extensions.Logging.Abstractions/LoggerExtensions.cs
             * the default MessageFormatter (for the extension methods) is not doing anything with the "error".  this plays out as not getting full exception information when using extension methods.  :(
             * 
             *         private static string MessageFormatter(FormattedLogValues state, Exception error)
             *         {
             *                  return state.ToString();
             *         }
             *          
             * Below code/implementation is purposely NOT using any extension method(s) to bypass the above MessageFormatter mishap.
             * 
             * */
/*
            this.concreteLogger.Log(LogLevel.Error, exception, exception.Message);
        }
    }
}
*/


/* IoC/DI below */

/*
private static System.IServiceProvider BuildDi(Microsoft.Extensions.Configuration.IConfiguration config) {
    //setup our DI
    IServiceProvider serviceProvider = new ServiceCollection()
        .AddLogging()
        .AddSingleton<IConfiguration>(config)
        .AddSingleton<disParityLib.Infrastructure.Logging.LoggingAbstractBase.ILogger, disParityLib.Infrastructure.Logging.LoggingCoreConcrete.DotNetCoreLogger<Program>>()
        .BuildServiceProvider();

    //configure console logging
    serviceProvider
        .GetService<ILoggerFactory>()
        .AddConsole(LogLevel.Debug);

    return serviceProvider;
}
*/