# Demo Scenario 2: Linux Capabilities (`CAP_NET_RAW`)

## The Context

Linux divides traditional superuser (root) privileges into distinct units called **capabilities** (e.g. `CAP_NET_BIND_SERVICE`, `CAP_CHOWN`, `CAP_NET_RAW`, `CAP_SYS_ADMIN`).

By default, Docker grants a container a subset of roughly 14 capabilities. Modern security benchmarks (such as CIS Docker Benchmark and NSA Kubernetes Hardening Guide) recommend **dropping ALL capabilities** by default:
```yaml
cap_drop:
  - ALL
```

In PayDesk, we have an Application Status page (`/Status`) that:
1. Connects to the PostgreSQL database over regular TCP port 5432 (standard stream socket).
2. Sends an ICMP Echo Request (Ping) to the database host using `System.Net.NetworkInformation.Ping` (raw datagram socket).

---

## Step 1: The Failure (Dropping Capabilities)

When we drop all capabilities, what happens to the application?

### Commands to Run:
```bash
docker compose -f demo/02-linux-capabilities/docker-compose.cap-drop.yml up -d
```

### What to Observe:
1. Open your browser to `http://localhost:8080/Status`.
2. Notice the striking visual contrast:
   * **Overall Health Banner**: Displays a bright red **System Health Degraded / Checks Failed** alert.
   * **Database Connection Card (Right)**: **Green / Connected** (Latency ~1ms, PostgreSQL version displayed). TCP port 5432 stream sockets are unprivileged and work without capabilities!
   * **ICMP Ping Card (Left)**: **Red / Ping Failed**.
     * Exception: `SocketException: Operation not permitted` (or `PingException`).
     * Linux Security Alert: Explains that opening a raw ICMP socket requires `CAP_NET_RAW`.
3. Check the container's capabilities from the host:
   ```bash
   docker exec paydesk-web-s2-cap-drop getpcaps 1
   # Outputs: Capabilities for `1': = (Empty! No capabilities held)
   ```

---

## Step 2: The Insecure Anti-Pattern (`--privileged`)

When a developer sees "Operation not permitted" on a network socket, a frequent dangerous shortcut is adding `--privileged` or `privileged: true`.

### Commands to Run:
```bash
docker compose -f demo/02-linux-capabilities/docker-compose.cap-drop.yml down
docker compose -f demo/02-linux-capabilities/docker-compose.insecure-privileged.yml up -d
```

### What to Observe:
1. Refresh `http://localhost:8080/Status`.
2. Both checks are green.
3. **The Enormous Security Risk**:
   Check what capabilities were granted:
   ```bash
   docker exec paydesk-web-s2-privileged getpcaps 1
   ```
   Notice that the process now possesses **every single Linux capability**, including:
   * `cap_sys_admin` (can mount filesystems, load BPF programs)
   * `cap_sys_ptrace` (can inspect host/other processes)
   * `cap_sys_rawio` (direct hardware access)
   * In addition, `privileged: true` disables AppArmor, removes Seccomp syscall filtering, and mounts all host devices from `/dev` inside the container!
4. **Session Talking Point**: Using `privileged` to solve a ping failure is like using a sledgehammer that removes the front door of your house.

---

## Step 3: The Proper Corrections

### Proper Docker Fix: Minimal Principle of Least Privilege
Drop `ALL` capabilities by default, and selectively add back **only** `NET_RAW`:

```bash
docker compose -f demo/02-linux-capabilities/docker-compose.insecure-privileged.yml down
docker compose -f demo/02-linux-capabilities/docker-compose.proper-cap-add.yml up -d
```

Check `http://localhost:8080/Status`:
* Status page is now completely **Green**!
* Inspect capabilities:
  ```bash
  docker exec paydesk-web-s2-proper getpcaps 1
  # Outputs: = cap_net_raw+ep (Only CAP_NET_RAW is held!)
  ```

### Proper Kubernetes Fix
In Kubernetes Pod manifests:
```yaml
securityContext:
  allowPrivilegeEscalation: false
  capabilities:
    drop:
      - ALL
    add:
      - NET_RAW
```
Review the complete Kubernetes manifest in [k8s-manifest.yaml](file:///home/rajch/projects/rajch/PUGDevCon2026-Demos/demo/02-linux-capabilities/k8s-manifest.yaml).

### Alternative Architectural Fix:
Ask the audience: *Why are we using raw ICMP ping to check a database?*
An application health probe should check TCP connectivity or an HTTP `/healthz` endpoint instead of relying on OS raw socket capabilities.

### Cleanup
```bash
docker compose -f demo/02-linux-capabilities/docker-compose.proper-cap-add.yml down
```
