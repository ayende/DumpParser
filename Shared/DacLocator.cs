using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Win32.SafeHandles;

namespace Winterdom.Diagnostics
{
	public class DacLocator : IDisposable
	{
		const int sfImage = 0;
		[DllImport("dbghelp.dll", SetLastError = true)]
		static extern bool SymInitialize(IntPtr hProcess, String symPath, bool fInvadeProcess);
		[DllImport("dbghelp.dll", SetLastError = true)]
		static extern bool SymCleanup(IntPtr hProcess);
		[DllImport("dbghelp.dll", SetLastError = true)]
		static extern bool SymFindFileInPath(IntPtr hProcess, String searchPath, String filename, uint id, uint two, uint three, uint flags, StringBuilder filePath, IntPtr callback, IntPtr context);

		[DllImport("kernel32.dll", SetLastError = true)]
		static extern LibrarySafeHandle LoadLibrary(String name);
		[DllImport("kernel32.dll", SetLastError = true)]
		static extern bool FreeLibrary(IntPtr hModule);

		private String searchPath;
		private LibrarySafeHandle dbghelpModule;
		private Process ourProcess;

		private DacLocator(String searchPath)
		{
			this.searchPath = searchPath;
			ourProcess = Process.GetCurrentProcess();
			dbghelpModule = LoadLibrary("dbghelp.dll");
			if (dbghelpModule.IsInvalid)
			{
				throw new Win32Exception(String.Format("Could not load dbghelp.dll: {0}", Marshal.GetLastWin32Error()));
			}
			if (!SymInitialize(ourProcess.Handle, searchPath, false))
			{
				throw new Win32Exception(String.Format("SymInitialize() failed: {0}", Marshal.GetLastWin32Error()));
			}
		}

		public static DacLocator FromPublicSymbolServer(String localCache)
		{
			return new DacLocator(String.Format("SRV*{0}*http://msdl.microsoft.com/download/symbols", localCache));
		}
		public static DacLocator FromEnvironment()
		{
			String ntSymbolPath = Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH");
			return new DacLocator(ntSymbolPath);
		}
		public static DacLocator FromSearchPath(String searchPath)
		{
			return new DacLocator(searchPath);
		}

		public String FindDac(ClrInfo clrInfo)
		{
			String dac = clrInfo.TryGetDacLocation();
			if (String.IsNullOrEmpty(dac))
			{
				dac = FindDac(clrInfo.DacInfo.FileName, clrInfo.DacInfo.TimeStamp, clrInfo.DacInfo.FileSize);
			}
			return dac;
		}
		public String FindDac(String dacname, uint timestamp, uint fileSize)
		{
			// attemp using the symbol server
			StringBuilder symbolFile = new StringBuilder(2048);
			if (SymFindFileInPath(ourProcess.Handle, searchPath, dacname,
				timestamp, fileSize, 0, 0x02, symbolFile, IntPtr.Zero, IntPtr.Zero))
			{
				return symbolFile.ToString();
			}
			else
			{
				throw new Win32Exception(String.Format("SymFindFileInPath() failed: {0}", Marshal.GetLastWin32Error()));
			}
		}

		public void Dispose()
		{
			if (ourProcess != null)
			{
				SymCleanup(ourProcess.Handle);
				ourProcess = null;
			}
			if (dbghelpModule != null && !dbghelpModule.IsClosed)
			{
				dbghelpModule.Dispose();
				dbghelpModule = null;
			}
		}

		class LibrarySafeHandle : SafeHandleZeroOrMinusOneIsInvalid
		{
			public LibrarySafeHandle()
				: base(true)
			{
			}
			public LibrarySafeHandle(IntPtr handle)
				: base(true)
			{
				this.SetHandle(handle);
			}
			protected override bool ReleaseHandle()
			{
				return FreeLibrary(this.handle);
			}
		}
	}
}
