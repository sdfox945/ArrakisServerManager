using Arrakis.Core;

namespace Arrakis.Tests;

internal static partial class Program
{
    private static void RegisterStabilizationAdapterTests()
    {
        TestAsync("v1.1.1: actual adapter retains ownership, reads Jobs and correlates successful completion",async()=>
        {
            var (gateway,transport)=await Adapter();
            transport.PodFixture=StabilizationRegression.List(StabilizationRegression.Pod(owner:"Job")).ToJsonString();transport.JobFixture=StabilizationRegression.Jobs().ToJsonString();
            var snapshot=await gateway.RefreshAsync(default);var utility=snapshot.Maps.Single(m=>m.Pod=="fixture-pod");
            True(utility.Workload==WorkloadKind.Infrastructure&&!utility.HasCurrentProblem&&utility.Evidence?.JobCondition=="Complete","Adapter must pass authoritative Job context through to health assessment.");
            True(transport.Commands.Any(c=>c.Contains("kubectl get pods")&&c.Contains("ownerReferences")&&c.Contains("restartPolicy")),"Live pod projection must retain owner/lifecycle evidence.");
            True(transport.Commands.Any(c=>c.Contains("kubectl get jobs")&&c.Contains("conditions")),"Job conditions need a read-only discovery query.");
            Equal(0,transport.Writes);Equal(0,transport.LifecycleWrites);Equal(0,transport.MapWrites);
        });
    }
}
