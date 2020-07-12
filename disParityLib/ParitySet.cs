﻿using disParityLib.Infrastructure.Logging.LoggingAbstractBase;
using Microsoft.Win32;
using Serilog.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace disParity {
	public class ParitySet : ProgressReporter {
		private static ILogger Logger = LoggerConstructor.newLogger(typeof(ParitySet));

		private List<DataDrive> drives;
		private Parity parity;
		private byte[] tempBuf = new byte[Parity.BLOCK_SIZE];
		private bool cancel;
		private HashSet<string> errorFiles = new HashSet<string>(); // files that generated errors during an operation; tracked here to avoid complaining about the same file over and over
		private IEnvironment env;

		// For a reportable error generated during a long-running operation (Recover, Verify, etc.)
		public event EventHandler<ErrorMessageEventArgs> ErrorMessage;

		const double TEMP_FLUSH_PERCENT = 0.2;

		public ParitySet(Config config, IEnvironment environment) {
			drives = new List<DataDrive>();
			Config = config;
			parity = new Parity(Config);
			Empty = true;
			env = environment;
		}

		public void ReloadDrives() {
			drives.Clear();
			try {
				foreach (Drive d in Config.Drives) {
					if (File.Exists(Path.Combine(Config.ParityDir, d.Metafile))) {
						Empty = false;
					}
					DataDrive dataDrive = new DataDrive(d.Path, d.Metafile, Config, env);
					dataDrive.ErrorMessage += HandleDataDriveErrorMessage;
					drives.Add(dataDrive);
				}
			} catch (Exception e) {
				drives.Clear();
				throw e;
			}
			// try to record how many drives in the registry
			try {
				Registry.SetValue("HKEY_CURRENT_USER\\Software\\disParity", "dc", Config.Drives.Count, RegistryValueKind.DWord);
				Registry.SetValue("HKEY_CURRENT_USER\\Software\\disParity", "mpb", MaxParityBlock(), RegistryValueKind.DWord);
			} catch { }
		}

		/// <summary>
		/// Returns whether or not there is any parity data generated yet for this parity set
		/// </summary>
		public bool Empty { get; private set; }

		/// <summary>
		/// List of zero or more regular expressions defining files to be ignored
		/// </summary>
		public List<string> Ignore { get; private set; }

		/// <summary>
		/// The config file in use by this parity set.
		/// </summary>
		public Config Config { get; private set; }

		/// <summary>
		/// Returns a copy of the master list of drives in this ParitySet.
		/// </summary>
		public DataDrive[] Drives {
			get {
				return drives.ToArray();
			}
		}

		/// <summary>
		/// Closes any open parity files (called when parity folder is about to move)
		/// </summary>
		public void CloseParity() {
			if (parity != null) {
				parity.Close();
			}
		}

		/// <summary>
		/// Close a parity set in preparation for application shutdown
		/// </summary>
		public void Close() {
			try {
				if (parity != null) {
					parity.Close();
				}
				Config.Save();
			} catch {
				// hide any errors saving config on shutdown
			}
		}

		/// <summary>
		/// Resets all data drives to state reflected in meta data
		/// </summary>
		public void Reset() {
			foreach (DataDrive d in drives) {
				d.Reset();
			}
		}

		/// <summary>
		/// Erase a previously created parity set. 
		/// </summary>
		public void Erase() {
			parity.Close();
			parity.DeleteAll();
			foreach (DataDrive d in drives) {
				d.Clear();
			}
			Empty = true;
		}

		// Update progress state.  Also used for RemoveAllFiles.
		private UInt32 currentUpdateBlocks;
		private UInt32 totalUpdateBlocks;

		/// <summary>
		/// Update a parity set to reflect the latest changes
		/// </summary>
		public void Update(bool scanFirst = false) {
			cancel = false;
			if (Empty) {
				LogFile.Log("No existing parity data found.  Creating new snapshot.");
				Create();
				return;
			}

			try {
				if (scanFirst) {
					// get the current list of files on each drive and compare to old state
					ScanAll();
				}

				if (cancel) {
					return;
				}

				Progress = 0;
				// count total blocks for this update, for progress reporting
				currentUpdateBlocks = 0;
				totalUpdateBlocks = 0;
				foreach (DataDrive d in drives) {
					foreach (FileRecord r in d.Adds) {
						totalUpdateBlocks += r.LengthInBlocks;
					}
					foreach (FileRecord r in d.Deletes) {
						totalUpdateBlocks += r.LengthInBlocks;
					}
				}

				// now process deletes
				int deleteCount = 0;
				long deleteSize = 0;
				DateTime start = DateTime.Now;
				foreach (DataDrive d in drives) {
					FileRecord[] deleteList = new FileRecord[d.Deletes.Count];
					d.Deletes.CopyTo(deleteList);
					foreach (FileRecord r in deleteList) {
						if (RemoveFromParity(d, r)) {
							deleteCount++;
							deleteSize += r.Length;
							d.Deletes.Remove(r);
						}
						if (cancel) {
							return;
						}
					}
					d.UpdateStatus();
				}

				if (deleteCount > 0) {
					TimeSpan elapsed = DateTime.Now - start;
					LogFile.Log("{0} file{1} ({2}) removed in {3:F2} sec", deleteCount, deleteCount == 1 ? "" : "s", Utils.SmartSize(deleteSize), elapsed.TotalSeconds);
				}

				// now process adds
				int addCount = 0;
				long addSize = 0;
				start = DateTime.Now;
				foreach (DataDrive d in drives) {
					FileRecord[] addList = new FileRecord[d.Adds.Count];
					d.Adds.CopyTo(addList);
					foreach (FileRecord r in addList) {
						if (AddToParity(d, r)) {
							addCount++;
							addSize += r.Length;
							d.Adds.Remove(r);
						}
						if (cancel) {
							return;
						}
					}
					d.UpdateStatus();
				}
				if (addCount > 0) {
					TimeSpan elapsed = DateTime.Now - start;
					LogFile.Log("{0} file{1} ({2}) added in {3:F2} sec", addCount, addCount == 1 ? "" : "s", Utils.SmartSize(addSize), elapsed.TotalSeconds);
				}

				// possibly reclaim unused parity space if any files were deleted off the end
				UInt32 maxParityBlock = MaxParityBlock();
				if (maxParityBlock < parity.MaxBlock) {
					UInt32 blocks = parity.MaxBlock - maxParityBlock;
					LogFile.Log(String.Format("Reclaiming {0} blocks of unused parity space...", blocks));
					parity.Trim(MaxParityBlock());
					LogFile.Log(Utils.SmartSize((long)blocks * Parity.BLOCK_SIZE) + " freed on parity drive.");
				}
			} finally {
				foreach (DataDrive d in drives) {
					d.UpdateStatus();
					d.Status = "";
				}
				// make sure all progress bars are reset
				foreach (DataDrive d in drives) {
					d.UpdateFinished();
				}
				parity.Close();
			}

		}

		// Caution: Keep this thread safe!
		public void CancelUpdate() {
			cancel = true;
			// in case we are still doing the pre-update scan
			foreach (DataDrive d in drives) {
				d.CancelScan();
			}
		}

		// Caution: Keep this thread safe!
		public void CancelRecover() {
			cancel = true;
		}

		public void CancelRemoveAll() {
			cancel = true;
		}

		// Caution: Keep this thread safe!
		public void CancelVerify() {
			cancel = true;
			// in case we are still doing the pre-verify scan
			foreach (DataDrive d in drives) {
				d.CancelScan();
			}
		}

		public void CancelHashcheck() {
			cancel = true;
		}

		public void CancelUndelete() {
			cancel = true;
		}

		private bool ValidDrive(DataDrive drive) {
			foreach (DataDrive d in drives) {
				if (d == drive) {
					return true;
				}
			}
			return false;
		}

		public void HashCheck(DataDrive driveToCheck = null) {
			cancel = false;

			int files = 0;
			int failures = 0;
			int inProgres = 0;
			long totalBlocksAllDrives = 0;
			long blocks = 0;

			if (driveToCheck != null) {
				totalBlocksAllDrives = driveToCheck.TotalFileBlocks;
			} else {
				foreach (DataDrive d in drives) {
					totalBlocksAllDrives += d.TotalFileBlocks;
				}
			}
			Progress = 0;

			// If a drive encounters a serious problem during its hash check, this will be set
			Exception fatalException = null;

			// Start hashcheck tasks for each drive
			foreach (DataDrive drive in drives) {
				if (driveToCheck != null && drive != driveToCheck) {
					continue;
				}

				DataDrive d = drive;
				Interlocked.Increment(ref inProgres);
				Task.Factory.StartNew(() => {
					try {
						UInt32 b = 0;
						UInt32 totalBlocks = d.TotalFileBlocks;
						byte[] buf = new byte[Parity.BLOCK_SIZE];
						LogFile.Log("Starting hashcheck for " + d.Root);
						d.Progress = 0;
						using (MD5 hash = MD5.Create())
							foreach (FileRecord r in d.Files) {
								Interlocked.Increment(ref files);
								// skip zero length files
								if (r.Length == 0) {
									continue;
								}
								// make sure file exists
								if (!File.Exists(r.FullPath)) {
									FireErrorMessage(r.FullPath + " not found.  Skipping hash check for this file.");
									b += r.LengthInBlocks;
									Interlocked.Add(ref blocks, r.LengthInBlocks);
									continue;
								}
								// warn if file has been modified
								if (r.Modified) {
									FireErrorMessage("Warning: " + r.FullPath + " has been modified.  Hashcheck will probably fail.");
								}
								d.Status = "Reading " + r.FullPath;
								LogFile.Log(d.Status);
								hash.Initialize();
								int read = 0;
								try {
									using (FileStream s = new FileStream(r.FullPath, FileMode.Open, FileAccess.Read)) {
										while (!cancel && ((read = s.Read(buf, 0, Parity.BLOCK_SIZE)) > 0)) {
											hash.TransformBlock(buf, 0, read, buf, 0);
											d.Progress = (double)b++ / totalBlocks;
											Interlocked.Increment(ref blocks);
										}
									}
								} catch (Exception e) {
									FireErrorMessage("Error reading " + r.FullPath + ": " + e.Message);
									// progress will be little off after this point, but this is very unlikely anyway so let it be
									continue;
								}
								if (cancel) {
									d.Status = "";
									return;
								}
								hash.TransformFinalBlock(buf, 0, 0);
								if (!Utils.HashCodesMatch(hash.Hash, r.HashCode)) {
									FireErrorMessage(r.FullPath + " hash check failed");
									Interlocked.Increment(ref failures);
								}
							}
						d.Status = "";
						if (failures == 0) {
							if (driveToCheck == null) {
								d.Status = "Hash check complete.  No errors found.";
							}
							LogFile.Log("Hash check of " + d.Root + " complete.  No errors found.");
						} else {
							if (driveToCheck == null) {
								d.Status = "Hash check complete.  Errors: " + failures;
							}
							LogFile.Error("Hash check of " + d.Root + " complete.  Errors: " + failures);
						}
					} catch (Exception e) {
						d.Status = "Hash check failed: " + e.Message;
						LogFile.Error("Hash check of " + d.Root + " failed: " + e.Message);
						fatalException = e; // this will halt the hash check of all drives below
					} finally {
						d.Progress = 0;
						Interlocked.Decrement(ref inProgres);
					}
				});

			}

			// wait for all hashcheck threads to complete
			while (inProgres > 0) {
				if (fatalException != null) {
					// something serious happened to one of the drives?
					cancel = true; // stop all the other tasks
								   // CancallableOperation will catch this and log it
					throw fatalException;
				}
				Status = String.Format("Hash check in progress.  Files checked: {0} Failures: {1}", files, failures);
				Progress = (double)blocks / totalBlocksAllDrives;
				Thread.Sleep(100);
			}

			string status;
			if (driveToCheck != null) {
				status = "Hash check of " + driveToCheck.Root + " complete.";
			} else {
				status = "Hash check of all drives complete.";
			}
			if (failures == 0) {
				Status = status;
			} else {
				Status = status + "  Errors: " + failures;
			}
			if (driveToCheck != null) {
				LogFile.Log(Status);
			}
		}

		/// <summary>
		/// Adds a new drive to the parity set to be protected
		/// </summary>
		public DataDrive AddDrive(string path) {
			// to do: Check here that this drive is not alredy in the list!
			string metaFile = FindAvailableMetafileName();

			// make sure there isn't already a file there with this name, if there is, rename it
			string fullPath = Path.Combine(Config.ParityDir, metaFile);
			if (File.Exists(fullPath)) {
				File.Move(fullPath, Path.ChangeExtension(fullPath, ".old"));
			}

			DataDrive newDrive = new DataDrive(path, metaFile, Config, env);
			newDrive.ErrorMessage += HandleDataDriveErrorMessage;
			drives.Add(newDrive);

			// update Config and save
			Config.Drives.Add(new Drive(path, metaFile));
			Config.Save();

			return newDrive;
		}

		public bool CheckAvailableSpaceForUpdate() {
			long available = parity.FreeSpace;
			if (available == -1) {
				LogFile.Error("Could not determine free space available on parity drive");
			} else {
				LogFile.Log("Free space on parity drive: " + Utils.SmartSize(available) + " (" + available + " bytes)");
			}
			if (!Empty) {
				return true;
			}
			UInt32 requiredBlocks = 0;
			foreach (DataDrive d in drives) {
				UInt32 scanBlocks = d.TotalScanBlocks;
				if (scanBlocks > requiredBlocks) {
					requiredBlocks = scanBlocks;
				}
			}
			// also include size of filesX.dat files.  Round up to nearest block
			// for each to give us a little extra wiggle room.
			foreach (DataDrive d in drives) {
				requiredBlocks += Parity.LengthInBlocks(d.PredictedMetaFileSize);
			}
			long required = (long)requiredBlocks * Parity.BLOCK_SIZE;
			LogFile.Log("Space required for initial update: " + Utils.SmartSize(required) + " (" + required + " bytes)");
			if (available != -1 && (available < required)) {
				return false;
			}
			return true;
		}

		private void HandleDataDriveErrorMessage(object sender, ErrorMessageEventArgs args) {
			// propagate the error back up to the top so it can be reported to the user
			// but don't log it because DataDrive already logged it
			FireErrorMessage(args.Message, false);
		}

		/// <summary>
		/// Remove all files from the given drive from the parity set
		/// </summary>
		public void RemoveAllFiles(DataDrive drive) {
			totalUpdateBlocks = 0;
			cancel = false;

			// make a copy of the drive's file table to work on
			FileRecord[] files = drive.Files.ToArray();

			// get total blocks for progress reporting
			foreach (FileRecord r in files) {
				totalUpdateBlocks += r.LengthInBlocks;
			}

			currentUpdateBlocks = 0;
			Progress = 0;
			drive.Progress = 0;
			foreach (FileRecord r in files) {
				RemoveFromParity(drive, r);
				if (cancel) {
					break;
				}
			}
			Progress = 0;
			drive.Progress = 0;
		}

		public void RemoveEmptyDrive(DataDrive drive) {
			if (drive.FileCount > 0) {
				throw new Exception("Attempt to remove non-empty drive");
			}

			// find the config entry for this drive
			Drive driveConfig = Config.Drives.Single(s => s.Path == drive.Root);

			// delete the meta data file, if any
			string metaFilePath = Path.Combine(Config.ParityDir, driveConfig.Metafile);
			if (File.Exists(metaFilePath)) {
				File.Delete(Path.Combine(Config.ParityDir, driveConfig.Metafile));
			}

			// remove it from the config and save
			Config.Drives.Remove(driveConfig);
			Config.Save();

			// finally remove the drive from the parity set
			drives.Remove(drive);
		}

		private string FindAvailableMetafileName() {
			int fileNo = 0;
			bool found = true;
			string metaFile = "";
			while (found) {
				fileNo++;
				metaFile = String.Format("files{0}.dat", fileNo);
				found = false;
				foreach (Drive d in Config.Drives) {
					if (d.Metafile == metaFile) {
						found = true;
						break;
					}
				}
			}
			return metaFile;
		}

		// Recover state variables
		private UInt32 recoverTotalBlocks;
		private UInt32 recoverBlocks;

		/// <summary>
		/// Recover all files from the given drive to the given location
		/// </summary>
		public void Recover(DataDrive drive, string path, out int successes, out int failures) {
			cancel = false;
			if (!ValidDrive(drive)) {
				throw new Exception("Invalid drive passed to Recover");
			}
			successes = 0;
			failures = 0;
			recoverTotalBlocks = 0;
			errorFiles.Clear();
			Progress = 0;
			foreach (FileRecord f in drive.Files) {
				recoverTotalBlocks += f.LengthInBlocks;
			}
			recoverBlocks = 0;
			try {
				foreach (FileRecord f in drive.Files)
					if (RecoverFile(drive, f, path)) {
						successes++;
					} else {
						if (cancel) {
							return;
						}
						failures++;
					}
			} finally {
				Progress = 0;
				drive.Progress = 0;
				drive.Status = "";
			}
		}

		public void Undelete(DataDrive drive, List<string> fileNames) {
			List<FileRecord> files = new List<FileRecord>();

			foreach (FileRecord r in drive.Deletes) {
				if (fileNames.Contains(r.FullPath)) {
					files.Add(r);
				}
			}
			if (files.Count == 0) {
				LogFile.Log("No files to undelete.");
				return;
			}

			LogFile.Log("Beginning undelete for {0} file{1}", files.Count, files.Count == 1 ? "" : "s");

			cancel = false;
			recoverTotalBlocks = 0;
			errorFiles.Clear();
			Progress = 0;

			try {
				foreach (FileRecord r in files) {
					recoverTotalBlocks += r.LengthInBlocks;
				}
				recoverBlocks = 0;
				int errors = 0;
				int restored = 0;
				foreach (FileRecord r in files) {
					if (RecoverFile(drive, r, drive.Root)) {
						restored++;
						drive.Deletes.Remove(r);
						drive.MaybeRemoveAddByName(r.FullPath);
					} else if (!cancel) {
						errors++;
					}
					if (cancel) {
						break;
					} else {
						string statusMsg = String.Format("{0} file{1} restored.", restored, restored == 1 ? "" : "s");
						if (errors > 0) {
							statusMsg += " Errors: " + errors;
						}
						Status = statusMsg;
					}
				}
			} finally {
				drive.Progress = 0;
				drive.Status = "";
			}

		}

		private bool RecoverFile(DataDrive drive, FileRecord r, string path) {
			string fullPath = Utils.MakeFullPath(path, r.Name);
			drive.Status = "Recovering " + r.Name + " ...";
			LogFile.Log(drive.Status);
			drive.Progress = 0;
			try {
				// make sure the destination directory exists
				Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
				MD5 hash = MD5.Create();
				hash.Initialize();
				using (FileStream f = new FileStream(fullPath, FileMode.Create, FileAccess.Write)) {
					ParityBlock parityBlock = new ParityBlock(parity);
					long leftToWrite = r.Length;
					UInt32 block = r.StartBlock;
					while (leftToWrite > 0) {
						RecoverBlock(drive, block, parityBlock);
						int blockSize = leftToWrite > Parity.BLOCK_SIZE ? Parity.BLOCK_SIZE : (int)leftToWrite;
						f.Write(parityBlock.Data, 0, blockSize);
						hash.TransformBlock(parityBlock.Data, 0, blockSize, parityBlock.Data, 0);
						leftToWrite -= Parity.BLOCK_SIZE;
						block++;
						drive.Progress = (double)(block - r.StartBlock) / r.LengthInBlocks;
						Progress = (double)(recoverBlocks + (block - r.StartBlock)) / recoverTotalBlocks;
						if (cancel) {
							f.Close();
							File.Delete(fullPath);
							return false;
						}
					}
					hash.TransformFinalBlock(parityBlock.Data, 0, 0);
				}
				drive.Progress = 0;
				File.SetCreationTime(fullPath, r.CreationTime);
				File.SetLastWriteTime(fullPath, r.LastWriteTime);
				File.SetAttributes(fullPath, r.Attributes);
				if (r.Length > 0 && !Utils.HashCodesMatch(hash.Hash, r.HashCode)) {
					FireErrorMessage("Hash verify failed for \"" + fullPath + "\".  Recovered file is probably corrupt.");
					return false;
				} else {
					return true;
				}
			} catch (Exception e) {
				FireErrorMessage("Error recovering \"" + fullPath + "\": " + e.Message);
				return false;
			} finally {
				// no matter what happens, keep the progress bar advancing by the right amount
				recoverBlocks += r.LengthInBlocks;
				Progress = (double)recoverBlocks / recoverTotalBlocks;
			}
		}

		private void RecoverBlock(DataDrive drive, UInt32 block, ParityBlock parity) {
			FileRecord r;
			parity.Load(block);
			foreach (DataDrive d in drives) {
				if (d != drive) {
					string error = "";
					try {
						if (d.ReadBlock(block, tempBuf, out r)) {
							parity.Add(tempBuf);
							if (r.Modified) {
								error = String.Format("Warning: {0} has been modified.  Some recovered files may be corrupt.", r.FullPath);
							}
						} else if (r != null && !File.Exists(r.FullPath)) {
							error = String.Format("Warning: {0} could not be found.  Some recovered files may be corrupt.", r.FullPath);
						}
					} catch (Exception e) {
						error = e.Message; // ReadBlock should have constructed a nice error message for us
					}
					if (error != "" && errorFiles.Add(error)) {
						FireErrorMessage(error);
					}
				}
			}
		}

		private bool AddToParity(DataDrive drive, FileRecord r) {
			string fullPath = r.FullPath;
			// file may have been deleted, or attributes may have changed since we scanned, so refresh
			if (!r.RefreshAttributes()) {
				LogFile.Error("{0} no longer exists.", r.FullPath);
				return false;
			}

			if (!drive.PrepareToAdd(r)) {
				FireErrorMessage(String.Format("Unable to expand {0} to add {1}.  File will be skipped this update.", drive.MetaFile, fullPath));
				return false;
			}

			if (r.Length > 0) {
				// See if we can find an empty chunk in the parity we can re-use.
				// We don't want just any empty spot, we want the smallest one 
				// that is large enough to contain the file, to minimize 
				// fragmentation.  A chunk that is exactly the same size is ideal.
				List<FreeNode> freeList = drive.GetFreeList();
				UInt32 startBlock = FreeNode.FindBest(freeList, r.LengthInBlocks);
				if (startBlock == FreeNode.INVALID_BLOCK) {
					startBlock = drive.MaxBlock;
				}
				UInt32 endBlock = startBlock + r.LengthInBlocks;

				// compute how much space this update is going to require
				long required = 0;
				if (endBlock > parity.MaxBlock) {
					// File is going on the end, so we are going to need additional space for the growing parityX.dat file
					required = ((long)(endBlock - parity.MaxBlock)) * Parity.BLOCK_SIZE;
				}
				long available = parity.FreeSpace;
				if ((available != -1) && (available < required)) {
					FireErrorMessage(String.Format("Insufficient space available on {0} to process " +
					  "{1}.  File will be skipped this update. (Required: {2} " +
					  "Available: {3})", Config.ParityDir, fullPath, Utils.SmartSize(required), Utils.SmartSize(available)));
					return false;
				}

				r.StartBlock = startBlock;
				if (LogFile.Verbose) {
					LogFile.Log("Adding {0} to blocks {1} to {2}...", fullPath, startBlock, endBlock - 1);
				} else {
					LogFile.Log("Adding {0}...", fullPath);
				}

				drive.Status = "Adding " + fullPath;

				// pre-allocate actual needed parity space before even trying to add the file
				if (endBlock > parity.MaxBlock) {
					LogFile.Log(String.Format("Extending parity by {0} blocks for add...", endBlock - parity.MaxBlock));
					if (!ExtendParity(endBlock)) {
						if (!cancel) {
							FireErrorMessage(String.Format("Unable to extend parity space for {0}.  File will be skipped this update.", fullPath));
						}
						return false;
					}
					LogFile.Log("Parity extended");
				}

				if (!XORFileWithParity(drive, r, false)) {
					if (!cancel) {
						// assume FireErrorMessage was already called
						LogFile.Error("Could not add {0} to parity.  File will be skipped.", r.FullPath);
					}
					return false;
				}
			}
			drive.AddFile(r);

			return true;
		}

		/// <summary>
		/// Extends on-disk parity space up to the given block number, by writing zeros to new empty blocks as necessary
		/// </summary>
		/// <returns>true on success, false on any failure (most likely out of disk space)</returns>
		private bool ExtendParity(UInt32 block) {
			if (block < parity.MaxBlock) {
				return true;
			}
			byte[] emptyBlock = new byte[Parity.BLOCK_SIZE];
			while (parity.MaxBlock < block) {
				if (!parity.WriteBlock(parity.MaxBlock, emptyBlock)) {
					return false;
				}
				if (cancel) {
					return false;
				}
			}
			return true;
		}

		private bool RemoveFromParity(DataDrive drive, FileRecord r) {
			if (r.Length > 0) {
				string fullPath = r.FullPath;
				UInt32 startBlock = r.StartBlock;
				UInt32 endBlock = startBlock + r.LengthInBlocks;
				if (LogFile.Verbose) {
					LogFile.Log("Removing {0} from blocks {1} to {2}...", fullPath, startBlock, endBlock - 1);
				} else {
					LogFile.Log("Removing {0}...", fullPath);
				}

				drive.Status = "Removing  " + fullPath;

				// Optimization: if the file still exists and is unmodified, we can remove it much faster this way
				if (!r.Modified && XORFileWithParity(drive, r, true)) {
					drive.RemoveFile(r);
					return true;
				}

				UInt32 totalProgresBlocks = r.LengthInBlocks + (UInt32)(TEMP_FLUSH_PERCENT * r.LengthInBlocks);

				// Recalulate parity from scratch for all blocks that contained the deleted file's data.
				using (ParityChange change = new ParityChange(parity, Config, startBlock, r.LengthInBlocks)) {
					try {
						byte[] data = new byte[Parity.BLOCK_SIZE];
						for (UInt32 b = startBlock; b < endBlock; b++) {
							change.Reset(false);
							foreach (DataDrive d in drives) {
								if (d == drive) {
									continue;
								}
								// Note it's possible that this file may also have been deleted. That's OK, ReadFileData 
								// returns false and we don't try to add the deleted file to the parity.
								FileRecord f;
								try {
									if (d.ReadBlock(b, data, out f)) {
										change.AddData(data);
									}
								} catch (Exception e) {
									FireErrorMessage(e.Message);
									return false;
								}
							}
							change.Write();
							currentUpdateBlocks++;
							drive.Progress = (double)(b - startBlock) / totalProgresBlocks;
							Progress = (double)currentUpdateBlocks / totalUpdateBlocks;
							if (cancel) {
								return false;
							}
						}
						FlushTempParity(drive, change);

					} catch (Exception e) {
						env.LogCrash(e);
						FireErrorMessage(String.Format("Error removing {0}: {1}", r.FullPath, e.Message));
						return false;
					}
				}
			}
			drive.RemoveFile(r);

			return true;
		}

		private void FlushTempParity(DataDrive drive, ParityChange change) {
			bool saveInProgress = true;
			Task.Factory.StartNew(() => {
				try {
					change.Save();
				} catch {
				} finally {
					saveInProgress = false;
				}
			});

			while (saveInProgress) {
				Thread.Sleep(20);
				drive.Progress = (1.0 - TEMP_FLUSH_PERCENT) + TEMP_FLUSH_PERCENT * change.SaveProgress;
			}
			drive.Progress = 0;
		}

		/// <summary>
		/// XORs the data from the given file with the parity data.  This either adds the file to 
		/// parity or removes it from parity if it was already there.  If checkHash is true,
		/// it verifies the file's hash matches the hash on record before commiting the parity.
		/// If false, it updates the file's hash on record.
		/// </summary>
		private bool XORFileWithParity(DataDrive drive, FileRecord r, bool checkHash) {
			if (!File.Exists(r.FullPath)) {
				return false;
			}
			if (r.Length == 0) {
				return true;
			}

			using (ParityChange change = new ParityChange(parity, Config, r.StartBlock, r.LengthInBlocks)) {
				byte[] data = new byte[Parity.BLOCK_SIZE];
				MD5 hash = MD5.Create();
				hash.Initialize();
				UInt32 endBlock = r.StartBlock + r.LengthInBlocks;
				UInt32 totalProgresBlocks = r.LengthInBlocks + (UInt32)(TEMP_FLUSH_PERCENT * r.LengthInBlocks);

				FileStream f;
				try {
					f = new FileStream(r.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
				} catch (Exception e) {
					FireErrorMessage(String.Format("Error opening {0}: {1}", r.FullPath, e.Message));
					return false;
				}
				try {
					for (UInt32 b = r.StartBlock; b < endBlock; b++) {
						Int32 bytesRead;
						try {
							bytesRead = f.Read(data, 0, Parity.BLOCK_SIZE);
						} catch (Exception e) {
							FireErrorMessage(String.Format("Error reading {0}: {1}", r.FullPath, e.Message));
							return false;
						}
						if (b == (endBlock - 1)) {
							hash.TransformFinalBlock(data, 0, bytesRead);
						} else {
							hash.TransformBlock(data, 0, bytesRead, data, 0);
						}
						while (bytesRead < Parity.BLOCK_SIZE) {
							data[bytesRead++] = 0;
						}
						change.Reset(true);
						change.AddData(data);
						change.Write();
						currentUpdateBlocks++;
						drive.Progress = (double)(b - r.StartBlock) / totalProgresBlocks;
						Progress = (double)currentUpdateBlocks / totalUpdateBlocks;
						if (cancel) {
							return false;
						}
					}
				} catch (Exception e) {
					env.LogCrash(e);
					FireErrorMessage(String.Format("Unexpected error while processing {0}: {1}", r.FullPath, e.Message));
					if (e is TempParityFailure) {
						FireErrorMessage(String.Format("Check available disk space for your temp parity location \"{0}\".  It should have at least as much space as the largest file in your backup.  The location of the temp parity folder can be changed from the Options dialog.", Config.TempDir));
					}
					return false;
				} finally {
					f.Dispose();
				}

				if (checkHash) {
					if (!Utils.HashCodesMatch(hash.Hash, r.HashCode)) {
						LogFile.Error("Tried to remove existing file but hash codes don't match.");
						return false;
					}
				} else {
					r.HashCode = hash.Hash;
				}

				FlushTempParity(drive, change); // commit the parity change to disk
			}
			drive.Progress = 0;
			return true;
		}



		private void PrintBlockMask(DataDrive d) {
			BitArray blockMask = d.BlockMask;
			foreach (bool b in blockMask) {
				Console.Write("{0}", b ? 'X' : '.');
			}
			Console.WriteLine();
		}

		/// <summary>
		/// Calculates the highest used parity block across all drives
		/// </summary>
		private UInt32 MaxParityBlock() {
			UInt32 maxBlock = 0;
			foreach (DataDrive d in drives) {
				if (d.MaxBlock > maxBlock) {
					maxBlock = d.MaxBlock;
				}
			}
			return maxBlock;
		}

		/// <summary>
		/// Scan all the drives.  Only called right before an update.
		/// </summary>
		private void ScanAll() {
			foreach (DataDrive d in drives) {
				d.Scan();
			}
		}

		/// <summary>
		/// Create a new snapshot from scratch
		/// </summary>
		private void Create() {
			DateTime start = DateTime.Now;
			// TO DO: check free space on parity drive here?

			UInt32 totalBlocks = 1; // make it one so no chance of divide-by-zero below
			foreach (DataDrive d in drives) {
				d.BeginFileEnum();
				UInt32 scanBlocks = d.TotalScanBlocks;
				if (scanBlocks > totalBlocks) {
					totalBlocks = scanBlocks;
				}
			}

			try {
				ParityBlock parityBlock = new ParityBlock(parity);
				byte[] dataBuf = new byte[Parity.BLOCK_SIZE];
				UInt32 block = 0;

				bool done = false;
				while (!done) {
					done = true;
					foreach (DataDrive d in drives) {
						if (d.GetNextBlock(done ? parityBlock.Data : dataBuf)) {
							if (done) {
								done = false;
							} else {
								parityBlock.Add(dataBuf);
							}
						}
					}
					if (!done) {
						parityBlock.Write(block);
					}
					Progress = (double)block / totalBlocks;
					block++;

					if (cancel) {
						// we can't salvage an initial update that was cancelled so we'll have to start again from scratch next time.
						LogFile.Error("Initial update cancelled.  Resetting parity to empty.");
						Erase();
						return;
					}

				}
			} catch (Exception e) {
				LogFile.Error("Fatal error on initial update: " + e.Message);
				LogFile.Log(e.StackTrace);
				// can't recover from errors either, must also start over from scratch
				Erase();
				throw new UpdateFailedException(e.Message);
			} finally {
				foreach (DataDrive d in drives) {
					d.EndFileEnum();
				}
				parity.Close();
				if (!cancel) {
					Empty = false;
				}
			}
		}

		public void Verify() {
			cancel = false;
			VerifyErrors = 0;
			VerifyRecovers = 0;
			UInt32 maxBlock = MaxParityBlock();
			List<FileRecord> suspectFiles = new List<FileRecord>();
			DateTime lastStatus = DateTime.Now;
			TimeSpan minTimeDelta = TimeSpan.FromMilliseconds(100); // don't update status more than 10x per second

			Progress = 0;

			FileRecord r;
			ParityBlock parityBlock = new ParityBlock(parity);
			ParityBlock calculatedParityBlock = new ParityBlock(parity);
			byte[] buf = new byte[Parity.BLOCK_SIZE];
			for (UInt32 block = 0; block < maxBlock; block++) {
				parityBlock.Load(block);
				bool firstRead = true;
				foreach (DataDrive d in drives) {
					try {
						if (firstRead) {
							if (d.ReadBlock(block, calculatedParityBlock.Data, out r)) {
								firstRead = false;
							}
						} else if (d.ReadBlock(block, buf, out r)) {
							calculatedParityBlock.Add(buf);
						}
					} catch (Exception e) {
						FireErrorMessage(e.Message);
					}
				}
				if (firstRead) {
					// no blocks were read, this block should be empty
					calculatedParityBlock.Clear();
				}
				if (!calculatedParityBlock.Equals(parityBlock)) {
					FireErrorMessage(String.Format("Block {0} does not match", block));
					VerifyErrors++;
					bool reported = false;
					bool canRecover = true;
					foreach (DataDrive dr in drives) {
						FileRecord f = dr.FileFromBlock(block);
						if (f == null) {
							continue;
						}
						if (f.Modified) {
							canRecover = false;
						}
						if (!suspectFiles.Contains(f)) {
							suspectFiles.Add(f);
							if (!reported) {
								FireErrorMessage("Block " + block + " contains data from the following file or files (each file will only be reported once per verify pass):");
								reported = true;
							}
							string error = f.FullPath;
							if (!File.Exists(f.FullPath)) {
								error += " (MISSING)";
							} else if (f.Modified) {
								error += " (MODIFIED)";
							}
							FireErrorMessage(error);
						}
					}
					if (canRecover) {
						parity.WriteBlock(block, calculatedParityBlock.Data);
						FireErrorMessage("Block " + block + " repaired.");
						VerifyRecovers++;
					} else {
						FireErrorMessage("Cannot repair block + " + block + " because one or more files are modified or missing.");
					}
				}
				if ((DateTime.Now - lastStatus) > minTimeDelta) {
					Status = String.Format("{0} of {1} parity blocks verified. Errors found: {2} Errors fixed: {3}", block, maxBlock, VerifyErrors, VerifyRecovers);
					lastStatus = DateTime.Now;
				}
				Progress = (double)block / maxBlock;
				if (cancel) {
					break;
				}
			}
		}

		private void FireErrorMessage(string message, bool log = true) {
			message = message.Trim();
			if (log) {
				LogFile.Error(message);
			}
			if (ErrorMessage != null) {
				ErrorMessage(this, new ErrorMessageEventArgs(message));
			}
		}

		#region Properties
		public int VerifyErrors { get; private set; }

		public int VerifyRecovers { get; private set; }

		public DateTime LastChange {
			get {
				DateTime lastChanges = DateTime.MinValue;
				foreach (DataDrive d in drives) {
					if (d.LastChange > lastChanges) {
						lastChanges = d.LastChange;
					}
				}
				return lastChanges;
			}
		}

		private string status;
		public string Status {
			get {
				return status;
			}
			set {
				SetProperty(ref status, "Status", value);
			}
		}
		#endregion
	}

	/// <summary>
	/// For a reportable generated during a long-running operation (Recover, Verify, etc.)
	/// </summary>
	public class ErrorMessageEventArgs : EventArgs {
		public ErrorMessageEventArgs(string msg) {
			Message = msg;
		}
		public string Message { get; private set; }
	}

	public class UpdateFailedException : Exception {
		public UpdateFailedException(string msg) : base(msg) { }
	}

}
