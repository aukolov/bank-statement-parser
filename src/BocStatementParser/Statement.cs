﻿using System;
using System.Collections.Generic;

namespace BocStatementParser
{
    public class Statement
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string AccountNumber { get; set; }
        public List<Transaction> Transactions { get; set; }
    }
}