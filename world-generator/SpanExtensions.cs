namespace System;

public static class SpanExtensions
{
    extension(ReadOnlySpan<int> span)
    {
        public int Max()
        {
            if (span.Length == 0)
            {
                throw new ArgumentException("Cannot find maximum value in an empty span.");
            }

            var result = span[0];
            for (int i = 1; i < span.Length; ++i)
            {
                if (span[i] > result)
                {
                    result = span[i];
                }
            }

            return result;
        }
    }
}
