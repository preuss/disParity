﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Shell; // for TaskbarItem stuff
using disParity;

namespace disParityUI
{

  class MainWindowViewModel : ViewModel
  {

    private ParitySet paritySet;
    private ObservableCollection<DataDriveViewModel> drives = new ObservableCollection<DataDriveViewModel>();
    private int runningScans;
    private DataDriveViewModel recoverDrive; // current drive being recovered, if any
    private bool updateAfterScan = false;
    private List<string> recoverErrors = new List<string>();
    private Window owner;
    private OptionsDialogViewModel optionsViewModel;
    private Config config;
    private bool scanInProgress;
    private bool updateInProgress;
    private bool recoverInProgress;

    public MainWindowViewModel(Window owner)
    {
      this.owner = owner;
      // Set up application data and log folders
      string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "disParity");
      if (!Directory.Exists(appDataPath))
        Directory.CreateDirectory(appDataPath);
      string logPath = Path.Combine(appDataPath, "logs");
      if (!Directory.Exists(logPath))
        Directory.CreateDirectory(logPath);
      LogFile.Open(Path.Combine(logPath, "disParity.log"), false);

      LoadConfig(appDataPath);

      paritySet = new ParitySet(config);
      AddDrives();
      paritySet.ProgressReport += HandleProgressReport;
      paritySet.RecoverError += HandleRecoverError;

      Left = config.MainWindowX;
      Top = config.MainWindowY;
      Height = config.MainWindowHeight;
      Width = config.MainWindowWidth;

      UpdateStartupMessage();
      ParityLocation = config.ParityDir;

    }

    private void LoadConfig(string appDataPath)
    {
      string ConfigPath = Path.Combine(appDataPath, "Config.xml");
      try {
        config = new Config(ConfigPath);
        config.Load();
      }
      catch (Exception e) {
        throw new Exception("Could not load Config file: " + e.Message);
      }
    }

    /// <summary>
    /// Called from View when main window has loaded
    /// </summary>
    public void Loaded()
    {
      if (!disParity.License.Accepted) {
        if (!ShowLicenseAgreement()) {
          owner.Close();
          return;
        }
        disParity.License.Accepted = true;
      }

      disParity.Version.DoUpgradeCheck(HandleNewVersionAvailable);
      ScanAll();
    }

    private bool ShowLicenseAgreement()
    {
      LicenseWindow window = new LicenseWindow(owner, new LicenseWindowViewModel());
      bool? result = window.ShowDialog();
      return result ?? false;
    }

    private void HandleNewVersionAvailable(string newVersion)
    {
      if (MessageWindow.Show(owner, "New version available", "There is a new version of disParity available.\r\n\r\n" +
        "Would you like to download the latest version now?", MessageWindowIcon.Caution, MessageWindowButton.YesNo) == true) {
        Process.Start("http://www.vilett.com/disParity/beta.html");
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
          {
            Application.Current.MainWindow.Close();
          }));
      }
    }

    public void OptionsChanged()
    {
      ParityLocation = config.ParityDir;

      if (optionsViewModel.ConfigImported) {
        paritySet.ReloadDrives();
        AddDrives();
        optionsViewModel.ConfigImported = false;
      }

      UpdateStartupMessage();
      if (StartupMessage == "")
        ScanAll();
    }

    private void UpdateStartupMessage()
    {
      if (String.IsNullOrEmpty(config.ParityDir))
        StartupMessage = "Welcome to disParity!\r\n\r\n" +
          "To use disParity you must first specify a location where the parity data will be stored.  This location should be a dedicated drive which is at least as large as the largest drive you want to protect.\r\n\r\n" +
          "Press the 'Options...' button on the right.";
      else if (drives.Count == 0)
        StartupMessage = "Add one or more drives to be backed up by pressing the 'Add Drive' button.";
      else
        StartupMessage = "";
    }

    public OptionsDialogViewModel GetOptionsDialogViewModel()
    {
      if (optionsViewModel == null)
        optionsViewModel = new OptionsDialogViewModel(paritySet);
      return optionsViewModel;
    }

    /// <summary>
    /// Called from the view when the app is closing
    /// </summary>
    public void Shutdown()
    {
      // save the main window position and size so it can be restored on next run
      config.MainWindowX = (int)left;
      config.MainWindowY = (int)top;
      config.MainWindowWidth = (int)Width;
      config.MainWindowHeight = (int)Height;
      paritySet.Close();
      LogFile.Close();
    }

    /// <summary>
    /// Adds a new drive with the given path to the parity set
    /// </summary>
    public void AddDrive(string path)
    {
      AddDrive(paritySet.AddDrive(path));
      UpdateStartupMessage();
    }

    private void AddDrives()
    {
      drives.Clear();
      foreach (DataDrive d in paritySet.Drives)
        AddDrive(d);
    }

    private void AddDrive(DataDrive drive)
    {
      drive.ScanCompleted += HandleScanCompleted;
      DataDriveViewModel vm = new DataDriveViewModel(drive);
      vm.PropertyChanged += HandleDataDrivePropertyChanged;
      drives.Add(vm);
    }

    public void ScanAll()
    {
      if (drives.Count == 0)
        return;
      scanInProgress = true;
      Status = "Scanning drives...";
      runningScans = drives.Count;
      foreach (DataDriveViewModel vm in drives)
        vm.Scan();
    }

    private void HandleScanCompleted(object sender, EventArgs args)
    {
      runningScans--;
      if (runningScans == 0) {
        bool anyDriveNeedsUpdate = false;
        scanInProgress = false;
        foreach (DataDrive d in paritySet.Drives)
          if (d.Status == DriveStatus.AccessError) {
            Status = "Error(s) encountered during scan!";
            return;
          } else if (d.Status == DriveStatus.UpdateRequired)
            anyDriveNeedsUpdate = true;
        if (anyDriveNeedsUpdate)
          if (updateAfterScan) {
            updateAfterScan = false;
            Update();
          } 
          else
            Status = "Changes detected.  Update required.";
        else
          DisplayUpToDateStatus();
        ProgressState = TaskbarItemProgressState.None;
      }
    }

    private void DisplayUpToDateStatus()
    {
      long totalSize = 0;
      int totalFiles = 0;
      foreach (DataDriveViewModel vm in drives) {
        totalSize += vm.DataDrive.TotalFileSize;
        totalFiles += vm.DataDrive.FileCount;
      }
      Status = String.Format("{1:N0} files ({0}) protected.  All drives up to date.",
        Utils.SmartSize(totalSize), totalFiles);
    }

    public void UpdateAll()
    {
      updateAfterScan = true;
      ScanAll();
    }

    private void Update()
    {
      updateInProgress = true;
      Status = "Update In Progress...";
      Task.Factory.StartNew(() =>
      {
        try {
          paritySet.Update();
          DisplayUpToDateStatus();
        }
        catch (Exception e) {
          Status = "Update failed: " + e.Message;
        }
        finally {
          Progress = 0;
          ProgressState = TaskbarItemProgressState.None;
          updateInProgress = false;
        }
      }
      );
    }

    public void RecoverDrive(DataDriveViewModel drive, string path)
    {
      recoverInProgress = true;
      Status = "Recovering " + drive.Root + " to " + path + "...";
      recoverDrive = drive;
      recoverErrors.Clear();
      // must run actual recover as a separate Task so UI can still update
      Task.Factory.StartNew(() =>
      {
        try {
          int successes;
          int failures;
          paritySet.Recover(drive.DataDrive, path, out successes, out failures);
          if (failures == 0) {
            string msg = String.Format("{0} file{1} successfully recovered!",
              successes, successes == 1 ? "" : "s");
            MessageWindow.Show(owner, "Recovery complete", msg);
          }
          else {
            string msg =
              String.Format("{0} file{1} recovered successfully.\r\n\r\n", successes, successes == 1 ? " was" : "s were") +
              String.Format("{0} file{1} encountered errors during the recovery.", failures, failures == 1 ? "" : "s") +
              "\r\n\r\nWould you like to see a list of errors?";
            if (MessageWindow.Show(owner, "Recovery complete", msg, MessageWindowIcon.Error, MessageWindowButton.YesNo) == true)
              ReportWindow.Show(owner, recoverErrors);
          }
          Status = String.Format("{0} file{1} recovered ({2} failure{3})",
            successes, successes == 1 ? "" : "s", failures, failures == 1 ? "" : "s");
        }
        catch (Exception e) {
          MessageWindow.Show(owner, "Recover failed!", 
            "Sorry, an unexpected error occurred while recovering the drive:\r\n\r\n" + e.Message);
        }
        finally {
          Progress = 0;
          // recoverDrive.UpdateStatus(); what was this for?
          ProgressState = TaskbarItemProgressState.None;
          recoverInProgress = false;
        }
      }
      );
    }

    private void HandleProgressReport(object sender, ProgressReportEventArgs args)
    {
      ProgressState = TaskbarItemProgressState.Normal;
      Progress = args.Progress;
    }


    private void HandleRecoverError(object sender, RecoverErrorEventArgs args)
    {
      recoverErrors.Add(args.Message);
    }

    // for updating overall progress bar during a scan
    private void HandleDataDrivePropertyChanged(object sender, PropertyChangedEventArgs args)
    {
      if (scanInProgress && args.PropertyName == "Progress") {
        double progress = 0;
        foreach (DataDriveViewModel vm in drives)
          progress += vm.Progress;
        ProgressState = TaskbarItemProgressState.Normal;
        Progress = progress;
      }
    }


    public void Cancel()
    {
      if (scanInProgress) {
        foreach (DataDriveViewModel vm in drives)
          vm.DataDrive.CancelScan();
      }
      else if (updateInProgress) {
        // TO DO: cancel update here
      }
      else if (recoverInProgress) {
        // TO DO: cancel recover here
      }
    }

    public ObservableCollection<DataDriveViewModel> Drives
    {
      get
      {
        return drives;
      }
    }

    #region Properties

    private string parityLocation;
    public string ParityLocation
    {
      get
      {
        return parityLocation;
      }
      set
      {
        SetProperty(ref parityLocation, "ParityLocation", value);
      }
    }

    private string startupMessage;
    public string StartupMessage
    {
      get
      {
        return startupMessage;
      }
      set
      {
        SetProperty(ref startupMessage, "StartupMessage", value);
        if (startupMessage != "")
          StartupMessageVisibility = Visibility.Visible;
        else
          StartupMessageVisibility = Visibility.Hidden;
      }
    }

    private Visibility startupMessageVisibility;
    public Visibility StartupMessageVisibility
    {
      get
      {
        return startupMessageVisibility;
      }
      set
      {
        SetProperty(ref startupMessageVisibility, "StartupMessageVisibility", value);
      }
    }

    public bool Busy
    {
      get
      {
        return scanInProgress || updateInProgress || recoverInProgress;
      }
    }

    private string status = "";
    public string Status
    {
      get
      {
        return status;
      }
      set
      {
        SetProperty(ref status, "Status", value);
      }
    }

    private double progress = 0.0;
    public double Progress
    {
      get
      {
        return progress;
      }
      set
      {
        SetProperty(ref progress, "Progress", value);
      }
    }

    private double top;
    public double Top
    {
      get
      {
        return top;
      }
      set
      {
        SetProperty(ref top, "Top", value);
      }
    }

    private double left;
    public double Left
    {
      get
      {
        return left;
      }
      set
      {
        SetProperty(ref left, "Left", value);
      }
    }

    private double height;
    public double Height
    {
      get
      {
        return height;
      }
      set
      {
        SetProperty(ref height, "Height", value);
      }
    }

    private double width;
    public double Width
    {
      get
      {
        return width;
      }
      set
      {
        SetProperty(ref width, "Width", value);
      }
    }

    /// <summary>
    /// This is for the taskbar icon's progress indicator
    /// </summary>
    private TaskbarItemProgressState progressState = TaskbarItemProgressState.None;
    public TaskbarItemProgressState ProgressState
    {
      get
      {
        return progressState;
      }
      set
      {
        SetProperty(ref progressState, "ProgressState", value);
      }
    }

    #endregion

  }

}

