using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BankStatementParser.Extensions;
using Pdf2Text;

namespace BankStatementParser.Banks;

public partial class Eurobank3FileProcessor : IFileProcessor
{
    private readonly PdfParser _pdfParser = new PdfParser();
    private readonly Regex _accountNumberRegex = new Regex(@"^\d{10,}$");
    private readonly Regex _amountRegex = new Regex(@"^(?<amount>(-?\d{1,3})([.]\d{3})*,\d{0,2}$)");
    private readonly Regex _fromToRegex = MyRegex();

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
        decimal? prevAmount = null;
        decimal? currentAmount = null;
        var statement = new Statement
        {
            Transactions = new List<Transaction>()
        };

        const int dateLeft = 38;

        foreach (var page in pdfModel.Pages)
        {
            var nextPage = false;
            if (page.Sentences.Count > 0 && page.Sentences[0].Text == "Account Statement")
            {
                if (currentTrxn != null)
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
                        if (s.Text == "Account:")
                        {
                            if (!_accountNumberRegex.IsMatch(next.Text))
                                throw new Exception(
                                    $"Account number has unexpected format: {next.Text}.");

                            statement.AccountNumber = next.Text;
                            state = State.SearchStatementPeriodFromTo;
                        }

                        break;
                    case State.SearchStatementPeriodFromTo:
                        if (s.Text.StartsWith("Statement from "))
                        {
                            var fromToMatch = _fromToRegex.Match(s.Text);
                            if (!fromToMatch.Success) throw new Exception("Unexpected from/to format.");
                            statement.FromDate = DateTime.ParseExact(fromToMatch.Groups["from"].Value, "dd/MM/yyyy",
                                CultureInfo.InvariantCulture);
                            statement.ToDate = DateTime.ParseExact(fromToMatch.Groups["to"].Value, "dd/MM/yyyy",
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
                        else if (s.Left.IsApproximately(112) && currentTrxn != null)
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

                        var amount = decimal.Parse(match.Groups["amount"].Value
                                .Replace(".", "")
                                .Replace(",", "."),
                            CultureInfo.InvariantCulture);
                        currentTrxn.Amount = amount;
                        prevAmount = currentAmount;
                        currentAmount = amount;
                        state = State.Balance;

                        break;
                    case State.Balance:
                        var oldBalance = currentBalance;
                        var matchBalance = _amountRegex.Match(s.Text);
                        if (!matchBalance.Success)
                            throw new Exception($"Balance was expected to be numeric but was {s.Text}.");
                        currentBalance = decimal.Parse(
                            matchBalance.Groups["amount"].Value
                                .Replace(".", "")
                                .Replace(",", "."),
                            CultureInfo.InvariantCulture);
                        if (currentTrxn == null)
                            throw new Exception("Current transaction was not expected to be null but was.");
                        if (oldBalance != null && prevAmount != null && oldBalance - prevAmount != currentBalance)
                            throw new Exception(
                                $"Balance was expected to be {oldBalance - prevAmount} but was {currentBalance}.");

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
        SearchStatementPeriodFromTo,
        SearchAccountNumber,
        SearchTrxn,
        FirstDescription,
        ValueDate,
        Amount,
        Balance,
    }

    [GeneratedRegex(@"^Statement from (?<from>\d{2}/\d{2}/\d{4}) to (?<to>\d{2}/\d{2}/\d{4})$")]
    private static partial Regex MyRegex();
}