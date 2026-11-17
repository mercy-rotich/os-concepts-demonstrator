# OS Concepts Demonstrator: Presentation Guide

Everything you need to present and defend this project: the overview, the architecture, each operating-system concept, a timed demo, likely lecturer questions with accurate answers, and honest limitations.

Read sections 1 to 3 to understand the project. Practise section 5 until you can do it without notes. Skim section 6 the night before.

---

## 1. Project overview (the 60-second version)

> **OS Concepts Demonstrator** is a desktop application, written in C# with Avalonia, that lets you *see* operating-system concepts working on a real Linux machine.
>
> Instead of drawing diagrams, it reads the information the Linux kernel already exposes, and turns it into ten live pages: processes, memory, files and storage, input/output, interprocess communication, services, process accounting, system audit, and the system-call interface.
>
> Every number on screen is read from the running system, never typed in. Where the system does not give us something (for example because it needs administrator rights), the page says so and says why, instead of showing a made-up value.
>
> The whole application is read-only and needs no administrator privileges.

**Key numbers:** 10 pages, about 13,000 lines of code in 91 files, .NET 10, Avalonia 12, MVVM architecture.

---

## 2. The problem it solves

- Operating-system theory is abstract. Students read about "processes", "page tables" or "system calls" and never see one.
- Linux already exposes almost everything as files (`/proc`, `/sys`), but reading raw files such as `/proc/diskstats` is unfriendly, and the numbers have no explanation.
- Existing tools (`top`, `df`, `systemctl`, `lsblk`) each show one slice, in a terminal, with no teaching.

**This project connects the two:** real data from the real kernel, one consistent interface, and a plain-English explanation next to every statistic.

A second, quieter goal: **honesty about limits.** Many system facts are restricted to administrators. A teaching tool should show *what is available and what is not*, so each page has a "What this system actually provides" panel with green, amber and red badges, each with a reason.

---

## 3. Architecture and technologies

### Technologies

| Part | Choice | Why |
|---|---|---|
| Language / runtime | C# on .NET 10 | Strong standard library; easy access to files, processes and native calls |
| UI toolkit | Avalonia 12 | Cross-platform desktop UI written in XAML; works on Linux |
| Pattern | MVVM with CommunityToolkit.Mvvm 8 | Separates data, logic and screen (see below) |
| Data sources | `/proc`, `/sys`, a few read-only commands | The kernel's own live information |

No other packages were added.

### MVVM in one paragraph

The code is split into three kinds of file:

- **Model**: plain data (for example `DiskIoModel`, a disk with its read speed).
- **View**: the screen, written in XAML (for example `IoManagementView.axaml`).
- **ViewModel**: the state and logic behind a screen (for example `IoManagementViewModel`). It exposes properties and commands. The View **binds** to them, so when a property changes the screen updates itself.

A fourth layer, **Services**, does the actual reading of the system (for example `IoService` reads `/proc/diskstats`). Services know nothing about the screen, so they could be tested on their own.

```
 Linux kernel (/proc, /sys)
        |  read-only
        v
   Services/*.cs        read and calculate (no UI code)
        |
        v
  ViewModels/*.cs       hold state, expose properties and commands
        |  data binding
        v
    Views/*.axaml       what the user sees
```

### How the pieces fit

- **Navigation:** `MainViewModel` holds a `CurrentViewModel`. Clicking a sidebar button calls a method such as `ShowIoManagement()`, which sets it. A `ViewLocator` finds the matching view by name (it replaces `ViewModel` with `View` in the class name), and the window displays it.
- **Live updates:** the kernel only keeps running totals ("this disk has read 38 GB since boot"). To show a *rate*, the app reads twice and divides the difference by the time between readings (measured with a stopwatch, not assumed).
- **Threads:** slow reads run on a background thread (`Task.Run`). Results come back to the UI thread before touching the screen. Timers are Avalonia `DispatcherTimer`s, which tick on the UI thread.
- **Resource handling:** the I/O and Accounting pages only sample while they are on screen (a timer starts when the page appears and stops when it disappears). The Dashboard and Memory timers run for the life of the app. All timers are stopped, and child processes and files cleaned up, when the window closes.
- **Safety rules the code follows:**
  - read-only throughout;
  - no administrator rights;
  - external programs are only ever run as a fixed list with no shell (`sed`, `head`, `systemctl`, `journalctl`, `dmesg`);
  - user text is never part of a command;
  - names are validated before use (for example service names);
  - failures are caught and explained, never allowed to crash the app.

### Folder map

```
Models/       plain data classes                    (28 files)
Services/     reading and calculating               (20 files)
ViewModels/   state and commands, one folder per page
Views/        XAML screens, one folder per page
Controls/     MemoryUsageChart: a custom-drawn graph
```

---

## 4. The ten pages and the concept behind each

For each page: the concept in plain words, where the data really comes from, and what to point at.

### 1. Dashboard: the machine at a glance
- **Concept:** an operating system manages the hardware; the first step is knowing what it is running on.
- **Data:** OS name from the runtime, CPU model from `/proc/cpuinfo`, memory from `/proc/meminfo` (refreshed every 2 s).
- **Point at:** the memory bar changing as you use the machine.

### 2. Process Management: what is running
- **Concept:** a **process** is a running program with its own memory, an ID (PID) and a parent. The kernel keeps a table of them.
- **Data:** the process list (the .NET `Process` class reads `/proc`), memory (working set), start time.
- **Point at:** search, sort by memory, select a process to see its details.

### 3. Memory Management: RAM in use
- **Concept:** physical memory is a shared, limited resource. Linux uses spare RAM as cache, so "used" must be defined carefully.
- **Data:** `/proc/meminfo`: `MemTotal` and `MemAvailable`. **Used = Total - Available.** A new reading every 5 s, the latest 20 kept.
- **Point at:** the graph (a custom control drawn from scratch), the time labels, the latest reading, the monitoring period.
- **Honest detail:** invalid or unreadable data shows a red banner instead of a wrong point.

### 4. File & Storage: disks, filesystems, mount points
- **Concept:** a **disk** holds **partitions**; a partition holds a **filesystem**; a filesystem is attached to the directory tree at a **mount point**. These four are different things.
- **Data:** `/proc/self/mountinfo` (what is mounted, from which device, as what type), `/sys/block` (disk model, size, SSD or HDD), `statvfs` through `DriveInfo` (capacity), and directory listing.
- **Point at:** `/` and `/home` sharing one card (on this machine they are subvolumes of one btrfs filesystem, so capacity is counted once; on a machine with separate partitions you would see separate cards); the virtual filesystems folded away as "not physical storage"; the permission bits (`rwxr-xr-x`); the "Permission denied" message.

### 5. I/O Management: devices and their traffic
- **Concept:** devices are managed by **drivers**; they signal the CPU with **interrupts**; disks are **block devices**, keyboards are **character devices**.
- **Data:** `/proc/diskstats`, `/proc/net/dev`, `/proc/stat` and `/proc/interrupts`, `/proc/bus/input/devices`, `/proc/devices`, `/sys/block/*/queue/scheduler`. Refreshed every 2 s while visible.
- **Point at:** read and write speed, IOPS, average time per operation, busy time, the I/O scheduler, interrupts per second, and the glossary explaining each.

### 6. Interprocess Communication: how programs talk
- **Concept:** processes are isolated by design, so the kernel provides controlled ways to share data.
- **Demos (all real):**
  - **Pipe:** a child `sed` process uppercases text sent through real OS pipes. The page proves it by showing `pipe:[inode]` numbers in `/proc/<pid>/fd`, open in both processes.
  - **Message passing:** a sender and slow receiver with a 5-message queue. When it is full, the sender really blocks.
  - **Shared memory:** a region in `/dev/shm` mapped twice; `/proc/self/maps` shows two views of one inode. A **race-condition demo** loses roughly a third of 2,000,000 increments without a lock (the exact amount differs every run) and none with one.

### 7. OS Services: background programs
- **Concept:** **services (daemons)** run without a user. **PID 1** (`systemd` here) starts them in dependency order, restarts them when they fail and tracks their state.
- **Data:** `systemctl list-units`, `list-unit-files`, `show`; `journalctl` for logs; `/proc/1/comm`.
- **Point at:** the overall "degraded" state and the Failed filter, enabled vs active, dependencies, and the honest explanation when the journal is restricted.

### 8. Process Accounting: who used what
- **Concept:** **accounting** records resources used per process: CPU time (user vs kernel), memory, I/O, context switches, page faults.
- **Data:** `/proc/<pid>/stat`, `status` and `io`.
- **Point at:** the red "not available" badge for historical accounting (records of exited processes), and the grey **restricted** I/O column for other users' processes: unavailable, never faked as zero.

### 9. System Audit: events and configuration
- **Concept:** **auditing** records security-relevant events (who logged in, when, from where) and checks that the system is configured safely.
- **Data:** the binary login records `/var/log/wtmp` and `/run/utmp` (decoded by the app itself), `/proc/self/loginuid`, `/proc/sys/*` hardening settings, file permission bits.
- **Point at:** the login history rebuilt from raw records, the audit identity, the "review" items (for example `ptrace_scope = 0`), and the sources that need root.

### 10. OS APIs: how a program reaches the kernel
- **Concept:** a program cannot touch hardware, so it makes a **system call**: a controlled jump from user mode into the kernel. Libraries wrap system calls.
- **Data:** `/proc/self/maps` (the real libraries in this process), direct calls into `libc` (P/Invoke) compared with .NET's own answers, `/proc/self/io` counters, kernel headers for syscall numbers.
- **Point at:** the experiment: **5,000 bytes read one byte at a time = 5,000 system calls; the same bytes in one call = 1.**

---

## 5. Demonstration sequence (about 15 to 18 minutes)

### Before you start (5 minutes, do this the day before and again that morning)
1. `cd` to the project and run `dotnet build`. It must say **0 Warning(s), 0 Error(s)**.
2. Close heavy applications so the numbers are not dominated by other programs.
3. Open a **second terminal** next to the app for the cross-checks below.
4. Run the app once (`dotnet run`) and click through all ten pages, so nothing surprises you.
5. Make sure there is spare disk space (a few hundred MB).

### The sequence

| # | Page | Do this | Say this |
|---|---|---|---|
| 1 | Dashboard (1 min) | Show OS, CPU, memory. Terminal: `uname -a` | "Everything here is read from the live system, not typed in." |
| 2 | Process Management (2 min) | Search `chrome` or `code`. Sort by Memory, descending. Select one. Terminal: `ps aux --sort=-rss \| head -5` | "Same processes, same order. A process is a running program with its own ID and memory." |
| 3 | Memory (2 min) | Point at the graph and the labels. Terminal: `free -h`. Open a browser tab and watch the next reading | "Used = total minus *available*. Linux uses spare RAM as cache, so a high percentage is not automatically a problem." |
| 4 | File & Storage (2 min) | Show the filesystem cards. Terminal: `lsblk -o NAME,SIZE,TYPE,FSTYPE,MOUNTPOINTS`. Then `mkdir ~/locked_demo && chmod 000 ~/locked_demo`, browse into it in the app, then `chmod 755 ~/locked_demo && rmdir ~/locked_demo` | "On this machine `/` and `/home` are *one* filesystem, so I count its space once. A mount point is not a disk." |
| 5 | I/O (2 min) | Terminal: `dd if=/dev/urandom of=~/iotest.bin bs=1M count=300 oflag=direct`. Watch write speed and busy time rise. Then `rm ~/iotest.bin` | "The kernel only keeps totals. A speed is the change between two readings divided by the time between them." |
| 6 | IPC (3 min) | Start the child, send a message, click **Refresh** under the proof. Click **Send 10 quickly**. Write to shared memory, read with a separate process, run the race demo | "Three mechanisms. The `pipe:[...]` number appearing on both sides proves they share one pipe. Without a lock, roughly a third of the additions are lost (it differs each run, which is the point)." |
| 7 | OS Services (1.5 min) | Point at "degraded". Filter to Failed. Select one | "PID 1 supervises services. 'Degraded' means at least one failed." |
| 8 | Accounting (1.5 min) | Terminal: `yes > /dev/null & echo $!` and watch it reach about 100%. Then `kill <that number>` | "Live accounting only knows running processes. The red badge shows historical accounting is off here." |
| 9 | Audit (1.5 min) | Compare login history with `who`. Show a red source and a "review" item | "I only show what an ordinary account may read. The rest says why it is hidden." |
| 10 | OS APIs (2 min) | Click the **one byte at a time** button, then the **one call** button | "Same data. 5,000 trips into the kernel versus 1. That is why programs buffer." |

### If something goes wrong
- **A page shows an unavailable message:** that is the design working. Read the reason aloud: it is part of the point.
- **A demo child process fails:** the page explains why. Move on.
- **The app is slow:** you are probably on a busy machine. Close other programs.
- **Never** run the cross-check commands with `sudo`.

### Cleanup after the demo
```
rm -f ~/iotest.bin
rmdir ~/locked_demo 2>/dev/null
```
(The IPC child process and shared-memory file are removed automatically when you leave that page.)

---

## 6. Likely questions and accurate answers

### About the project and its design

**Why MVVM?**
It separates what is shown (View) from the logic (ViewModel) and from the data (Model). The screen updates by data binding, so the logic never touches controls directly. It also keeps the code that reads the system (Services) free of UI code.

**How does the screen update when data changes?**
ViewModels raise a `PropertyChanged` event (the `INotifyPropertyChanged` interface). The CommunityToolkit attribute `[ObservableProperty]` generates that code. The XAML binding listens for it and redraws.

**How does navigation work?**
`MainViewModel.CurrentViewModel` is the page being shown. A sidebar button sets it. A `ViewLocator` turns the ViewModel's class name into the matching View's name.

**Is any data fake or hard-coded?**
No. Every figure is read from the running system. The few fixed texts are explanations, not measurements, and are labelled as such (for example the system-call catalog is marked "typical").

**How do you know the numbers are right?**
I cross-checked them against independent tools: `ps` for processes, `df` and `lsblk` for storage, `who` for sessions, `uname` and `getconf` for system facts, `stat` for permissions. For example, a busy loop I started showed 99.4% CPU and the CPU time matched `ps`.

**Does it change anything on the system?**
No. It is read-only. The only things it creates are a temporary file in `/dev/shm` and a short-lived child process for the IPC demos, both removed when you leave the page or close the app.

**Why does it not need root?**
It only reads information the kernel makes available to ordinary users. Anything that needs root is reported as unavailable, with the reason.

**What happens on a system that is not Linux, or has no systemd?**
It is a Linux application because it reads `/proc` and `/sys`. Without systemd the Services page says so instead of failing.

**Why C# and Avalonia on Linux?**
.NET runs on Linux and has good access to files and processes, and Avalonia gives a modern desktop UI that is not tied to Windows.

**How do timers avoid wasting resources?**
The I/O and Accounting pages start sampling when they appear on screen and stop when you leave. I measured this from outside for the Accounting page, the heavier of the two: it made about 1,800 read system calls per second in the seconds it was sampling, and only a handful per second on pages without a timer once I left it (apart from a short burst when a new page first loads its data). All timers also stop when the window closes.

### Processes and memory

**What is the difference between a process and a thread?**
A process has its own address space and resources. Threads are separate paths of execution *inside* one process and share its memory.

**What is a PID, and what is PID 1?**
A PID identifies a process. PID 1 is the first process the kernel starts (here `systemd`). It adopts orphaned processes and starts the rest of the system.

**What does "memory" mean in the process list?**
The working set, also called the resident set size (RSS): the part of the process that is in physical RAM right now. It is not the same as virtual memory, which is the address space the process has reserved.

**Why is "used memory" total minus *available*, not total minus *free*?**
Linux uses spare RAM for the page cache to speed up disk access. That memory is reclaimable the moment a program needs it. `MemAvailable` is the kernel's estimate of what can be used without swapping, so it is a fairer measure than `MemFree`.

**Why is 80% memory use not necessarily a problem?**
Because much of it can be cache that is released on demand. It becomes a problem when available memory is very low and the system starts swapping.

**What is a zombie process?**
One that has finished but whose parent has not yet collected its exit status. It uses no CPU or memory, only a table entry.

**What are context switches?**
The CPU stops running one process or thread and starts another. *Voluntary* ones happen when a process waits (for I/O or a lock). *Involuntary* ones happen when the scheduler takes the CPU because its time slice ended.

**What are minor and major page faults?**
A minor fault is resolved from memory. A major fault requires reading from disk, which is far slower.

**Why can CPU use be above 100%?**
The figure is a share of *one* core. A multi-threaded program using several cores can exceed 100%.

**What is the difference between user and kernel CPU time?**
User time is the program's own code. Kernel (system) time is the kernel working on its behalf, for example during system calls.

### Storage and I/O

**What is the difference between a disk, a partition, a filesystem and a mount point?**
The disk is hardware. A partition is a slice of it. A filesystem is the data structure written into a partition. A mount point is the directory where a filesystem is attached to the tree.

**Why do `/` and `/home` show the same capacity (on this machine)?**
They are two btrfs subvolumes of the same filesystem on one partition, so they share one pool of space. Counting both would double-count.

**Why do Used + Available not equal Total?**
Linux reserves a few percent for root.

**What is the sticky bit on `/tmp`?**
Everyone may create files there, but only a file's owner (or root) may delete it.

**What do `rwx` mean on a directory?**
`r` lets you list names, `w` lets you create or delete entries, `x` lets you enter the directory.

**What is the difference between throughput, IOPS and latency?**
Throughput is bytes per second. IOPS is operations per second. Latency is the time one operation takes. Many small random requests give high IOPS but low throughput.

**What is an I/O scheduler, and why is it `none` on an SSD?**
It orders disk requests. Reordering helps spinning disks avoid head movement. SSDs have no moving parts and fast internal queues, so extra ordering in the kernel only adds cost.

**What is an interrupt?**
A device's way of getting the CPU's attention when it needs service, so the CPU does not have to poll it constantly.

**Block device vs character device?**
Block devices (disks) transfer fixed-size blocks and allow random access. Character devices (keyboards) deliver a stream of bytes.

**Why does the disk page show "measuring" at first?**
The kernel only provides running totals. A rate needs two readings.

### Interprocess communication

**Pipe vs message queue vs shared memory?**
A pipe is a one-way byte stream with no message boundaries. A message queue holds separate messages managed by the system. Shared memory maps the same RAM into several processes: fastest, because nothing is copied, but the programs must synchronise themselves.

**What happens when a pipe is full?**
The writer blocks until the reader takes data. When the writer closes its end, the reader gets end-of-file. That is how the page stops the `sed` child: closing its input.

**What is a race condition?**
The result depends on the timing of threads. In the demo, "add 1" is three steps: read, add, write. Two threads can read the same value, so one addition is lost. A lock lets only one thread through those steps at a time.

**What is `/dev/shm`?**
A filesystem that lives entirely in RAM (`tmpfs`). It is how POSIX shared memory is implemented on Linux.

**Why does the pipe demo show the same `pipe:[number]` on both sides?**
The number identifies one pipe object in the kernel. Seeing it open in both processes proves they hold the two ends of the same pipe.

### Services, accounting and audit

**What is systemd?**
The init system: PID 1. It starts services in dependency order, restarts them, and records their state.

**What is the difference between a process and a service?**
A service is a managed definition (a unit). Its running instance is one or more processes.

**What do enabled, static and masked mean?**
Enabled starts at boot. Static has no boot setting and is started only when something needs it. Masked is blocked completely.

**Why is a service "active (exited)"?**
It is a one-shot task that ran and succeeded. systemd keeps it marked active.

**Why can the page not show logs for system services?**
Reading them needs membership of the `adm` or `systemd-journal` group. The page detects this and explains it instead of showing an empty box.

**Why is process accounting for exited processes "not available"?**
Linux only records exited processes if an administrator enables BSD process accounting (`accton`). Otherwise it forgets a process when it exits.

**Why is some process I/O "restricted"?**
The kernel only lets a process's owner (or root) read its I/O counters, since they could reveal what the process is doing. The page shows "restricted" rather than zero.

**What is the audit login UID?**
The user ID recorded at login. It does not change after `su` or `sudo`, so audit records can always name the real person.

**What is ASLR?**
Address-space layout randomisation. The system loads programs at random addresses so an attacker cannot rely on fixed locations.

**Why does the audit page say "review" for some settings?**
They differ from common hardening advice. That is a prompt to think, not a failure: a security-testing machine like Parrot deliberately relaxes some, such as `ptrace_scope`.

**Why are `/etc/shadow` and `/etc/passwd` treated differently?**
`/etc/passwd` is world-readable because many programs need account names. `/etc/shadow` holds password hashes, so others must have no access.

### System calls and APIs

**What is a system call?**
A request from a program to the kernel for a protected service, made by switching the processor from user mode to kernel mode.

**Why is reading a file one byte at a time so slow?**
Each system call has a fixed cost: a mode switch and a permission check. The experiment shows 5,000 calls against 1 for the same data.

**Does every library call make a system call?**
No. Some are answered inside the process (the page size, the clock tick rate), and the clock is read through the vDSO without entering the kernel.

**What is the vDSO?**
A few pages of kernel code mapped into every process so that harmless, frequent operations like reading the clock avoid a real system call.

**What is P/Invoke?**
.NET's way of calling native functions. The app uses it to call the C library directly and compare answers with .NET's.

**What is a file descriptor?**
A small number the kernel gives for an open file, pipe or socket. It is what `read` and `write` use.

**What are capabilities?**
The kernel splits administrator power into separate privileges. The app holds none, so privileged calls would be refused.

---

## 7. How it was tested (use this if asked about quality)

- `dotnet build`: **0 errors, 0 warnings**.
- **Navigation test:** the application clicked its own ten sidebar buttons using real click events. All ten reached the correct page. The console printed no errors.
- **Cross-checks against independent tools** (examples): `ps`, `df`, `lsblk`, `uname`, `who`, `stat`, `getconf`, `systemctl`.
- **Resource test, measured from outside:**
  - timers stop when pages are left;
  - the IPC child process and shared-memory file disappear when you leave the page;
  - closing the window exits with code 0, leaving no processes or files behind;
  - stopping the app with a signal also removes the shared-memory file.
- **Real bugs found by this testing and fixed** (worth mentioning: it shows the process worked):
  - a binding error on the Process page when nothing was selected;
  - Memory cards that did not update with the graph;
  - an experiment on the OS APIs page that counted its own overhead;
  - a clock description that ignored time the machine spent suspended.

There are no automated unit tests yet (see future work).

---

## 8. Honest limitations and future improvements

### Known limitations
- **Linux only.** It relies on `/proc` and `/sys`.
- **Read-only by design.** It cannot kill a process, start a service or change a setting.
- **Some information needs administrator rights**, so it is shown as unavailable: failed logins, `dmesg`, system journal for ordinary users, other users' process I/O, `auditd` records, historical process accounting.
- **Several pages refresh only on demand** (Processes, Storage, Services, Audit, OS APIs). Dashboard, Memory, I/O and Accounting refresh automatically.
- **Accounting assumes 100 ticks per second**, the value Linux uses for `/proc`. The OS APIs page confirms this with `sysconf`.
- **System-call numbers are read for x86-64 only.**
- **Process Accounting shows the 30 busiest or newest processes.** Search reaches the rest.
- **The message-queue demo is between two parts of the same program**, not two separate processes.
- **No live system-call tracing** (`strace` is not installed here, and the app never attaches to other processes).
- **No automated tests** and no saved history between runs.

### Future improvements
1. **Automated tests.** The parsing code (`/proc/stat`, `mountinfo`, `wtmp`) takes plain text or bytes, so it can be tested with fixture files.
2. **I/O history graphs**, reusing the custom chart control built for memory.
3. **Network connections** from `/proc/net/tcp`.
4. **Safe actions** with confirmation (renice a process, restart a user service).
5. **Decode `pacct` files** when process accounting is enabled.
6. **Containers and control groups** (`/sys/fs/cgroup`).
7. **Run `strace` for fixed demo commands** when it is installed.
8. **Export** any table to CSV for reports.
9. **Package** the app as a single file for easy sharing.

---

## 9. Quick reference: commands to cross-check the app

| App page | Cross-check in a terminal |
|---|---|
| Dashboard | `uname -a`, `lscpu \| head`, `free -h` |
| Processes | `ps aux --sort=-rss \| head`, `ps -o pid,lstart,cputime,rss -p <pid>` |
| Memory | `free -h`, `grep -E 'MemTotal\|MemAvailable' /proc/meminfo` |
| Storage | `lsblk -o NAME,SIZE,TYPE,FSTYPE,MOUNTPOINTS`, `df -h`, `findmnt` |
| I/O | `cat /proc/diskstats`, `cat /proc/net/dev`, `cat /proc/interrupts` |
| IPC | `ls -l /proc/<pid>/fd`, `ls -l /dev/shm`, `grep osdemo /proc/<pid>/maps` |
| Services | `systemctl is-system-running`, `systemctl list-units --type=service --state=failed` |
| Accounting | `cat /proc/<pid>/stat`, `cat /proc/<pid>/io`, `getconf CLK_TCK` |
| Audit | `who`, `stat -c '%a %n' /etc/shadow /tmp`, `sysctl kernel.yama.ptrace_scope` |
| OS APIs | `ldd --version`, `getconf PAGESIZE`, `cat /proc/self/maps \| grep libc` |
