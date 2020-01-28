using System;
using System.Threading;

namespace TwilightBoxart.Helpers
{
    public class RetryHelper
    {
        private readonly TimeSpan _defaultDelay;
        private readonly int _defaultRetries;

        public RetryHelper(TimeSpan defaultDelay, int defaultRetries = 3)
        {
            _defaultDelay = defaultDelay;
            _defaultRetries = defaultRetries;
        }

        public void RetryOnException(Action operation)
        {
            RetryOnException(_defaultRetries, _defaultDelay, operation);
        }

        public static void RetryOnException(int times, TimeSpan delay, Action operation) 
        {
            var attempts = 0;
            while (true)
            {
                try
                {
                    attempts++;
                    operation();
                    break;
                }   
                catch (Exception e)
                {
                    if (attempts == times)
                        throw;

                    Thread.Sleep(delay);
                }
            }
        }
    }
}
