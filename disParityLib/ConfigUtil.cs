using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace disParityLib {
	class ConfigUtil {
		public static string ToString(bool boolValue) {
			// Boolean object ToString returns TrueString or FalseString, witch is upper case.
			return boolValue.ToString().ToLower();
		}
		public static string ToString(bool? nullableBoolValue) {
			return nullableBoolValue == null ? null : ToString(nullableBoolValue.Value);
		}
		public static bool ParseToBoolean(string value, bool defaultValue = false) {
			return value == null ? defaultValue : "true".Equals(value, StringComparison.InvariantCultureIgnoreCase);
		}
		public static bool? ParseToNullableBoolean(string value, bool? defaultValue = null) {
			return value == null ? defaultValue : "true".Equals(value, StringComparison.InvariantCultureIgnoreCase);
		}
	}
}
