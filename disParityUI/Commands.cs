﻿using System.Windows.Input;

namespace disParityUI {
	internal class Commands {
		public static RoutedUICommand AddDrive;
		public static RoutedUICommand RemoveDrive;
		public static RoutedUICommand ScanDrive;
		public static RoutedUICommand ScanAll;
		public static RoutedUICommand UpdateAll;
		public static RoutedUICommand RecoverDrive;
		public static RoutedUICommand Options;
		public static RoutedUICommand About;
		public static RoutedUICommand CancelOperation;
		public static RoutedUICommand Verify;
		public static RoutedUICommand Hashcheck;
		public static RoutedUICommand HashcheckAll;
		public static RoutedUICommand Undelete;
		public static RoutedUICommand Reset;
		public static RoutedUICommand Log;

		static Commands() {
			AddDrive = new RoutedUICommand("Add Drive...", "AddDrive", typeof(MainWindow));
			RemoveDrive = new RoutedUICommand("Remove Drive", "RemoveDrive", typeof(MainWindow));
			ScanDrive = new RoutedUICommand("Scan Drive", "ScanDrive", typeof(MainWindow));
			ScanAll = new RoutedUICommand("Scan All", "ScanAll", typeof(MainWindow));
			UpdateAll = new RoutedUICommand("Update All", "UpdateAll", typeof(MainWindow));
			RecoverDrive = new RoutedUICommand("Recover Drive...", "RecoverDrive", typeof(MainWindow));
			Options = new RoutedUICommand("Options...", "Options", typeof(MainWindow));
			About = new RoutedUICommand("About...", "About", typeof(MainWindow));
			CancelOperation = new RoutedUICommand("Cancel", "Cancel", typeof(MainWindow));
			Verify = new RoutedUICommand("Verify", "Verify", typeof(MainWindow));
			Hashcheck = new RoutedUICommand("Hashcheck", "Hashcheck", typeof(MainWindow));
			HashcheckAll = new RoutedUICommand("Hashcheck All", "Hashcheck All", typeof(MainWindow));
			Undelete = new RoutedUICommand("Undelete", "Undelete", typeof(MainWindow));
			Reset = new RoutedUICommand("Reset", "Reset", typeof(MainWindow));
			Log = new RoutedUICommand("Log", "Log", typeof(MainWindow));
		}
	}
}
