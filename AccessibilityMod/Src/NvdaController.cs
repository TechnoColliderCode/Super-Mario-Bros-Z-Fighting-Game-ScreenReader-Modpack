using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;

namespace SMBZG.Accessibility
{
    /// <summary>
    /// Bridges to the NVDA screen reader via its Controller Client native library.
    /// Loads the x64 client DLL dynamically so the mod still loads when NVDA is not
    /// installed; all speech calls become silent no-ops in that case.
    /// </summary>
    internal static class NvdaController
    {
        private const string LibName64 = "nvdaControllerClient64.dll";
        private const string LibName32 = "nvdaControllerClient.dll";
        private const string EmbeddedResourceName = "SMBZG.Accessibility.Resources.nvdaControllerClient64.dll";

        private const string ProcTestIfRunning = "nvdaController_testIfRunning";
        private const string ProcSpeakText = "nvdaController_speakText";
        private const string ProcSpeakTextUtf8 = "nvdaController_speakText_utf8";
        private const string ProcStopSpeech = "nvdaController_stopSpeech";
        private const string ProcCancelSpeech = "nvdaController_cancelSpeech";
        private const string ProcBraille = "nvdaController_brailleMessage";

        private static IntPtr _handle = IntPtr.Zero;

        private static SpeakTextDelegate _speakWide;
        private static SpeakTextUtf8Delegate _speakUtf8;
        private static TestIfRunningDelegate _testIfRunning;
        private static StopSpeechDelegate _stopSpeech;

        private static bool _initialized;
        private static bool _initAttempted;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int TestIfRunningDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SpeakTextDelegate([MarshalAs(UnmanagedType.LPWStr)] string text);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SpeakTextUtf8Delegate(IntPtr utf8Bytes);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void StopSpeechDelegate();

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        public static bool IsLoaded { get { return _handle != IntPtr.Zero && _testIfRunning != null; } }

        /// <summary>Attempts to load the NVDA client library and resolve its exports.</summary>
        public static void Init()
        {
            if (_initAttempted)
            {
                return;
            }
            _initAttempted = true;
            try
            {
                _handle = LoadEmbedded();
                if (_handle == IntPtr.Zero)
                {
                    _handle = TryLoadFromCandidatePaths();
                }
                if (_handle == IntPtr.Zero)
                {
                    _handle = LoadLibrary(LibName64);
                }
                if (_handle == IntPtr.Zero)
                {
                    _handle = LoadLibrary(LibName32);
                }
                if (_handle == IntPtr.Zero)
                {
                    MelonLogger.Msg("NVDA controller client not found. NVDA speech is disabled; mod will run silently.");
                    return;
                }
                _testIfRunning = GetProc<TestIfRunningDelegate>(ProcTestIfRunning);
                _speakWide = GetProc<SpeakTextDelegate>(ProcSpeakText);
                _speakUtf8 = GetProc<SpeakTextUtf8Delegate>(ProcSpeakTextUtf8);
                _stopSpeech = GetProc<StopSpeechDelegate>(ProcStopSpeech);
                if (_stopSpeech == null)
                {
                    _stopSpeech = GetProc<StopSpeechDelegate>(ProcCancelSpeech);
                }
                _initialized = true;

                if (_testIfRunning == null)
                {
                    MelonLogger.Warning("NVDA client DLL loaded but nvdaController_testIfRunning was not found.");
                }
                if (_speakWide == null && _speakUtf8 == null)
                {
                    MelonLogger.Warning("NVDA client DLL loaded but no nvdaController_speakText export was found.");
                }
                MelonLogger.Msg("NVDA controller client loaded successfully.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("Failed to initialize NVDA controller client: " + ex);
                _initialized = false;
            }
        }

        /// <summary>
        /// Loads the x64 controller client DLL that is embedded in this mod, so a
        /// separate nvdaControllerClient file is never needed on the target machine.
        /// The DLL is extracted to a per-user temp folder on first use (or reused
        /// from there if the game was launched before), then loaded.
        /// </summary>
        private static IntPtr LoadEmbedded()
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "SMBZG_Accessibility");
                string path = Path.Combine(dir, LibName64);

                IntPtr existing = LoadLibrary(path);
                if (existing != IntPtr.Zero)
                {
                    MelonLogger.Msg("NVDA controller client loaded from temp copy.");
                    return existing;
                }

                Directory.CreateDirectory(dir);
                using (Stream stream = typeof(NvdaController).Assembly.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (stream == null)
                    {
                        MelonLogger.Warning("Embedded NVDA controller client resource missing.");
                        return IntPtr.Zero;
                    }
                    using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        stream.CopyTo(fs);
                    }
                }

                IntPtr loaded = LoadLibrary(path);
                if (loaded != IntPtr.Zero)
                {
                    MelonLogger.Msg("NVDA controller client loaded from embedded resource.");
                }
                return loaded;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("Failed to load embedded NVDA controller client: " + ex.Message);
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Searches well-known locations for the controller client DLL: game root,
        /// Mods folder, MelonLoader folder, and the NVDA install directory.
        /// Modern NVDA (2024+) ships the client as a separate download, so it may
        /// not exist inside the NVDA install folder at all.
        /// </summary>
        private static IntPtr TryLoadFromCandidatePaths()
        {
            string gameDir = string.Empty;
            try
            {
                gameDir = MelonEnvironment.GameRootDirectory;
            }
            catch (Exception)
            {
                // ignore
            }

            string[] dirs =
            {
                gameDir,
                !string.IsNullOrEmpty(gameDir) ? Path.Combine(gameDir, "Mods") : string.Empty,
                !string.IsNullOrEmpty(gameDir) ? Path.Combine(gameDir, "MelonLoader") : string.Empty,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVDA"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVDA"),
            };

            string[] names = { LibName64, LibName32 };
            foreach (string dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    continue;
                }
                foreach (string name in names)
                {
                    string path = Path.Combine(dir, name);
                    if (File.Exists(path))
                    {
                        IntPtr h = LoadLibrary(path);
                        if (h != IntPtr.Zero)
                        {
                            return h;
                        }
                    }
                }
            }
            return IntPtr.Zero;
        }

        private static T GetProc<T>(string name) where T : class
        {
            IntPtr ptr = GetProcAddress(_handle, name);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }
            return Marshal.GetDelegateForFunctionPointer(ptr, typeof(T)) as T;
        }

        /// <summary>True when NVDA is currently running and the client can be used.</summary>
        public static bool IsNvdaRunning()
        {
            if (!_initialized || _testIfRunning == null)
            {
                return false;
            }
            try
            {
                return _testIfRunning() == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Speaks text through NVDA. Non-blocking; NVDA queues the speech.</summary>
        public static void Speak(string text)
        {
            if (!_initialized || string.IsNullOrEmpty(text))
            {
                return;
            }
            try
            {
                if (_speakWide != null)
                {
                    _speakWide(text);
                }
                else if (_speakUtf8 != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(text + "\0");
                    IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, ptr, bytes.Length);
                        _speakUtf8(ptr);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error("NVDA speakText failed: " + ex.Message);
            }
        }

        /// <summary>Stops NVDA speech (used on scene changes to clear stale queue).</summary>
        public static void StopSpeech()
        {
            if (!_initialized || _stopSpeech == null)
            {
                return;
            }
            try
            {
                _stopSpeech();
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }
}
