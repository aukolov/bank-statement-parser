using System;

namespace BankStatementParser
{
    public class Transaction
    {
        public string Description { get; set; }
        public DateTime? Date { get; set; }
        public decimal? Amount { get; set; }
    }
}