﻿namespace disParityUI {

	class HashcheckOperation : CancellableOperation {

		protected override void DoOperation() {
			if (drive != null) {
				viewModel.ParitySet.HashCheck(drive.DataDrive); 
			}
			else {
				viewModel.ParitySet.HashCheck(); 
			}
		}

		protected override void CancelOperation() {
			viewModel.ParitySet.CancelHashcheck();
		}

		public override string Name { get { return "Hash check"; } }

		protected override bool ScanFirst { get { return false; } }
	}
}
