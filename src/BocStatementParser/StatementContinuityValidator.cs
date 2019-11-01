using System.Collections.Generic;

namespace BocStatementParser
{
    internal static class StatementContinuityValidator
    {
        public static IEnumerable<string> GetErrors(Statement[] statements)
        {
            for (var i = 0; i < statements.Length - 1; i++)
            {
                var s1 = statements[i];
                var s2 = statements[i + 1];
                if (s1.FromDate == s2.FromDate && s1.ToDate == s2.ToDate)
                {
                    yield return $"Account {s1.AccountNumber}: " +
                                 $"duplicating statements: [{s1.FromDate:d} - {s1.ToDate:d}]";
                }
                else if (s1.ToDate.AddDays(1) < s2.FromDate)
                {
                    yield return $"Account {s1.AccountNumber}: " +
                                 $"gap between statements:[{s1.ToDate:d} - {s2.FromDate:d}]";
                }
                else if (s1.ToDate >= s2.FromDate)
                {
                    yield return $"Account {s1.AccountNumber}: " +
                                 $"overlapping statements: [{s1.FromDate:d} - {s1.ToDate:d}] " +
                                 $"and [{s2.FromDate:d} - {s2.ToDate:d}]";
                }
            }
        }
    }
}