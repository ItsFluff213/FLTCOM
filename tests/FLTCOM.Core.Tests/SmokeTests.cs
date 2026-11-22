using System.Reflection;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void CoreAssemblyLoads()
        {
            Assembly core = Assembly.Load("FLTCOM.Core");

            Assert.Equal("FLTCOM.Core", core.GetName().Name);
        }
    }
}
