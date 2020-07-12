using System;
using System.Windows;
using System.Windows.Documents;

namespace disParityUI {
	public partial class LicenseWindow : Window {
		public LicenseWindow(Window owner, LicenseWindowViewModel viewModel) {
			this.Owner = owner;
			InitializeComponent();
			Loaded += HandleLoaded;

			string licenseText = viewModel.LicenseText;
			FlowDocument flowDoc = new FlowDocument();
			flowDoc.Blocks.Add(new Paragraph(new Run(licenseText)));
			LicenseText.Document = flowDoc;
		}

		private void HandleLoaded(object sender, EventArgs args) {
			WindowUtils.RemoveCloseButton(this);
		}

		public void HandleAcceptClick(object Sender, RoutedEventArgs args) {
			DialogResult = true;
		}

		public void HandleDontAcceptClick(object Sender, RoutedEventArgs args) {
			DialogResult = false;
		}

	}
}
