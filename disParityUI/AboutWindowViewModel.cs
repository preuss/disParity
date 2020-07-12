using disParity;

namespace disParityUI {

	public class AboutWindowViewModel : NotifyPropertyChanged {

		public string VersionString {
			get {
				return disParity.Version.VersionString;
			}
		}

		public string Beta {
			get {
				return disParity.Version.Beta ? "beta" : "";
			}
		}

		public string ForumURL {
			get {
				return disParity.Info.Url;
			}
		}
	}
}
