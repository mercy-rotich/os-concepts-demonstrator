# Process Control – Study Guide (for explaining the code to your lecturer)

Read this top to bottom once, then use the Q&A at the end to rehearse.
All line numbers refer to the current code.

## 1. The big picture (say this first)

> "The UI never touches the operating system. The button calls a command in the
> ViewModel, the ViewModel calls `ProcessService`, and `ProcessService` calls the
> Linux C library (`libc`) to send a **signal** to the process."

```
Button click  ->  ProcessesViewModel (command)  ->  ProcessService  ->  NativeLibc.Kill()  ->  libc kill()  ->  Linux kernel
 (XAML)            ViewModels/Processes/            Services/            Services/            system call
```

This is **MVVM**: View (XAML) shows things, ViewModel holds state and commands,
Service does the real work. That is why the process code is in `ProcessService`
and not in the button handlers.

## 2. The key OS concept: signals

A **signal** is a small message the kernel delivers to a process. The process
(or the kernel) reacts to it. Process control on Linux is just sending signals.

| Action | Signal | Number | Can the process ignore it? | What happens |
|---|---|---|---|---|
| Suspend | SIGSTOP | 19 | **No** | Kernel stops scheduling the process. State becomes `T`. |
| Resume | SIGCONT | 18 | No | Process becomes runnable again. |
| Terminate | SIGTERM | 15 | **Yes** (can catch it) | Polite request to exit; the program can clean up first. |
| Force Kill | SIGKILL | 9 | **No** | Kernel destroys the process immediately, no cleanup. |

Constants: [ProcessService.cs:11-14](../Services/ProcessService.cs#L11-L14).

Why two ways to terminate? SIGTERM lets a program save files and close
connections. SIGKILL is the last resort, so the UI makes you confirm it.

## 3. How a C# method reaches the kernel (P/Invoke)

[NativeLibc.cs:50-54](../Services/NativeLibc.cs#L50-L54):

```csharp
[DllImport("libc", EntryPoint = "kill", SetLastError = true)]
internal static extern int Kill(int pid, int signal);
```

- `DllImport` = "call a function from a native (C) library". This is **P/Invoke**.
- The C function is `kill(pid, signal)`. Despite its name it just *sends* any
  signal. It returns `0` on success and `-1` on failure.
- `SetLastError = true` makes .NET save the C `errno`, so we can read it with
  `Marshal.GetLastWin32Error()` and know *why* it failed.
- Under the hood, `kill()` is a **system call**: the program switches from user
  mode to kernel mode, the kernel delivers the signal, then switches back.

Priority uses `setpriority()` the same way (line 53-54).

## 4. How Suspend and Resume work

[ProcessService.cs:76-84](../Services/ProcessService.cs#L76-L84): both call the
shared private method `SendSignal(process, signal, ...)`:

- Suspend -> `SendSignal(process, 19 /*SIGSTOP*/ ...)`
- Resume  -> `SendSignal(process, 18 /*SIGCONT*/ ...)`

`SendSignal` ([line 199](../Services/ProcessService.cs#L199)) does these steps in order:

1. **Valid PID?** (PID > 0)
2. **Protected process?** If yes, refuse (section 6).
3. **Read `/proc/<pid>/stat`** to see if the process exists and get its state.
4. **Identity check** (section 5).
5. **Skip pointless actions**: already suspended, not suspended (for resume), or zombie.
6. **`NativeLibc.Kill(pid, signal)`**: the real action.
7. If it returned non-zero, read `errno`: `1` = permission denied (EPERM),
   `3` = no such process (ESRCH). Show a clear message.

You can *see* suspension in the UI: the Status field changes from `S`/`R`
to `T` (Stopped). `T` comes from `/proc/<pid>/stat`.

## 5. `/proc` and the PID-reuse check

`/proc` is a virtual folder the kernel exposes. `/proc/<pid>/stat` is one line of
numbers about a process. We parse three fields
([ParseStatLine](../Services/ProcessService.cs#L402)):

- **State** (R running, S sleeping, T stopped, Z zombie)
- **Nice value** (priority)
- **starttime** (when the process started, in clock ticks since boot)

**Problem:** PIDs are recycled. If process 4000 exits and a new, unrelated
process gets PID 4000, a stale button click could kill the wrong program.

**Solution:** when you select a process we remember its PID *and* `starttime`.
Before every action [IdentityMatches](../Services/ProcessService.cs#L318) re-reads
`starttime`. Different value = different process = action refused.
If either value is unknown we also refuse (safer).

## 6. Safety rules

- **Protected processes** ([IsProtectedTarget](../Services/ProcessService.cs#L326)):
  PIDs 0-2, `systemd`/`init`/`kthreadd`, kernel threads (parent PID 2), this app
  itself, and the program that launched this app. Buttons are disabled and the
  service refuses too ("defence in depth": UI *and* service both check).
- **Not root:** we never elevate. Linux itself only lets you signal your own
  processes, so other users' processes give "Permission denied" (EPERM).
- **Confirmation:** Terminate and Force Kill first show a confirm box. The
  service method `ForceKillProcess(process, confirmed)` also refuses if
  `confirmed` is false.
- **SIGTERM on a stopped process:** a stopped process cannot run its SIGTERM
  handler, so the signal would sit pending. After sending SIGTERM to a state-`T`
  process the code also sends SIGCONT so it wakes up and exits.

## 7. Changing priority (nice value)

[ChangePriority](../Services/ProcessService.cs#L105) calls
`setpriority(PRIO_PROCESS, pid, nice)`.

- Nice range: **-20 (highest priority) to +19 (lowest)**. Default 0.
- "Nice" = how *nice* the process is to others. Higher number = nicer = gets
  less CPU when the CPU is busy. It influences the **scheduler**, which decides
  who runs next.
- Without root you may only **raise** the number (be nicer). Lowering it needs
  privilege (EPERM). The smoke test checks exactly this.
- Linux detail: `PRIO_PROCESS` with a PID changes the main thread's nice value.

## 8. How the UI knows which buttons to enable

[ProcessesViewModel.cs:124](../ViewModels/Processes/ProcessesViewModel.cs#L124):
properties like `CanSuspend` are true only if a process is selected, nothing is
busy, it is not protected, and its state allows it (e.g. Resume only when `T`).
The XAML binds `IsEnabled="{Binding CanSuspend}"`. When `SelectedProcess` changes,
`[NotifyPropertyChangedFor]` tells the UI to re-check every button.

Actions run through `RunActionAsync` ([line 326](../ViewModels/Processes/ProcessesViewModel.cs#L326)):
run the service call on a background thread (`Task.Run`) so the UI doesn't
freeze, show the green/red message, then refresh the list after 300 ms.

## 9. How it was tested

`dotnet run --project tests/ProcessServiceSmoke` starts harmless `sleep 300`
child processes and checks: suspend -> state `T`, resume, raise nice, permission
error on lowering nice, rejected wrong start time, protected PID 1, unconfirmed
force kill refused, terminate a suspended process, force kill. All 16 checks pass.

## 10. Live demo script (2 minutes)

1. Terminal: `sleep 600 &` (note the PID it prints).
2. App -> Processes -> search that PID -> select it. Status shows `S`.
3. **Suspend** -> status `T`. Say: "SIGSTOP, kernel stopped scheduling it."
4. **Resume** -> back to `S`. "SIGCONT."
5. **Change Priority** to 10 -> works. Then try 0 -> permission denied. "Only root may raise priority."
6. **Terminate** -> confirm -> process disappears. "SIGTERM, graceful."
7. Select PID 1 or `systemd` -> buttons disabled. "Protected."
8. (Optional) start another sleep, **Force Kill**. "SIGKILL, cannot be ignored."

## 11. Likely lecturer questions

**How do you terminate a process?** We call the libc function `kill(pid, SIGTERM)`
through P/Invoke. That is a system call; the kernel delivers signal 15 and the
process exits, usually after cleanup.

**Difference between SIGTERM and SIGKILL?** SIGTERM can be caught or ignored so
the program can clean up. SIGKILL cannot be caught; the kernel removes the
process immediately. We use SIGKILL only after explicit confirmation.

**How does suspend work? Does it use CPU while suspended?** SIGSTOP (19) cannot be
ignored. The kernel stops scheduling the process, so it uses no CPU, but it keeps
its memory. SIGCONT (18) makes it runnable again.

**Why not just use `Process.Kill()` in .NET?** It only terminates; it can't
suspend, resume or choose SIGTERM. Calling `kill()` directly gives all four
operations and the `errno` for precise errors.

**What if the process already exited?** `/proc/<pid>/stat` no longer exists, so we
report "already exited". Also, `kill()` returns ESRCH (errno 3), which we map to
a message.

**What is PID reuse and how did you handle it?** (Section 5.) We compare the
process's `starttime` before acting.

**What is a zombie (Z)?** A process that finished but whose parent hasn't read
its exit status yet. It is already dead, so signals do nothing; we don't send one.

**What is `/proc`?** A virtual filesystem the kernel uses to expose live process
and system data. Nothing is stored on disk.

**Why can't you kill other users' processes?** The kernel checks that the
sender's user ID matches the target's (or is root). We don't run as root, so it
returns EPERM and we show "Permission denied".

**What is MVVM and where is it here?** View = `ProcessesView.axaml`, ViewModel =
`ProcessesViewModel`, Model = `ProcessInfoModel`/`ProcessActionResult`; the OS
work is in `ProcessService`, so the UI stays testable and simple.

**What does nice do?** It biases the scheduler: higher nice = lower priority.
Range -20 to 19.

**What is P/Invoke?** .NET's way to call native C functions from a library such
as `libc`.
