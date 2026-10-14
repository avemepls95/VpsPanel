namespace VpsPanel.Ssh;

internal sealed record ExecResult(int? ExitCode, string Stdout, string Stderr);