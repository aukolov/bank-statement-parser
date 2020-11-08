using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BankStatementParser.Extensions;
using Pdf2Text;

namespace BankStatementParser.Banks
{
    public class BocFileProcessor : IFileProcessor
    {
        private readonly PdfParser _pdfParser = new PdfParser();
        private readonly Regex _accountNumberRegex = new Regex(@"^\d{10,}$");
        private readonly Regex _dateRegex = new Regex(@"^\d{2}/\d{2}/\d{4}$");
        private readonly Regex _amountRegex = new Regex(@"^(\d{1,3})(,\d{3})*\.\d{2}$");

        public Statement[] Process(string path)
        {
            var files = new List<string>();
            if (Directory.Exists(path))
            {
                files.AddRange(
                    Directory.GetFiles(path, "*.pdf", SearchOption.AllDirectories));
                if (!files.Any())
                    throw new Exception("No PDF files found.");
            }
            else if (File.Exists(path))
            {
                files.Add(path);
            }

            var statements = files.Select(ProcessFile).ToArray();
            return statements;
        }

        private Statement ProcessFile(string pdfPath)
        {
            var pdfModel = _pdfParser.Parse(pdfPath);

            var state = State.SearchAccountNumber;
            Transaction currentTrxn = null;
            var currentBalance = 0m;
            var statement = new Statement
            {
                Transactions = new List<Transaction>()
            };

            foreach (var page in pdfModel.Pages)
            {
                var nextPage = false;

                for (var i = 0; i < page.Sentences.Count - 1 && !nextPage; i++)
                {
                    var s = page.Sentences[i];

                    var next = page.Sentences[i + 1];
                    switch (state)
                    {
                        case State.SearchAccountNumber:
                            if (s.Text == "Account Number"
                                && s.Left.IsApproximately(380))
                            {
                                if (!_accountNumberRegex.IsMatch(next.Text))
                                    throw new Exception($"Account number was expected to be numeric but was {next.Text}.");
                                statement.AccountNumber = next.Text;
                                state = State.SearchStatementPeriod;
                            }

                            break;
                        case State.SearchStatementPeriod:
                            if (s.Text.StartsWith("Statement Period:"))
                            {
                                var periodMatch = Regex.Match(s.Text, 
                                    @"Statement Period\: (?<from>\d{2}/\d{2}/\d{4}) - (?<to>\d{2}/\d{2}/\d{4})");
                                var fromText = periodMatch.Groups["from"].Captures[0].Value;
                                var toText = periodMatch.Groups["to"].Captures[0].Value;
                                statement.FromDate = DateTime.ParseExact(fromText, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                                statement.ToDate = DateTime.ParseExact(toText, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                                state = State.ScrollToTable;
                            }

                            break;
                        case State.ScrollToTable:
                            if (s.Text == "Balance"
                                && s.Left.IsApproximately(538))
                            {
                                state = page.PageNumber == 0 ? State.SearchBalance : State.SearchTrxn;
                            }

                            break;
                        case State.SearchBalance:
                            if (s.Text == "forward")
                            {
                                if (!decimal.TryParse(next.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out currentBalance))
                                    throw new Exception($"Account balance was expected to be numeric but was {next.Text}.");

                                state = State.SearchTrxn;
                            }

                            break;
                        case State.SearchTrxn:
                            var potentialTrxnDate = _dateRegex.Match(s.Text);
                            if (potentialTrxnDate.Success
                                && _dateRegex.IsMatch(next.Text)
                                && s.Left.IsApproximately(42))
                            {
                                if (currentTrxn != null)
                                {
                                    statement.Transactions.Add(currentTrxn);
                                }

                                currentTrxn = new Transaction
                                {
                                    Date = DateTime.ParseExact(s.Text, "dd/MM/yyyy",
                                        CultureInfo.InvariantCulture)
                                };
                                state = State.ValueDate;
                            }
                            else if (s.Text == "Continue on next Page"
                                     || s.Text == "Total / Balance Carried Forward")
                            {
                                nextPage = true;
                                state = State.ScrollToTable;
                            }
                            else if (s.Left.IsApproximately(141)
                                     && currentTrxn != null)
                            {
                                currentTrxn.Description += " " + s.Text;
                            }

                            break;
                        case State.ValueDate:
                            if (!_dateRegex.IsMatch(s.Text))
                                throw new Exception($"Date was expected but got {s.Text}.");
                            state = State.FirstDescription;
                            break;
                        case State.FirstDescription:
                            if (currentTrxn == null)
                                throw new Exception("Current transaction must not be null.");
                            if (currentTrxn.Description != null)
                                throw new Exception("Description was expected to be null but was not.");

                            currentTrxn.Description = s.Text;
                            state = State.Amount;
                            break;
                        case State.Amount:
                            var match = _amountRegex.Match(s.Text);
                            if (!match.Success)
                                throw new Exception($"Amount was expected but got {s.Text}.");
                            if (currentTrxn == null)
                                throw new Exception("Current transaction must not be null.");
                            if (currentTrxn.Amount.HasValue)
                                throw new Exception("Amount was expected to be null but was not.");

                            var amount = decimal.Parse(s.Text, CultureInfo.InvariantCulture);
                            if (s.Right.IsApproximately(411))
                                currentTrxn.Amount = -amount;
                            else if (s.Right.IsApproximately(484))
                                currentTrxn.Amount = +amount;
                            else
                                throw new Exception($"Found amount at unexpected location: {s.Right}.");
                            state = State.Balance;

                            break;
                        case State.Balance:
                            var oldBalance = currentBalance;
                            if (!decimal.TryParse(s.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out currentBalance))
                                throw new Exception($"Balance was expected to be numeric but was {s.Text}.");
                            if (currentTrxn == null)
                                throw new Exception("Current transaction was not expected to be null but was.");
                            if (oldBalance + currentTrxn.Amount != currentBalance)
                                throw new Exception($"Balance was expected to be {oldBalance + currentTrxn.Amount} but was {currentBalance}.");
                            
                            state = State.SearchTrxn;
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            if (currentTrxn != null)
            {
                statement.Transactions.Add(currentTrxn);
            }

            return statement;
        }
        
        enum State
        {
            SearchAccountNumber,
            SearchStatementPeriod,
            ScrollToTable,
            SearchBalance,
            SearchTrxn,
            ValueDate,
            FirstDescription,
            Amount,
            Balance,
        }
    }
}