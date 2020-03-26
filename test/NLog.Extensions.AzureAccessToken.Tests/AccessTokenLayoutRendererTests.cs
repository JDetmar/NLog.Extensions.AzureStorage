using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NLog.Extensions.AzureAccessToken.Tests
{
    public class AccessTokenLayoutRendererTests
    {
        [Fact]
        public void SingleLogEventTest()
        {
            NLog.LogManager.ThrowConfigExceptions = true;
            NLog.LayoutRenderers.LayoutRenderer.Register("AzureAccessToken", typeof(AccessTokenLayoutRenderer));
            var logFactory = new LogFactory();
            var logConfig = new Config.LoggingConfiguration(logFactory);
            var memTarget = new NLog.Targets.MemoryTarget("Test") { Layout = "${AzureAccessToken:ResourceName=https\\://database.windows.net/}" };
            logConfig.AddRuleForAllLevels(memTarget);
            logFactory.Configuration = logConfig;
            logFactory.GetLogger("test").Info("Hello World");
            Assert.Single(memTarget.Logs);   // One queue
            logFactory.Configuration = null;
            Assert.True(WaitForTokenTimersStoppedAndGarbageCollected());
        }

        [Fact]
        public void SingleRenderRefreshTokenProviderTest()
        {
            var layout = CreateAccessTokenLayoutRenderer("TestResource", null, TimeSpan.FromMilliseconds(20));
            var uniqueTokens = new HashSet<string>();
            for (int i = 0; i < 5000; ++i)
            {
                uniqueTokens.Add(layout.Render(LogEventInfo.CreateNullEvent()));
                if (uniqueTokens.Count > 1)
                    break;
                System.Threading.Thread.Sleep(1);
            }
            Assert.NotEmpty(uniqueTokens);
            Assert.NotEqual(1, uniqueTokens.Count);
            layout.ResetTokenRefresher();
            Assert.True(WaitForTokenTimersStoppedAndGarbageCollected());
        }

        [Fact]
        public void TwoEqualRendersReusesSameTokenProvider()
        {
            AccessTokenLayoutRenderer.AccessTokenProviders.Clear();
            var layout1 = CreateAccessTokenLayoutRenderer();
            var layout2 = CreateAccessTokenLayoutRenderer();
            Assert.False(AccessTokenTimersStoppedAndGarbageCollected());
            Assert.Single(AccessTokenLayoutRenderer.AccessTokenProviders);

            var result1 = layout1.Render(LogEventInfo.CreateNullEvent());
            var result2 = layout2.Render(LogEventInfo.CreateNullEvent());
            Assert.NotEmpty(result1);
            Assert.Equal(result1, result2);

            layout1.ResetTokenRefresher();
            Assert.False(AccessTokenTimersStoppedAndGarbageCollected());
            layout2.ResetTokenRefresher();
            Assert.True(WaitForTokenTimersStoppedAndGarbageCollected());
        }

        [Fact]
        public void TwoDifferentRendersNotReusingSameTokenProvider()
        {
            AccessTokenLayoutRenderer.AccessTokenProviders.Clear();
            var layout1 = CreateAccessTokenLayoutRenderer("TestResource1");
            var layout2 = CreateAccessTokenLayoutRenderer("TestResource2");
            Assert.False(AccessTokenTimersStoppedAndGarbageCollected());
            Assert.Equal(2, AccessTokenLayoutRenderer.AccessTokenProviders.Count);

            var result1 = layout1.Render(LogEventInfo.CreateNullEvent());
            var result2 = layout2.Render(LogEventInfo.CreateNullEvent());
            Assert.NotEmpty(result1);
            Assert.NotEqual(result1, result2);

            layout1.ResetTokenRefresher();
            Assert.False(AccessTokenTimersStoppedAndGarbageCollected());
            layout2.ResetTokenRefresher();
            Assert.True(WaitForTokenTimersStoppedAndGarbageCollected());
        }

        private static AccessTokenLayoutRenderer CreateAccessTokenLayoutRenderer(string resourceName = "TestResource", string accessToken = null, TimeSpan? refreshInterval = null)
        {
            NLog.LogManager.ThrowConfigExceptions = true;
            var layout = new AccessTokenLayoutRenderer((connectionString, azureAdInstance) => new AzureServiceTokenProviderMock(accessToken, refreshInterval ?? TimeSpan.FromMinutes(10))) { ResourceName = resourceName };
            layout.Render(LogEventInfo.CreateNullEvent());  // Initializes automatically
            Assert.False(AccessTokenTimersStoppedAndGarbageCollected());
            return layout;
        }

        private static bool WaitForTokenTimersStoppedAndGarbageCollected()
        {
            for (int i = 0; i < 500; ++i)
            {
                GC.Collect(2, GCCollectionMode.Forced);
                System.Threading.Thread.Sleep(10);
                if (AccessTokenTimersStoppedAndGarbageCollected())
                    return true;
            }

            return false;
        }

        private static bool AccessTokenTimersStoppedAndGarbageCollected()
        {
            return AccessTokenLayoutRenderer.AccessTokenProviders.All(token => !token.Value.TryGetTarget(out var provider));
        }
    }
}
