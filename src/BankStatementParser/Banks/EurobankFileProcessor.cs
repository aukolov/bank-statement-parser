using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BankStatementParser.Extensions;
using Pdf2Text;

namespace BankStatementParser.Banks;

public class EurobankFileProcessor : IFileProcessor
{
    private readonly PdfParser _pdfParser = new PdfParser();
    private readonly Regex _accountNumberRegex = new Regex(@"^\w{2}\d{10,}$");
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

        var state = State.SearchAccountNumber;
        Transaction currentTrxn = null;
        decimal? currentBalance = null;
        var statement = new Statement
        {
            Transactions = new List<Transaction>()
        };
        ;

        const int dateLeft = 40;
        const int moneyOutRight = 394;
        const double moneyInRight = 478;

        foreach (var page in pdfModel.Pages)
        {
            var nextPage = false;
            if (page.Sentences.Count > 0 && page.Sentences[0].Text == "ACCOUNT STATEMENT")
            {
                if (currentTrxn != null && statement != null)
                {
                    statement.Transactions.Add(currentTrxn);
                }

                currentTrxn = null;
                currentBalance = null;
            }

            for (var i = 0; i < page.Sentences.Count - 1 && !nextPage; i++)
            {
                var s = page.Sentences[i];
                var next = page.Sentences[i + 1];
                switch (state)
                {
                    case State.SearchAccountNumber:
                        if (s.Text == "IBAN Number / Αριθμός IBAN")
                        {
                            if (!_accountNumberRegex.IsMatch(next.Text))
                                throw new Exception(
                                    $"Account number has unexpected format: {next.Text}.");

                            statement.AccountNumber = next.Text;
                            state = State.SearchStatementPeriodFrom;
                        }

                        break;
                    case State.SearchStatementPeriodFrom:
                        if (s.Text == "Date From / Ημερομηνία Από")
                        {
                            statement.FromDate =
                                DateTime.ParseExact(next.Text, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                            state = State.SearchStatementPeriodTo;
                        }

                        break;
                    case State.SearchStatementPeriodTo:
                        if (s.Text == "Date To / Ημερομηνία Μέχρι")
                        {
                            statement.ToDate =
                                DateTime.ParseExact(next.Text, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                            state = State.BalanceColumn;
                        }

                        break;
                    case State.BalanceColumn:
                        if (s.Text == "Balance B/F / Υπόλοιπο Μεταφοράς")
                        {
                            var amountMatch = _amountRegex.Match(next.Text);
                            if (!amountMatch.Success)
                                throw new Exception($"Amount was expected but got {s.Text}.");
                            if (currentTrxn != null)
                                throw new Exception("Current transaction must be null.");

                            currentBalance = decimal.Parse(amountMatch.Groups["amount"].Value.Replace(" ", ""),
                                CultureInfo.InvariantCulture);
                            state = State.SearchTrxn;
                        }

                        break;
                    case State.SearchTrxn:
                        if (DateTime.TryParseExact(s.Text, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out _)
                            && s.Left.IsApproximately(dateLeft))
                        {
                            if (currentTrxn != null)
                            {
                                statement.Transactions.Add(currentTrxn);
                            }

                            currentTrxn = new Transaction
                            {
                                Date = DateTime.ParseExact(s.Text, "dd/MM/yyyy", CultureInfo.InvariantCulture),
                                Description = ""
                            };
                            state = State.FirstDescription;
                        }
                        else if (s.Text == "info@eurobank.com.cy")
                        {
                            nextPage = true;
                        }
                        else if (s.Text == "Total Amounts / Συνολικά Ποσά")
                        {
                            nextPage = true;
                        }
                        else if (s.Left.IsApproximately(113) && currentTrxn != null)
                        {
                            currentTrxn.Description += " " + s.Text;
                        }

                        break;
                    case State.FirstDescription:
                        if (currentTrxn == null)
                            throw new Exception("Current transaction must not be null.");
                        if (currentTrxn.Description.Length > 0)
                            throw new Exception("Description was expected to be empty but was not.");

                        currentTrxn.Description = s.Text;
                        state = State.ValueDate;
                        break;
                    case State.ValueDate:
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
                        if (oldBalance != null && oldBalance + currentTrxn.Amount != currentBalance)
                            throw new Exception(
                                $"Balance was expected to be {oldBalance + currentTrxn.Amount} but was {currentBalance}.");

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

        return new[] { statement };
    }

    enum State
    {
        SearchAccountNumber,
        SearchStatementPeriodFrom,
        SearchStatementPeriodTo,
        BalanceColumn,
        SearchTrxn,
        FirstDescription,
        ValueDate,
        Amount,
        Balance,
    }
}