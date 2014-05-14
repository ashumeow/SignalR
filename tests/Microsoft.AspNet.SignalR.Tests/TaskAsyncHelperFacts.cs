using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.AspNet.SignalR.Tests
{
    public class TaskAsyncHelperFacts
    {
        [Fact]
        public void TaskAsyncHelpersPreserveCulture()
        {
            TaskCompletionSource<CultureInfo> tcs = null;
            TaskCompletionSource<CultureInfo> uiTcs = null;
            var defaultCulture = Thread.CurrentThread.CurrentCulture;
            var defaultUiCulture = Thread.CurrentThread.CurrentUICulture;
            var testCulture = new CultureInfo("zh-Hans");
            var testUICulture = new CultureInfo("zh-CN");

            Action saveThreadCulture = () =>
            {
                tcs.SetResult(Thread.CurrentThread.CurrentCulture);
                uiTcs.SetResult(Thread.CurrentThread.CurrentUICulture);
            };

            Action<IEnumerable<Func<Task>>, Action<Task>> ensureCulturePreserved = (taskGenerators, testAction) =>
            {
                foreach (var taskGenerator in taskGenerators)
                {
                    tcs = new TaskCompletionSource<CultureInfo>();
                    uiTcs = new TaskCompletionSource<CultureInfo>();
                    testAction(taskGenerator());
                    Assert.Equal(testCulture, tcs.Task.Result);
                    Assert.Equal(testUICulture, uiTcs.Task.Result);
                }
            };

            try
            {
                Thread.CurrentThread.CurrentCulture = testCulture;
                Thread.CurrentThread.CurrentUICulture = testUICulture;

                var successfulTaskGenerators = new Func<Task>[]
                {
                    () => TaskAsyncHelper.FromResult<object>(null), // Completed
                    async () => await Task.Yield(), // Async Completed
                };

                // Then with sync/async completed tasks
                ensureCulturePreserved(successfulTaskGenerators, task => task.Then(saveThreadCulture));

                var faultedTcs = new TaskCompletionSource<object>();
                var canceledTcs = new TaskCompletionSource<object>();
                faultedTcs.SetException(new Exception());
                canceledTcs.SetCanceled();
                var allTaskGenerators = successfulTaskGenerators.Concat(new Func<Task>[]
                {
                    () => faultedTcs.Task, // Faulted
                    () => canceledTcs.Task, // Canceled
                    async () => 
                    {
                        await Task.Yield();
                        throw new Exception();
                    },  // Async Faulted
                    async () =>
                    {
                        await Task.Yield();
                        throw new OperationCanceledException();
                    } // Async Canceled
                });

                // ContinueWithPreservedCulture with sync/async faulted, canceled and completed tasks
                ensureCulturePreserved(allTaskGenerators, task => task.ContinueWithPreservedCulture(_ => saveThreadCulture()));

                // PreserveCultureAwaiter with sync/async faulted, canceled and completed tasks
                ensureCulturePreserved(allTaskGenerators, async task =>
                {
                    try
                    {
                        await task.PreserveCulture();
                    }
                    catch
                    {
                        // The MSBuild xUnit.net runner crashes if we don't catch here
                    }
                    finally
                    {
                        saveThreadCulture();
                    }
                });

                // Verify that threads in the ThreadPool keep the default culture
                tcs = new TaskCompletionSource<CultureInfo>();
                uiTcs = new TaskCompletionSource<CultureInfo>();
                TaskAsyncHelper.Delay(TimeSpan.FromMilliseconds(100)).ContinueWith(_ => saveThreadCulture());
                Assert.Equal(defaultCulture, tcs.Task.Result);
                Assert.Equal(defaultUiCulture, uiTcs.Task.Result);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = defaultCulture;
                Thread.CurrentThread.CurrentUICulture = defaultUiCulture;
            }
        }
    }
}
