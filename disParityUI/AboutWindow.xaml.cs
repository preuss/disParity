using System;
using System.Diagnostics;
using System.Windows;

namespace disParityUI {

	public partial class AboutWindow : Window {

		private AboutWindowViewModel viewModel;

		public AboutWindow(Window owner, AboutWindowViewModel viewModel) {
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
			try {
				Process.Start(new ProcessStartInfo(viewModel.ForumURL));
			} catch (Exception e) {
				// log & hide crash trying to launch forum URL.  User 913413526 crashed here according the logs.
				App.LogCrash(e);
			}
			args.Handled = true;
		}



	}

}
