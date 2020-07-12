using disParity;
using System;

namespace disParityUI {
	internal class Environment : IEnvironment {
		public void LogCrash(Exception e) {
			App.LogCrash(e);
		}
	}
}
