# Demo Scenario 3: Process Execution Boundaries, Seccomp, and AppArmor

## The Context

In PayDesk, an administrative user can trigger a monthly consolidation:
* The web app (`PayDesk.Web`) uses `Process.Start` to launch a standalone executable (`PayDesk.Consolidator`).
* On Linux, launching a child process invokes system calls like `fork`, `clone`, `clone3`, `execve`, and `execveat`.

In modern containerized and orchestrated environments (Docker / Kubernetes):
1. **Seccomp** (Secure Computing Mode) uses BPF filters to restrict which system calls an application is allowed to make.
2. **AppArmor** enforces Mandatory Access Control (MAC) based on file paths and execution permissions.
3. **Architecture Principle**: Spawning long-running or batch child processes from inside a web container is an architectural anti-pattern that violates container isolation and single-responsibility principles.

---

## Step 1: Running with Seccomp Filtering

Docker applies a default seccomp profile that blocks around 44-50 dangerous syscalls (such as `ptrace`, `sys_admin`, `reboot`, `bpf`).

In `demo/03-process-seccomp-apparmor/docker-compose.seccomp-apparmor.yml`, we configure:
* `no-new-privileges:true`: Prevents processes from gaining additional privileges via `setuid` binaries.
* `seccomp:./seccomp-profile.json`: Explicit syscall profile configuration.

### Commands to Run:
```bash
docker compose -f demo/03-process-seccomp-apparmor/docker-compose.seccomp-apparmor.yml up -d
```

### What to Observe:
1. Open `http://localhost:8080/Admin/Consolidation` (Sign in as `admin@paydesk.local` / `Admin@123!`).
2. Run consolidation for `2026-11`.
3. Notice that `Process.Start` executes within the allowed syscalls.
4. **Session Discussion Point**:
   * What if a strict seccomp profile blocks `clone3` or `fork`? The call fails with `EPERM` / `Operation not permitted`.
   * What if an AppArmor profile (like [apparmor-profile.txt](file:///home/rajch/projects/rajch/PUGDevCon2026-Demos/demo/03-process-seccomp-apparmor/apparmor-profile.txt)) specifies `deny /app/consolidator/** x`? The process execution fails with `Permission denied`.

---

## Step 2: The Architectural Lesson & Kubernetes Solution

Why is spawning `Process.Start` inside a containerized web application problematic?
1. **Container Life Cycle**: If the web pod restarts, crashes, or is killed by Kubernetes (e.g. OOMKilled or rolling update), the child batch process is orphaned or aborted mid-run.
2. **Resource Constraints**: CPU and memory limits defined for the web container must accommodate both the web traffic and the heavyweight batch PDF generation process.
3. **Security Privileges**: Granting execution permissions inside a web-facing container increases the risk of remote code execution (RCE) vulnerabilities.

### The Proper Solution: Kubernetes Batch Job (`batch/v1`)
Instead of launching child processes inside the web pod, decouple the workload:

```text
[Web Portal] ---> [Kubernetes API / Message Queue] ---> [Kubernetes Job (PayDesk.Consolidator)]
```

In [k8s-manifest.yaml](file:///home/rajch/projects/rajch/PUGDevCon2026-Demos/demo/03-process-seccomp-apparmor/k8s-manifest.yaml):
1. **Web Pod**:
   * Uses `readOnlyRootFilesystem: true` (no binaries or scripts can be overwritten or added).
   * Drops `ALL` capabilities.
   * Uses `seccompProfile: { type: RuntimeDefault }`.
2. **Batch Consolidator Job**:
   * Runs as an isolated Kubernetes Job using the exact same `paydesk:latest` image.
   * Entrypoint invokes `dotnet /app/consolidator/PayDesk.Consolidator.dll --month 2026-11`.
   * When finished, the container terminates cleanly with exit code 0.

### Cleanup:
```bash
docker compose -f demo/03-process-seccomp-apparmor/docker-compose.seccomp-apparmor.yml down
```
