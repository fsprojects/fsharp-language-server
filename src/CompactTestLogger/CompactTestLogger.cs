namespace FSharpLanguageServer
{
    using System;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

    [FriendlyName("compact")]
    [ExtensionUri("logger://CompactTestLogger/v1")]
    public class NUnitXmlTestLogger : ITestLogger
    {
        public void Initialize(TestLoggerEvents events, string testResultsDirPath)
        {
            events.TestRunMessage += TestMessageHandler;
            events.TestResult += TestResultHandler;
            events.TestRunComplete += TestRunCompleteHandler;
        }

        /// <summary>
        /// Called when a test message is received.
        /// </summary>
        internal void TestMessageHandler(object sender, TestRunMessageEventArgs e)
        {
        }

        /// <summary>
        /// Called when a test result is received.
        /// </summary>
        internal void TestResultHandler(object sender, TestResultEventArgs e)
        {
            var test = e.Result.TestCase;
            Console.WriteLine("Failed {name} at {source}:{line}", test.FullyQualifiedName, test.Source, test.LineNumber);
            foreach (var m in e.Result.Messages) {
                Console.WriteLine("  ${m}", m);
            }
        }

        /// <summary>
        /// Called when a test run is completed.
        /// </summary>
        internal void TestRunCompleteHandler(object sender, TestRunCompleteEventArgs e)
        {
            
        }
    }
}