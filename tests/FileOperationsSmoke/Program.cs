using System.Diagnostics;
using OS_Concepts_Demonstrator.Services;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]

// Smoke test for FileOperationsService + FileSafetyPolicy.
// Uses a throw-away folder as the "home" root, so real user files are never touched.
// Run: dotnet run --project tests/FileOperationsSmoke
int failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? "  -> " + detail : "")}");
    if (!ok) failures++;
}
string Sh(string cmd)
{
    var p = Process.Start(new ProcessStartInfo("sh", new[] { "-c", cmd }) { RedirectStandardOutput = true })!;
    string o = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); return o;
}

string root = Path.Combine(Path.GetTempPath(), "fileops-smoke-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(root);
try
{
    var svc = new FileOperationsService(new FileSafetyPolicy(root));

    // ---- New File ----
    var r = svc.CreateFile(root, "notes.txt");
    Check("create file", r.Success && File.Exists(Path.Combine(root, "notes.txt")), r.Message);
    File.WriteAllText(Path.Combine(root, "notes.txt"), "keep me");
    r = svc.CreateFile(root, "notes.txt");
    Check("create file refuses existing name (no overwrite)", !r.Success && File.ReadAllText(Path.Combine(root, "notes.txt")) == "keep me", r.Message);

    // ---- New Folder ----
    r = svc.CreateDirectory(root, "docs");
    Check("create folder", r.Success && Directory.Exists(Path.Combine(root, "docs")), r.Message);
    r = svc.CreateDirectory(root, "docs");
    Check("create folder refuses existing name", !r.Success, r.Message);
    r = svc.CreateDirectory(root, "notes.txt");
    Check("create folder refuses name of existing file", !r.Success, r.Message);

    // ---- name validation ----
    foreach (string bad in new[] { "", "   ", "a/b", "..", ".", " lead", new string('x', 256) })
        Check($"invalid name rejected: '{(bad.Length > 12 ? bad[..12] + "..." : bad)}'", !svc.CreateFile(root, bad).Success);

    // ---- Rename ----
    string notes = Path.Combine(root, "notes.txt");
    string inodeBefore = Sh($"stat -c %i '{notes}'");
    r = svc.Rename(notes, "renamed.txt");
    string renamed = Path.Combine(root, "renamed.txt");
    Check("rename file", r.Success && File.Exists(renamed) && !File.Exists(notes), r.Message);
    Check("rename keeps the same inode and content", Sh($"stat -c %i '{renamed}'") == inodeBefore && File.ReadAllText(renamed) == "keep me");
    r = svc.Rename(Path.Combine(root, "docs"), "documents");
    Check("rename folder", r.Success && Directory.Exists(Path.Combine(root, "documents")), r.Message);
    svc.CreateFile(root, "other.txt");
    r = svc.Rename(renamed, "other.txt");
    Check("rename refuses to replace an existing item", !r.Success && File.ReadAllText(renamed) == "keep me", r.Message);
    r = svc.Rename(Path.Combine(root, "missing.txt"), "x.txt");
    Check("rename of missing item reported", !r.Success, r.Message);

    // ---- safety: outside the allowed root ----
    r = svc.CreateFile("/tmp", "should-not-exist-" + Guid.NewGuid().ToString("N"));
    Check("create outside root blocked", !r.Success && r.IsBlockedBySafetyPolicy, r.Message);
    r = svc.Rename("/etc/hostname", "hostname2");
    Check("rename of system file blocked", !r.Success && r.IsBlockedBySafetyPolicy && File.Exists("/etc/hostname"), r.Message);
    r = svc.Rename(root, "newroot");
    Check("renaming the root itself blocked", !r.Success && r.IsBlockedBySafetyPolicy && Directory.Exists(root), r.Message);

    // ---- safety: symlink pointing out of root ----
    string link = Path.Combine(root, "etc-link");
    File.CreateSymbolicLink(link, "/etc");
    r = svc.CreateFile(link, "evil.txt");
    Check("create THROUGH symlink to /etc blocked (realpath)", !r.Success && r.IsBlockedBySafetyPolicy && !File.Exists("/etc/evil.txt"), r.Message);
    r = svc.Rename(Path.Combine(link, "hostname"), "x");
    Check("rename THROUGH symlink to /etc blocked", !r.Success && r.IsBlockedBySafetyPolicy && File.Exists("/etc/hostname"), r.Message);
    r = svc.Rename(link, "etc-link-renamed");
    Check("renaming the link itself is allowed and leaves /etc alone",
          r.Success && Directory.Exists("/etc") && new FileInfo(Path.Combine(root, "etc-link-renamed")).LinkTarget == "/etc", r.Message);

    // ---- safety: hidden top-level items ----
    Directory.CreateDirectory(Path.Combine(root, ".config"));
    r = svc.CreateFile(Path.Combine(root, ".config"), "x.txt");
    Check("create inside hidden top-level folder blocked", !r.Success && r.IsBlockedBySafetyPolicy, r.Message);
    r = svc.Rename(Path.Combine(root, ".config"), "config");
    Check("rename of hidden top-level folder blocked", !r.Success && r.IsBlockedBySafetyPolicy, r.Message);
    r = svc.CreateFile(Path.Combine(root, "documents"), ".hidden-but-fine");
    Check("hidden names deeper down are allowed", r.Success, r.Message);

    // ---- permission error ----
    string locked = Path.Combine(root, "locked");
    Directory.CreateDirectory(locked);
    File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
    r = svc.CreateFile(locked, "x.txt");
    Check("permission denied reported cleanly", !r.Success && r.IsPermissionError, r.Message);
    File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    // ---- Properties ----
    var p = svc.GetProperties(renamed, out string err);
    Check("properties: file", p is not null && p.FileType.StartsWith("Regular file (TXT)") && p.Inode == inodeBefore && p.Size.Contains("7 bytes"),
          p is null ? err : $"{p.FileType}; {p.Size}; inode {p.Inode}");
    Check("properties: permissions match stat", p is not null && p.Permissions.StartsWith(Sh($"stat -c %A '{renamed}'")), p?.Permissions ?? "");
    Check("properties: creation time reported", p is not null && p.Created.Length > 0, p?.Created ?? "");
    var pd = svc.GetProperties(Path.Combine(root, "documents"), out _);
    Check("properties: directory", pd is not null && pd.FileType == "Directory" && pd.Permissions.StartsWith("d"), pd?.Permissions ?? "");
    var pl = svc.GetProperties(Path.Combine(root, "etc-link-renamed"), out _);
    Check("properties: symlink shows target", pl is not null && pl.FileType == "Symbolic link" && pl.LinkTarget!.Contains("/etc"), pl?.LinkTarget ?? "");
    var ps = svc.GetProperties("/etc/passwd", out _);
    Check("properties: readable outside root, marked read-only", ps is not null && ps.ModifyStatus.StartsWith("Read-only"), ps?.Owner ?? "");
    Check("properties: missing file reports error", svc.GetProperties(Path.Combine(root, "nope"), out err) is null && err.Length > 0, err);

    // ================= DELETE =================
    string victim = Path.Combine(root, "victim.txt");
    File.WriteAllText(victim, "bye");
    r = svc.Delete(victim);
    Check("delete file", r.Success && !File.Exists(victim), r.Message);
    r = svc.Delete(victim);
    Check("delete missing file reported", !r.Success, r.Message);

    string full = Path.Combine(root, "full-folder");
    Directory.CreateDirectory(full);
    File.WriteAllText(Path.Combine(full, "inside.txt"), "x");
    r = svc.Delete(full);
    Check("delete NON-empty folder refused (no recursion)", !r.Success && File.Exists(Path.Combine(full, "inside.txt")), r.Message);
    Directory.CreateDirectory(Path.Combine(root, "empty-folder"));
    r = svc.Delete(Path.Combine(root, "empty-folder"));
    Check("delete empty folder", r.Success && !Directory.Exists(Path.Combine(root, "empty-folder")), r.Message);

    string target = Path.Combine(root, "target.txt");
    File.WriteAllText(target, "still here");
    string fileLink = Path.Combine(root, "link-to-target");
    File.CreateSymbolicLink(fileLink, target);
    r = svc.Delete(fileLink);
    Check("delete symlink removes only the link", r.Success && !FileStatReader.Exists(fileLink) && File.ReadAllText(target) == "still here", r.Message);
    r = svc.Delete(Path.Combine(root, "etc-link-renamed"));
    Check("delete symlink-to-/etc removes link, /etc intact", r.Success && Directory.Exists("/etc") && File.Exists("/etc/hostname"), r.Message);

    string hard = Path.Combine(root, "hard.txt");
    Sh($"ln '{target}' '{hard}'");
    r = svc.Delete(hard);
    Check("delete one hard link keeps data via other name", r.Success && File.ReadAllText(target) == "still here" && r.Message.Contains("other name"), r.Message);

    r = svc.Delete("/etc/hostname");
    Check("delete system file blocked", !r.Success && r.IsBlockedBySafetyPolicy && File.Exists("/etc/hostname"), r.Message);
    r = svc.Delete(Path.Combine(root, ".config"));
    Check("delete hidden top-level folder blocked", !r.Success && r.IsBlockedBySafetyPolicy && Directory.Exists(Path.Combine(root, ".config")), r.Message);
    r = svc.Delete(root);
    Check("delete root blocked", !r.Success && Directory.Exists(root), r.Message);

    // ================= COPY =================
    string dest = Path.Combine(root, "dest");
    Directory.CreateDirectory(dest);
    r = svc.Copy(target, dest, overwrite: false);
    string copied = Path.Combine(dest, "target.txt");
    Check("copy file", r.Success && File.ReadAllText(copied) == "still here", r.Message);
    Check("copy is a different inode", Sh($"stat -c %i '{copied}'") != Sh($"stat -c %i '{target}'"));
    File.WriteAllText(target, "new version");
    r = svc.Copy(target, dest, overwrite: false);
    Check("copy onto existing asks for confirmation, file untouched", !r.Success && r.NeedsOverwriteConfirmation && File.ReadAllText(copied) == "still here", r.Message);
    r = svc.Copy(target, dest, overwrite: true);
    Check("copy with confirmed overwrite replaces", r.Success && File.ReadAllText(copied) == "new version", r.Message);
    r = svc.Copy(target, root, overwrite: false);
    Check("copy into same folder makes '(copy)' name", r.Success && File.Exists(Path.Combine(root, "target (copy).txt")), r.Message);
    r = svc.Copy(target, root, overwrite: false);
    Check("second copy makes '(copy 2)'", r.Success && File.Exists(Path.Combine(root, "target (copy 2).txt")), r.Message);
    r = svc.Copy(full, dest, overwrite: false);
    Check("copy folder refused", !r.Success, r.Message);
    File.CreateSymbolicLink(Path.Combine(root, "passwd-link"), "/etc/passwd");
    r = svc.Copy(Path.Combine(root, "passwd-link"), dest, overwrite: false);
    Check("copy of symlink refused (not followed)", !r.Success && !File.Exists(Path.Combine(dest, "passwd-link")), r.Message);
    r = svc.Copy("/etc/hostname", dest, overwrite: false);
    Check("copy readable system file INTO root allowed", r.Success && File.Exists(Path.Combine(dest, "hostname")), r.Message);
    r = svc.Copy(target, "/tmp", overwrite: false);
    Check("copy to outside root blocked", !r.Success && r.IsBlockedBySafetyPolicy, r.Message);
    Directory.CreateDirectory(Path.Combine(dest, "target.txt-dir-clash"));
    Directory.CreateDirectory(Path.Combine(root, "d2"));
    Directory.CreateDirectory(Path.Combine(root, "d2", "target.txt"));
    r = svc.Copy(target, Path.Combine(root, "d2"), overwrite: true);
    Check("copy never replaces a folder", !r.Success && Directory.Exists(Path.Combine(root, "d2", "target.txt")), r.Message);
    File.CreateSymbolicLink(Path.Combine(root, "d2", "x.txt"), "/etc/hostname");
    File.WriteAllText(Path.Combine(root, "x.txt"), "evil");
    r = svc.Copy(Path.Combine(root, "x.txt"), Path.Combine(root, "d2"), overwrite: true);
    Check("copy never writes through an existing symlink", !r.Success && File.ReadAllText("/etc/hostname") != "evil", r.Message);

    // ================= MOVE =================
    string mover = Path.Combine(root, "mover.txt");
    File.WriteAllText(mover, "moving");
    string moverInode = Sh($"stat -c %i '{mover}'");
    r = svc.Move(mover, dest, overwrite: false);
    string moved = Path.Combine(dest, "mover.txt");
    Check("move file", r.Success && File.Exists(moved) && !File.Exists(mover), r.Message);
    Check("move within filesystem keeps inode", Sh($"stat -c %i '{moved}'") == moverInode);
    File.WriteAllText(mover, "second");
    r = svc.Move(mover, dest, overwrite: false);
    Check("move onto existing asks for confirmation", !r.Success && r.NeedsOverwriteConfirmation && File.ReadAllText(moved) == "moving" && File.Exists(mover), r.Message);
    r = svc.Move(mover, dest, overwrite: true);
    Check("move with confirmed overwrite", r.Success && File.ReadAllText(moved) == "second" && !File.Exists(mover), r.Message);
    r = svc.Move(moved, dest, overwrite: false);
    Check("move into same folder refused", !r.Success, r.Message);
    r = svc.Move(full, dest, overwrite: false);
    Check("move folder (rename, no recursion needed)", r.Success && File.Exists(Path.Combine(dest, "full-folder", "inside.txt")), r.Message);
    r = svc.Move(dest, Path.Combine(dest, "full-folder"), overwrite: false);
    Check("move folder into its own subfolder refused", !r.Success && Directory.Exists(dest), r.Message);
    r = svc.Move("/etc/hostname", dest, overwrite: false);
    Check("move system file blocked", !r.Success && r.IsBlockedBySafetyPolicy && File.Exists("/etc/hostname"), r.Message);
    r = svc.Move(moved, "/tmp", overwrite: false);
    Check("move to outside root blocked", !r.Success && r.IsBlockedBySafetyPolicy && File.Exists(moved), r.Message);

    // ================= SEARCH =================
    Directory.CreateDirectory(Path.Combine(root, "deep", "er"));
    File.WriteAllText(Path.Combine(root, "deep", "er", "Report-Final.PDF"), "");
    File.WriteAllText(Path.Combine(root, "deep", "report.txt"), "");
    File.CreateSymbolicLink(Path.Combine(root, "etc-again"), "/etc");
    var s1 = svc.Search(root, "report", 500, CancellationToken.None);
    Check("search finds names recursively, case-insensitive", s1.Paths.Count(p => p.Contains("deep")) == 2, string.Join(", ", s1.Paths.Select(Path.GetFileName)));
    var s2 = svc.Search(root, "*.pdf", 500, CancellationToken.None);
    Check("search wildcard", s2.Paths.Count == 1 && s2.Paths[0].EndsWith("Report-Final.PDF"));
    var s3 = svc.Search(root, "passwd", 500, CancellationToken.None);
    Check("search does not follow symlinked folders", !s3.Paths.Any(p => p.Contains("etc-again/")), string.Join(", ", s3.Paths));
    var s4 = svc.Search(root, "", 500, CancellationToken.None);
    Check("empty search rejected", s4.Error is not null);
    var s5 = svc.Search(root, "t", 3, CancellationToken.None);
    Check("search result cap", s5.Paths.Count == 3 && s5.Truncated);
    using (var cts = new CancellationTokenSource())
    {
        cts.Cancel();
        bool cancelled = false;
        try { svc.Search("/usr", "a", 100000, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
        Check("search can be cancelled", cancelled);
    }
    var s6 = svc.Search("/etc", "shadow", 500, CancellationToken.None);
    Check("search outside home works read-only", s6.Error is null, $"{s6.Paths.Count} found, {s6.SkippedFolders} unreadable folders skipped");
    var s7 = svc.Search("/", "meminfo", 500, CancellationToken.None);
    Check("search from / stays on one filesystem (no /proc)", !s7.Paths.Any(p => p.StartsWith("/proc/")), string.Join(", ", s7.Paths.Take(5)));
}
finally
{
    Sh($"chmod -R u+w '{root}' 2>/dev/null; rm -rf '{root}'");
}

Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
