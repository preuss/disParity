using System;

namespace disParityLib.Infrastructure.Logging.LoggingAbstractBase {
	public class LogEntry {
		public readonly LoggingEventType Severity;
		public readonly string Message;
		public readonly Exception Exception;

		public LogEntry(LoggingEventType severity, string message, Exception exception = null) {
			if (message == null) throw new ArgumentNullException("message");
			if (message == string.Empty) throw new ArgumentException("empty", "message");

			this.Severity = severity;
			this.Message = message;
			this.Exception = exception;
		}
	}
}
