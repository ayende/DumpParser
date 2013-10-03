using System.Diagnostics;
using Microsoft.Diagnostics.Runtime;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using Winterdom.Diagnostics;

namespace ClrMD_Test
{
    class Program
    {
        private const int TRACES_PER_WORKSHEET = 10;
        private const decimal SIMILARITY_THRESHOLD = 90.0m;

        private class StackTraceGroupedBySimilarity
        {
            public int ThreadId { get; set; }
            
            public IList<ClrStackFrame> StackTrace { get; set; }            

            public ulong StackBase { get; set; }

            public decimal SimilarityCount { get; set; }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                var otherObject = obj as StackTraceGroupedBySimilarity;

                return otherObject != null && StackTraceUtil.CompareStackTraces(StackTrace, otherObject.StackTrace) >= 50.0m;
            }

            public override int GetHashCode()
            {
                return StackTrace.Aggregate(123, (current, stackFrame) => (current ^ stackFrame.DisplayString.GetHashCode()));
            }
        }

        private static Dictionary<ulong, ClrType> heapObjectsByPointer;
        private static ClrHeap currentHeap;
        static void Main(string[] args)
        {
            var path = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
			var dumpData = DataTarget.LoadCrashDump(@"C:\Users\Ayende\Downloads\Raven.Unresponsive-on-indexing\Raven.Server.DMP");
            dumpData.SetSymbolPath(path);
	        var dacLocator = DacLocator.FromPublicSymbolServer("cache");
	        var dacLocation = dacLocator.FindDac(dumpData.ClrVersions[0]);
            var runtime = dumpData.CreateRuntime(dacLocation);
            var heap = runtime.GetHeap();
            
            currentHeap = heap;
            heapObjectsByPointer = new Dictionary<ulong, ClrType>();
            if (!heap.CanWalkHeap)
            {
                throw new ApplicationException("Cannot walk heap --> unable to proceed!");    
            }
        
            //clrstack -p -l
            heap.EnumerateObjects().ToList()
                                   .ForEach(objPtr => heapObjectsByPointer.Add(objPtr, heap.GetObjectType(objPtr)));
            
            var threadStackTracesGroupBySimilarity = runtime.Threads.Where(t => t.IsAlive &&
                                                                                !t.IsUserSuspended &&
                                                                                !t.IsGC &&
                                                                                t.StackTrace.Count > 1)
                                                                    .Select(row => new StackTraceGroupedBySimilarity
                                                                    {                                                                        
                                                                        ThreadId = row.ManagedThreadId,
                                                                        StackBase = row.StackBase,
                                                                        StackTrace = row.StackTrace,
                                                                        SimilarityCount = runtime.Threads.Count(t => StackTraceUtil.StackTraceSimilarity(row.StackTrace, t.StackTrace) >= SIMILARITY_THRESHOLD)
                                                                    })                                                                          
                                                                    .OrderByDescending(row => row.SimilarityCount)                                                                    
                                                                    .ToList();
            
            var afterDistinct = new List<StackTraceGroupedBySimilarity>();
            foreach (var stackTraceData in threadStackTracesGroupBySimilarity)
            {
                if (
                    !afterDistinct.Any(
                        row =>
                            StackTraceUtil.StackTraceSimilarity(row.StackTrace, stackTraceData.StackTrace) >=
                            SIMILARITY_THRESHOLD))
                {
                    afterDistinct.Add(stackTraceData);
                }
            }

            DumpStackTraceOccurenceToExcel(afterDistinct.OrderByDescending(row => row.SimilarityCount).Take(5).ToList());
            //DumpStackTraceToExcelByThreads(stackTraceDataByThreads);

        }

        private static void DumpStackTraceOccurenceToExcel(IList<StackTraceGroupedBySimilarity> stackTraceGrouped)
        {
            using (var pck = new ExcelPackage())
            {
                var stackTraceWorksheetCount = stackTraceGrouped.Count / TRACES_PER_WORKSHEET;
                var tracesPerWorkSheet = TRACES_PER_WORKSHEET;
                if (stackTraceWorksheetCount == 0)
                {
                    stackTraceWorksheetCount = 1;
                    tracesPerWorkSheet = stackTraceGrouped.Count;
                }
            
                for (int stackTraceWorksheetIndex = 0;
                    stackTraceWorksheetIndex < stackTraceWorksheetCount;
                    stackTraceWorksheetIndex++)
                {
                    var ws = pck.Workbook.Worksheets.Add(String.Format("Stack Traces {0} - {1}", stackTraceWorksheetIndex * tracesPerWorkSheet, (stackTraceWorksheetIndex * tracesPerWorkSheet) + stackTraceWorksheetCount));
                    
                    ws.Row(1).Style.Font.Bold = true;
                    ws.Row(1).Style.Font.UnderLine = true;
                    var startColumn = 1;

                    for (var threadIndex = stackTraceWorksheetIndex;
                        threadIndex < (stackTraceWorksheetIndex + tracesPerWorkSheet);
                        threadIndex++)
                    {
                        ws.Cells[1, startColumn].Value = String.Format("Stack trace from thread Id #{0}", stackTraceGrouped[threadIndex].ThreadId);

                        ws.Cells[2, startColumn].Value = String.Format("Stack trace similar to this occured {0} times in the existing threads", stackTraceGrouped[threadIndex].SimilarityCount);

                        ws.Cells[3, startColumn].Value = String.Format("Similarity of stack traces is {0}%", SIMILARITY_THRESHOLD);

                        ws.Cells[5, startColumn].LoadFromDataTable(GetDataTableFromStackTrace(stackTraceGrouped[threadIndex]),false);
                        ws.Column(startColumn).AutoFit();

                        startColumn += 2;
                    }
                }
                
                pck.SaveAs(new FileInfo("Stacktrace Data (grouped by similarities).xlsx"));
            }
        }

        private static DataTable GetDataTableFromStackTrace(StackTraceGroupedBySimilarity stackTraceGrouped)
        {
            var stackTraceDt = new DataTable();

            stackTraceDt.Columns.Add("DisplayString");
            
            foreach (var stackFrame in stackTraceGrouped.StackTrace)
            {
                var dtRow = stackTraceDt.NewRow();
                dtRow["DisplayString"] = stackFrame.DisplayString;
                stackTraceDt.Rows.Add(dtRow);
            }

            return stackTraceDt;
        }

    }
}
