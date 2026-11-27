# Demo Scenario 2: Linux Capabilities

## The Context

Linux breaks administrative privileges into granular units called capabilities (for example, CAP_NET_BIND_SERVICE, CAP_CHOWN, CAP_NET_RAW, and CAP_SYS_ADMIN).

Capabilities are not tied directly to user IDs. A process running as a non-root user can be granted selected capabilities, and a process running as root can drop capabilities it does not require. This enables a more fine-grained application of the principle of least privilege.

In containers, all processes are given a set of capabilties by default regarding of which user the process runs as. This is a subset of all the capabilties available in Lunux, still these may be more than what an application requires. Security guidelines frequently recommend that containerized applications drop all capabilities because normal operations like reading or writing files, TCP or UDP communication over the network, etc., do not require any capabilites.

In our Paydesk application, the Status page tries to ping the database server. Pinging requires a capability called CAP_NET_RAW.

## Step 1: The Failure (Dropping Capabilities)

When we drop all capabilities, what happens to the application?

### Commands to Run:

```bash
docker compose -f deploy/d2-1.docker-compose.yml up -d
```

### What to Observe:

1. Open your browser to `http://localhost:8080/Status`.
2. Check the System Health Status. 
    * The ping has failed with the error. 
     * Exception Details `Operation not permitted` 
3. Check the container's capabilities from the host:
   ```bash
   docker exec d2-1-paydesk-web capsh --print
   
   Look for 'Bounding Set = ', it is empty.
   ```
---

## Step 2: The Insecure Anti-Pattern (`--privileged`)

When a developer sees "Operation not permitted" on a network socket, a frequent dangerous shortcut is adding `--privileged` or `privileged: true`.

### Commands to Run:
```bash
docker compose -f deploy/d2-1.docker-compose.yml down --volumes

docker compose -f deploy/d2-2.docker-compose.yml up -d
```

### What to Observe:
1. Refresh `http://localhost:8080/Status`.
2. Both checks are green.
3. **The Enormous Security Risk**:
   Check what capabilities were granted:
   ```bash
   docker exec d2-2-paydesk-web capsh --print
   ```
   Look for 'Bounding set = '. This shows all the capabilities assigned.
   
   Notice that the process now possesses **every single Linux capability**, including:
   * `cap_sys_admin` (can mount filesystems, load BPF programs)
   * `cap_sys_ptrace` (can inspect host/other processes)
   * `cap_sys_rawio` (direct hardware access)
 
In addition, `privileged: true` disables AppArmor, removes Seccomp syscall filtering, and mounts all host devices from `/dev` inside the container!

Using `privileged` to solve a ping failure (or similar capability-based failures) is like using a sledgehammer that removes the front door of your house.

---

## Step 3: The Proper Corrections

### Proper Docker Fix: Minimal Principle of Least Privilege
Drop `ALL` capabilities by default, and selectively add back **only** `NET_RAW`:

### Commands to Run:
```bash
docker compose -f deploy/d2-2.docker-compose.yml down --volumes

docker compose -f deploy/d2-3.docker-compose.yml up -d
```

Check `http://localhost:8080/Status`:
* Status page is now completely **Green**!
* Inspect capabilities:
  ```bash
  docker exec d2-3-paydesk-web capsh --print
  ```
  Look for 'Bounding set = cap_net_raw'. This shows only the capability assigned.

## Cleaning Up

Run the following command:

```bash
docker compose -f deploy/d2-3.docker-compose.yml down --volumes
```
---


