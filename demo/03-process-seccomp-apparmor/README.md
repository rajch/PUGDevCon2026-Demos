# Demo Scenario 3: Seccomp, AppArmor and Process Execution

## The Context

PayDesk's admin **Consolidation** page (`/Admin/Consolidation`) turns the month's uploaded expense files
into payslip PDFs. It does this by calling `Process.Start` on a standalone executable
(`/app/consolidator/PayDesk.Consolidator`), a very natural thing for a .NET developer to write.

On Linux that means `vfork`/`clone` followed by `execve`. Two kernel-level mechanisms can sit in the way:

| | **seccomp** | **AppArmor** |
|---|---|---|
| Filters | *which system calls* a process may make | *which files / paths / operations* a process may touch |
| Here it blocks | creating a new process (`fork`/`vfork`/`clone`) | executing `/app/consolidator/**` |
| Errno the app sees | `EPERM`, "Operation not permitted" | `EACCES`, "Permission denied" |

Note what seccomp **cannot** do here: it can't say "exec only this one file". It sees `execve`, not paths.
(And a profile that blocks `execve` outright would stop the container from even starting.) Path-based
rules are AppArmor's job.

## Prerequisites (once per run)

The consolidator only does work if a pending expense file exists, so before each step below:

1. Sign in as `alice@paydesk.local` / `User@123!` and upload `samples/expenses_alice_2026_11.csv`.
2. Then sign in as `admin@paydesk.local` / `Admin@123!` and open `/Admin/Consolidation`.

(Uploads go to the container's own `/app/data`, which the image already owns as `app`, so this works in every
variant here. Volume ownership was demo 1's story.)

---

## Step 0: Baseline (Docker defaults)

```bash
docker compose -f demo/03-process-seccomp-apparmor/docker-compose.baseline.yml up -d
```

Run consolidation for `2026-11`. It **succeeds**. Docker's default seccomp and AppArmor profiles allow it.

```bash
docker compose -f demo/03-process-seccomp-apparmor/docker-compose.baseline.yml down
```

---

## Step 1: The Failure (a custom seccomp profile)

A security team mandates a custom profile: "web containers must not create processes". The file
[`seccomp-profile.json`](seccomp-profile.json) is **Docker's default profile with three small edits**: `fork` and
`vfork` removed, and `clone` allowed only when `CLONE_THREAD` is set (threads yes, new processes no).

```bash
docker compose -f demo/03-process-seccomp-apparmor/docker-compose.seccomp.yml up -d
```

### What to Observe
1. Everything else still works: login, upload, the dashboard, the database check on `/Status`.
   Thread-pool growth under load is fine too, because threads are still allowed.
2. Run consolidation for `2026-11`. It fails with:
   ```text
   Execution Exception: An error occurred trying to start process '/app/consolidator/PayDesk.Consolidator'
   with working directory '/app/web'. Operation not permitted
   ```
3. Check the filter is really on (`2` = seccomp filter mode):
   ```bash
   docker exec paydesk-web-s3-seccomp grep Seccomp: /proc/1/status
   ```
4. Side effect worth pointing out: .NET's `Ping` falls back to the system `ping` binary when it can't open a raw
   socket, and that is also a `Process.Start`. If the `/Status` ICMP card goes red in this variant, it is the
   same root cause showing up in a second feature.

> **Talking point:** the profile is not "wrong": it does exactly what was asked. The application was written
> assuming it could spawn processes, and nobody told it otherwise.

> **Why not write the profile from scratch?** Hand-rolled allow-lists are fragile. The previous version of this
> demo's profile was a 138-syscall allow-list that omitted `set_tid_address`, `rseq`, `statx` and others, and
> .NET died at startup (`Fatal glibc error: Cannot allocate TLS block`) before serving a single request.
> Start from the runtime's default and change as little as possible.

---

## Step 2: The AppArmor version of the same failure

```bash
docker compose -f demo/03-process-seccomp-apparmor/docker-compose.seccomp.yml down

# Load the profile into the host kernel (Linux host with AppArmor; not Docker Desktop on macOS/Windows)
sudo apparmor_parser -r -W demo/03-process-seccomp-apparmor/apparmor-profile.txt

docker compose -f demo/03-process-seccomp-apparmor/docker-compose.apparmor.yml up -d
```

[`apparmor-profile.txt`](apparmor-profile.txt) is Docker's `docker-default` profile plus **one rule**:
`deny /app/consolidator/** x,`.

### What to Observe
1. Run consolidation. The failure text is different: **"Permission denied"** (`EACCES`), not "Operation not permitted".
   The `vfork` succeeded; the `execve` of that path was refused.
2. Confirm the profile is attached:
   ```bash
   docker exec paydesk-web-s3-apparmor cat /proc/1/attr/current
   # paydesk-restricted (enforce)
   ```
3. Show that AppArmor reasons about *paths*, not syscalls. Both work, only the denied path doesn't:
   ```bash
   docker exec paydesk-web-s3-apparmor id                                                       # allowed
   docker exec paydesk-web-s3-apparmor /app/consolidator/PayDesk.Consolidator --month 2026-11   # permission denied
   ```
   (`docker exec` processes are confined by the same profile.)

> **Honest limitation to mention:** the rule blocks *executing that file*. Running
> `dotnet /app/consolidator/PayDesk.Consolidator.dll` would execute `/usr/share/dotnet/dotnet` (allowed) which merely
> *reads* the DLL. Path rules are exactly as strong as the paths you thought of.

---

## Step 3: The Insecure Anti-Pattern ("just turn it off")

```bash
docker compose -f demo/03-process-seccomp-apparmor/docker-compose.apparmor.yml down
docker compose -f demo/03-process-seccomp-apparmor/docker-compose.insecure-unconfined.yml up -d
```

Consolidation works again, because `seccomp:unconfined` and `apparmor:unconfined` removed **both** safety nets,
not just the rule that was in the way. Docker's default seccomp profile (which blocks ~40-50 dangerous syscalls such
as `bpf` and `kexec_load`, among others) is gone along with the one rule that annoyed us.

---

## Step 4: The Proper Fix (architecture, then Kubernetes)

Spawning a batch process from inside a web container is the real problem:

1. **Lifecycle**: if the web pod restarts or is OOM-killed (or a rolling update happens), the child process is orphaned or aborted mid-run.
2. **Resources**: web traffic and heavyweight PDF generation now share one container's CPU and memory limits.
3. **Privilege**: the web container needs permission to create and execute processes, which is the first thing
   anyone with a code-execution bug in the web app will use.

So decouple it:

```text
[Web Portal] ---> [Kubernetes API / queue] ---> [Kubernetes Job: PayDesk.Consolidator]
```

[`k8s-manifest.yaml`](k8s-manifest.yaml) has both halves:

* **Web Deployment**: `RuntimeDefault` seccomp, `readOnlyRootFilesystem`, drop `ALL` capabilities, no consolidator involved.
  Commented lines show the `Localhost` seccomp and AppArmor variants.
* **Consolidator Job** (`batch/v1`): same image, runs `dotnet /app/consolidator/PayDesk.Consolidator.dll --month 2026-11`
  to completion and exits.

### Cleanup
```bash
docker compose -f demo/03-process-seccomp-apparmor/docker-compose.insecure-unconfined.yml down
sudo apparmor_parser -R demo/03-process-seccomp-apparmor/apparmor-profile.txt   # unload the profile
```
