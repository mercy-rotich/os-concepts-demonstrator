using System.Diagnostics;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

// Smoke test: drives ProcessService against harmless `sleep` children it starts itself.
// Run: dotnet run --project tests/ProcessServiceSmoke
int failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name} {detail}");
    if (!ok) failures++;
}

var svc = new ProcessService();

ProcessInfoModel Snapshot(int pid) =>
    svc.GetRunningProcesses().First(p => p.ProcessId == pid);

Process Start() => Process.Start(new ProcessStartInfo("sleep", "300") { UseShellExecute = false })!;

// --- suspend / resume ---
var child = Start();
Thread.Sleep(200);
var info = Snapshot(child.Id);
Check("child visible and not stopped", info.StatusCode != "T", info.StatusCode);

var r = svc.SuspendProcess(info);
Thread.Sleep(200);
Check("suspend succeeds", r.Success, r.Message);
Check("status is T after suspend", Snapshot(child.Id).StatusCode == "T");

r = svc.ResumeProcess(Snapshot(child.Id));
Thread.Sleep(200);
Check("resume succeeds", r.Success, r.Message);
Check("status not T after resume", Snapshot(child.Id).StatusCode != "T");

// --- priority ---
r = svc.ChangePriority(Snapshot(child.Id), 5);
Thread.Sleep(100);
Check("raise nice to 5", r.Success && Snapshot(child.Id).NiceValue == 5, r.Message);
r = svc.ChangePriority(Snapshot(child.Id), 0);
Check("lower nice back needs privilege -> permission error", !r.Success && r.IsPermissionError, r.Message);
r = svc.ChangePriority(Snapshot(child.Id), 99);
Check("out-of-range nice rejected", !r.Success, r.Message);

// --- identity check ---
var stale = Snapshot(child.Id);
stale.StartTicks = (stale.StartTicks ?? 0) + 1;
r = svc.SuspendProcess(stale);
Check("wrong StartTicks rejected", !r.Success && r.IsIdentityMismatch, r.Message);
stale.StartTicks = null;
r = svc.SuspendProcess(stale);
Check("null StartTicks rejected", !r.Success && r.IsIdentityMismatch, r.Message);

// --- protected ---
r = svc.TerminateProcess(new ProcessInfoModel { ProcessId = 1, Name = "systemd" });
Check("PID 1 protected", !r.Success && r.IsProtectedProcess, r.Message);
r = svc.TerminateProcess(new ProcessInfoModel { ProcessId = Environment.ProcessId, Name = "self", StartTicks = 1 });
Check("self protected", !r.Success && r.IsProtectedProcess, r.Message);

// --- force kill needs confirmation ---
r = svc.ForceKillProcess(Snapshot(child.Id), confirmed: false);
Check("force kill unconfirmed refused", !r.Success, r.Message);

// --- SIGTERM on a stopped process still terminates it ---
svc.SuspendProcess(Snapshot(child.Id));
Thread.Sleep(200);
r = svc.TerminateProcess(Snapshot(child.Id));
bool exited = child.WaitForExit(2000);
Check("terminate of suspended process works", r.Success && exited, r.Message);

// --- already exited ---
r = svc.TerminateProcess(info);
Check("already-exited reported", !r.Success && r.IsProcessMissing, r.Message);

// --- force kill ---
var child2 = Start();
Thread.Sleep(200);
r = svc.ForceKillProcess(Snapshot(child2.Id), confirmed: true);
Check("force kill works", r.Success && child2.WaitForExit(2000), r.Message);

Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures;
