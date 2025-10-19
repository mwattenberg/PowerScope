using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PowerScope.Model
{
    /// <summary>
    /// Standalone utility class for testing FTDI DLL availability and functionality
    /// Specifically designed for x64 systems
    /// </summary>
    public static class FTDIDLLTester
    {
        // Import the x64 FTDI functions for testing
        [DllImport("ftd2xx64.dll", EntryPoint = "FT_GetDeviceInfoList")]
        private static extern uint FT_GetDeviceInfoList(out uint numDevices, IntPtr pDest);

        [DllImport("ftd2xx64.dll", EntryPoint = "FT_CreateDeviceInfoList")]
        private static extern uint FT_CreateDeviceInfoList(out uint numDevices);

        [DllImport("ftd2xx64.dll", EntryPoint = "FT_GetLibraryVersion")]
        private static extern uint FT_GetLibraryVersion(out uint libraryVersion);

        /// <summary>
        /// Performs comprehensive FTDI DLL testing for x64 systems
        /// </summary>
        /// <returns>Test results as formatted string</returns>
        public static string RunDLLTest()
        {
            var results = new List<string>();
            results.Add("=== FTDI x64 DLL Verification Test ===");
            results.Add($"Test Time: {DateTime.Now}");
            results.Add($"Process: {Process.GetCurrentProcess().ProcessName}");
            results.Add($"Architecture: {RuntimeInformation.ProcessArchitecture}");
            results.Add($"OS: {RuntimeInformation.OSDescription}");
            results.Add("");

            try
            {
                // Test 1: Basic DLL file existence
                results.Add("Test 1: DLL File Existence");
                var fileCheck = CheckDLLFiles();
                foreach (var check in fileCheck)
                {
                    results.Add($"  {check.Key}: {check.Value}");
                }
                results.Add("");

                // Test 2: Basic DLL loading
                results.Add("Test 2: Basic DLL Loading");
                uint numDevices = 0;
                uint status = FT_GetDeviceInfoList(out numDevices, IntPtr.Zero);
                results.Add($"  FT_GetDeviceInfoList Status: {status} ({GetStatusDescription(status)})");
                results.Add($"  Device Count: {numDevices}");
                results.Add("  Result: ? DLL loaded successfully");
                results.Add("");

                // Test 3: Alternative function test
                results.Add("Test 3: Alternative Function Test");
                uint numDevices2 = 0;
                uint status2 = FT_CreateDeviceInfoList(out numDevices2);
                results.Add($"  FT_CreateDeviceInfoList Status: {status2} ({GetStatusDescription(status2)})");
                results.Add($"  Device Count: {numDevices2}");
                results.Add("  Result: ? Alternative function works");
                results.Add("");

                // Test 4: Library version (if supported)
                try
                {
                    results.Add("Test 4: Library Version");
                    uint libVersion = 0;
                    uint versionStatus = FT_GetLibraryVersion(out libVersion);
                    results.Add($"  Status: {versionStatus} ({GetStatusDescription(versionStatus)})");
                    if (versionStatus == 0) // FT_OK
                    {
                        results.Add($"  Library Version: {libVersion:X8}");
                    }
                    results.Add("");
                }
                catch (EntryPointNotFoundException)
                {
                    results.Add("Test 4: Library Version");
                    results.Add("  FT_GetLibraryVersion not available (older DLL version)");
                    results.Add("");
                }

                // Test 5: Module information
                results.Add("Test 5: Loaded Module Information");
                var moduleInfo = GetLoadedModuleInfo();
                foreach (var kvp in moduleInfo)
                {
                    results.Add($"  {kvp.Key}: {kvp.Value}");
                }

                results.Add("");
                results.Add("=== Overall Result: SUCCESS ===");
                results.Add("The FTDI x64 DLL is properly loaded and functional.");
            }
            catch (DllNotFoundException ex)
            {
                results.Add($"? DLL NOT FOUND: {ex.Message}");
                results.Add("  Possible causes:");
                results.Add("  - ftd2xx64.dll is not in the application directory");
                results.Add("  - ftd2xx64.dll is not in the system PATH");
                results.Add("  - FTDI x64 drivers not installed");
                results.Add("");
                results.Add("  Solutions:");
                results.Add("  1. Download FTDI drivers from: https://ftdichip.com/drivers/");
                results.Add("  2. Copy ftd2xx64.dll to your application directory");
                results.Add("  3. Install FTDI CDM drivers which include the DLL");
            }
            catch (BadImageFormatException ex)
            {
                results.Add($"? ARCHITECTURE MISMATCH: {ex.Message}");
                results.Add("  Possible causes:");
                results.Add("  - 32-bit DLL with 64-bit application");
                results.Add("  - Corrupted DLL file");
                results.Add("  - Wrong DLL version");
                results.Add("");
                results.Add("  Solutions:");
                results.Add("  1. Ensure you have the 64-bit version of ftd2xx64.dll");
                results.Add("  2. Re-download FTDI drivers for your architecture");
            }
            catch (Exception ex)
            {
                results.Add($"? UNEXPECTED ERROR: {ex.Message}");
                results.Add($"  Exception Type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    results.Add($"  Inner Exception: {ex.InnerException.Message}");
                }
            }

            return string.Join(Environment.NewLine, results);
        }

        /// <summary>
        /// Checks for FTDI DLL files in various locations
        /// </summary>
        private static Dictionary<string, string> CheckDLLFiles()
        {
            var info = new Dictionary<string, string>();
            
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                
                // Check application directory
                string appDll64 = Path.Combine(appDir, "ftd2xx64.dll");
                string appDll32 = Path.Combine(appDir, "ftd2xx.dll");
                
                info["App Dir ftd2xx64.dll"] = File.Exists(appDll64) ? 
                    $"? Found ({new FileInfo(appDll64).Length:N0} bytes)" : "? Not found";
                    
                info["App Dir ftd2xx.dll"] = File.Exists(appDll32) ? 
                    $"Found ({new FileInfo(appDll32).Length:N0} bytes)" : "Not found";
                
                // Check system directory
                string sysDll64 = Path.Combine(systemDir, "ftd2xx64.dll");
                string sysDll32 = Path.Combine(systemDir, "ftd2xx.dll");
                
                info["System32 ftd2xx64.dll"] = File.Exists(sysDll64) ? 
                    $"? Found ({new FileInfo(sysDll64).Length:N0} bytes)" : "? Not found";
                    
                info["System32 ftd2xx.dll"] = File.Exists(sysDll32) ? 
                    $"Found ({new FileInfo(sysDll32).Length:N0} bytes)" : "Not found";
                
                // Check SysWOW64 directory (32-bit DLLs on 64-bit systems)
                if (Environment.Is64BitOperatingSystem)
                {
                    string sysWow64Dir = Path.Combine(windowsDir, "SysWOW64");
                    string wow64Dll = Path.Combine(sysWow64Dir, "ftd2xx.dll");
                    
                    info["SysWOW64 ftd2xx.dll"] = File.Exists(wow64Dll) ? 
                        $"Found ({new FileInfo(wow64Dll).Length:N0} bytes)" : "Not found";
                }
                
                info["Application Directory"] = appDir;
            }
            catch (Exception ex)
            {
                info["File Check Error"] = ex.Message;
            }
            
            return info;
        }

        /// <summary>
        /// Gets information about loaded modules
        /// </summary>
        private static Dictionary<string, string> GetLoadedModuleInfo()
        {
            var info = new Dictionary<string, string>();
            
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var ftdiModules = currentProcess.Modules.Cast<ProcessModule>()
                    .Where(m => m.ModuleName.ToLowerInvariant().Contains("ftd2xx"))
                    .ToList();
                
                if (ftdiModules.Any())
                {
                    for (int i = 0; i < ftdiModules.Count; i++)
                    {
                        var module = ftdiModules[i];
                        string prefix = ftdiModules.Count > 1 ? $"Module {i + 1} " : "";
                        
                        info[$"{prefix}Name"] = module.ModuleName;
                        info[$"{prefix}File Path"] = module.FileName;
                        info[$"{prefix}Base Address"] = $"0x{module.BaseAddress.ToInt64():X}";
                        info[$"{prefix}Module Size"] = $"{module.ModuleMemorySize:N0} bytes";
                        
                        try
                        {
                            var fileInfo = new FileInfo(module.FileName);
                            info[$"{prefix}File Size"] = $"{fileInfo.Length:N0} bytes";
                            info[$"{prefix}File Date"] = fileInfo.LastWriteTime.ToString();
                            
                            var versionInfo = FileVersionInfo.GetVersionInfo(module.FileName);
                            info[$"{prefix}File Version"] = versionInfo.FileVersion ?? "Unknown";
                            info[$"{prefix}Product Version"] = versionInfo.ProductVersion ?? "Unknown";
                            info[$"{prefix}Company"] = versionInfo.CompanyName ?? "Unknown";
                        }
                        catch (Exception ex)
                        {
                            info[$"{prefix}Version Error"] = ex.Message;
                        }
                    }
                }
                else
                {
                    info["FTDI Modules"] = "No FTDI modules currently loaded";
                }
            }
            catch (Exception ex)
            {
                info["Module Info Error"] = ex.Message;
            }
            
            return info;
        }

        /// <summary>
        /// Gets status code descriptions
        /// </summary>
        private static string GetStatusDescription(uint status)
        {
            return status switch
            {
                0 => "FT_OK",
                1 => "FT_INVALID_HANDLE",
                2 => "FT_DEVICE_NOT_FOUND",
                3 => "FT_DEVICE_NOT_OPENED",
                4 => "FT_IO_ERROR",
                5 => "FT_INSUFFICIENT_RESOURCES",
                6 => "FT_INVALID_PARAMETER",
                7 => "FT_INVALID_BAUD_RATE",
                8 => "FT_DEVICE_NOT_OPENED_FOR_ERASE",
                9 => "FT_DEVICE_NOT_OPENED_FOR_WRITE",
                10 => "FT_FAILED_TO_WRITE_DEVICE",
                11 => "FT_EEPROM_READ_FAILED",
                12 => "FT_EEPROM_WRITE_FAILED",
                13 => "FT_EEPROM_ERASE_FAILED",
                14 => "FT_EEPROM_NOT_PRESENT",
                15 => "FT_EEPROM_NOT_PROGRAMMED",
                16 => "FT_INVALID_ARGS",
                17 => "FT_NOT_SUPPORTED",
                18 => "FT_OTHER_ERROR",
                19 => "FT_DEVICE_LIST_NOT_READY",
                _ => $"Unknown ({status})"
            };
        }

        /// <summary>
        /// Quick verification method for use in applications
        /// </summary>
        /// <returns>True if FTDI x64 DLL is available and functional</returns>
        public static bool QuickVerify()
        {
            try
            {
                uint numDevices = 0;
                uint status = FT_GetDeviceInfoList(out numDevices, IntPtr.Zero);
                return true; // If we get here, DLL loaded successfully
            }
            catch
            {
                return false;
            }
        }
    }
}