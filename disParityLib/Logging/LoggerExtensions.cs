using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace disParityLib.Infrastructure.Logging.LoggingAbstractBase {

	public static class LoggerExtensions {
		public static void Log(this ILogger logger, string message) {
			logger.Log(new LogEntry(LoggingEventType.Information, message));
		}

		public static void Log(this ILogger logger, Exception exception) {
			logger.Log(new LogEntry(LoggingEventType.Error, exception.Message, exception));
		}

		// More methods here.
	}
}