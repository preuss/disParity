using System;
using System.Diagnostics;
using System.Windows;

namespace disParityUI {

	public partial class CrashWindow : Window {

		private CrashWindowViewModel viewModel;

		public CrashWindow(Window owner, CrashWindowViewModel viewModel) {
			this.Owner = owner;
			this.viewModel = viewModel;
			InitializeComponent();
			DataContext = viewModel;
			Loaded += HandleLoaded;
		}

		private void HandleLoaded(object sender, EventArgs args) {
			WindowUtils.RemoveCloseButton(this);
		}

		public void HandleOKClick(object Sender, RoutedEventArgs args) {
			DialogResult = true;
		}

		public void HandleSupportClick(object Sender, RoutedEventArgs args) {
			Process.Start(new ProcessStartInfo(viewModel.ForumURL));
			args.Handled = true;
		}


	}

}

