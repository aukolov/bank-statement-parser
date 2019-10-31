using System;

namespace BocStatementParser
{
    public class Line
    {
        public string Description { get; set; }
        public DateTime? Date { get; set; }
        public decimal? Amount { get; set; }
    }
}