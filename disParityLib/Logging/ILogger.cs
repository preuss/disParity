using System;

namespace disParityLib.Infrastructure.Logging.LoggingAbstractBase {
	public interface ILogger {
		void Log(LogEntry entry);
		//void Log(string message);
		//void Log(Exception exception);
	}
}