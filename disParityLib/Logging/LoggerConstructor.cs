using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace disParityLib.Infrastructure.Logging.LoggingAbstractBase {
	class LoggerConstructor {
		public static ILogger newLogger(Type type) {
			Serilog.ILogger adaptee = Serilog.Log.Logger.ForContext(type);
			ILogger logger = new SerilogAdapter(adaptee);
			return logger;
		}
	}
}
