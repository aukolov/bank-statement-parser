using System.Collections.Generic;

namespace BocStatementParser
{
    public class Statement
    {
        public string AccountNumber { get; set; }
        public List<Transaction> Transactions { get; set; }
    }
}