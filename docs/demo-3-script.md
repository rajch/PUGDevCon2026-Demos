# Demo Scenario 3: Seccomp and AppArmor

## The Context

Seccomp (secure computing mode) in Linux restricts the system calls a process can invoke, shrinking the kernel attack surface exposed to an application. When a process runs in strict mode, it can only call read, write, _exit, and sigreturn; any other system call triggers a SIGKILL signal that terminates the process immediately. In filter mode (SECCOMP_FILTER), administrators attach a Berkeley Packet Filter (BPF) ruleset that inspects system call numbers and arguments to allow, block, log, or return custom error codes, allowing container runtimes, sandboxes, and daemons to block risky operations like ptrace, mount, or kexec_load without breaking normal execution.

AppArmor is a Linux security module that restricts what resources an application can access by binding a security profile to the program rather than relying on user IDs alone. Through mandatory access control, profiles define explicit permissions for files, directories, network sockets, capabilities, and IPC mechanisms, operating either in enforce mode—which blocks unauthorized actions and logs them—or complain mode, which logs violations without blocking so administrators can tune rules safely. This path-based confinement limits blast radius if a service is compromised, complementing syscall filtering like Seccomp by controlling resource access rather than system calls.

Container engines enforce defense-in-depth at startup by applying default Seccomp and AppArmor profiles together to confine container processes. While the Seccomp profile filters system calls—permitting routine operations for standard Linux workloads and blocking high-risk calls like `reboot`, `swapon`, or `bpf`—the AppArmor profile restricts what resources those permitted calls can touch, bounding access to specific file paths, network sockets, and Linux capabilities. When workloads require tighter sandboxing or specialized permissions, administrators can swap in custom profiles for either layer to tune isolation without altering the underlying host kernel.

In our Paydesk application, the admin user can run a consolidation process. In our architecture, this happens by the Paydesk webapp launching a child process which runs the Consolidator application.

A webapp is accessible to the outside world. If a hacker manages to compromise it, they may instead execute a malicious program. To prevent situations like these, a Seccomp profile which does not allow child processes to be launched at all. Administrators may apply such a profile to public-facing applications.

## Step 1: The Failure (Custom Seccomp Profile)

Container engines apply a default Seccomp profile which restricts some potentially dangerous system calls. here an even more restricted custom profile is added which does not permit the launching of child processes. On Linux child processes are launched using one of the system calls `fork`, `vfork` and `clone`, followed by `execve`. The restricted profile does not permit `fork`, `vfork` or `clone`.

### Commands to Run:

```bash
docker compose -f deploy/d3-1.docker-compose.yml up -d
```

### What to Observe:

1. Open your browser to `http://localhost:8080/Status`.
2. Sign in as `admin@paydesk.local` (Password: `Admin@123!`).
3. Upload `samples/expenses_alice_2026_11.csv`
4. Try to run the consolidation process. It fails with the error: `Operation not permitted`.

## The Fix
This is not an error. It is reasonable for administrators to apply this kind of policy for public facing applications. Ideally the consolidation process should not be in the same container as the webapp. It should be run in a separate container which is never exposed to users. This is anyway in line with containerization best practices, which say that `one container should do only one thing`.

Rearchitecting the application allows different levels of Seccomp policies to be applied to different components.


## Cleaning Up

Run the following command:

```bash
docker compose -f deploy/d3-1.docker-compose.yml down --volumes
```
---


