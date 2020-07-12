using disParity;
using System;
using System.Windows.Media;

namespace disParityUI {
	public class CrashWindowViewModel : NotifyPropertyChanged {
		private Exception error;

		public CrashWindowViewModel(Exception e) {
			error = e;
		}

		public ImageSource Icon {
			get {
				return Icons.Urgent;
			}
		}

		public string ForumURL {
			get {
				return @"http://www.vilett.com/disParity/forum/";
			}
		}

		public string ErrorMessage {
			get {
				return "\"" + error.Message + "\"";
			}
		}
	}
}
