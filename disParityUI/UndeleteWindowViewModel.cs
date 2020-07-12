using disParity;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace disParityUI {

	class UndeleteWindowViewModel : NotifyPropertyChanged {

		public UndeleteWindowViewModel(List<string> files) {
			this.files = new ObservableCollection<string>();
			foreach (string s in files) {
				Files.Add(s);
			}
			SelectedFiles = new List<string>();
		}

		public List<string> SelectedFiles { get; private set; }

		private ObservableCollection<string> files;
		public ObservableCollection<string> Files {
			get {
				return files;
			}
		}

	}

}
