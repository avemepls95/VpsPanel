namespace VpsPanel.Overview;

internal static class StatusIndicator
{
    public static ServerStatus Compute(ServerOverview overview)
    {
        if (overview.Error is not null)
            return ServerStatus.Error;

        var degraded = overview.ComposeProjects.Any(p => p.Stopped || p.Running < p.Total)
                       || overview.StandaloneContainers.Any(c => c.State != "running")
                       || overview.Timers.Any(t => t.Result is not null && t.Result != "success");
        return degraded ? ServerStatus.Warning : ServerStatus.Ok;
    }
}