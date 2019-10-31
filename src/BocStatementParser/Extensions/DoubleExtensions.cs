namespace BocStatementParser.Extensions
{
    public static class DoubleExtensions
    {
        public static bool IsApproximately(
            this double originalValue,
            double value,
            double delta = 5)
        {
            return originalValue > value - delta
                   && originalValue < value + delta;
        }
    }
}