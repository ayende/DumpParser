using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Runtime;
using OfficeOpenXml;
using Shared;

namespace ActivityDumper
{
	class Program
	{
		private const double Similarity = 0.9;
		static void Main(string[] args)
		{
			using (var reader = new DumpReader(@"C:\Users\Ayende\Downloads\Raven.Unresponsive-on-indexing\Raven.Server.DMP"))
			{
				var groups = GetThreadGroups(reader);

				WriteToExcel(groups);
			}
		}

		private static void WriteToExcel(List<ThreadGroup> groups)
		{
			groups.Sort((a, b) => b.Threads.Count - a.Threads.Count);
			using (var pck = new ExcelPackage())
			{
				for (int i = 0; i < groups.Count; i++)
				{
					var ws = pck.Workbook.Worksheets.Add("Group #" + i);
					ws.Row(1).Style.Font.Bold = true;
					ws.Row(1).Style.Font.UnderLine = true;

					var current = groups[i];
					ws.Cells[1, 1].Value = "Number of threads: " + current.Threads.Count;
					ws.Cells[2, 1].Value = "Shared stacks : " + current.SharedStackTrace.Count;

					ws.Cells[4, 1].Value = "Shared stack traces:";
					int currentRow = 6;

					foreach (var stackFrame in current.SharedStackTrace)
					{
						ws.Cells[currentRow++, 2].Value = stackFrame.DisplayString;
					}

					currentRow += 2;


					ws.Cells[currentRow++, 1].Value = "Distinct stack traces:";

					foreach (var thread in current.Threads)
					{
						ws.Cells[currentRow++, 1].Value = "Thread # " + thread.ManagedThreadId;
						ws.Cells[currentRow++, 1].Style.Font.Bold = true;

						for (int j = current.SharedStackTrace.Count; j < thread.StackTrace.Count; j++)
						{
							ws.Cells[currentRow++, 2].Value = thread.StackTrace[i].DisplayString;
						}
						currentRow++;
					}


					ws.Column(1).AutoFit();
					ws.Column(2).AutoFit();
				}

				pck.SaveAs(new FileInfo("Threads.xlsx"));
			}
		}

		private static List<ThreadGroup> GetThreadGroups(DumpReader reader)
		{
			var relevantThreads = reader.Runtime.Threads
			                            .Where(t => t.IsAlive && !t.IsUserSuspended && !t.IsGC && t.StackTrace.Count > 1)
			                            .OrderByDescending(x => x.StackTrace.Count)
			                            .ToList();

			var groups = new List<ThreadGroup>();

			while (relevantThreads.Count > 0)
			{
				var thread = relevantThreads.First();
				relevantThreads.RemoveAt(0);

				var count = (int)(thread.StackTrace.Count * Similarity);

				var group = new ThreadGroup
					{
						Threads = {thread},
						SharedStackTrace = thread.StackTrace.Take(count).ToList()
					};
				groups.Add(@group);

				foreach (var clrThread in relevantThreads.ToList())
				{
					if (clrThread.StackTrace.Count < count)
						continue;
					bool match = true;
					for (int i = 0; i < count; i++)
					{
						if (clrThread.StackTrace[i].DisplayString != thread.StackTrace[i].DisplayString)
						{
							match = false;
							break;
						}
					}
					if (match == false)
						continue;
					@group.Threads.Add(clrThread);
					relevantThreads.Remove(clrThread);
				}
			}
			return groups;
		}
	}

	public class ThreadGroup
	{
		public List<ClrThread> Threads { get; set; }
		public List<ClrStackFrame> SharedStackTrace { get; set; }

		public ThreadGroup()
		{
			Threads = new List<ClrThread>();
			SharedStackTrace = new List<ClrStackFrame>();
		}
	}
}
