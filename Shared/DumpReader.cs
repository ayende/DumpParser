using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Diagnostics.Runtime;
using Winterdom.Diagnostics;

namespace Shared
{
    public class DumpReader : IDisposable
    {
	    private readonly DataTarget dumpData;
	    private readonly DacLocator dacLocator;

	    public DumpReader(string file)
	    {
			var path = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "SymbolCache");
		    if (Directory.Exists(path) == false)
			    Directory.CreateDirectory(path);
			dumpData = DataTarget.LoadCrashDump(file);
			dumpData.SetSymbolPath(path);
		    dacLocator = DacLocator.FromPublicSymbolServer(path);
			var dacLocation = dacLocator.FindDac(dumpData.ClrVersions[0]);
			Runtime = dumpData.CreateRuntime(dacLocation);
	    }

		public ClrRuntime Runtime { get; private set; }

	    public void Dispose()
	    {
		    dumpData.Dispose();
			dacLocator.Dispose();
	    }
    }
}
