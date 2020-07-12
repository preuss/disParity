﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace disParityUI {

	public partial class ReportWindow : Window {

		public ReportWindow() {
			InitializeComponent();
			Loaded += HandleLoaded;
		}

		private void HandleLoaded(object sender, EventArgs args) {
			WindowUtils.RemoveCloseButton(this);
		}

		public void HandleOKClick(object Sender, RoutedEventArgs args) {
			DialogResult = true;
		}

		public void HandleSaveClick(object Sender, RoutedEventArgs args) {
			((ReportWindowViewModel)DataContext).Save();
		}

		internal static void Show(Window owner, List<string> report) {
			Application.Current.Dispatcher.Invoke(new Action(() => {
				ReportWindow window = new ReportWindow();
				window.Owner = owner;
				StringBuilder sb = new StringBuilder();
				foreach (string s in report)
					sb.AppendLine(s);
				window.DataContext = new ReportWindowViewModel(sb.ToString());
				window.ShowDialog();
			}));
		}

	}

}
