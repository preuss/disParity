using System;
using System.Windows;

namespace disParityUI {

	public partial class UndeleteWindow : Window {

		UndeleteWindowViewModel vm;

		public UndeleteWindow() {
			InitializeComponent();
			Loaded += HandleLoaded;
		}

		private void HandleLoaded(object sender, EventArgs args) {
			WindowUtils.RemoveCloseButton(this);
			vm = (UndeleteWindowViewModel)DataContext;
			SelectAll();
		}

		private void SelectAll() {
			listBox.SelectedItems.Clear();
			foreach (var s in vm.Files)
				listBox.SelectedItems.Add(s);
		}

		public void HandleSelectAllClick(object Sender, RoutedEventArgs args) {
			SelectAll();
		}

		public void HandleUnselectAllClick(object Sender, RoutedEventArgs args) {
			listBox.SelectedItems.Clear();
		}

		public void HandleOKClick(object Sender, RoutedEventArgs args) {
			foreach (var s in listBox.SelectedItems)
				vm.SelectedFiles.Add((string)s);
			DialogResult = true;
		}

	}
}
