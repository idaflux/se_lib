using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace se_boot {
    class Inject {
        #region win32
        [Flags]
        private enum AllocationType {
            Commit = 0x1000,
            Reserve = 0x2000,
            Decommit = 0x4000,
            Release = 0x8000,
            Reset = 0x80000,
            Physical = 0x400000,
            TopDown = 0x100000,
            WriteWatch = 0x200000,
            LargePages = 0x20000000
        }

        [Flags]
        private enum MemoryProtection {
            Execute = 0x10,
            ExecuteRead = 0x20,
            ExecuteReadWrite = 0x40,
            ExecuteWriteCopy = 0x80,
            NoAccess = 0x01,
            ReadOnly = 0x02,
            ReadWrite = 0x04,
            WriteCopy = 0x08,
            GuardModifierflag = 0x100,
            NoCacheModifierflag = 0x200,
            WriteCombineModifierflag = 0x400
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetModuleHandleW(string filename);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, AllocationType flAllocationType, MemoryProtection flProtect);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, int dwFreeType);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, IntPtr lpNumberOfBytesWritten);
        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out int lpThreadId);
        [DllImport("kernel32.dll", SetLastError = true)]
        [System.Security.SuppressUnmanagedCodeSecurity]
        private static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", SetLastError = true)]
        private static extern IntPtr LoadLibraryW([MarshalAs(UnmanagedType.LPWStr)] string lpszLib);
        #endregion

        internal int Pid { get; }

        private IntPtr ptrLoadLibraryW { get; }
        private IntPtr ptrTrampolineEntry { get; set; }
        private IntPtr ptrRelTrampolineEntry { get; }
        private string trampolineDllPath { get; }

        private IntPtr hProcess;
        private bool trampolineInjected;

        public Inject(int pid, string trampolineDllPath = @"cboot.dll") {
            this.Pid = pid;
            this.trampolineDllPath = System.IO.Path.GetFullPath(trampolineDllPath);
            this.trampolineInjected = false;

            var hk32 = GetModuleHandleW("kernel32");
            var lladr = GetProcAddress(hk32, "LoadLibraryW");
            if (lladr == IntPtr.Zero) {
                throw new NotSupportedException("kernel32 without LoadLibraryW");
            }
            this.ptrLoadLibraryW = lladr;

            if (!System.IO.File.Exists(this.trampolineDllPath)) {
                this.trampolineDllPath = System.IO.Path.Combine(Environment.CurrentDirectory, this.trampolineDllPath);
                if (!System.IO.File.Exists(this.trampolineDllPath)) {
                    throw new ArgumentException($"cannot find trampoline lib at {this.trampolineDllPath}");
                }
            }

            var lladrLocal = LoadLibraryW(this.trampolineDllPath);
            if (lladrLocal == IntPtr.Zero) {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            var entryProcAdr = GetProcAddress(lladrLocal, "RunManagedDll");
            this.ptrRelTrampolineEntry = new IntPtr(entryProcAdr.ToInt64() - lladrLocal.ToInt64());
        }


        public void InjectManagedDll(string dllPath, string dllClass, string dllEntryFunction, string cliVersion = "v4.0.30319") {
            this.OpenProcess();

            try {
                if (dllPath is null) throw new ArgumentNullException(nameof(dllPath));
                if (dllClass is null) throw new ArgumentNullException(nameof(dllClass));
                if (dllEntryFunction is null) throw new ArgumentNullException(nameof(dllEntryFunction));
                if (cliVersion is null) throw new ArgumentNullException(nameof(cliVersion));

                if (!System.IO.Directory.Exists(System.IO.Path.Combine(@"C:\Windows\Microsoft.NET\Framework64\", cliVersion))) {
                    throw new ArgumentException("invalid cli version");
                }

                if (!this.trampolineInjected) this.InjectTrampoline();
                this.InjectAndInvokeManagedDll(dllPath, dllClass, dllEntryFunction, cliVersion);
            } finally {
                CloseHandle(this.hProcess);
                this.hProcess = IntPtr.Zero;
            }
        }

        private void InjectAndInvokeManagedDll(string dllPath, string dllClass, string dllEntryFunction, string cliVersion) {
            string[] strs = new string[] {
                cliVersion, dllPath, dllClass, dllEntryFunction
            };
            Tuple<IntPtr, int>[] ptrs = new Tuple<IntPtr, int>[strs.Length + 1];

            IntPtr hThread = IntPtr.Zero;

            try {
                for (int i = 0; i < strs.Length; i++) {
                    ptrs[i] = this.WriteString(strs[i]);
                }

                IntPtr lpiPtr = VirtualAllocEx(this.hProcess, IntPtr.Zero, IntPtr.Size * 4, AllocationType.Commit | AllocationType.Reserve, MemoryProtection.ReadWrite);
                if (lpiPtr == IntPtr.Zero) {
                    throw new OutOfMemoryException("memory allocation in remote process failed");
                }
                ptrs[ptrs.Length - 1] = new Tuple<IntPtr, int>(lpiPtr, IntPtr.Size * 4);

                for (int i = 0; i < strs.Length; i++) {
                    WriteProcessMemory(this.hProcess, new IntPtr(lpiPtr.ToInt64() + IntPtr.Size * i), BitConverter.GetBytes(ptrs[i].Item1.ToInt64()), IntPtr.Size, IntPtr.Zero);
                }

                hThread = CreateRemoteThread(
                    hProcess: this.hProcess,
                    lpThreadAttributes: IntPtr.Zero,
                    dwStackSize: 0,
                    lpStartAddress: this.ptrTrampolineEntry,
                    lpParameter: lpiPtr,
                    dwCreationFlags: 0,
                    out _
                    );

                WaitForSingleObject(hThread, dwMilliseconds: unchecked((uint)-1));
            } finally {
                if (hThread != IntPtr.Zero) CloseHandle(hThread);
                foreach (var ptr in ptrs) {
                    if (ptr is null) continue;
                    VirtualFreeEx(this.hProcess, ptr.Item1, ptr.Item2, 0x8000);
                }
            }

        }

        private void OpenProcess() {
            var hProcess = OpenProcess(0xFFFF, false, this.Pid);
            if (hProcess == IntPtr.Zero) {
                throw new UnauthorizedAccessException("failed to open process");
            }

            this.hProcess = hProcess;
        }

        private Tuple<IntPtr, int> WriteString(string value) {
            var strBytes = System.Text.Encoding.Unicode.GetBytes(value);

            var remoteDataPtr = VirtualAllocEx(
                hProcess: this.hProcess,
                lpAddress: IntPtr.Zero,
                dwSize: strBytes.Length,
                flAllocationType: AllocationType.Commit | AllocationType.Reserve,
                flProtect: MemoryProtection.ReadWrite
            );

            if (remoteDataPtr == IntPtr.Zero) {
                throw new OutOfMemoryException("memory allocation in remote process failed");
            }

            int nfoo;
            nfoo = WriteProcessMemory(
                hProcess: this.hProcess,
                lpBaseAddress: remoteDataPtr,
                lpBuffer: strBytes,
                nSize: strBytes.Length,
                lpNumberOfBytesWritten: IntPtr.Zero
            );

            if (nfoo == 0) {
                throw new Exception("failed to write into remote process");
            }

            return new Tuple<IntPtr, int>(remoteDataPtr, strBytes.Length);
        }

        private void InjectTrampoline() {
            Tuple<IntPtr, int> ptrTrampolinePath = this.WriteString(this.trampolineDllPath);

            int nfoo;
            var hRemoteThread = CreateRemoteThread(
                hProcess: this.hProcess,
                lpThreadAttributes: IntPtr.Zero,
                dwStackSize: 0,
                lpStartAddress: this.ptrLoadLibraryW,
                lpParameter: ptrTrampolinePath.Item1,
                dwCreationFlags: 0,
                lpThreadId: out nfoo
            );

            if (hRemoteThread == IntPtr.Zero) {
                throw new Exception("failed to start remote thread");
            }

            nfoo = WaitForSingleObject(hRemoteThread, dwMilliseconds: 10000);
            if (nfoo != 0) {
                throw new TimeoutException("remote loadlibrary timeout");
            }

            CloseHandle(hRemoteThread);
            VirtualFreeEx(this.hProcess, ptrTrampolinePath.Item1, ptrTrampolinePath.Item2, 0x8000);

            //calculate trampoline entry
            var p = System.Diagnostics.Process.GetProcessById(this.Pid);
            IntPtr baseAdr = IntPtr.Zero;

            foreach (System.Diagnostics.ProcessModule module in p.Modules) {
                if (module.FileName == this.trampolineDllPath) {
                    //module found
                    baseAdr = module.BaseAddress;
                    break;
                }
            }
            if (baseAdr == IntPtr.Zero) {
                throw new DllNotFoundException("can't find injected module");
            }

            this.ptrTrampolineEntry = new IntPtr(baseAdr.ToInt64() + this.ptrRelTrampolineEntry.ToInt64());

            this.trampolineInjected = true;
        }
    }
}
