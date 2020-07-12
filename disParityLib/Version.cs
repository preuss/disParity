﻿using Microsoft.Win32;
using System;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace disParity {
	public static class Info {
		private static string url;

		private static void GetUrl() {
			if (String.IsNullOrEmpty(url)) {
				url = @"http://www.vilett.com/disParity/forum/";
			}
		}
		public static string Url {
			get {
				GetUrl();
				return url;
			}
		}

	}
	public static class Version {

		private static string version;
		private static bool firstRun;

		private static void GetVersion() {
			if (String.IsNullOrEmpty(version)) {
				Assembly thisAssembly = Assembly.GetExecutingAssembly();
				object[] attributes = thisAssembly.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false);
				if (attributes.Length == 1) {
					version = ((AssemblyFileVersionAttribute)attributes[0]).Version; 
				}
			}
		}

		public delegate void NewVersionAvailablekDelegate(string newVersion);

		public static void DoUpgradeCheck(NewVersionAvailablekDelegate callback) {
#if !DEBUG
			Task.Factory.StartNew(() => {
				try {
					LogFile.Log("Checking for upgrade...");
					UInt32 id = GetID();
					int dc, mpb;
					GetStats(out dc, out mpb);
					string url = @"http://www.vilett.com/disParity/ping.php?id=" + id.ToString() + (firstRun ? "&firstRun=1" : "") +
					  "&dc=" + dc + "&mpb=" + mpb + "&beta=" + (Beta ? "1" : "0") + "&ver=" + Version.VersionString;
					using (WebClient webClient = new WebClient()) {
						byte[] buf = webClient.DownloadData(new System.Uri(url));
						double currentVersion = VersionNum;
						double latestVersion = double.Parse(Encoding.ASCII.GetString(buf), CultureInfo.InvariantCulture);
						LogFile.Log("Current version: {0} Latest version: {1}", currentVersion, latestVersion);
						if (latestVersion > 0 && latestVersion > currentVersion) {
							callback(Encoding.ASCII.GetString(buf));
						}
					}
				} catch (Exception e) {
					LogFile.Error("Error checking for upgrade: " + e.Message);
				}
			});
#endif
		}

		private static void GetStats(out int dc, out int mpb) {
			dc = 0;
			mpb = 0;
			try {
				Object entry = Registry.GetValue("HKEY_CURRENT_USER\\Software\\disParity", "dc", 0);
				if (entry != null) {
					dc = (int)entry; 
				}
				entry = Registry.GetValue("HKEY_CURRENT_USER\\Software\\disParity", "mpb", 0);
				if (entry != null) {
					mpb = (int)entry; 
				}
			} catch (Exception e) {
				LogFile.Error("Error accessing registry: " + e.Message);
			}
		}

		public static UInt32 GetID() {
			firstRun = false;
			try {
				UInt32 id;
				Object entry = Registry.GetValue("HKEY_CURRENT_USER\\Software\\disParity", "ID", 0);
				if (entry == null || (int)entry == 0) {
					firstRun = true;
					Random r = new Random();
					id = (UInt32)r.Next();
					Registry.SetValue("HKEY_CURRENT_USER\\Software\\disParity", "ID", id, RegistryValueKind.DWord);
				} else {
					id = (UInt32)(int)entry; 
				}
				return id;
			} catch (Exception e) {
				LogFile.Error("Error accessing registry: " + e.Message);
				return 0;
			}
		}

		public static bool Beta { get { return true; } }

		public static string VersionString {
			get {
				GetVersion();
				return version;
			}
		}

		public static double VersionNum {
			get {
				GetVersion();
				return double.Parse(version, CultureInfo.InvariantCulture);
			}
		}
	}

}
