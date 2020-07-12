﻿namespace disParityUI {
	internal class ScanOperation : CancellableOperation {

		private bool auto;

		public ScanOperation(bool auto = false) : base() {
			this.auto = auto;
		}

		public override void Begin(MainWindowViewModel viewModel, DataDriveViewModel selectedDrive = null) {
			scanDrive = selectedDrive;
			base.Begin(viewModel);
		}

		protected override void DoOperation() {
			if (anyDriveNeedsUpdate) {
				Status = "Update required."; 
			}
			else {
				DisplayUpToDateStatus(); 
			}
		}

		protected override void CancelOperation() {
			// nothing to cancel
		}

		public override string Name { get { return "Scan"; } }

		// Force a scan even if no changes have been detected, since user is manually requesting it
		protected override bool ForceScan { get { return true; } }
	}
}
