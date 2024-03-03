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
    public class RevolutFileProcessor : IFileProcessor
    {
        private readonly PdfParser _pdfParser = new PdfParser();
        private readonly Regex _accountNumberRegex = new Regex(@"^\w{2}\d{10,}$");
        private readonly Regex _trxnTypeRegex = new Regex(@"^\w{3}$");
        private readonly Regex _amountRegex = new Regex(@"^[$€₺£]?[A-Z]{0,3}(?<amount>(\d{1,3})([, ]\d{3})*\.\d{2}$)");

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

            var statements = files.SelectMany(ProcessFile).ToArray();
            return statements;
        }

        private Statement[] ProcessFile(string pdfPath)
        {
            var pdfModel = _pdfParser.Parse(pdfPath);

            var state = State.SearchAccountName;
            Transaction currentTrxn = null;
            decimal? currentBalance = null;
            var statements = new List<Statement>();
            Statement statement = null;

            var dateLeft = 0d;
            var moneyOutRight = 0d;
            var moneyInRight = 0d;

            foreach (var page in pdfModel.Pages)
            {
                var nextPage = false;
                if (page.Sentences.Count > 0 && page.Sentences[0].Text is "Monthly statement" or "Statement")
                {
                    if (currentTrxn != null && statement != null)
                    {
                        statement.Transactions.Add(currentTrxn);
                    }

                    statement = new Statement
                    {
                        Transactions = new List<Transaction>()
                    };
                    currentTrxn = null;
                    currentBalance = null;
                    moneyOutRight = 0;
                    moneyInRight = 0;
                    state = State.SearchAccountName;
                }

                for (var i = 0; i < page.Sentences.Count - 1 && !nextPage; i++)
                {
                    var s = page.Sentences[i];
                    Console.WriteLine($@"{s.Text} - {Math.Round(s.Top)}");
                    var next = page.Sentences[i + 1];
                    switch (state)
                    {
                        case State.SearchAccountName:
                            if (s.Text == "Account name")
                            {
                                statement.AccountNumber = next.Text.Replace(" ", "_");
                                statements.Add(statement);
                                state = State.SearchAccountNumber;
                            }
                            else if (s.Text.StartsWith("Transactions from"))
                            {
                                nextPage = true;
                            }

                            break;
                        case State.SearchAccountNumber:
                            if (s.Text == "IBAN (SEPA)" || s.Text == "IBAN")
                            {
                                if (!_accountNumberRegex.IsMatch(next.Text))
                                    throw new Exception(
                                        $"Account number has unexpected format: {next.Text}.");

                                statement.AccountNumber += "-" + next.Text;
                                state = State.SearchStatementPeriod;
                            }

                            break;
                        case State.SearchStatementPeriod:
                            if (s.Text.StartsWith("Transactions"))
                            {
                                state = State.SearchDateColumn;
                            }

                            break;
                        case State.SearchDateColumn:
                            if (s.Text.StartsWith("Date") && s.Left < 60)
                            {
                                state = State.MoneyOutColumn;
                                dateLeft = s.Left;
                            }

                            break;
                        case State.MoneyOutColumn:
                            if (s.Text == "Money out")
                            {
                                state = State.MoneyInColumn;
                                moneyOutRight = s.Right;
                            }

                            break;
                        case State.MoneyInColumn:
                            if (s.Text != "Money in")
                            {
                                throw new Exception($"Money In column expected, but got '{s.Text}'.");
                            }

                            moneyInRight = s.Right;
                            state = State.BalanceColumn;
                            break;
                        case State.BalanceColumn:
                            if (s.Text != "Balance"
                                && s.Left> 500)
                            {
                                throw new Exception($"Balance column expected, but got '{s.Text}'.");
                            }

                            state = State.SearchTrxn;
                            break;
                        case State.SearchTrxn:
                            if (DateTime.TryParse(s.Text, CultureInfo.InvariantCulture, out _)
                                && s.Left.IsApproximately(dateLeft))
                            {
                                if (currentTrxn != null)
                                {
                                    statement.Transactions.Add(currentTrxn);
                                }

                                currentTrxn = new Transaction
                                {
                                    Date = DateTime.Parse(s.Text, CultureInfo.InvariantCulture),
                                    Description = ""
                                };
                                state = State.TrxnType;
                            }
                            else if (s.Text == "© 2021 Revolut Payments UAB")
                            {
                                nextPage = true;
                            }
                            else if (s.Left.IsApproximately(116)
                                     && currentTrxn != null)
                            {
                                currentTrxn.Description += " " + s.Text;
                            }

                            break;
                        case State.TrxnType:
                            if (!_trxnTypeRegex.IsMatch(s.Text))
                                throw new Exception($"Transaction type was expected, but got '{s.Text}'.");

                            state = next.Right.IsApproximately(moneyOutRight) ||
                                    next.Right.IsApproximately(moneyInRight)
                                ? State.Amount
                                : State.FirstDescription;
                            break;
                        case State.FirstDescription:
                            if (currentTrxn == null)
                                throw new Exception("Current transaction must not be null.");
                            if (currentTrxn.Description.Length > 0)
                                throw new Exception("Description was expected to be empty but was not.");

                            currentTrxn.Description = s.Text;
                            state = State.Amount;
                            break;
                        case State.Amount:
                            if (s.Text.Length == 0) continue;
                            var match = _amountRegex.Match(s.Text);
                            if (!match.Success)
                                throw new Exception($"Amount was expected but got {s.Text}.");
                            if (currentTrxn == null)
                                throw new Exception("Current transaction must not be null.");
                            if (currentTrxn.Amount.HasValue)
                                throw new Exception("Amount was expected to be null but was not.");

                            var amount = decimal.Parse(match.Groups["amount"].Value.Replace(" ", ""),
                                CultureInfo.InvariantCulture);
                            if (s.Right.IsApproximately(moneyOutRight))
                                currentTrxn.Amount = -amount;
                            else if (s.Right.IsApproximately(moneyInRight))
                                currentTrxn.Amount = +amount;
                            else
                                throw new Exception($"Found amount at unexpected location: {s.Right}.");
                            state = State.Balance;

                            break;
                        case State.Balance:
                            var oldBalance = currentBalance;
                            var matchBalance = _amountRegex.Match(s.Text);
                            if (!matchBalance.Success)
                                throw new Exception($"Balance was expected to be numeric but was {s.Text}.");
                            currentBalance = decimal.Parse(matchBalance.Groups["amount"].Value.Replace(" ", ""),
                                CultureInfo.InvariantCulture);
                            if (currentTrxn == null)
                                throw new Exception("Current transaction was not expected to be null but was.");
                            if (oldBalance != null && oldBalance != currentBalance)
                                throw new Exception(
                                    $"Balance was expected to be {oldBalance + currentTrxn.Amount} but was {currentBalance}.");

                            currentBalance -= currentTrxn.Amount;
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

            return statements.ToArray();
        }

        enum State
        {
            SearchAccountName,
            SearchAccountNumber,
            SearchStatementPeriod,
            SearchDateColumn,
            MoneyOutColumn,
            MoneyInColumn,
            BalanceColumn,
            SearchTrxn,
            TrxnType,
            FirstDescription,
            Amount,
            Balance,
        }
    }
}